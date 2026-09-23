module State

open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Types

// ---------------------------------------------------------------------------
// small local DOM/interop helpers
// ---------------------------------------------------------------------------

[<Emit("document.title = $0")>]
let private setDocumentTitle (title: string) : unit = jsNative

[<Emit("document.body.classList.add($0)")>]
let private addBodyClass (cls: string) : unit = jsNative

[<Emit("document.body.classList.remove($0)")>]
let private removeBodyClass (cls: string) : unit = jsNative

[<Emit("window.scrollTo(0,0)")>]
let private scrollToTop () : unit = jsNative

[<Emit("document.getElementById($0)?.focus()")>]
let private focusElementById (id: string) : unit = jsNative

[<Emit("matchMedia('(prefers-color-scheme: dark)').matches")>]
let private prefersDarkColorScheme () : bool = jsNative

[<Emit("document.documentElement.style.setProperty($0, $1)")>]
let private setDocStyleProp (name: string) (value: string) : unit = jsNative

[<Emit("document.documentElement.style.removeProperty($0)")>]
let private removeDocStyleProp (name: string) : unit = jsNative

[<Emit("document.documentElement.classList.toggle($0, $1)")>]
let private toggleDocClass (cls: string) (on: bool) : unit = jsNative

[<Emit("document.documentElement.removeAttribute('data-theme')")>]
let private removeDocThemeAttr () : unit = jsNative

[<Emit("document.documentElement.setAttribute('data-theme', $0)")>]
let private setDocThemeAttr (v: string) : unit = jsNative

[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private clipboardWriteText (text: string) : JS.Promise<unit> option = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeoutMs (f: unit -> unit) (ms: int) : unit = jsNative

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

// The CSS viewport, excluding any scrollbar — the box `left`/`top` resolve
// against, so clamping matches what is actually on screen.
[<Emit("document.documentElement.clientWidth")>]
let private viewportWidth () : float = jsNative

[<Emit("document.documentElement.clientHeight")>]
let private viewportHeight () : float = jsNative

/// How long a message about where texts are coming from stays up. These say
/// something the reader needs to act on — which copy is connected, or that a
/// text quietly came from GitHub instead — so they outlast the couple of
/// seconds a "Copied" confirmation gets, and carry a close button besides.
let private sourceNoticeMs = 10000

/// Roughly how wide each draggable is, used only to decide how far off an edge
/// it may go before it would be out of reach.
let private notesPanelWidth = 320.0
let private notesButtonWidth = 48.0

/// Keeps at least a grabbable strip of a dragged thing on screen, whichever
/// way it is flung.
let private clampToViewport (width: float) (x: float) (y: float) : float * float =
    let edge = 40.0
    let left = x |> max (edge - width) |> min (viewportWidth () - edge)
    let top = y |> max 0.0 |> min (viewportHeight () - edge)
    left, top

let private dragUpdate (width: float) (phase: string) (x: float) (y: float) (d: Draggable) : Draggable =
    match phase with
    | "grab" -> { d with Dragging = true }
    | "move" ->
        let left, top = clampToViewport width x y
        { d with X = Some left; Y = Some top; Dragging = true }
    | _ -> { d with Dragging = false }

// Elmish runs effects before React paints the update that caused them, so a
// passage in a work that has only just been opened isn't in the DOM yet. Try
// again briefly until it is, rather than silently leaving the reader at the top.
[<Emit("""(() => { const __ref = $0; let __n = 0; const go = () => { const el = document.getElementById('s-' + __ref); if (el) { el.scrollIntoView({block:'start'}); el.classList.add('flash'); } else if (__n++ < 40) setTimeout(go, 50); }; go(); })()""")>]
let private scrollToSegAndFlash (segRef: string) : unit = jsNative

/// The last dot-separated component of a CTS URN (e.g. "perseus-grc2" from
/// "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2") — used to build the short
/// route/hash segments, mirroring the original's `urn.split('.').pop()`. This
/// is distinct from `Views.Reader`'s `urnSuffix` (colon-split), which is used
/// for on-page *display* of the full local name and must not be reused here.
let private urnHashSegment (urn: string) : string = urn.Split('.') |> Array.last

/// Rebuilds a full CTS URN from a route's short edition segment. "none" is a
/// sentinel meaning *explicitly no translation*, not an absent choice, so it is
/// passed through rather than collapsed to `None` — otherwise reloading a
/// Greek-only URL would silently pick a translation back up.
let private fullUrn (workId: string) (suffix: string) : string option =
    if suffix = "" then None
    elif suffix = "none" then Some "none"
    else Some(sprintf "urn:cts:greekLit:%s.%s" workId suffix)

let private collapsedByDefault (key: string) : bool =
    match key with
    | "paths"
    | "eras"
    | "alphabet"
    | "tips"
    | "howto" -> true
    | _ -> false

// ---------------------------------------------------------------------------
// text loading pipeline
// ---------------------------------------------------------------------------

let private PAGE_SIZE = 250

let private langNames =
    Map.ofList [
        "grc", "Greek"; "eng", "English"; "lat", "Latin"; "ger", "German"; "fre", "French"
        "ita", "Italian"; "mul", "Multilingual"; "ara", "Arabic"; "cop", "Coptic"
    ]

let private langNameOf (l: string) = langNames.TryFind l |> Option.defaultValue l

let private fmtKbOf (kb: int) : string =
    if kb >= 1024 then (float kb / 1024.0).ToString("F1") + " MB" else string kb + " KB"

let private classifyError (msg: string) : LoadError =
    if msg = "NET" then
        NetworkBlocked
    elif msg.StartsWith "HTTP " then
        match System.Int32.TryParse(msg.Substring 5) with
        | true, code -> HttpStatus code
        | false, _ -> ErrorMessage msg
    else
        ErrorMessage msg

let private loadTextCmd
    (token: int)
    (mode: SourceMode)
    (baseUrl: string)
    (cache: Map<string, RawSegment list>)
    (origins: Map<string, TextOrigin>)
    (t: TextMeta)
    (onLoaded: RawSegment list -> TextOrigin -> Msg)
    (onFailed: LoadError -> Msg)
    : Cmd<Msg> =
    match cache.TryFind t.Urn with
    // A cache hit still knows where it came from, so re-opening a text doesn't
    // quietly lose the "read from your disk" claim it was loaded under.
    | Some segs -> Cmd.ofMsg (onLoaded segs (origins.TryFind t.Urn |> Option.defaultValue OriginGitHub))
    | None ->
        Cmd.ofEffect (fun dispatch ->
            promise {
                dispatch (
                    Reader_(
                        LoadStatus(
                            token,
                            sprintf "%s %s text (%s)…" (if mode = SourceGitHub then "Fetching" else "Reading") (langNameOf t.Lang) (fmtKbOf t.Kb)
                        )
                    )
                )
                try
                    let! xml, origin = Sources.fetchXml mode baseUrl (fun notice -> dispatch (ShowToastFor(notice, sourceNoticeMs))) t
                    dispatch (Reader_(LoadStatus(token, sprintf "Parsing %s text…" (langNameOf t.Lang))))
                    let inlineMs = if t.Lang = "grc" then Set.singleton "page" else set [ "page"; "line" ]
                    let events = Tei.Tokenizer.tokenize xml |> Tei.Tokenizer.fixKnownTypos t.Urn
                    let segs = Tei.Segmenter.segment events inlineMs
                    dispatch (onLoaded segs origin)
                with ex ->
                    dispatch (onFailed (classifyError ex.Message))
            }
            |> Promise.start)

// ---------------------------------------------------------------------------
// routing helpers shared by Navigate / HashChanged / BackClicked / Boot
// ---------------------------------------------------------------------------

let private pageTitle (model: Model) (route: Route) : string =
    let suffix = " — Μάθησις"
    match route with
    | Landing -> "Μάθησις — Ancient Greek Reader"
    | Browse -> "Browse" + suffix
    | LibraryRoute -> "My library" + suffix
    | AboutRoute -> "About" + suffix
    | AuthorRoute(id, _) ->
        (model.Catalog.Authors |> List.tryFind (fun a -> a.Id = id) |> Option.map (fun a -> a.Name) |> Option.defaultValue "Authors") + suffix
    | WikiRoute WikiHome -> "Wiki" + suffix
    | WikiRoute(WikiAuthors _) -> "Authors" + suffix
    | WikiRoute(WikiEras _) -> "Eras of Greek" + suffix
    | WikiRoute(WikiArticles Manuscripts) -> "Manuscripts & transmission" + suffix
    | WikiRoute(WikiArticles Variants) -> "Textual variants" + suffix
    | WikiRoute WikiEditions -> "Editions & translations" + suffix
    | WikiRoute(WikiGuide slug) ->
        (match GuideData.tryFind slug with
         | Some p when p.Slug <> "" -> p.Title + " — Start here"
         | _ -> "Start here")
        + suffix
    | ReaderRoute(id, _, _, _, _) -> (model.Catalog.WorkById.TryFind id |> Option.map (fun w -> w.Title) |> Option.defaultValue "") + suffix

let private leaveReaderEffect (title: string) : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        setDocumentTitle title
        removeBodyClass "reading"
        scrollToTop ())

/// Resolves whatever a route needs once it becomes current: for a reader route,
/// kicks off `OpenWork` (unknown work ids fall back to Landing, mirroring the
/// original `route()`'s `if(!w) return landing()`); for any other route, clears
/// the reader and applies the page's title/body-class (mirrors `APP.leaveReader`).
let private loadForRoute (model: Model) (route: Route) : Model * Cmd<Msg> =
    match route with
    | ReaderRoute(id, grcSuffix, engSuffix, chunk, seg) ->
        match model.Catalog.WorkById.TryFind id with
        | Some _ -> model, Cmd.ofMsg (Reader_(OpenWork(id, fullUrn id grcSuffix, fullUrn id engSuffix, chunk, seg)))
        | None ->
            let model2 = { model with Route = Landing; Reader = None; CurrentHash = "#" }
            model2, leaveReaderEffect (pageTitle model2 Landing)
    | _ -> { model with Reader = None }, leaveReaderEffect (pageTitle model route)

// ---------------------------------------------------------------------------
// init
// ---------------------------------------------------------------------------

let init () : Model * Cmd<Msg> =
    let hash = Router.currentHash ()
    let model =
        { Boot = Booting "Opening the catalogue…"
          Catalog = { Authors = []; WorkById = Map.empty; AuthorOfWork = Map.empty }
          Meta = { Eras = []; Authors = Map.empty }
          Route = Router.parseHash hash
          Settings = Storage.loadSettings ()
          Filter = Storage.loadFilter ()
          Library = Storage.loadLibrary ()
          Source =
            { Mode = Storage.loadSourceMode ()
              BaseUrl = Storage.loadSourceBaseUrl ()
              Local = Map.empty
              NeedsReconnect = [] }
          Reader = None
          NavHidden = Storage.loadNavHidden ()
          NavHiddenReader = Storage.loadNavHiddenReader ()
          SideOpen = false
          SettingsOpen = false
          SourceMenuOpen = false
          Notes =
            { Open = false
              Panel = { X = None; Y = None; Dragging = false }
              Button = { X = None; Y = None; Dragging = false }
              ButtonMoved = false }
          Popover = None
          Toast = None
          JumpInput = ""
          NavQuery = ""
          OpenAuthors = Set.empty
          ClosedAuthors = Set.empty
          PartsOpen = true
          Genre = None
          HomeQuery = ""
          BrowseQuery = ""
          WikiQuery = ""
          Recent = Storage.loadRecent ()
          Collapsed = Storage.loadCollapsed ()
          TextCache = Map.empty
          OriginCache = Map.empty
          History = []
          CurrentHash = hash
          DrawerDrag = None
          NextToken = 0 }
    model,
    Cmd.batch [
        Cmd.OfPromise.either Catalog.decodeEmbedded () (Ok >> Boot) (fun e -> Boot(Error e.Message))
        Cmd.OfPromise.perform Sources.reconnectRemembered () (fun names -> Source_(RememberedFound names))
    ]

// ---------------------------------------------------------------------------
// update
// ---------------------------------------------------------------------------

/// Paints the page scrollbar's track with a band at every marked passage.
///
/// Positions come from the segments' real document offsets rather than their
/// index: the scrollbar maps the whole document (heading, pager and all), not
/// just the run of passages, so index fractions would sit noticeably off. Runs
/// inside a frame so it measures the DOM React has just rendered, and keeps the
/// stops monotonic, since a gradient with overlapping stops silently collapses
/// to one flat colour.
[<Emit("""
// setTimeout, not requestAnimationFrame: rAF is throttled hard — and in a
// background tab suspended entirely — so the marks would silently never be
// painted for anyone who opened a text in a tab they weren't looking at. A
// zero timeout still lands after React has rendered, which is all this needs.
setTimeout(() => {
    // `__marks`, not `marks`: Fable pastes the caller's expression in for $0, so
    // `const marks = $0` beside an F# binding of that name emits `const marks =
    // marks` — a dead-zone ReferenceError. Same trap as `__root` in Sources.fs.
    const __marks = $0;
    const root = document.documentElement;
    const docH = root.scrollHeight;
    // Phones draw an overlay scrollbar the OS owns, which ::-webkit-scrollbar
    // cannot paint — the gutter is 0px wide there. Flag it so the stylesheet can
    // stand a strip of the same width in the same place, on the same scale.
    root.classList.toggle('overlay-scrollbars', (window.innerWidth - root.clientWidth) < 1);
    if (!__marks.length || docH <= window.innerHeight) {
        root.style.setProperty('--sb-marks', 'none');
        return;
    }
    const stops = [];
    let cursor = 0;
    for (const m of __marks) {
        const el = document.querySelector('.seg[data-ref="' + (window.CSS && CSS.escape ? CSS.escape(m[0]) : m[0]) + '"]');
        if (!el) continue;
        const centre = (el.offsetTop + el.offsetHeight / 2) / docH * 100;
        const half = m[1] ? 0.85 : 0.6;
        const a = Math.max(cursor, centre - half);
        const b = Math.min(100, Math.max(a + 0.35, centre + half));
        if (a >= 100) break;
        if (a > cursor) stops.push('transparent ' + cursor.toFixed(3) + '% ' + a.toFixed(3) + '%');
        stops.push((m[1] ? 'var(--sb-mark)' : 'var(--sb-mark-soft)') + ' ' + a.toFixed(3) + '% ' + b.toFixed(3) + '%');
        cursor = b;
    }
    if (!stops.length) { root.style.setProperty('--sb-marks', 'none'); return; }
    if (cursor < 100) stops.push('transparent ' + cursor.toFixed(3) + '% 100%');
    root.style.setProperty('--sb-marks', 'linear-gradient(to bottom,' + stops.join(',') + ')');
}, 0);
""")>]
let private paintScrollMarks (marks: (string * bool) array) : unit = jsNative

/// The marked passages of the page currently on screen, in document order.
let private scrollMarksEffect (model: Model) : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        let marks =
            match model.Reader with
            | Some rm ->
                match rm.Data with
                | Some d ->
                    let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = rm.Chunk) |> Option.defaultValue (List.head d.Chunks)
                    chunk.Segments
                    |> Array.skip (min (rm.Page * PAGE_SIZE) chunk.Segments.Length)
                    |> Array.truncate PAGE_SIZE
                    |> Array.choose (fun seg ->
                        LibraryData.markFor model.Library rm.Work.Id seg.Ref
                        |> Option.map (fun m -> seg.Ref, m.Note <> ""))
                | None -> [||]
            | None -> [||]
        paintScrollMarks marks)

let private saveLib (model: Model) (lib: Library) : Model * Cmd<Msg> =
    // Repaint the scrollbar here rather than at each of the dozen places marks
    // are added, edited or removed — they all funnel through this one save.
    let model = { model with Library = lib }
    model, Cmd.batch [ Cmd.ofEffect (fun _ -> Storage.saveLibrary lib); scrollMarksEffect model ]

let private thousands (n: int) : string = n.ToString("N0")

let private texts (n: int) : string = thousands n + (if n = 1 then " text" else " texts")

/// Shared tail of every successful local connection (ZIP, folder handle or
/// file list). Connecting is itself the instruction to read locally, so it
/// switches the source mode over and persists it — otherwise texts would keep
/// coming from GitHub and the connection would look like it did nothing. The
/// text cache is dropped at the same time so anything already fetched over the
/// network is re-read from the local copy.
let private connectedLocally (model: Model) (local: Map<RepoKey, LocalKind>) (message: string) : Model * Cmd<Msg> =
    { model with
        Source = { model.Source with Local = local; Mode = SourceLocal; NeedsReconnect = [] }
        TextCache = Map.empty
        OriginCache = Map.empty
        // Connecting was the point of the menu; the toast confirms the result.
        SourceMenuOpen = false },
    Cmd.batch [ Cmd.ofEffect (fun _ -> Storage.saveSourceMode SourceLocal); Cmd.ofMsg (ShowToastFor(message, sourceNoticeMs)) ]

let private applySettingsEffect (s: Settings) : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        // Text size scales the root, not just the reader. Nearly every length in
        // the stylesheet is in rem, so one percentage carries the setting to the
        // chrome, the wiki, the library and the settings sheet as well — and the
        // reader comes out byte-identical to when `--fs` drove it alone, because
        // `.col` is `--fs` rem (1.18) against a root scaled by fs/1.18.
        //
        // A percentage rather than a pixel size so it compounds with whatever
        // base size the reader has set in their browser, instead of overriding
        // it — someone browsing at 20px still gets their 20px as "100%".
        setDocStyleProp "font-size" (sprintf "%.3f%%" (100.0 * s.FontSize / Storage.baseFontSize))
        // `--fs` is now the reader's fixed reference (see :root), so any value
        // left inline by an earlier build must go or it would scale twice.
        removeDocStyleProp "--fs"
        // Past about a sixth larger there is no longer room in the header for
        // everything at once, so the parts that are decoration or duplicated by
        // an icon step aside — the same ones the narrow-window rules drop.
        toggleDocClass "ui-compact" (s.FontSize > Storage.baseFontSize * 1.16)
        setDocStyleProp "--lh" (string s.LineHeight)
        // `--body` drives the translation column only (`.col`; `.col.grc` always
        // overrides to Cardo). The serif setting gives it Source Serif 4 rather
        // than Cardo, so the two columns never share a face.
        setDocStyleProp "--body" (if s.Face = Sans then "var(--sans)" else "var(--serif-eng)")
        (match s.Theme with
         | ThemeAuto -> removeDocThemeAttr ()
         | ThemeLight -> setDocThemeAttr "light"
         | ThemeDark -> setDocThemeAttr "dark")
        Storage.saveMode s.Mode
        Storage.saveFace s.Face
        Storage.saveFontSize s.FontSize
        Storage.saveLineHeight s.LineHeight
        Storage.saveTheme s.Theme)

/// Pure port of `gotoRef`: exact-ref match, a numbered verse-line within a
/// segment's line range, a segment whose ref extends the query, an exact
/// chunk ref, or (last resort) any segment ending in `.<query>`.
///
/// A passage that actually holds the line wins over one whose first-to-last
/// range merely spans it: a passage with lines out of order (a transposed or
/// misplaced line) has a range far wider than what it holds, and would
/// otherwise swallow every reference after it.
let private gotoRefResolve (d: AlignedText) (vRaw: string) : (string * string option) option =
    let v =
        System.Text.RegularExpressions.Regex.Replace(vRaw, @"\s+", "").Replace("–", "-")
        |> fun s -> s.Split('-').[0]
    let parts = v.Split('.')
    let parseNum (s: string) =
        match System.Double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture) with
        | true, n -> Some n
        | false, _ -> None
    let wanted = parseNum parts.[parts.Length - 1]
    let want = parts |> Array.truncate (max 0 (parts.Length - 1)) |> String.concat "."
    /// The segment's numbered verse lines, when its key sits under the queried prefix.
    let linesUnderPrefix (s: Segment) : float list =
        let pre = s.Key |> List.truncate (max 0 (s.Key.Length - 1)) |> String.concat "."
        if pre <> want then []
        else
            s.Grc
            |> List.collect (function
                | Verse(_, lines) -> lines
                | _ -> [])
            |> List.choose fst
            |> List.choose parseNum
    let holdsLine (s: Segment) : bool =
        match wanted with
        | Some n -> linesUnderPrefix s |> List.contains n
        | None -> false
    let spansLine (s: Segment) : bool =
        match wanted, linesUnderPrefix s with
        | Some n, (_ :: _ as ls) -> n >= List.head ls && n <= List.last ls
        | _ -> false
    let pick (test: Segment -> bool) =
        d.Chunks
        |> List.tryPick (fun c -> c.Segments |> Array.tryFind test |> Option.map (fun s -> c.Ref, Some s.Ref))
    let byChunkSeg =
        pick (fun s -> s.Ref = v || holdsLine s)
        |> Option.orElseWith (fun () ->
            d.Chunks
            |> List.tryPick (fun c ->
                match c.Segments |> Array.tryFind spansLine with
                | Some s -> Some(c.Ref, Some s.Ref)
                | None ->
                    match c.Segments |> Array.tryFind (fun s -> s.Ref.StartsWith(v + ".")) with
                    | Some s -> Some(c.Ref, Some s.Ref)
                    | None -> None))
    match byChunkSeg with
    | Some x -> Some x
    | None ->
        match d.Chunks |> List.tryFind (fun c -> c.Ref = v) with
        | Some c -> Some(c.Ref, None)
        | None ->
            d.Chunks
            |> List.tryPick (fun c ->
                c.Segments
                |> Array.tryFind (fun s -> s.Ref.EndsWith("." + v) || (v.Contains "." && s.Ref = (v.Split('.') |> Array.last)))
                |> Option.map (fun s -> c.Ref, Some s.Ref))

// ---------------------------------------------------------------------------
// study lenses: data the rail needs that has to be fetched or drawn
// ---------------------------------------------------------------------------

let private currentChunk (rm: ReaderModel) : Chunk option =
    rm.Data |> Option.map (fun d -> d.Chunks |> List.tryFind (fun c -> Some c.Ref = rm.Chunk) |> Option.defaultValue d.Chunks.Head)

/// The verse lines of a segment, in order, as printed.
let private verseLinesOf (seg: Segment) : string list =
    seg.Grc |> List.collect (function Verse(_, lines) -> lines |> List.map snd | _ -> [])

/// Metres of the chunk on screen, judged once from its opening lines.
let private chunkMeters (rm: ReaderModel) : string list =
    match currentChunk rm with
    | Some c -> c.Segments |> Array.truncate 60 |> Array.toList |> List.collect verseLinesOf |> Lenses.Prosody.detect
    | None -> []

let private placesPlotted (rm: ReaderModel) (hits: PlaceHit list) : Lenses.Widgets.MapPlace array =
    let here = rm.Lens |> Option.bind (fun l -> l.Seg) |> Option.orElse rm.ScrollRef
    hits
    |> List.map (fun h ->
        Lenses.Widgets.mapPlace h.Lat h.Lon h.Label h.Name (Array.ofList h.Refs) h.Pleiades
            (match here with Some r -> List.contains r h.Refs | None -> false))
    |> Array.ofList

/// Whatever the open lens needs next: places for the chunk now on screen, the
/// map redrawn around them, a manuscript loaded, its page shown. Called after
/// any message that can change one of those, so each handler doesn't have to.
let private lensFollowUp (model: Model) : Model * Cmd<Msg> =
    match model.Reader with
    | Some({ Lens = Some lens; Phase = Ready } as rm) ->
        match lens.Kind with
        | LensMap ->
            match currentChunk rm, rm.Places with
            | None, _ -> model, Cmd.none
            | Some c, PlacesReady(ch, hits) when ch = c.Ref ->
                let places = placesPlotted rm hits
                model,
                Cmd.ofEffect (fun dispatch ->
                    setTimeoutMs
                        (fun () ->
                            Lenses.Widgets.renderMap "lensMap" places (fun r -> dispatch (Reader_(PlacePicked r)))
                            |> Promise.catch (fun e -> dispatch (ShowToast e.Message))
                            |> Promise.start)
                        0)
            | Some c, (PlacesLoading ch | PlacesFailed(ch, _)) when ch = c.Ref -> model, Cmd.none
            | Some c, _ when rm.Eng.IsNone ->
                { model with Reader = Some { rm with Places = PlacesFailed(c.Ref, "no-translation") } }, Cmd.none
            | Some c, _ ->
                let token = rm.Token
                let cands = Lenses.Places.candidates c.Segments
                { model with Reader = Some { rm with Places = PlacesLoading c.Ref } },
                Cmd.OfPromise.either
                    Lenses.Places.resolve
                    cands
                    (fun hits -> Reader_(PlacesLoaded(token, c.Ref, Ok hits)))
                    (fun e -> Reader_(PlacesLoaded(token, c.Ref, Error e.Message)))
        | LensManuscript ->
            match rm.Manifest with
            | ManifestNone ->
                let remembered = (Storage.loadManifests ()).TryFind rm.Work.Id
                let known =
                    Lenses.Paleography.witnesses.TryFind rm.Work.AuthorId
                    |> Option.bind List.tryHead
                    |> Option.map (fun w -> w.Manifest)
                match remembered |> Option.orElse known with
                | Some url -> model, Cmd.ofMsg (Reader_(LoadManifest url))
                | None -> model, Cmd.none
            | ManifestReady(_, _, canvases, i) when i < canvases.Length ->
                let svc = canvases.[i].Service
                model,
                Cmd.ofEffect (fun dispatch ->
                    setTimeoutMs
                        (fun () ->
                            Lenses.Widgets.renderImage "lensImage" svc
                            |> Promise.catch (fun e -> dispatch (ShowToast e.Message))
                            |> Promise.start)
                        0)
            | _ -> model, Cmd.none
        | _ -> model, Cmd.none
    | _ -> model, Cmd.none

/// One note per syllable of a scanned line: longs twice the length of
/// shorts, the acute a fifth above the level pitch, the circumflex falling
/// back from it within the syllable.
let private rhythmNotes (scan: Lenses.Prosody.LineScan) : (float * float * float) array =
    let lvl = 196.0
    let high = lvl * 1.5
    match scan.Scan with
    | Some s ->
        scan.Syls
        |> Array.mapi (fun i syl ->
            let units = if s.Long.[i] then 2.0 else 1.0
            match syl.Pitch with
            | Lenses.Prosody.Rise -> units, high, high
            | Lenses.Prosody.RiseFall -> units, high, lvl
            | Lenses.Prosody.Level -> units, lvl, lvl)
    | None -> [||]

let rec updateCore (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Boot(Ok(catalog, meta)) ->
        let route = Router.parseHash model.CurrentHash
        let model2 = { model with Boot = Booted; Catalog = catalog; Meta = meta; Route = route }
        let model3, cmd = loadForRoute model2 route
        model3, Cmd.batch [ cmd; applySettingsEffect model.Settings ]
    | Boot(Error msg) -> { model with Boot = BootFailed msg }, Cmd.none

    | Navigate(hash, replace) ->
        if hash = model.CurrentHash then
            model, Cmd.none
        else
            let route = Router.parseHash hash
            // Not named `history`: Fable emits locals under their F# name, and a
            // local `history` shadows `window.history` inside the effect below.
            let historyStack = if replace then model.History else model.CurrentHash :: model.History
            let model2 = { model with History = historyStack; CurrentHash = hash; Route = route }
            let model3, cmd = loadForRoute model2 route
            model3, Cmd.batch [ Cmd.ofEffect (fun _ -> if replace then Router.replaceState hash else Router.pushState hash); cmd ]

    // Fired by the browser itself: the back/forward buttons, Alt+Left, a
    // two-finger swipe, or a manually edited URL. Our own pushState/replaceState
    // calls never trigger it, so every occurrence is real navigation. If the
    // incoming hash is the one on top of our stack we went *back* (pop it);
    // anything else is forward navigation (push what we're leaving).
    | HashChanged hash ->
        if hash = model.CurrentHash then
            model, Cmd.none
        else
            let route = Router.parseHash hash
            let history =
                match model.History with
                | top :: rest when top = hash -> rest
                | rest -> model.CurrentHash :: rest
            let model2 = { model with History = history; CurrentHash = hash; Route = route }
            loadForRoute model2 route

    // Hands the step to the browser rather than rewriting the URL in place, so
    // the native history actually shrinks and back/forward and the swipe gesture
    // keep working. The resulting HashChanged does the routing and stack upkeep.
    | BackClicked ->
        if List.isEmpty model.History then
            model, Cmd.none
        else
            model, Cmd.ofEffect (fun _ -> Router.historyBack ())

    // -- settings ------------------------------------------------------
    | Settings_(SetMode m) ->
        let s = { model.Settings with Mode = m }
        { model with Settings = s }, applySettingsEffect s
    | Settings_(SetFace f) ->
        let s = { model.Settings with Face = f }
        { model with Settings = s }, applySettingsEffect s
    | Settings_(SetTheme t) ->
        let s = { model.Settings with Theme = t }
        { model with Settings = s }, applySettingsEffect s
    | Settings_(BumpFontSize d) ->
        let fs = System.Math.Round(model.Settings.FontSize + d, 2) |> max Storage.minFontSize |> min Storage.maxFontSize
        let s = { model.Settings with FontSize = fs }
        { model with Settings = s }, applySettingsEffect s
    | Settings_(BumpLineHeight d) ->
        let lh = System.Math.Round(model.Settings.LineHeight + d, 2) |> max Storage.minLineHeight |> min Storage.maxLineHeight
        let s = { model.Settings with LineHeight = lh }
        { model with Settings = s }, applySettingsEffect s
    // The two header panels are mutually exclusive — opening one dismisses the other.
    | Settings_ ToggleSettingsPane -> { model with SettingsOpen = not model.SettingsOpen; SourceMenuOpen = false }, Cmd.none
    | Settings_ ToggleThemeQuick ->
        let dark = prefersDarkColorScheme ()
        let cur = match model.Settings.Theme with
                  | ThemeAuto -> if dark then ThemeDark else ThemeLight
                  | t -> t
        let newTheme = if cur = ThemeDark then ThemeLight else ThemeDark
        let s = { model.Settings with Theme = newTheme }
        { model with Settings = s }, applySettingsEffect s

    // -- source ----------------------------------------------------------
    | Source_ ToggleSourceMenu -> { model with SourceMenuOpen = not model.SourceMenuOpen; SettingsOpen = false }, Cmd.none
    // Switching source invalidates anything already fetched under the old one.
    // The menu stays open for local, where connecting is usually the next step.
    | Source_(SetSourceMode m) ->
        { model with
            Source = { model.Source with Mode = m }
            TextCache = Map.empty
            OriginCache = Map.empty
            SourceMenuOpen = model.SourceMenuOpen && m = SourceLocal },
        Cmd.ofEffect (fun _ -> Storage.saveSourceMode m)
    | Source_(SetBaseUrl url) -> { model with Source = { model.Source with BaseUrl = url } }, Cmd.ofEffect (fun _ -> Storage.saveSourceBaseUrl url)
    | Source_ PickZipClicked -> model, Cmd.none
    | Source_ PickDirClicked -> model, Cmd.none
    | Source_(ZipConnected(repo, name)) ->
        let local = Map.add repo (ZipArchive name) model.Source.Local
        connectedLocally model local (sprintf "%s connected — %s" (Sources.repoName repo) (texts (Sources.localTextCount repo)))
    | Source_(DirConnected([], _)) ->
        // Nothing came back. Saying "connected" here would be a plain lie, and
        // it used to switch the mode to Local with no local copy behind it.
        model,
        Cmd.ofMsg (
            ShowToastFor(
                (if Sources.hasRemembered () then "Could not reopen your local copy — choose the folder or ZIP again"
                 else "No local copy connected yet — add a ZIP or choose a folder"),
                sourceNoticeMs
            )
        )
    | Source_(DirConnected(repos, name)) ->
        // Report what is actually connected now, not just what this click found:
        // reconnecting one of two remembered folders should still say both.
        let local = Sources.localConnections () |> List.fold (fun m (k, kind) -> Map.add k kind m) model.Source.Local
        let local = repos |> List.fold (fun m r -> if Map.containsKey r m then m else Map.add r (DirHandle name) m) local
        let what = repos |> List.map Sources.repoName |> String.concat " and "
        connectedLocally model local (what + " connected — reading from your computer")
    | Source_(FileListConnected count) ->
        let repos = Sources.localHave ()
        let local = repos |> List.fold (fun m r -> Map.add r FileList m) model.Source.Local
        connectedLocally model local (sprintf "%s connected (until this tab is closed)" (texts count))
    | Source_ ReconnectRemembered ->
        model,
        Cmd.OfPromise.perform
            (fun () ->
                promise {
                    do! Sources.confirmReconnect ()
                    return Sources.localHave ()
                })
            ()
            (fun repos -> Source_(DirConnected(repos, "remembered folder")))
    | Source_(RememberedFound names) ->
        // Anything that reconnected silently at boot is already usable, so
        // reflect it in the model rather than leaving the pane saying nothing
        // is connected until the next click.
        let local = Sources.localConnections () |> List.fold (fun m (k, kind) -> Map.add k kind m) model.Source.Local
        let model = { model with Source = { model.Source with Local = local; NeedsReconnect = names } }
        if List.isEmpty names then
            model, Cmd.none
        else
            model,
            Cmd.ofMsg (
                ShowToastFor(
                    sprintf "%s needs permission again — use Reconnect in Settings › Text source" (String.concat ", " names),
                    sourceNoticeMs
                )
            )
    | Source_(SourceFailed msg) -> model, Cmd.ofMsg (ShowToastFor(msg, sourceNoticeMs))

    // -- library -----------------------------------------------------------
    | Library_(ToggleFav workId) -> saveLib model (LibraryData.toggleFav model.Library workId)
    | Library_(OpenMarkEditor(workId, segRef, label, snippet)) ->
        let isNew = (LibraryData.markFor model.Library workId segRef).IsNone
        let lib, _ = LibraryData.addMark model.Library workId segRef label snippet (nowMs ())
        let rm = model.Reader |> Option.map (fun r -> { r with OpenEditor = Some(workId, segRef) })
        { model with Library = lib; Reader = rm },
        Cmd.batch [ Cmd.ofEffect (fun _ -> Storage.saveLibrary lib); (if isNew then Cmd.ofMsg (ShowToast "Saved to My library") else Cmd.none) ]
    | Library_ CloseMarkEditor -> { model with Reader = model.Reader |> Option.map (fun r -> { r with OpenEditor = None }) }, Cmd.none
    | Library_(SetMarkNote(workId, segRef, note)) -> saveLib model (LibraryData.setMarkNote model.Library workId segRef note)
    | Library_(AddMarkLink(workId, segRef, link)) ->
        let already =
            model.Library.Marks
            |> List.exists (fun m -> m.Work = workId && m.Ref = segRef && (m.Links |> List.exists (fun l -> l.Work = link.Work && l.Ref = link.Ref)))
        let m2, cmd = saveLib model (LibraryData.addMarkLink model.Library workId segRef link)
        m2, Cmd.batch [ cmd; Cmd.ofMsg (ShowToast(if already then "Already linked" else "Link added")) ]
    | Library_(RemoveMarkLink(workId, segRef, linkWork, linkRef)) -> saveLib model (LibraryData.removeMarkLink model.Library workId segRef linkWork linkRef)
    | Library_(RemoveMark(workId, segRef)) ->
        let rm = model.Reader |> Option.map (fun r -> if r.OpenEditor = Some(workId, segRef) then { r with OpenEditor = None } else r)
        saveLib { model with Reader = rm } (LibraryData.removeMark model.Library workId segRef)
    | Library_(SetAuthorNote(authorId, text)) -> { model with Library = LibraryData.setAuthorNote model.Library authorId text }, Cmd.none
    | Library_(SaveAuthorNote _) ->
        let m2, cmd = saveLib model model.Library
        m2, Cmd.batch [ cmd; Cmd.ofMsg (ShowToast "Saved") ]
    | Library_ ExportRequested -> model, Cmd.none
    | Library_(ImportText text) ->
        match LibraryData.parseImport text with
        | Ok imported ->
            let m2, cmd = saveLib model (LibraryData.mergeImport model.Library imported)
            m2, Cmd.batch [ cmd; Cmd.ofMsg (ShowToast "Imported") ]
        | Error errMsg -> model, Cmd.ofMsg (ShowToast errMsg)
    | Library_ ImportConfirmed -> model, Cmd.none
    | Library_ ClearLibraryConfirmed -> saveLib model { Favs = []; Marks = []; AuthorNotes = Map.empty }

    // -- reader: opening / loading -----------------------------------------
    | Reader_(OpenWork(workId, grcUrnOpt, engUrnOpt, chunkOpt, segOpt)) ->
        match model.Catalog.WorkById.TryFind workId with
        | None -> model, Cmd.none
        | Some w ->
            let grcCandidates = w.Texts |> List.filter (fun t -> t.Lang = "grc" || t.Kind = Edition)
            let trCandidates = w.Texts |> List.filter (fun t -> t.Kind = Translation && t.Lang <> "grc")
            let grcOpt =
                grcCandidates
                |> List.tryFind (fun t -> Some t.Urn = grcUrnOpt)
                |> Option.orElse (grcCandidates |> List.tryFind (fun t -> t.Lang = "grc"))
                |> Option.orElse (List.tryHead grcCandidates)
                |> Option.orElse (List.tryHead w.Texts)
            match grcOpt with
            | None -> model, Cmd.none
            | Some grc ->
                let eng =
                    if engUrnOpt = Some "none" then
                        None
                    else
                        trCandidates
                        |> List.tryFind (fun t -> Some t.Urn = engUrnOpt)
                        |> Option.orElse (trCandidates |> List.tryFind (fun t -> t.Lang = "eng"))
                        |> Option.orElse (List.tryHead trCandidates)
                let token = model.NextToken + 1
                let engSuffixStr = eng |> Option.map (fun e -> urnHashSegment e.Urn) |> Option.defaultValue "none"
                let targetHash = Router.toHash (ReaderRoute(workId, urnHashSegment grc.Urn, engSuffixStr, chunkOpt, None))
                let route = ReaderRoute(workId, urnHashSegment grc.Urn, engSuffixStr, chunkOpt, None)
                let rm =
                    { Token = token
                      Work = w
                      Grc = grc
                      Eng = eng
                      Phase = Loading "Loading…"
                      Data = None
                      Chunk = chunkOpt
                      Page = 0
                      PendingSeg = segOpt
                      FlashSeg = None
                      OpenEditor = None
                      GrcOrigin = None
                      EngOrigin = None
                      ScrollRef = None
                      ScrollSeq = 0
                      ScrollLive = false
                      // Changing edition reopens the work; the lens the reader
                      // had open stays open, about no passage in particular yet.
                      Lens =
                        model.Reader
                        |> Option.filter (fun r -> r.Work.Id = workId)
                        |> Option.bind (fun r -> r.Lens)
                        |> Option.map (fun l -> { l with Seg = None })
                      MeterOn = Storage.loadMeterOn ()
                      Playhead = None
                      PlayToken = 0
                      Places = PlacesIdle
                      Manifest =
                        model.Reader
                        |> Option.filter (fun r -> r.Work.Id = workId)
                        |> Option.map (fun r -> r.Manifest)
                        |> Option.defaultValue ManifestNone }
                let recent = { Id = workId; Chunk = None; Ts = nowMs () } :: (model.Recent |> List.filter (fun r -> r.Id <> workId)) |> List.truncate 6
                // The URL we settle on names the editions actually resolved, so it
                // often differs from the one navigated to (`…/none`, or no edition
                // at all). Canonicalising the entry we're already on must *replace*
                // it: pushing would add a second entry for the same work, and
                // stepping back onto the pre-canonical URL would just push forward
                // again, trapping the reader. Only arriving from elsewhere pushes.
                let arrivedFromElsewhere =
                    match Router.parseHash model.CurrentHash with
                    | ReaderRoute(currentId, _, _, _, _) -> currentId <> workId
                    | _ -> true
                let historyStack =
                    if targetHash = model.CurrentHash || not arrivedFromElsewhere then model.History
                    else model.CurrentHash :: model.History
                let model2 =
                    { model with
                        Reader = Some rm
                        Route = route
                        History = historyStack
                        CurrentHash = targetHash
                        Recent = recent
                        OpenAuthors = Set.add w.AuthorId model.OpenAuthors
                        SideOpen = false
                        NextToken = token }
                model2,
                Cmd.batch [
                    Cmd.ofEffect (fun _ ->
                        if targetHash <> model.CurrentHash then
                            if arrivedFromElsewhere then Router.pushState targetHash else Router.replaceState targetHash
                        setDocumentTitle (w.Title + " — Μάθησις")
                        addBodyClass "reading"
                        Storage.saveRecent recent)
                    loadTextCmd token model.Source.Mode model.Source.BaseUrl model.TextCache model.OriginCache grc
                        (fun segs origin -> Reader_(GrcLoaded(token, segs, origin)))
                        (fun err -> Reader_(LoadFailedMsg(token, err)))
                ]

    | Reader_(GrcLoaded(token, segs, origin)) ->
        match model.Reader with
        | Some rm when rm.Token = token ->
            let cache = Map.add rm.Grc.Urn segs model.TextCache
            let origins = Map.add rm.Grc.Urn origin model.OriginCache
            let rm = { rm with GrcOrigin = Some origin }
            match rm.Eng with
            | None ->
                { model with TextCache = cache; OriginCache = origins; Reader = Some rm },
                Cmd.ofMsg (Reader_(EngLoaded(token, None, None)))
            | Some eng ->
                { model with
                    TextCache = cache
                    OriginCache = origins
                    Reader = Some { rm with Phase = Loading "Loading…" } },
                loadTextCmd token model.Source.Mode model.Source.BaseUrl cache origins eng
                    (fun segs origin -> Reader_(EngLoaded(token, Some segs, Some origin)))
                    (fun _ -> Reader_(EngLoaded(token, None, None)))
        | _ -> model, Cmd.none

    | Reader_(EngLoaded(token, engSegsOpt, engOrigin)) ->
        match model.Reader with
        | Some rm when rm.Token = token ->
            let rm = { rm with EngOrigin = engOrigin }
            let cache =
                match engSegsOpt, rm.Eng with
                | Some segs, Some eng -> Map.add eng.Urn segs model.TextCache
                | _ -> model.TextCache
            let origins =
                match engOrigin, rm.Eng with
                | Some o, Some eng -> Map.add eng.Urn o model.OriginCache
                | _ -> model.OriginCache
            let grcSegs = cache.TryFind rm.Grc.Urn |> Option.defaultValue []
            let aligned = Tei.Aligner.buildAlignedText grcSegs engSegsOpt
            // A cross-reference in a note names a passage but no chunk, so when
            // the chunk is absent (or names one this edition doesn't have) fall
            // back to whichever chunk actually holds the passage being asked
            // for — otherwise a link into book 3 would open book 1 and then fail
            // to find the passage there.
            let chunkHolding (segRef: string) =
                aligned.Chunks
                |> List.tryFind (fun ch -> ch.Segments |> Array.exists (fun s -> s.Ref = segRef))
                |> Option.map (fun ch -> ch.Ref)
            // A link can name a line inside a passage rather than the passage
            // itself — an echo found in another text is cited by its TEI line,
            // and the reader may group that line with its neighbours. Resolve
            // it the way the jump box does, to the passage that holds it.
            let rm =
                match rm.PendingSeg with
                | Some s when (chunkHolding s).IsNone ->
                    match gotoRefResolve aligned s with
                    | Some(c, segOpt) -> { rm with Chunk = Some c; PendingSeg = segOpt }
                    | None -> rm
                | _ -> rm
            let initialChunkRef =
                rm.Chunk
                |> Option.filter (fun c -> aligned.Chunks |> List.exists (fun ch -> ch.Ref = c))
                |> Option.orElseWith (fun () -> rm.PendingSeg |> Option.bind chunkHolding)
                |> Option.defaultValue (List.head aligned.Chunks).Ref
            let toastFailedCmd =
                if rm.Eng.IsSome && engSegsOpt.IsNone then
                    Cmd.ofMsg (ShowToast "Translation could not be loaded; showing Greek only")
                else
                    Cmd.none
            let toastCoverageCmd =
                match aligned.Coverage with
                | Some c when c.Total > 0 && float c.Covered < float c.Total * 0.3 -> Cmd.ofMsg (ShowToast(sprintf "This translation covers only %d of %d passages" c.Covered c.Total))
                | _ -> Cmd.none
            { model with
                TextCache = cache
                OriginCache = origins
                Reader = Some { rm with Phase = Ready; Data = Some aligned } },
            Cmd.batch [ toastFailedCmd; toastCoverageCmd; Cmd.ofMsg (Reader_(ShowChunk(initialChunkRef, rm.PendingSeg, None))) ]
        | _ -> model, Cmd.none

    | Reader_(LoadFailedMsg(token, err)) ->
        match model.Reader with
        | Some rm when rm.Token = token -> { model with Reader = Some { rm with Phase = LoadFailed err } }, Cmd.none
        | _ -> model, Cmd.none

    | Reader_(LoadStatus(token, statusMsg)) ->
        match model.Reader with
        | Some rm when rm.Token = token -> { model with Reader = Some { rm with Phase = Loading statusMsg } }, Cmd.none
        | _ -> model, Cmd.none

    | Reader_ RetryLoad ->
        match model.Reader with
        | Some rm ->
            update
                (Reader_(OpenWork(rm.Work.Id, Some rm.Grc.Urn, Some(rm.Eng |> Option.map (fun e -> e.Urn) |> Option.defaultValue "none"), rm.Chunk, rm.PendingSeg)))
                model
        | None -> model, Cmd.none

    | Reader_(PickGrcEdition urn) ->
        match model.Reader with
        | Some rm -> update (Reader_(OpenWork(rm.Work.Id, Some urn, Some(rm.Eng |> Option.map (fun e -> e.Urn) |> Option.defaultValue "none"), None, None))) model
        | None -> model, Cmd.none
    | Reader_(PickEngEdition urn) ->
        match model.Reader with
        | Some rm -> update (Reader_(OpenWork(rm.Work.Id, Some rm.Grc.Urn, Some urn, None, None))) model
        | None -> model, Cmd.none

    // -- reader: navigation within an already-open work --------------------
    | Reader_(ShowChunk(chunkRef, segRefOpt, pageOpt)) ->
        match model.Reader with
        // Only act while this reader is still the page being shown. A chunk
        // render can arrive late — a slow load finishing, or the flash-clear
        // timer — and rewriting the URL then would stamp this work's hash over
        // the history entry of whatever the reader has since moved on to.
        | Some rm when (match model.Route with
                        | ReaderRoute(id, _, _, _, _) -> id = rm.Work.Id
                        | _ -> false) ->
            match rm.Data with
            | None -> model, Cmd.none
            | Some d ->
                let chunk = d.Chunks |> List.tryFind (fun c -> c.Ref = chunkRef) |> Option.defaultValue (List.head d.Chunks)
                let npages = max 1 (int (ceil (float chunk.Segments.Length / float PAGE_SIZE)))
                let page =
                    match pageOpt with
                    | Some p -> p
                    | None ->
                        match segRefOpt with
                        | Some segRef ->
                            match chunk.Segments |> Array.tryFindIndex (fun s -> s.Ref = segRef) with
                            | Some i -> i / PAGE_SIZE
                            | None -> 0
                        | None -> 0
                let page = page |> max 0 |> min (npages - 1)
                let rm2 = { rm with Chunk = Some chunk.Ref; Page = page; FlashSeg = segRefOpt; PendingSeg = None }
                let engSuffixStr = rm.Eng |> Option.map (fun e -> urnHashSegment e.Urn) |> Option.defaultValue "none"
                let hash = Router.toHash (ReaderRoute(rm.Work.Id, urnHashSegment rm.Grc.Urn, engSuffixStr, Some chunk.Ref, segRefOpt))
                let route = ReaderRoute(rm.Work.Id, urnHashSegment rm.Grc.Urn, engSuffixStr, Some chunk.Ref, segRefOpt)
                let recent = model.Recent |> List.map (fun r -> if r.Id = rm.Work.Id then { r with Chunk = Some chunk.Ref } else r)
                let model2 = { model with Reader = Some rm2; Route = route; CurrentHash = hash; Recent = recent }
                model2,
                Cmd.batch [
                    Cmd.ofEffect (fun _ ->
                        Router.replaceState hash
                        Storage.saveRecent recent)
                    // A different chunk or page is a different set of passages
                    // under the scrollbar, so the bands are rebuilt here.
                    scrollMarksEffect model2
                    (match segRefOpt with
                     | Some s ->
                         Cmd.batch [
                             Cmd.ofEffect (fun _ -> scrollToSegAndFlash s)
                             // Clearing the highlight used to go back through
                             // ShowChunk with no passage, which took its "scroll
                             // to the top" branch — so arriving at a passage from
                             // a note's cross-reference threw you to the top of
                             // the text a second and a half later.
                             Cmd.ofEffect (fun dispatch -> setTimeoutMs (fun () -> dispatch (Reader_ ClearFlash)) 1600)
                         ]
                     | None -> Cmd.ofEffect (fun _ -> scrollToTop ()))
                ]
        | _ -> model, Cmd.none

    | Reader_ PrevUnit ->
        match model.Reader with
        | Some { Phase = Ready; Data = Some d; Chunk = chunkOpt; Page = page } ->
            let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = chunkOpt) |> Option.defaultValue (List.head d.Chunks)
            if page > 0 then
                update (Reader_(ShowChunk(chunk.Ref, None, Some(page - 1)))) model
            else
                let ci = d.Chunks |> List.findIndex (fun c -> c.Ref = chunk.Ref)
                if ci > 0 then update (Reader_(ShowChunk(d.Chunks.[ci - 1].Ref, None, None))) model else model, Cmd.none
        | _ -> model, Cmd.none

    | Reader_ NextUnit ->
        match model.Reader with
        | Some { Phase = Ready; Data = Some d; Chunk = chunkOpt; Page = page } ->
            let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = chunkOpt) |> Option.defaultValue (List.head d.Chunks)
            let npages = max 1 (int (ceil (float chunk.Segments.Length / float PAGE_SIZE)))
            if page < npages - 1 then
                update (Reader_(ShowChunk(chunk.Ref, None, Some(page + 1)))) model
            else
                let ci = d.Chunks |> List.findIndex (fun c -> c.Ref = chunk.Ref)
                if ci < d.Chunks.Length - 1 then update (Reader_(ShowChunk(d.Chunks.[ci + 1].Ref, None, None))) model else model, Cmd.none
        | _ -> model, Cmd.none

    | Reader_(SetJumpInput v) -> { model with JumpInput = v }, Cmd.none
    | Reader_ GotoRefSubmitted ->
        match model.Reader with
        | Some { Data = Some d } ->
            let v = model.JumpInput.Trim()
            if v = "" then
                model, Cmd.none
            else
                match gotoRefResolve d v with
                | Some(chunkRef, segRefOpt) -> { model with JumpInput = "" }, Cmd.ofMsg (Reader_(ShowChunk(chunkRef, segRefOpt, None)))
                | None -> model, Cmd.ofMsg (ShowToast(sprintf "No passage “%s” in this text" v))
        | _ -> model, Cmd.none

    | Reader_(CopyUrn text) ->
        model,
        Cmd.ofEffect (fun dispatch ->
            match clipboardWriteText text with
            | Some p ->
                p
                |> Promise.map (fun () -> dispatch (ShowToast "CTS URN copied"))
                |> Promise.catch (fun _ ->
                    dispatch (ShowToast text)
                    ())
                |> Promise.start
            | None -> dispatch (ShowToast text))

    // The readout appears while scrolling and fades once it stops. Each report
    // starts a fresh timer and only the newest one is allowed to hide it, so a
    // timer left over from an earlier scroll can't blank a live readout.
    | Reader_(ScrolledTo segRef) ->
        match model.Reader with
        | Some rm when rm.Phase = Ready ->
            let seq_ = rm.ScrollSeq + 1
            { model with Reader = Some { rm with ScrollRef = segRef; ScrollSeq = seq_; ScrollLive = true } },
            Cmd.OfAsync.perform
                (fun () ->
                    async {
                        do! Async.Sleep 1100
                        return seq_
                    })
                ()
                (fun s -> Reader_(ScrollIdle s))
        | _ -> model, Cmd.none

    // A resize changes both the document height the bands are measured against
    // and whether there is a classic scrollbar to paint at all, so the marks are
    // rebuilt rather than left pointing at the old layout.
    // Drops the arrival highlight and takes the passage back out of the URL, so
    // a reload doesn't flash it again — without touching the scroll position,
    // which is the whole point of having followed the link.
    | Reader_ ClearFlash ->
        match model.Reader with
        | Some rm when rm.FlashSeg.IsSome ->
            let engSuffix = rm.Eng |> Option.map (fun e -> urnHashSegment e.Urn) |> Option.defaultValue "none"
            let hash = Router.toHash (ReaderRoute(rm.Work.Id, urnHashSegment rm.Grc.Urn, engSuffix, rm.Chunk, None))
            { model with Reader = Some { rm with FlashSeg = None }; CurrentHash = hash },
            Cmd.ofEffect (fun _ -> Router.replaceState hash)
        | _ -> model, Cmd.none

    | Reader_ RepaintMarks -> model, scrollMarksEffect model

    | Reader_(ScrollIdle seq_) ->
        match model.Reader with
        | Some rm when rm.ScrollSeq = seq_ && rm.ScrollLive ->
            // Also the cheapest place to keep the bands honest: the document
            // height they are measured against changes when the window is
            // resized or an editor opens, and this runs once scrolling settles.
            { model with Reader = Some { rm with ScrollLive = false } }, scrollMarksEffect model
        | _ -> model, Cmd.none

    | Reader_(WordClicked(word, rectObj)) ->
        let rect: {| left: float; top: float; bottom: float |} =
            {| left = rectObj?left |> unbox<float>; top = rectObj?top |> unbox<float>; bottom = rectObj?bottom |> unbox<float> |}
        { model with Popover = Some(WordPopover(word, rect)) }, Cmd.none

    // -- reader: study lenses ------------------------------------------
    | Reader_(OpenLens(kind, segOpt)) ->
        match model.Reader with
        | Some rm ->
            // A lens opened from the column heading is about whatever passage
            // is at the top of the screen; one opened from a passage is about
            // that passage.
            let firstOnScreen =
                currentChunk rm
                |> Option.bind (fun c -> c.Segments |> Array.skip (min (rm.Page * PAGE_SIZE) c.Segments.Length) |> Array.tryHead)
                |> Option.map (fun s -> s.Ref)
            let seg = segOpt |> Option.orElse rm.ScrollRef |> Option.orElse firstOnScreen
            match rm.Lens with
            | existing ->
                let lens =
                    match existing with
                    | Some l -> { l with Kind = kind; Seg = seg }
                    | None -> { Kind = kind; Seg = seg; Word = None; Stem = ""; MsView = Diplomatic }
                { model with Reader = Some { rm with Lens = Some lens }; Popover = None }, Cmd.none
        | None -> model, Cmd.none
    // The ◇ is a toggle: pressed on the passage the rail is already about, it
    // closes the rail; on any other passage it moves the rail there.
    | Reader_(StudyPassage segRef) ->
        match model.Reader |> Option.bind (fun rm -> rm.Lens) with
        | Some l when l.Seg = Some segRef -> update (Reader_ CloseLens) model
        | lens ->
            let kind = lens |> Option.map (fun l -> l.Kind) |> Option.defaultValue LensEchoes
            update (Reader_(OpenLens(kind, Some segRef))) model
    | Reader_ CloseLens ->
        match model.Reader with
        | Some rm ->
            { model with Reader = Some { rm with Lens = None; Playhead = None; PlayToken = rm.PlayToken + 1 } },
            Cmd.ofEffect (fun _ -> Lenses.Widgets.stopRhythm ())
        | None -> model, Cmd.none
    | Reader_(TraceWord word) ->
        match model.Reader with
        | Some rm ->
            let seg = rm.Lens |> Option.bind (fun l -> l.Seg) |> Option.orElse rm.ScrollRef
            let msView = rm.Lens |> Option.map (fun l -> l.MsView) |> Option.defaultValue Diplomatic
            let lens = { Kind = LensWords; Seg = seg; Word = Some word; Stem = Lenses.Corpus.defaultStem word; MsView = msView }
            { model with Reader = Some { rm with Lens = Some lens }; Popover = None }, Cmd.none
        | None -> model, Cmd.none
    | Reader_(SetLensStem s) ->
        match model.Reader with
        | Some({ Lens = Some l } as rm) -> { model with Reader = Some { rm with Lens = Some { l with Stem = s } } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(SetMsView v) ->
        match model.Reader with
        | Some({ Lens = Some l } as rm) -> { model with Reader = Some { rm with Lens = Some { l with MsView = v } } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_ ToggleMeter ->
        match model.Reader with
        | Some rm ->
            let v = not rm.MeterOn
            { model with Reader = Some { rm with MeterOn = v } }, Cmd.ofEffect (fun _ -> Storage.saveMeterOn v)
        | None -> model, Cmd.none
    | Reader_(PlayLine(segRef, line)) ->
        match model.Reader with
        | Some rm ->
            let seg = currentChunk rm |> Option.bind (fun c -> c.Segments |> Array.tryFind (fun s -> s.Ref = segRef))
            let text = seg |> Option.bind (fun s -> verseLinesOf s |> List.tryItem line)
            match text with
            | Some t ->
                let scan = Lenses.Prosody.scanLine (Views.Lens.chunkMeters rm) (Interop.greekWords t)
                let notes = rhythmNotes scan
                if notes.Length = 0 then
                    model, Cmd.ofMsg (ShowToast "This line doesn't scan in a metre the reader knows, so there is no rhythm to play")
                else
                    let token = rm.PlayToken + 1
                    { model with Reader = Some { rm with PlayToken = token; Playhead = Some(segRef, line, 0) } },
                    Cmd.ofEffect (fun dispatch ->
                        Lenses.Widgets.playRhythm
                            notes
                            190.0
                            (fun i -> dispatch (Reader_(PlayheadAt(token, segRef, line, i))))
                            (fun () -> dispatch (Reader_(PlaybackDone token))))
            | None -> model, Cmd.none
        | None -> model, Cmd.none
    | Reader_ StopPlayback ->
        match model.Reader with
        | Some rm ->
            { model with Reader = Some { rm with Playhead = None; PlayToken = rm.PlayToken + 1 } },
            Cmd.ofEffect (fun _ -> Lenses.Widgets.stopRhythm ())
        | None -> model, Cmd.none
    | Reader_(PlayheadAt(token, segRef, line, syl)) ->
        match model.Reader with
        | Some rm when rm.PlayToken = token -> { model with Reader = Some { rm with Playhead = Some(segRef, line, syl) } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(PlaybackDone token) ->
        match model.Reader with
        | Some rm when rm.PlayToken = token -> { model with Reader = Some { rm with Playhead = None } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(PlacesLoaded(token, chunk, result)) ->
        match model.Reader with
        | Some rm when rm.Token = token ->
            let places =
                match result with
                | Ok hits -> PlacesReady(chunk, hits)
                | Error e -> PlacesFailed(chunk, e)
            { model with Reader = Some { rm with Places = places } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(PlacePicked segRef) ->
        match model.Reader with
        | Some({ Lens = Some l } as rm) ->
            let model2 = { model with Reader = Some { rm with Lens = Some { l with Seg = Some segRef } } }
            match currentChunk rm with
            | Some c -> update (Reader_(ShowChunk(c.Ref, Some segRef, None))) model2
            | None -> model2, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(LoadManifest url) ->
        match model.Reader with
        | Some rm when url.Trim() <> "" ->
            let url = url.Trim()
            let workId = rm.Work.Id
            { model with Reader = Some { rm with Manifest = ManifestLoading url } },
            Cmd.OfPromise.either
                Lenses.Paleography.load
                url
                (fun r -> Reader_(ManifestLoaded(url, Ok r)))
                (fun e -> Reader_(ManifestLoaded(url, Error e.Message)))
            |> fun cmd -> Cmd.batch [ cmd; Cmd.ofEffect (fun _ -> Storage.saveManifestFor workId url) ]
        | _ -> model, Cmd.none
    | Reader_(ManifestLoaded(url, result)) ->
        match model.Reader with
        | Some({ Manifest = ManifestLoading u } as rm) when u = url ->
            let m =
                match result with
                | Ok(title, canvases) ->
                    // Skip the binding: the first canvases of a digitised codex
                    // are almost always covers, flyleaves and colour charts.
                    let firstLeaf =
                        canvases
                        // "1", "1r", "fol. 1r", "f. 001r" — but not a starred or
                        // lettered flyleaf such as "1*r" or "1ar".
                        |> List.tryFindIndex (fun c -> System.Text.RegularExpressions.Regex.IsMatch(c.Label, @"^\s*(fol\.?|f\.|p\.)?\s*0*1\s*r?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        |> Option.defaultValue 0
                    ManifestReady(url, title, canvases, firstLeaf)
                | Error e -> ManifestFailed(url, e)
            { model with Reader = Some { rm with Manifest = m } }, Cmd.none
        | _ -> model, Cmd.none
    | Reader_(ManifestPage i) ->
        match model.Reader with
        | Some({ Manifest = ManifestReady(url, title, canvases, _) } as rm) ->
            let i = i |> max 0 |> min (canvases.Length - 1)
            { model with Reader = Some { rm with Manifest = ManifestReady(url, title, canvases, i) } }, Cmd.none
        | _ -> model, Cmd.none

    // -- shell / chrome ------------------------------------------------
    | SetFilter f -> { model with Filter = f }, Cmd.ofEffect (fun _ -> Storage.saveFilter f)
    | SetNavQuery q -> { model with NavQuery = q }, Cmd.none
    | SetGenre g -> { model with Genre = g }, Cmd.none
    | ToggleAuthor id ->
        let hasQuery = model.NavQuery.Trim() <> ""
        if hasQuery then
            let closed = if model.ClosedAuthors.Contains id then Set.remove id model.ClosedAuthors else Set.add id model.ClosedAuthors
            { model with ClosedAuthors = closed }, Cmd.none
        else
            let opened = if model.OpenAuthors.Contains id then Set.remove id model.OpenAuthors else Set.add id model.OpenAuthors
            { model with OpenAuthors = opened }, Cmd.none
    | ToggleParts -> { model with PartsOpen = not model.PartsOpen }, Cmd.none
    // Toggling updates only the state belonging to the kind of route you are on,
    // so hiding the library to read doesn't also hide it on the home page.
    | ToggleNavHidden when Router.isReader model.Route ->
        let v = not model.NavHiddenReader
        { model with NavHiddenReader = v }, Cmd.ofEffect (fun _ -> Storage.saveNavHiddenReader v)
    | ToggleNavHidden ->
        let v = not model.NavHidden
        { model with NavHidden = v }, Cmd.ofEffect (fun _ -> Storage.saveNavHidden v)
    | ToggleSide open_ -> { model with SideOpen = open_ }, Cmd.none
    | SetHomeQuery q -> { model with HomeQuery = q }, Cmd.none
    | SetBrowseQuery q -> { model with BrowseQuery = q }, Cmd.none
    | SetWikiQuery q -> { model with WikiQuery = q }, Cmd.none
    | ToggleCollapsed key ->
        let isOpen = model.Collapsed.TryFind key |> Option.defaultValue (not (collapsedByDefault key))
        let collapsed = Map.add key (not isOpen) model.Collapsed
        { model with Collapsed = collapsed }, Cmd.ofEffect (fun _ -> Storage.saveCollapsed collapsed)
    | ForgetRecent None -> { model with Recent = [] }, Cmd.ofEffect (fun _ -> Storage.saveRecent [])
    | ForgetRecent(Some id) ->
        let recent = model.Recent |> List.filter (fun r -> r.Id <> id)
        { model with Recent = recent }, Cmd.ofEffect (fun _ -> Storage.saveRecent recent)
    | ClosePopover -> { model with Popover = None }, Cmd.none
    | ShowToast msg -> update (ShowToastFor(msg, 2200)) model
    | ShowToastFor(msg, lingerMs) ->
        // The id both supersedes an earlier toast's pending timer (HideToast
        // ignores a stale id) and lets the close button dismiss this one.
        let id = model.NextToken + 1
        { model with Toast = Some(id, msg); NextToken = id },
        Cmd.ofEffect (fun dispatch -> setTimeoutMs (fun () -> dispatch (HideToast id)) lingerMs)
    | HideToast id ->
        match model.Toast with
        | Some(curId, _) when curId = id -> { model with Toast = None }, Cmd.none
        | _ -> model, Cmd.none
    | WikipediaSummary _ -> model, Cmd.none
    | DrawerTouch(phase, x, _y) ->
        match phase with
        | "start" ->
            let isOpenNow = model.SideOpen
            let mode = if not isOpenNow && x < 32.0 then "open" elif isOpenNow then "close" else "none"
            if mode = "none" then { model with DrawerDrag = None }, Cmd.none else { model with DrawerDrag = Some {| StartX = x; Dx = 0.0; Mode = mode; Width = 280.0 |} }, Cmd.none
        | "move" ->
            match model.DrawerDrag with
            | Some d -> { model with DrawerDrag = Some {| d with Dx = x - d.StartX |} }, Cmd.none
            | None -> model, Cmd.none
        | "end" ->
            match model.DrawerDrag with
            | Some d ->
                let shouldOpen = if d.Mode = "open" then d.Dx > d.Width * 0.3 else not (d.Dx < -d.Width * 0.25)
                { model with DrawerDrag = None; SideOpen = shouldOpen }, Cmd.none
            | None -> model, Cmd.none
        | _ -> model, Cmd.none

    | ToggleNotesPanel -> { model with Notes = { model.Notes with Open = not model.Notes.Open } }, Cmd.none
    | NotesDrag("button", phase, x, y) ->
        { model with
            Notes =
                { model.Notes with
                    Button = dragUpdate notesButtonWidth phase x y model.Notes.Button
                    // Reset on grab, set once the view reports real movement;
                    // pointerup reads it to tell a drag from a click.
                    ButtonMoved = (phase = "move") || (phase <> "grab" && model.Notes.ButtonMoved) } },
        Cmd.none
    | NotesDrag(_, phase, x, y) ->
        { model with Notes = { model.Notes with Panel = dragUpdate notesPanelWidth phase x y model.Notes.Panel } }, Cmd.none

    | KeyPressed(key, alt) ->
        match key, alt with
        | "Escape", _ ->
            { model with
                Popover = None
                SettingsOpen = false
                SourceMenuOpen = false
                SideOpen = false
                Notes = { model.Notes with Open = false } },
            Cmd.none
        | "ArrowLeft", true -> update BackClicked model
        | "ArrowLeft", false -> update (Reader_ PrevUnit) model
        | "ArrowRight", _ -> update (Reader_ NextUnit) model
        | "/", _ -> model, Cmd.ofEffect (fun _ -> focusElementById "q")
        // Backslash collapses/restores the library without reaching for the
        // mouse — the counterpart to "/" focusing its search box.
        | "\\", _ -> update ToggleNavHidden model
        | _ -> model, Cmd.none
    | NoOp -> model, Cmd.none

/// `updateCore`, then whatever the open study lens needs as a result.
and update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    let model2, cmd = updateCore msg model
    match msg with
    | Reader_(OpenLens _ | ShowChunk _ | PlacesLoaded _ | ManifestLoaded _ | ManifestPage _ | ScrolledTo _) ->
        let model3, cmd2 = lensFollowUp model2
        model3, Cmd.batch [ cmd; cmd2 ]
    | _ -> model2, cmd

// ---------------------------------------------------------------------------
// view
// ---------------------------------------------------------------------------

let private mainContent (model: Model) (dispatch: Msg -> unit) : Fable.React.ReactElement =
    match model.Route with
    | Landing -> Views.Home.render model dispatch
    | Browse -> Views.Browse.render model dispatch
    | LibraryRoute -> Views.LibraryPage.render model dispatch
    | AboutRoute -> Views.About.render model
    | AuthorRoute(id, section) -> Views.WikiPages.AuthorPage model dispatch id section
    | WikiRoute WikiHome -> Views.WikiPages.home model dispatch
    | WikiRoute(WikiAuthors scope) -> Views.WikiPages.authorsIndex model dispatch scope model.WikiQuery
    | WikiRoute(WikiEras None) -> Views.WikiPages.eras model dispatch
    | WikiRoute(WikiEras(Some id)) -> Views.WikiPages.era model dispatch id
    | WikiRoute(WikiArticles kind) -> Views.WikiPages.articleIndex model dispatch kind
    | WikiRoute WikiEditions -> Views.WikiPages.editions model dispatch
    | WikiRoute(WikiGuide slug) -> Views.Guide.render model dispatch slug
    | ReaderRoute _ ->
        match model.Reader with
        | Some rm -> Views.Reader.render model rm dispatch
        | None -> Feliz.Html.none

let view (model: Model) (dispatch: Msg -> unit) : Fable.React.ReactElement =
    match model.Boot with
    | Booting _
    | BootFailed _ -> Views.Shared.bootOverlay model.Boot
    | Booted ->
        let modeClass =
            match model.Reader with
            | Some rm when rm.Eng.IsNone -> "grc"
            | _ ->
                match model.Settings.Mode with
                | Both -> "both"
                | GrcOnly -> "grc"
                | EngOnly -> "eng"
        Feliz.React.Fragment [
            Views.Header.render model dispatch
            Feliz.Html.div [
                Feliz.prop.className (
                    "shell"
                    + (if Router.navHiddenOn model.Route model.NavHidden model.NavHiddenReader then " nav-hidden" else "")
                )
                Feliz.prop.children [
                    Views.Nav.render model dispatch
                    Feliz.Html.main [ Feliz.prop.id "main"; Feliz.prop.className ("mode-" + modeClass); Feliz.prop.children [ mainContent model dispatch ] ]
                ]
            ]
            Views.SettingsPane.render model dispatch
            Views.NotesPanel.render model dispatch
            Views.Popover.render model.Popover dispatch
            Views.Shared.toast model.Toast dispatch
            Views.Shared.backdrop model.SideOpen dispatch
        ]
