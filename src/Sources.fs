module Sources

open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Types

// ---------------------------------------------------------------------------
// runtime-only local-source registry — mirrors the original app's global `SRC`
// object. Live File System Access handles, parsed ZIP indices and in-memory
// file maps can't be serialized/compared, so (unlike Mode/BaseUrl, which stay
// in the pure Elmish Model) they live here instead.
// ---------------------------------------------------------------------------

type private Registry =
    { mutable Zips: Map<RepoKey, Zip.ZipIndex>
      mutable Handles: Map<RepoKey, obj>
      mutable Files: Map<string, File> option
      mutable RememberedDirs: obj array
      mutable RememberedZips: obj array }

let private reg =
    { Zips = Map.empty; Handles = Map.empty; Files = None; RememberedDirs = [||]; RememberedZips = [||] }

// ---------------------------------------------------------------------------
// repo helpers
// ---------------------------------------------------------------------------

let repoKey (repo: string) : RepoKey =
    if System.Text.RegularExpressions.Regex.IsMatch(repo, "First1K", System.Text.RegularExpressions.RegexOptions.IgnoreCase) then
        First1K
    else
        Perseus

let repoName =
    function
    | Perseus -> "canonical-greekLit"
    | First1K -> "First1KGreek"

let repoFolders =
    function
    | First1K -> [ "First1KGreek"; "First1KGreek-master"; "First1KGreek-main" ]
    | Perseus -> [ "canonical-greekLit"; "canonical-greekLit-master"; "canonical-greekLit-main" ]

let rawUrl (t: TextMeta) : string =
    let parts = t.File.Split('.')
    sprintf "https://raw.githubusercontent.com/%s/master/data/%s/%s/%s" t.Repo parts.[0] parts.[1] t.File

/// How many texts a connected repo actually holds, for the "connected" message.
/// ZIPs know their own entry count; a `webkitdirectory` file list is a single
/// flat pool, so it reports the whole pool.
let localTextCount (repo: RepoKey) : int =
    match reg.Zips.TryFind repo with
    | Some z -> z.Entries.Count
    | None ->
        // Only a session file list reports a pool count; a directory handle is
        // browsed lazily and has no count to give, so say nothing rather than
        // borrowing an unrelated number.
        match reg.Handles.TryFind repo, reg.Files with
        | None, Some files -> files.Count
        | _ -> 0

/// Whether anything at all is connected — drives the "Local" chip's warning state.
let hasAnyLocal () : bool =
    not (reg.Zips.IsEmpty && reg.Handles.IsEmpty && reg.Files.IsNone)

/// Whether there are remembered handles we could offer to reconnect.
let hasRemembered () : bool =
    reg.RememberedDirs.Length > 0 || reg.RememberedZips.Length > 0

let localHave () : RepoKey list =
    let fromZips = reg.Zips |> Map.toList |> List.map fst
    let fromFiles =
        match reg.Files with
        | Some files ->
            let names = files |> Map.toList |> List.map fst
            [ if names |> List.exists (fun n -> n.Contains "1st1K") then yield First1K
              if names |> List.exists (fun n -> n.Contains "perseus-") then yield Perseus ]
        | None -> []
    let fromHandles = reg.Handles |> Map.toList |> List.map fst
    fromZips @ fromFiles @ fromHandles |> List.distinct

// ---------------------------------------------------------------------------
// low-level browser interop — File System Access API has no Fable binding
// package, and `fetch`/`Response` aren't covered by our referenced packages
// ---------------------------------------------------------------------------

[<Emit("'showDirectoryPicker' in window")>]
let hasDirectoryPicker: bool = jsNative

[<Emit("'showOpenFilePicker' in window")>]
let hasOpenFilePicker: bool = jsNative

[<Emit("window.showDirectoryPicker({ mode: 'read' })")>]
let private showDirectoryPickerJs (): JS.Promise<obj> = jsNative

[<Emit("window.showOpenFilePicker({ types: [{ description: 'ZIP archive', accept: { 'application/zip': ['.zip'] } }] })")>]
let private showOpenFilePickerForZipJs (): JS.Promise<obj array> = jsNative

[<Emit("$0.getFile()")>]
let private getFileFromHandle (h: obj) : JS.Promise<File> = jsNative

[<Emit("$0.name")>]
let private handleName (h: obj) : string = jsNative

[<Emit("$0.queryPermission({ mode: 'read' })")>]
let private queryReadPermission (h: obj) : JS.Promise<string> = jsNative

[<Emit("$0.requestPermission({ mode: 'read' })")>]
let private requestReadPermission (h: obj) : JS.Promise<string> = jsNative

[<Emit("Array.from($0)")>]
let private toFileArray (fl: obj) : File array = jsNative

/// A picker rejects with AbortError when the person closes it without choosing.
/// Everything else is a genuine failure and has to be shown, not swallowed.
[<Emit("!!$0 && ($0.name === 'AbortError' || $0.name === 'NotAllowedError')")>]
let private isCancellation (e: obj) : bool = jsNative

[<Emit("fetch($0)")>]
let private fetchUrl (url: string) : JS.Promise<obj> = jsNative

[<Emit("$0.ok")>]
let private responseOk (res: obj) : bool = jsNative

[<Emit("$0.status")>]
let private responseStatus (res: obj) : int = jsNative

[<Emit("$0.text()")>]
let private responseText (res: obj) : JS.Promise<string> = jsNative

/// Reads a text file at data/<g>/<w>/<fileName> under a connected directory handle,
/// resolving to `null` on any failure (missing subfolder, permission, etc.) — mirrors
/// the original's silently-swallowed try/catch around the same lookup.
// `__d` / `__fh` rather than `d` / `fh` for the same reason as `__root` below:
// a caller binding of the same name would be shadowed into the dead zone.
[<Emit("""
(async () => {
    try {
        let __d = await $0.getDirectoryHandle('data');
        __d = await __d.getDirectoryHandle($1);
        __d = await __d.getDirectoryHandle($2);
        const __fh = await __d.getFileHandle($3);
        const __f = await __fh.getFile();
        return await __f.text();
    } catch (e) { return null; }
})()
""")>]
let private readFromDirHandle (h: obj) (g: string) (w: string) (fileName: string) : JS.Promise<string> = jsNative

/// Recursively looks for canonical-greekLit / First1KGreek repo roots under a picked
/// directory (mirrors `findRepos`/`sniff`/`check` verbatim) — ported as one embedded
/// script since Fable has no clean way to bind JS async iterators (`for await...of`
/// over `FileSystemDirectoryHandle.entries()`) individually.
// The local names here are deliberately mangled. Fable substitutes `$0` with
// the *caller's* expression verbatim, so `const dir = $0` next to a caller whose
// binding is also called `dir` emits `const dir = dir` — a temporal-dead-zone
// ReferenceError thrown before the function does anything. Every call site here
// passes something named `dir`, so folder connection failed 100% of the time.
// Same trap the `window.history` / `window.location` bindings above avoid.
[<Emit("""
(async () => {
    const __root = $0;
    const found = {};
    const hasData = async (h) => { try { await h.getDirectoryHandle('data'); return true; } catch (e) { return false; } };
    const asRepo = (h, dataH) => ({ name: h.name, getDirectoryHandle: (n) => n === 'data' ? Promise.resolve(dataH) : Promise.reject(new Error('no')) });
    const sniff = async (dataH) => {
        try { await dataH.getDirectoryHandle('tlg0012'); return 'perseus'; } catch (e) {}
        try { await dataH.getDirectoryHandle('tlg0093'); return 'f1k'; } catch (e) {}
        for await (const [n, e] of dataH.entries()) {
            if (e.kind === 'directory' && /^tlg\d{4}$/.test(n)) {
                try {
                    for await (const [m, f] of e.entries()) {
                        if (f.kind === 'directory') {
                            for await (const [x] of f.entries()) {
                                if (/1st1K/.test(x)) return 'f1k';
                                if (/perseus-/.test(x)) return 'perseus';
                            }
                        }
                    }
                } catch (e) {}
            }
        }
        return null;
    };
    const check = async (h, depth) => {
        if (/canonical-greekLit/i.test(h.name) && await hasData(h)) { found.perseus = h; return; }
        if (/First1KGreek/i.test(h.name) && await hasData(h)) { found.f1k = h; return; }
        if (await hasData(h)) { const d = await h.getDirectoryHandle('data'); const k = await sniff(d); if (k) { found[k] = h; return; } }
        if (depth === 0 && h.name === 'data') { const k = await sniff(h); if (k) found[k] = asRepo(h, h); return; }
        if (depth >= 2) return;
        for await (const [name, e] of h.entries()) {
            if (e.kind === 'directory' && !name.startsWith('.') && name !== 'node_modules') await check(e, depth + 1);
        }
    };
    await check(__root, 0);
    return found;
})()
""")>]
let private findReposJs (dir: obj) : JS.Promise<obj> = jsNative

let private extractFound (found: obj) : (RepoKey * obj) list =
    [ if not (isNullOrUndefined found?perseus) then yield Perseus, found?perseus
      if not (isNullOrUndefined found?f1k) then yield First1K, found?f1k ]

/// How each repo is currently connected, for the settings pane's status list.
/// Defined here rather than beside `localHave` because it needs `handleName`.
let localConnections () : (RepoKey * LocalKind) list =
    let fromZips = reg.Zips |> Map.toList |> List.map (fun (k, z) -> k, ZipArchive z.Name)
    let fromHandles = reg.Handles |> Map.toList |> List.map (fun (k, h) -> k, DirHandle(handleName h))
    let fromFiles =
        match reg.Files with
        | Some files ->
            let names = files |> Map.toList |> List.map fst
            [ if names |> List.exists (fun n -> n.Contains "1st1K") then yield First1K, FileList
              if names |> List.exists (fun n -> n.Contains "perseus-") then yield Perseus, FileList ]
        | None -> []
    // A ZIP is the most specific claim, then a live handle, then session files.
    (fromZips @ fromHandles @ fromFiles) |> List.distinctBy fst

let private rememberHandle (key: string) (h: obj) : JS.Promise<unit> =
    promise {
        let! existingObj = Storage.idbGet key
        let existing: obj array = if isNullOrUndefined existingObj then [||] else unbox existingObj
        let alreadyThere = existing |> Array.exists (fun x -> handleName x = handleName h)
        let updated = if alreadyThere then existing else Array.append existing [| h |]
        let trimmed = if updated.Length > 4 then updated.[updated.Length - 4 ..] else updated
        do! Storage.idbSet key (box trimmed)
    }

// ---------------------------------------------------------------------------
// ZIP archives
// ---------------------------------------------------------------------------

/// Parses and registers a ZIP archive (mirrors `addZipFile`); remembers the picked
/// handle in IndexedDB (capped to the last 4) when one is available.
let addZipFile (file: File) (handle: obj option) : JS.Promise<Result<RepoKey * string, string>> =
    promise {
        try
            let! z = Zip.buildIndex file
            reg.Zips <- Map.add z.Repo z reg.Zips
            match handle with
            | Some h -> do! rememberHandle "zips" h
            | None -> ()
            return Ok(z.Repo, z.Name)
        with e ->
            return Error(if System.String.IsNullOrEmpty e.Message then "Could not read that ZIP" else e.Message)
    }

/// Opens the native file picker for a ZIP archive; `None` if the user cancelled
/// (mirrors `pickZip`'s empty catch). Caller should fall back to a hidden
/// `<input type="file">` when `hasOpenFilePicker` is false.
let pickZip (): JS.Promise<Result<RepoKey * string, string> option> =
    promise {
        try
            let! handles = showOpenFilePickerForZipJs ()
            let! file = getFileFromHandle handles.[0]
            let! result = addZipFile file (Some handles.[0])
            return Some result
        with e ->
            if isCancellation (box e) then return None
            else return Some(Error("Could not read that ZIP: " + e.Message))
    }

// ---------------------------------------------------------------------------
// local directory (File System Access)
// ---------------------------------------------------------------------------

/// Registers whichever repo roots were found under a directory handle (mirrors
/// `connectDirHandle`); remembers the handle in IndexedDB when `remember` is true.
let connectDirHandle (dir: obj) (remember: bool) : JS.Promise<Result<RepoKey list * string, string>> =
    promise {
        let! foundJs = findReposJs dir
        let found = extractFound foundJs
        if List.isEmpty found then
            return Error "That folder doesn't look like canonical-greekLit or First1KGreek"
        else
            reg.Handles <- found |> List.fold (fun m (k, h) -> Map.add k h m) reg.Handles
            // Keep any session file list: the two are alternatives to try in
            // order, not a choice, and dropping it here threw away a working
            // source when a folder happened to hold only one of the repos.
            // Remembering is best-effort — a private window or a full disk
            // makes IndexedDB throw, and that must not discard a connection
            // that has already succeeded.
            if remember then
                try
                    do! rememberHandle "dirs" dir
                with _ -> ()
            return Ok(found |> List.map fst, handleName dir)
    }

/// Opens the native directory picker. `None` means the person cancelled;
/// anything else that goes wrong is reported rather than silently swallowed —
/// the previous blanket catch turned every real failure into "user cancelled",
/// which is what hid the crash in `findReposJs` for so long.
let pickDir (): JS.Promise<Result<RepoKey list * string, string> option> =
    promise {
        try
            let! dir = showDirectoryPickerJs ()
            let! result = connectDirHandle dir true
            return Some result
        with e ->
            if isCancellation (box e) then return None
            else return Some(Error("Could not read that folder: " + e.Message))
    }

// ---------------------------------------------------------------------------
// file list (webkitdirectory <input> fallback, session-only)
// ---------------------------------------------------------------------------

/// Indexes the `.xml` files from a `webkitdirectory` file input. Adds to
/// whatever is already connected rather than replacing it: the read path tries
/// ZIP, then this pool, then directory handles in turn, so clearing the handles
/// here only ever threw away a working source.
let connectFileList (fileList: obj) : Result<int, string> =
    let files = toFileArray fileList
    let xmlFiles = files |> Array.filter (fun f -> f.name.EndsWith ".xml")
    if xmlFiles.Length = 0 then
        Error "No text files found in that folder"
    else
        let existing = reg.Files |> Option.defaultValue Map.empty
        reg.Files <- Some(xmlFiles |> Array.fold (fun m f -> Map.add f.name f m) existing)
        Ok xmlFiles.Length

// ---------------------------------------------------------------------------
// remembered handles (IndexedDB) — silent reconnect at boot, then a one-click
// confirm when permission wasn't already granted
// ---------------------------------------------------------------------------

/// Attempts to silently reconnect remembered directories/ZIPs (mirrors the
/// original's boot-time IIFE). Returns the names still needing a permission
/// click; an empty list means everything reconnected silently (or nothing was
/// remembered).
let reconnectRemembered (): JS.Promise<string list> =
    promise {
        let! dirsObj = Storage.idbGet "dirs"
        let! zipsObj = Storage.idbGet "zips"
        let dirs: obj array = if isNullOrUndefined dirsObj then [||] else unbox dirsObj
        let zips: obj array = if isNullOrUndefined zipsObj then [||] else unbox zipsObj
        reg.RememberedDirs <- dirs
        reg.RememberedZips <- zips

        if dirs.Length = 0 && zips.Length = 0 then
            return []
        else
            // Collect the names that actually need a click. Previously any one
            // handle short of permission caused *every* remembered name to be
            // listed, so the prompt named folders that were already connected.
            let pending = ResizeArray<string>()
            for dir in dirs do
                try
                    let! perm = queryReadPermission dir
                    if perm = "granted" then
                        let! foundJs = findReposJs dir
                        let found = extractFound foundJs
                        reg.Handles <- found |> List.fold (fun m (k, h) -> Map.add k h m) reg.Handles
                    else
                        pending.Add(handleName dir)
                with _ -> ()
            for h in zips do
                try
                    let! perm = queryReadPermission h
                    if perm = "granted" then
                        let! file = getFileFromHandle h
                        let! z = Zip.buildIndex file
                        reg.Zips <- Map.add z.Repo z reg.Zips
                    else
                        pending.Add(handleName h)
                with _ -> ()

            return List.ofSeq pending
    }

/// Names of everything remembered, whether or not it currently needs a click —
/// what the Reconnect button offers to re-grant.
let rememberedNames () : string list =
    Array.append (reg.RememberedDirs |> Array.map handleName) (reg.RememberedZips |> Array.map handleName)
    |> List.ofArray
    |> List.distinct

/// Requests permission (via the one-click prompt) for whichever remembered
/// handles `reconnectRemembered` flagged as needing it.
let confirmReconnect (): JS.Promise<unit> =
    promise {
        for dir in reg.RememberedDirs do
            try
                let! perm = requestReadPermission dir
                if perm = "granted" then
                    let! foundJs = findReposJs dir
                    let found = extractFound foundJs
                    reg.Handles <- found |> List.fold (fun m (k, h) -> Map.add k h m) reg.Handles
            with _ -> ()
        for h in reg.RememberedZips do
            try
                let! perm = requestReadPermission h
                if perm = "granted" then
                    let! file = getFileFromHandle h
                    let! z = Zip.buildIndex file
                    reg.Zips <- Map.add z.Repo z reg.Zips
            with _ -> ()
    }

// ---------------------------------------------------------------------------
// fetch dispatch
// ---------------------------------------------------------------------------

/// Reads a text's XML given the current source mode (mirrors `fetchXml`):
/// local (ZIP, then a connected file list, else a directory handle, falling
/// back to GitHub if nothing local has it), a custom base URL tried across the
/// repo's candidate folder names, or GitHub directly.
///
/// `onNotice` reports the silent local→GitHub fallback, so a text missing from
/// the connected copy reads as that rather than as local reading being broken.
let fetchXml (mode: SourceMode) (baseUrl: string) (onNotice: string -> unit) (t: TextMeta) : JS.Promise<string * TextOrigin> =
    let parts = t.File.Split('.')
    let g, w = parts.[0], parts.[1]
    let rel = sprintf "data/%s/%s/%s" g w t.File

    let fromGitHub (): JS.Promise<string> =
        promise {
            let! res =
                promise {
                    try return! fetchUrl (rawUrl t)
                    with _ -> return failwith "NET"
                }
            if not (responseOk res) then
                return failwith ("HTTP " + string (responseStatus res))
            else
                return! responseText res
        }

    let rec tryFolders (folders: string list) (lastErr: string) : JS.Promise<Result<string, string>> =
        promise {
            match folders with
            | [] -> return Error lastErr
            | folder :: rest ->
                try
                    let baseTrimmed = if baseUrl = "" then "." else baseUrl
                    let! r = fetchUrl (sprintf "%s/%s/%s" baseTrimmed folder rel)
                    if responseOk r then
                        let! txt = responseText r
                        return Ok txt
                    else
                        return! tryFolders rest ("HTTP " + string (responseStatus r))
                with _ -> return! tryFolders rest "NET"
        }

    // The three local sources are alternatives to be tried in order, not
    // branches of a condition. Previously they were wired as `if zip-missed &&
    // have-a-file-list then ... else <read the directory handle>`, which meant a
    // successful ZIP read was immediately overwritten by a directory read, and a
    // connected file list that didn't happen to hold the file stopped the
    // directory handle from ever being consulted.
    let key = repoKey t.Repo

    let tryZip () : JS.Promise<(string * TextOrigin) option> =
        promise {
            match reg.Zips.TryFind key with
            | Some z ->
                try
                    let! r = Zip.readEntry z t.File
                    return r |> Option.map (fun txt -> txt, OriginLocalZip z.Name)
                with _ -> return None
            | None -> return None
        }

    let tryFileList () : JS.Promise<(string * TextOrigin) option> =
        promise {
            match reg.Files |> Option.bind (fun files -> files.TryFind t.File) with
            | Some f ->
                try
                    let! txt = f.text ()
                    return Some(txt, OriginLocalFiles)
                with _ -> return None
            | None -> return None
        }

    let tryDirHandle () : JS.Promise<(string * TextOrigin) option> =
        promise {
            match reg.Handles.TryFind key with
            | Some h ->
                let! r = readFromDirHandle h g w t.File
                if isNullOrUndefined r then return None else return Some(r, OriginLocalFolder(handleName h))
            | None -> return None
        }

    let rec firstHit (steps: (unit -> JS.Promise<(string * TextOrigin) option>) list) : JS.Promise<(string * TextOrigin) option> =
        promise {
            match steps with
            | [] -> return None
            | step :: rest ->
                let! hit = step ()
                match hit with
                | Some _ -> return hit
                | None -> return! firstHit rest
        }

    promise {
        match mode with
        | SourceLocal ->
            let! local = firstHit [ tryZip; tryFileList; tryDirHandle ]
            match local with
            | Some(txt, origin) -> return txt, origin
            | None ->
                onNotice (sprintf "Not in your local copy — loading %s from GitHub" t.File)
                let! txt = fromGitHub ()
                return txt, OriginGitHub
        | SourceUrl ->
            let folders = repoFolders (repoKey t.Repo)
            let! outcome = tryFolders folders ""
            match outcome with
            | Ok txt -> return txt, OriginUrl(if baseUrl = "" then "." else baseUrl)
            | Error lastErr ->
                if lastErr = "NET" then
                    return failwith "NET"
                else
                    let baseTrimmed = if baseUrl = "" then "." else baseUrl
                    let triedFolders = String.concat ", " folders
                    return failwith (sprintf "Not found under %s (tried %s): %s" baseTrimmed triedFolders lastErr)
        | SourceGitHub ->
            let! txt = fromGitHub ()
            return txt, OriginGitHub
    }
