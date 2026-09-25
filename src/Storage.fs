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

let private encodeWord (w: WordCard) =
    Encode.object [
        "id", Encode.string w.Id
        "word", Encode.string w.Word
        "lemma", Encode.string w.Lemma
        "gloss", Encode.string w.Gloss
        "work", Encode.string w.Work
        "ref", Encode.string w.Ref
        "ctx", Encode.string w.Context
        "ts", Encode.float w.Ts
        "box", Encode.int w.Box
        "due", Encode.float w.Due
    ]

let private decodeWord : Decoder<WordCard> =
    Decode.object (fun get ->
        ({ Id = get.Required.Field "id" Decode.string
           Word = get.Required.Field "word" Decode.string
           Lemma = get.Optional.Field "lemma" Decode.string |> Option.defaultValue ""
           Gloss = get.Optional.Field "gloss" Decode.string |> Option.defaultValue ""
           Work = get.Optional.Field "work" Decode.string |> Option.defaultValue ""
           Ref = get.Optional.Field "ref" Decode.string |> Option.defaultValue ""
           Context = get.Optional.Field "ctx" Decode.string |> Option.defaultValue ""
           Ts = get.Optional.Field "ts" Decode.float |> Option.defaultValue 0.0
           Box = get.Optional.Field "box" Decode.int |> Option.defaultValue 0
           Due = get.Optional.Field "due" Decode.float |> Option.defaultValue 0.0 }
        : WordCard))

let private encodePlace (p: SavedPlace) =
    Encode.object [
        "qid", Encode.string p.Qid
        "label", Encode.string p.Label
        "pleiades", Encode.string p.Pleiades
        "lat", Encode.float p.Lat
        "lon", Encode.float p.Lon
        "seen", p.Seen |> List.map (fun (w, r) -> Encode.list [ Encode.string w; Encode.string r ]) |> Encode.list
        "note", Encode.string p.Note
        "ts", Encode.float p.Ts
    ]

let private decodeSeen : Decoder<string * string> =
    Decode.list Decode.string
    |> Decode.andThen (function
        | [ w; r ] -> Decode.succeed (w, r)
        | _ -> Decode.fail "expected [work, ref]")

let private decodePlace : Decoder<SavedPlace> =
    Decode.object (fun get ->
        { Qid = get.Required.Field "qid" Decode.string
          Label = get.Optional.Field "label" Decode.string |> Option.defaultValue ""
          Pleiades = get.Optional.Field "pleiades" Decode.string |> Option.defaultValue ""
          Lat = get.Required.Field "lat" Decode.float
          Lon = get.Required.Field "lon" Decode.float
          Seen = get.Optional.Field "seen" (Decode.list decodeSeen) |> Option.defaultValue []
          Note = get.Optional.Field "note" Decode.string |> Option.defaultValue ""
          Ts = get.Optional.Field "ts" Decode.float |> Option.defaultValue 0.0 })

/// The one JSON shape of a library: in localStorage, in exports, and on the
/// sync server. Fields added later are optional when read, so older saves load.
let encodeLibrary (lib: Library) =
    Encode.object [
        "favs", Encode.list (List.map Encode.string lib.Favs)
        "marks", Encode.list (List.map encodeMark lib.Marks)
        "anotes", lib.AuthorNotes |> Map.toList |> List.map (fun (k, v) -> k, Encode.string v) |> Encode.object
        "words", Encode.list (List.map encodeWord lib.Words)
        "places", Encode.list (List.map encodePlace lib.Places)
        "stamps", lib.Stamps |> Map.toList |> List.map (fun (k, v) -> k, Encode.float v) |> Encode.object
    ]

let decodeLibrary : Decoder<Library> =
    Decode.object (fun get ->
        { Favs = get.Optional.Field "favs" (Decode.list Decode.string) |> Option.defaultValue []
          Marks = get.Optional.Field "marks" (Decode.list decodeMark) |> Option.defaultValue []
          AuthorNotes =
            get.Optional.Field "anotes" (Decode.keyValuePairs Decode.string)
            |> Option.map Map.ofList
            |> Option.defaultValue Map.empty
          Words = get.Optional.Field "words" (Decode.list decodeWord) |> Option.defaultValue []
          Places = get.Optional.Field "places" (Decode.list decodePlace) |> Option.defaultValue []
          Stamps =
            get.Optional.Field "stamps" (Decode.keyValuePairs Decode.float)
            |> Option.map Map.ofList
            |> Option.defaultValue Map.empty })

let libraryToJson (lib: Library) : string = Encode.toString 0 (encodeLibrary lib)

let defaultLibrary : Library =
    { Favs = []; Marks = []; AuthorNotes = Map.empty; Words = []; Places = []; Stamps = Map.empty }

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

// ---------------------------------------------------------------------------
// account session (see Account.fs). Kept in localStorage so a reload stays
// signed in; the refresh token renews the short-lived access token.
// ---------------------------------------------------------------------------

let private encodeSession (s: Session) =
    Encode.object [
        "access", Encode.string s.AccessToken
        "refresh", Encode.string s.RefreshToken
        "exp", Encode.float s.ExpiresAt
        "uid", Encode.string s.UserId
        "email", Encode.string s.Email
    ]

let private decodeSession : Decoder<Session> =
    Decode.object (fun get ->
        { AccessToken = get.Required.Field "access" Decode.string
          RefreshToken = get.Required.Field "refresh" Decode.string
          ExpiresAt = get.Required.Field "exp" Decode.float
          UserId = get.Required.Field "uid" Decode.string
          Email = get.Optional.Field "email" Decode.string |> Option.defaultValue "" })

/// Whose library this browser holds: the account it last synced with. A
/// different account signing in replaces it rather than merging into it.
let loadLibOwner () : string = getJson "libOwner" Decode.string ""
let saveLibOwner (uid: string) = setJson "libOwner" Encode.string uid

let loadSession () : Session option = getJson "session" (Decode.option decodeSession) None
let saveSession (s: Session option) =
    match s with
    | Some v -> setJson "session" encodeSession v
    | None -> (try localStorage.removeItem (prefixed "session") with _ -> ())

/// Searches a result was chosen for, newest first (the search box's "Recent").
let loadSearches () : string list = getJson "searches" (Decode.list Decode.string) []
let saveSearches (v: string list) = setJson "searches" (fun (xs: string list) -> Encode.list (List.map Encode.string xs)) v
