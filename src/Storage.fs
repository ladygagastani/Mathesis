module Storage

open Fable.Core
open Fable.Core.JsInterop
open Browser.WebStorage
open Thoth.Json
open Types

let private prefixed (key: string) = "anag:" + key

// ---------------------------------------------------------------------------
// generic localStorage helpers (mirrors the original `store.get`/`store.set`)
// ---------------------------------------------------------------------------

let private tryGetRaw (key: string) : string option =
    try
        match localStorage.getItem (prefixed key) with
        | null -> None
        | v -> Some v
    with _ -> None

let private setRaw (key: string) (raw: string) : unit =
    try localStorage.setItem (prefixed key, raw)
    with _ -> ()

let private getJson (key: string) (decoder: Decoder<'T>) (fallback: 'T) : 'T =
    match tryGetRaw key with
    | None -> fallback
    | Some raw ->
        match Decode.fromString decoder raw with
        | Ok v -> v
        | Error _ -> fallback

let private setJson (key: string) (encoder: 'T -> JsonValue) (value: 'T) : unit =
    setRaw key (Encode.toString 0 (encoder value))

// ---------------------------------------------------------------------------
// enum-ish codecs (string tokens match the original app exactly, so existing
// users' localStorage keeps working)
// ---------------------------------------------------------------------------

let private encodeColumnMode (m: ColumnMode) =
    Encode.string (
        match m with
        | Both -> "both"
        | GrcOnly -> "grc"
        | EngOnly -> "eng")

let private decodeColumnMode (fallback: ColumnMode) : Decoder<ColumnMode> =
    Decode.string
    |> Decode.map (function
        | "grc" -> GrcOnly
        | "eng" -> EngOnly
        | "both" -> Both
        | _ -> fallback)

let private encodeTypeface =
    function
    | Serif -> Encode.string "serif"
    | Sans -> Encode.string "sans"

let private decodeTypeface : Decoder<Typeface> =
    Decode.string
    |> Decode.map (function
        | "sans" -> Sans
        | _ -> Serif)

let private encodeThemePref =
    function
    | ThemeAuto -> Encode.string "auto"
    | ThemeLight -> Encode.string "light"
    | ThemeDark -> Encode.string "dark"

let private decodeThemePref : Decoder<ThemePref> =
    Decode.string
    |> Decode.map (function
        | "light" -> ThemeLight
        | "dark" -> ThemeDark
        | _ -> ThemeAuto)

let private encodeWorksFilter =
    function
    | FilterAll -> Encode.string "all"
    | FilterTranslated -> Encode.string "trans"
    | FilterGreekOnly -> Encode.string "grc"

let private decodeWorksFilter : Decoder<WorksFilter> =
    Decode.string
    |> Decode.map (function
        | "trans" -> FilterTranslated
        | "grc" -> FilterGreekOnly
        | _ -> FilterAll)

let private encodeSourceMode =
    function
    | SourceGitHub -> Encode.string "github"
    | SourceLocal -> Encode.string "local"
    | SourceUrl -> Encode.string "url"

let private decodeSourceMode : Decoder<SourceMode> =
    Decode.string
    |> Decode.map (function
        | "local" -> SourceLocal
        | "url" -> SourceUrl
        | _ -> SourceGitHub)

// ---------------------------------------------------------------------------
// Settings — each field lives under its own top-level key, as the original did
// ---------------------------------------------------------------------------

/// Polytonic Greek stacks up to three things over one vowel (breathing, accent,
/// and — under it — an iota subscript), so it needs more leading and more
/// vertical room than Latin at the same nominal size. Hence 1.72 rather than
/// the 1.65 a Latin-only reader would want, and the floors below.
let minFontSize = 1.0    // under ~16px Cardo's breathings and circumflexes mush
let maxFontSize = 1.8

/// The size the reader's Greek is set at by default, and the reference point the
/// text-size control scales everything from: at this value the site renders at
/// the browser's own base size, and a step up or down scales the whole page —
/// chrome, wiki and library included — by the same ratio as the Greek.
let baseFontSize = 1.18
let minLineHeight = 1.45 // under ~1.4 accents collide with descenders above
let maxLineHeight = 2.2

let defaultSettings =
    { Mode = Both; Face = Serif; FontSize = baseFontSize; LineHeight = 1.72; Theme = ThemeAuto }

let loadSettings () : Settings =
    { Mode = getJson "mode" (decodeColumnMode Both) defaultSettings.Mode
      Face = getJson "face" decodeTypeface defaultSettings.Face
      // Clamp on load as well as on change: a value saved under the old, looser
      // floors would otherwise persist below what polytonic can carry.
      FontSize = getJson "fs" Decode.float defaultSettings.FontSize |> max minFontSize |> min maxFontSize
      LineHeight = getJson "lh" Decode.float defaultSettings.LineHeight |> max minLineHeight |> min maxLineHeight
      Theme = getJson "theme" decodeThemePref defaultSettings.Theme }

let saveMode (v: ColumnMode) = setJson "mode" encodeColumnMode v
let saveFace (v: Typeface) = setJson "face" encodeTypeface v
let saveFontSize (v: float) = setJson "fs" Encode.float v
let saveLineHeight (v: float) = setJson "lh" Encode.float v
let saveTheme (v: ThemePref) = setJson "theme" encodeThemePref v

// ---------------------------------------------------------------------------
// misc top-level flags
// ---------------------------------------------------------------------------

let loadFilter () : WorksFilter = getJson "filter" decodeWorksFilter FilterAll
let saveFilter (v: WorksFilter) = setJson "filter" encodeWorksFilter v

let loadNavHidden () : bool = getJson "navHidden" Decode.bool false
let saveNavHidden (v: bool) = setJson "navHidden" Encode.bool v

/// The reader keeps its own sidebar state, defaulting to collapsed: an 18rem
/// tree of 1,800 works beside a poem is a distraction, and reading is the one
/// place where the chrome should get out of the way. Once the reader opens it
/// there, that choice sticks and this returns false from then on.
let loadNavHiddenReader () : bool = getJson "navHiddenReader" Decode.bool true
let saveNavHiddenReader (v: bool) = setJson "navHiddenReader" Encode.bool v

let loadSourceMode () : SourceMode = getJson "src" decodeSourceMode SourceGitHub
let saveSourceMode (v: SourceMode) = setJson "src" encodeSourceMode v

let loadSourceBaseUrl () : string = getJson "srcBase" Decode.string ""
let saveSourceBaseUrl (v: string) = setJson "srcBase" Encode.string v

// ---------------------------------------------------------------------------
// recent (max 6, enforced by the caller)
// ---------------------------------------------------------------------------

let private encodeRecentEntry (e: RecentEntry) =
    Encode.object [
        "id", Encode.string e.Id
        "ts", Encode.float e.Ts
        "chunk", (match e.Chunk with Some c -> Encode.string c | None -> Encode.nil)
    ]

let private decodeRecentEntry : Decoder<RecentEntry> =
    Decode.object (fun get ->
        { Id = get.Required.Field "id" Decode.string
          Ts = get.Required.Field "ts" Decode.float
          Chunk = get.Optional.Field "chunk" Decode.string })

let loadRecent () : RecentEntry list =
    getJson "recent" (Decode.list decodeRecentEntry) []

let saveRecent (v: RecentEntry list) =
    setJson "recent" (Encode.list << List.map encodeRecentEntry) v

// ---------------------------------------------------------------------------
// collapsed section state (data-col on phones)
// ---------------------------------------------------------------------------

let loadCollapsed () : Map<string, bool> =
    getJson "colState" (Decode.keyValuePairs Decode.bool |> Decode.map Map.ofList) Map.empty

let saveCollapsed (v: Map<string, bool>) =
    setJson "colState" (Map.toList >> List.map (fun (k, b) -> k, Encode.bool b) >> Encode.object) v

// ---------------------------------------------------------------------------
// study lenses: the IIIF manifest last opened for each work, and whether
// scansion marks are showing over verse
// ---------------------------------------------------------------------------

let loadManifests () : Map<string, string> =
    getJson "iiif" (Decode.keyValuePairs Decode.string |> Decode.map Map.ofList) Map.empty

let saveManifestFor (workId: string) (url: string) =
    let m = loadManifests () |> Map.add workId url
    setJson "iiif" (Map.toList >> List.map (fun (k, u) -> k, Encode.string u) >> Encode.object) m

let loadMeterOn () : bool = getJson "meter" Decode.bool false
let saveMeterOn (v: bool) = setJson "meter" Encode.bool v

// ---------------------------------------------------------------------------
// Learn progress
// ---------------------------------------------------------------------------

let learnDefault: LearnProgress = { Onboarded = false; Pace = 1; Step = 0; AlphaDone = false }

let loadLearn () : LearnProgress =
    let decoder =
        Decode.object (fun get ->
            { Onboarded = get.Optional.Field "onboarded" Decode.bool |> Option.defaultValue false
              Pace = get.Optional.Field "pace" Decode.int |> Option.defaultValue 1 |> max 0 |> min 2
              Step = get.Optional.Field "step" Decode.int |> Option.defaultValue 0 |> max 0 |> min 6
              AlphaDone = get.Optional.Field "alpha" Decode.bool |> Option.defaultValue false })
    getJson "learn" decoder learnDefault

let saveLearn (p: LearnProgress) =
    setJson "learn" (fun (p: LearnProgress) ->
        Encode.object [
            "onboarded", Encode.bool p.Onboarded
            "pace", Encode.int p.Pace
            "step", Encode.int p.Step
            "alpha", Encode.bool p.AlphaDone
        ]) p

// ---------------------------------------------------------------------------
// library (favourites, bookmarks/marks, per-author notes)
// ---------------------------------------------------------------------------

let private encodeMarkLink (l: MarkLink) =
    Encode.object [
        "work", Encode.string l.Work
        "ref", Encode.string l.Ref
        "label", (match l.Label with Some s -> Encode.string s | None -> Encode.nil)
    ]

let private decodeMarkLink : Decoder<MarkLink> =
    Decode.object (fun get ->
        { Work = get.Required.Field "work" Decode.string
          Ref = get.Required.Field "ref" Decode.string
          Label = get.Optional.Field "label" Decode.string })

let private encodeMark (m: Mark) =
    Encode.object [
        "id", Encode.string m.Id
        "work", Encode.string m.Work
        "ref", Encode.string m.Ref
        "label", Encode.string m.Label
        "snippet", Encode.string m.Snippet
        "note", Encode.string m.Note
        "links", Encode.list (List.map encodeMarkLink m.Links)
        "ts", Encode.float m.Ts
    ]

let private decodeMark : Decoder<Mark> =
    Decode.object (fun get ->
        { Id = get.Required.Field "id" Decode.string
          Work = get.Required.Field "work" Decode.string
          Ref = get.Required.Field "ref" Decode.string
          Label = get.Optional.Field "label" Decode.string |> Option.defaultValue ""
          Snippet = get.Optional.Field "snippet" Decode.string |> Option.defaultValue ""
          Note = get.Optional.Field "note" Decode.string |> Option.defaultValue ""
          Links = get.Optional.Field "links" (Decode.list decodeMarkLink) |> Option.defaultValue []
          Ts = get.Required.Field "ts" Decode.float })

let private encodeLibrary (lib: Library) =
    Encode.object [
        "favs", Encode.list (List.map Encode.string lib.Favs)
        "marks", Encode.list (List.map encodeMark lib.Marks)
        "anotes", lib.AuthorNotes |> Map.toList |> List.map (fun (k, v) -> k, Encode.string v) |> Encode.object
    ]

let private decodeLibrary : Decoder<Library> =
    Decode.object (fun get ->
        { Favs = get.Optional.Field "favs" (Decode.list Decode.string) |> Option.defaultValue []
          Marks = get.Optional.Field "marks" (Decode.list decodeMark) |> Option.defaultValue []
          AuthorNotes =
            get.Optional.Field "anotes" (Decode.keyValuePairs Decode.string)
            |> Option.map Map.ofList
            |> Option.defaultValue Map.empty })

let defaultLibrary : Library = { Favs = []; Marks = []; AuthorNotes = Map.empty }

let loadLibrary () : Library = getJson "lib" decodeLibrary defaultLibrary
let saveLibrary (v: Library) = setJson "lib" encodeLibrary v

// ---------------------------------------------------------------------------
// IndexedDB — db "anag", store "kv" (remembered local-folder / ZIP handles)
// ---------------------------------------------------------------------------

// The bodies below run inside `onsuccess`, i.e. a DOM event callback rather
// than the Promise executor, so a *synchronous* throw in there (a missing 'kv'
// store makes `transaction()` throw NotFoundError; `put` throws DataCloneError
// on anything unstructured-cloneable) escapes as an uncaught error and leaves
// the promise pending for ever — hanging whatever awaited it. Each one is
// wrapped so those failures reject instead.

/// Reads a value by key from the "kv" store; resolves `null` if absent.
[<Emit("""
new Promise((resolve, reject) => {
    const req = indexedDB.open('anag', 1);
    req.onupgradeneeded = () => { if (!req.result.objectStoreNames.contains('kv')) req.result.createObjectStore('kv'); };
    req.onsuccess = () => {
        try {
            const db = req.result;
            const tx = db.transaction('kv', 'readonly');
            const getReq = tx.objectStore('kv').get($0);
            getReq.onsuccess = () => resolve(getReq.result === undefined ? null : getReq.result);
            getReq.onerror = () => reject(getReq.error);
            tx.oncomplete = () => db.close();
        } catch (e) { reject(e); }
    };
    req.onerror = () => reject(req.error);
})
""")>]
let idbGet (key: string) : JS.Promise<obj> = jsNative

/// Writes (or overwrites) a value by key in the "kv" store.
[<Emit("""
new Promise((resolve, reject) => {
    const req = indexedDB.open('anag', 1);
    req.onupgradeneeded = () => { if (!req.result.objectStoreNames.contains('kv')) req.result.createObjectStore('kv'); };
    req.onsuccess = () => {
        try {
            const db = req.result;
            const tx = db.transaction('kv', 'readwrite');
            tx.objectStore('kv').put($1, $0);
            tx.oncomplete = () => { db.close(); resolve(undefined); };
            tx.onerror = () => reject(tx.error);
        } catch (e) { reject(e); }
    };
    req.onerror = () => reject(req.error);
})
""")>]
let idbSet (key: string) (value: obj) : JS.Promise<unit> = jsNative

/// Removes a key from the "kv" store.
[<Emit("""
new Promise((resolve, reject) => {
    const req = indexedDB.open('anag', 1);
    req.onupgradeneeded = () => { if (!req.result.objectStoreNames.contains('kv')) req.result.createObjectStore('kv'); };
    req.onsuccess = () => {
        try {
            const db = req.result;
            const tx = db.transaction('kv', 'readwrite');
            tx.objectStore('kv').delete($0);
            tx.oncomplete = () => { db.close(); resolve(undefined); };
            tx.onerror = () => reject(tx.error);
        } catch (e) { reject(e); }
    };
    req.onerror = () => reject(req.error);
})
""")>]
let idbDelete (key: string) : JS.Promise<unit> = jsNative
