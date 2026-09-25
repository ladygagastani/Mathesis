module Views.Reader

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Types

let private PAGE_SIZE = 250

/// The passage a clicked word sits in (its `.seg`'s data-ref), so a saved
/// word can keep its context.
[<Emit("(($0.closest && $0.closest('.seg')) ? $0.closest('.seg').getAttribute('data-ref') : null)")>]
let private segRefAttr (el: obj) : string = jsNative

let private segRefOf (el: obj) : string option =
    match segRefAttr el with
    | null -> None
    | r -> Some r

let private langNames =
    Map.ofList [
        "grc", "Greek"; "eng", "English"; "lat", "Latin"; "ger", "German"; "fre", "French"
        "ita", "Italian"; "mul", "Multilingual"; "ara", "Arabic"; "cop", "Coptic"
    ]

let private langName (l: string) = langNames.TryFind l |> Option.defaultValue l

let private fmtKb (kb: int) : string =
    if kb >= 1024 then (float kb / 1024.0).ToString("F1") + " MB" else string kb + " KB"

let private urnSuffix (urn: string) : string = urn.Split(':') |> Array.last

let private interspersed (sep: ReactElement) (items: ReactElement list) : ReactElement list =
    items |> List.mapi (fun i x -> if i = 0 then [ x ] else [ sep; x ]) |> List.collect id

// ---------------------------------------------------------------------------
// segLabel — a nicer label for verse segments spanning multiple numbered lines
// ---------------------------------------------------------------------------

let private segLabel (seg: Segment) : string =
    let ls =
        seg.Grc
        |> List.collect (function
            | Verse(_, lines) -> lines
            | _ -> [])
        |> List.choose fst
    if ls.Length > 1 then
        let a = ls.Head
        let z = ls.[ls.Length - 1]
        let pre = seg.Key |> List.truncate (max 0 (seg.Key.Length - 1)) |> String.concat "."
        if seg.Key.Length > 1 && List.last seg.Key = a then
            (if pre <> "" then pre + "." else "") + a + "–" + z
        elif seg.Key.Length = 1 && seg.Key.Head = a then
            a + "–" + z
        else
            seg.Ref
    else
        seg.Ref

/// Plain concatenated text of a segment's Greek blocks (mirrors the bookmark
/// snippet capture, which read the rendered `.col.grc` text content).
let private plainGrcText (blocks: Block list) : string =
    blocks
    |> List.map (function
        | Heading h -> h
        | Prose(sp, t) -> (sp |> Option.map (fun s -> s + " ") |> Option.defaultValue "") + t
        | Verse(sp, lines) -> (sp |> Option.map (fun s -> s + " ") |> Option.defaultValue "") + (lines |> List.map snd |> String.concat " "))
    |> String.concat " "
    |> fun s -> System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ")
    |> fun s -> s.Trim()
    |> fun s -> if s.Length > 140 then s.Substring(0, 140) else s

/// True when a passage holds nothing but page or line markers (⟦17⟧) on both
/// sides, e.g. the bare Stephanus page number Perseus puts before 17a. Such a
/// passage is kept (so links and "go to" still land) but drawn with no height.
let private isMarkerOnly (seg: Segment) : bool =
    let hasWords (blocks: Block list) =
        let t = plainGrcText blocks
        let t = System.Text.RegularExpressions.Regex.Replace(t, @"⟦[^⟧]*⟧", "")
        System.Text.RegularExpressions.Regex.IsMatch(t, @"\p{L}")
    not (hasWords seg.Grc) && not (seg.Eng |> Option.exists hasWords)

// ---------------------------------------------------------------------------
// word/marker tokenization and block rendering (no dangerouslySetInnerHTML)
// ---------------------------------------------------------------------------

/// `initial` sets the first Greek word's first letter as an ochre capital (the
/// first passage of each part). Words carry their own text in `data-w`, since
/// with an initial their `textContent` no longer matches the edited text, and
/// `tabIndex -1` so the keyboard handler on the column can focus them.
let private tokensWith (dispatch: Msg -> unit) (greek: bool) (initial: bool) (text: string) : ReactElement list =
    let matches = Interop.scanTokens text
    let out = ResizeArray<ReactElement>()
    let mutable pos = 0
    let mutable pendingInitial = initial
    for i in 0 .. matches.Length - 1 do
        let idx, m = matches.[i]
        if idx > pos then
            out.Add(Html.text (text.Substring(pos, idx - pos)))
        if m.StartsWith "⟦" && m.EndsWith "⟧" then
            out.Add(Html.span [ prop.key (string i); prop.className "mk"; prop.text (m.Substring(1, m.Length - 2)) ])
        elif greek then
            let body =
                match (if pendingInitial then Shared.initialOf m else None) with
                | Some(cap, rest) ->
                    [ Html.span [ prop.className "ini"; prop.ariaHidden true; prop.text cap ]
                      Html.span [ prop.className "sr-only"; prop.text (m.Substring(0, 1)) ]
                      Html.text rest ]
                | None -> [ Html.text m ]
            pendingInitial <- false
            out.Add(
                Html.span [
                    prop.key (string i)
                    prop.className "w"
                    prop.tabIndex -1
                    prop.custom ("data-w", m)
                    prop.children body
                    prop.onClick (fun e ->
                        e.stopPropagation ()
                        let target = e.currentTarget :?> Element
                        dispatch (Reader_(WordClicked(m, box (target.getBoundingClientRect ()), segRefOf target))))
                ]
            )
        else
            out.Add(Html.text m)
        pos <- idx + m.Length
    if pos < text.Length then
        out.Add(Html.text (text.Substring pos))
    List.ofSeq out

let private tokens (dispatch: Msg -> unit) (greek: bool) (text: string) : ReactElement list =
    tokensWith dispatch greek false text

let private shouldShowLineNumber (bi: int) (i: int) (n: string option) : bool =
    match n with
    | None -> false
    | Some "" -> false
    | Some nv ->
        match System.Double.TryParse(nv, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture) with
        | true, v -> v % 5.0 = 0.0 || (i = 0 && bi = 0)
        | false, _ -> true

/// Scansion context for the Greek column while "show scansion" is on: the
/// metres of the chunk, the passage, and which syllable is sounding.
type private MeterCtx = { Meters: string list; SegRef: string; Playhead: (string * int * int) option }

/// A verse line with every syllable marked long or short above it. Words stay
/// clickable for lookup exactly as in the plain rendering; the syllables are
/// spans inside each word. A line that won't scan is shown plain.
let private scannedTokens (dispatch: Msg -> unit) (ctx: MeterCtx) (line: int) (text: string) : ReactElement list option =
    let matches = Interop.scanTokens text
    let words = matches |> Array.map snd |> Array.filter (fun m -> not (m.StartsWith "⟦"))
    let scan = Lenses.Prosody.scanLine ctx.Meters words
    match scan.Scan with
    | None -> None
    | Some s ->
        let playing = match ctx.Playhead with Some(r, l, k) when r = ctx.SegRef && l = line -> Some k | _ -> None
        let caesura = s.Caesura |> Option.map fst
        let out = ResizeArray<ReactElement>()
        let mutable pos = 0
        let mutable wi = 0
        for i in 0 .. matches.Length - 1 do
            let idx, m = matches.[i]
            if idx > pos then out.Add(Html.text (text.Substring(pos, idx - pos)))
            if m.StartsWith "⟦" && m.EndsWith "⟧" then
                out.Add(Html.span [ prop.key (string i); prop.className "mk"; prop.text (m.Substring(1, m.Length - 2)) ])
            else
                let pieces = if wi < scan.Pieces.Length then scan.Pieces.[wi] else [ (m, None) ]
                wi <- wi + 1
                out.Add(
                    Html.span [
                        prop.key (string i)
                        prop.className "w"
                        prop.tabIndex -1
                        prop.custom ("data-w", m)
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            let target = e.currentTarget :?> Element
                            dispatch (Reader_(WordClicked(m, box (target.getBoundingClientRect ()), segRefOf target))))
                        prop.children [
                            for (pi, (t, ko)) in List.indexed pieces ->
                                match ko with
                                | Some k when k < s.Long.Length ->
                                    Html.span [
                                        prop.key (string pi)
                                        prop.classes [
                                            "syl"
                                            (if s.Long.[k] then "long" else "short")
                                            if s.FootEnd.[k] && (pi = pieces.Length - 1 || (match pieces.[pi + 1] with (_, Some k2) -> k2 <> k | _ -> true)) then "fe"
                                            if caesura = Some k && pi = pieces.Length - 1 then "cz"
                                            if playing = Some k then "now"
                                        ]
                                        prop.custom ("data-q", Lenses.Prosody.mark s.Long.[k])
                                        prop.text t
                                    ]
                                | _ -> Html.text t
                        ]
                    ]
                )
            pos <- idx + m.Length
        if pos < text.Length then out.Add(Html.text (text.Substring pos))
        out.Add(
            Html.button [
                prop.key "play"
                prop.className "line-play"
                prop.title (if playing.IsSome then "Stop" else "Play this line's rhythm")
                prop.ariaLabel (if playing.IsSome then "Stop" else "Play line")
                prop.text (if playing.IsSome then "■" else "▶")
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    dispatch (Reader_(if playing.IsSome then StopPlayback else PlayLine(ctx.SegRef, line))))
            ])
        Some(List.ofSeq out)

let private verseLine (dispatch: Msg -> unit) (greek: bool) (meter: MeterCtx option) (initial: bool) (bi: int) (flat: int) (i: int) (n: string option, t: string) : ReactElement =
    let show = shouldShowLineNumber bi i n
    let numText = if show then (n |> Option.defaultValue "") else ""
    let body =
        match meter with
        | Some ctx when greek -> scannedTokens dispatch ctx (flat + i) t |> Option.defaultWith (fun () -> tokens dispatch greek t)
        | _ -> tokensWith dispatch greek (initial && i = 0) t
    Html.span [
        prop.key (string bi + "-" + string i)
        prop.className "line"
        prop.children ([ Html.span [ prop.className "n"; prop.text numText ] ] @ body)
    ]

/// `initial` marks the first passage of a part: its first text block (not a
/// heading) opens with the ochre initial.
let private renderBlocksWith (dispatch: Msg -> unit) (greek: bool) (meter: MeterCtx option) (initial: bool) (blocks: Block list) : ReactElement list =
    // Line numbering across the segment's verse blocks, so a line's index here
    // matches the one PlayLine counts.
    let offsets =
        blocks
        |> List.scan (fun acc b -> match b with Verse(_, ls) -> acc + ls.Length | _ -> acc) 0
    let firstText = blocks |> List.tryFindIndex (function Heading _ -> false | _ -> true)
    blocks
    |> List.mapi (fun bi b -> bi, b)
    |> List.collect (fun (bi, b) ->
        let ini = initial && greek && firstText = Some bi
        match b with
        | Heading h -> [ Html.div [ prop.key (string bi); prop.className "head-block"; prop.text h ] ]
        | Prose(speaker, text) ->
            (speaker |> Option.map (fun sp -> Html.span [ prop.key (string bi + "-sp"); prop.className "speaker"; prop.text sp ]) |> Option.toList)
            @ [ Html.p [ prop.key (string bi); prop.children (tokensWith dispatch greek ini text) ] ]
        | Verse(speaker, lines) ->
            (speaker |> Option.map (fun sp -> Html.span [ prop.key (string bi + "-sp"); prop.className "speaker"; prop.text sp ]) |> Option.toList)
            @ (lines |> List.mapi (verseLine dispatch greek meter ini bi offsets.[bi])))

let private renderBlocks (dispatch: Msg -> unit) (greek: bool) (blocks: Block list) : ReactElement list =
    renderBlocksWith dispatch greek None false blocks

// ---------------------------------------------------------------------------
// keyboard access to words
// ---------------------------------------------------------------------------

[<Emit("Array.from($0.querySelectorAll('.w'))")>]
let private wordsIn (el: Element) : HTMLElement array = jsNative

/// One Tab stop per passage: the Greek column itself. Inside it, ← → move
/// between words (Home / End to either end), and Enter or Space looks the
/// focused word up exactly as a click would. Handled keys stop here, so the
/// page-level ← → (previous / next part) does not fire as well.
let private wordKeys (dispatch: Msg -> unit) (e: KeyboardEvent) =
    let col = e.currentTarget :?> Element
    let words = wordsIn col
    let target = e.target :?> HTMLElement
    let at = words |> Array.tryFindIndex (fun w -> obj.ReferenceEquals(w, target))
    let focusAt (i: int) =
        e.preventDefault ()
        e.stopPropagation ()
        words.[i].focus ()
    if words.Length > 0 then
        match e.key, at with
        | "ArrowRight", None -> focusAt 0
        | "ArrowRight", Some i -> focusAt (min (i + 1) (words.Length - 1))
        | "ArrowLeft", None -> focusAt 0
        | "ArrowLeft", Some i -> focusAt (max (i - 1) 0)
        | "Home", _ -> focusAt 0
        | "End", _ -> focusAt (words.Length - 1)
        | ("Enter" | " "), Some _ ->
            e.preventDefault ()
            e.stopPropagation ()
            let word = target.getAttribute "data-w"
            dispatch (Reader_(WordClicked(word, box (target.getBoundingClientRect ()), segRefOf target)))
        | _ -> ()

// ---------------------------------------------------------------------------
// work head: title, favourite, meta, edition pickers
// ---------------------------------------------------------------------------

/// A short name for the edition in hand, for the collapsed picker line.
///
/// `Desc` is a full printed-edition citation — far too long to sit above the
/// text — and it opens with the *author*, which is the one thing that cannot
/// distinguish two editions of the same work. What identifies an edition to a
/// reader is its editor or translator, so pull that surname out (plus a year
/// where the imprint carries one): "Monro 1920", "Murray 1924".
let private editionShortLabel (t: TextMeta) : string =
    let raw = (t.Desc |> Option.defaultValue t.Label) |> fun s -> if s = "" then urnSuffix t.Urn else s
    let reName =
        System.Text.RegularExpressions.Regex.Match(raw, @"([\p{L}'’\-]{2,}),\s*(?:[^,;]*,\s*)?(?:editor|translator)")
    let year =
        let m = System.Text.RegularExpressions.Regex.Match(raw, @"\b(1[5-9]\d\d|20[0-2]\d)\b")
        if m.Success then " " + m.Groups.[1].Value else ""
    if reName.Success then
        reName.Groups.[1].Value + year
    else
        // No named editor: fall back to the first substantive clause of the
        // citation rather than the bare author name.
        let head =
            raw.Split('.')
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (fun s -> s.Length > 3)
            |> Array.tryHead
            |> Option.defaultValue (raw.Trim())
        if head.Length > 42 then head.Substring(0, 42).TrimEnd() + "…" else head

/// The `<select>` that holds these options carries the selection in its own
/// `value`; React warns if an option also says `selected`.
let private editionOption (t: TextMeta) : ReactElement =
    let label = (t.Desc |> Option.defaultValue t.Label) |> fun s -> if s = "" then urnSuffix t.Urn else s
    let label = if label.Length > 110 then label.Substring(0, 110) else label
    let langTag = if t.Lang <> "grc" && t.Lang <> "eng" then " [" + langName t.Lang + "]" else ""
    Html.option [
        prop.value t.Urn
        prop.text (label + langTag + " · " + fmtKb t.Kb)
    ]

let private workHead (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) : ReactElement =
    let w = rm.Work
    let author = Catalog.authorOf model.Catalog w.Id
    let grcOptions = w.Texts |> List.filter (fun t -> t.Lang = "grc" || t.Kind = Edition)
    let trOptions = w.Texts |> List.filter (fun t -> t.Kind = Translation && t.Lang <> "grc")
    let repoLabel = Sources.repoName (Sources.repoKey rm.Grc.Repo)
    Html.div [
        prop.className "workhead"
        prop.children [
            Html.h1 [
                prop.children (
                    [ Shared.titleText w.Title ]
                    @ (match rm.Grc.Label with
                       | l when l <> "" && l <> w.Title -> [ Html.span [ prop.className "grc"; prop.text l ] ]
                       | _ -> [])
                    @ [ Shared.favButton model.Library w.Id dispatch ]
                )
            ]
            Html.div [
                prop.className "meta"
                prop.children (
                    [ Html.b [ prop.text (author |> Option.map (fun a -> a.Name) |> Option.defaultValue "") ]
                      Html.text (" · " + w.Id + " · " + repoLabel) ]
                    @ (if not (List.isEmpty rm.Grc.Refs) then [ Html.text (" · cited by " + String.concat "." rm.Grc.Refs) ] else [])
                    // Where *this* text came from, which in Local mode is not
                    // always what the source setting says: a text the connected
                    // copy lacks falls back to GitHub, and the reader should be
                    // able to see that rather than infer it.
                    @ (match rm.GrcOrigin with
                       | Some origin ->
                           let cls, text =
                               match origin with
                               | OriginLocalZip name -> "from-local", "read from " + name
                               | OriginLocalFolder name -> "from-local", "read from " + name
                               | OriginLocalFiles -> "from-local", "read from your chosen folder"
                               | OriginUrl b -> "from-local", "read from " + b
                               | OriginGitHub -> "from-net", "downloaded from GitHub"
                           [ Html.text " · "; Html.span [ prop.className ("origin " + cls); prop.text text ] ]
                       | None -> [])
                    @ (match WikiData.workArticles.TryFind w.Id, author with
                       | Some art, Some a ->
                           let hash = Router.toHash (AuthorRoute(a.Id, Some w.Id))
                           [ Html.text " · "
                             Html.a [
                                 prop.className "wa-link"
                                 prop.href hash
                                 prop.text ("About the " + art.Title)
                                 prop.onClick (fun e ->
                                     e.preventDefault ()
                                     dispatch (Navigate(hash, false)))
                             ] ]
                       | _ -> [])
                )
            ]
            // Two full-width selects above every reading session outweighed the
            // text itself. Show the pair in hand as one quiet line; expand only
            // when the reader actually wants to change edition.
            Html.details [
                prop.className "ed-pick"
                prop.children [
                    Html.summary [
                        prop.children [
                            Html.text "Greek: "
                            Html.b [ prop.text (editionShortLabel rm.Grc) ]
                            Html.text " · Translation: "
                            Html.b [
                                prop.text (
                                    match rm.Eng with
                                    | Some t -> editionShortLabel t
                                    | None -> if List.isEmpty trOptions then "none available" else "none"
                                )
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "pickers"
                        prop.children [
                    Html.label [
                        prop.children [
                            Html.text "Greek "
                            Html.select [
                                prop.id "selGrc"
                                prop.value rm.Grc.Urn
                                prop.onChange (fun (urn: string) -> dispatch (Reader_(PickGrcEdition urn)))
                                prop.children [ for t in grcOptions -> editionOption t ]
                            ]
                        ]
                    ]
                    Html.label [
                        prop.children [
                            Html.text "Translation "
                            Html.select [
                                prop.id "selEng"
                                prop.value (rm.Eng |> Option.map (fun t -> t.Urn) |> Option.defaultValue "none")
                                prop.onChange (fun (urn: string) -> dispatch (Reader_(PickEngEdition urn)))
                                prop.children (
                                    [ Html.option [
                                          prop.value "none"
                                          prop.text (if List.isEmpty trOptions then "No translation available" else "None (Greek only)")
                                      ] ]
                                    @ [ for t in trOptions -> editionOption t ]
                                )
                            ]
                        ]
                    ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// status pane (loading / error)
// ---------------------------------------------------------------------------

let private errorMessage (sourceMode: SourceMode) (baseUrl: string) (err: LoadError) : string =
    match err with
    | NetworkBlocked ->
        match sourceMode with
        | SourceUrl -> "Nothing answered at " + (if baseUrl = "" then "this address" else baseUrl) + ". Check that the server is running and that it allows this page to read from it."
        | _ ->
            "Couldn't reach GitHub. Texts are downloaded from the Perseus and First1KGreek collections as you open them, so reading needs an internet connection. To read offline, connect a local copy under Settings › Text source."
    | HttpStatus code -> "HTTP " + string code
    | XmlParseFailed msg -> msg
    | ErrorMessage msg -> msg

let private statusPane (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) : ReactElement option =
    match rm.Phase with
    | Ready -> None
    | Loading status ->
        Some(
            Html.div [
                prop.className "status"
                prop.children [ Html.div [ prop.className "g"; prop.text (if rm.Grc.Label <> "" then rm.Grc.Label else rm.Work.Title) ]; Html.div [ prop.id "st"; prop.text status ] ]
            ]
        )
    | LoadFailed err ->
        let url = if model.Source.Mode = SourceGitHub then Sources.rawUrl rm.Grc else rm.Grc.File
        Some(
            Html.div [
                prop.className "status"
                prop.children [
                    Html.div [ prop.className "g"; prop.text (if rm.Grc.Label <> "" then rm.Grc.Label else rm.Work.Title) ]
                    Html.div [
                        prop.className "err"
                        prop.children [
                            Html.b [ prop.text "This text didn't load." ]
                            Html.br []
                            Html.text (errorMessage model.Source.Mode model.Source.BaseUrl err)
                            Html.br []
                            Html.br []
                            Html.text "File: "
                            Html.code [ prop.text url ]
                        ]
                    ]
                    Html.button [ prop.id "retry"; prop.text "Try again"; prop.onClick (fun _ -> dispatch (Reader_ RetryLoad)) ]
                    Html.text " "
                    Html.button [
                        prop.id "openSrc"
                        prop.text "Text source…"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            dispatch (Settings_ ToggleSettingsPane))
                    ]
                ]
            ]
        )

// ---------------------------------------------------------------------------
// page bar / cover note / column headings
// ---------------------------------------------------------------------------

/// One stepper: previous · a picker · next. Used for the parts of a work
/// (books, speeches, odes) and for the pages of a long part.
let private stepper (cls: string) (label: string) (noun: string) (options: (string * string) list) (current: int) (go: int -> unit) : ReactElement =
    let n = options.Length
    Html.div [
        prop.className ("pages " + cls)
        prop.role "group"
        prop.ariaLabel label
        prop.children [
            Html.span [ prop.className "pg-label"; prop.text label ]
            Html.button [
                prop.className "pg-step"
                prop.ariaLabel ("Previous " + noun)
                prop.title ("Previous " + noun)
                prop.disabled (current <= 0)
                prop.text "‹"
                prop.onClick (fun _ -> go (current - 1))
            ]
            Html.label [
                prop.className "pg-pick"
                prop.children [
                    Html.select [
                        prop.ariaLabel label
                        prop.value (string current)
                        prop.onChange (fun (v: string) -> go (int v))
                        prop.children [ for i, (_, text) in List.indexed options -> Html.option [ prop.key (string i); prop.value (string i); prop.text text ] ]
                    ]
                ]
            ]
            Html.button [
                prop.className "pg-step"
                prop.ariaLabel ("Next " + noun)
                prop.title ("Next " + noun)
                prop.disabled (current >= n - 1)
                prop.text "›"
                prop.onClick (fun _ -> go (current + 1))
            ]
        ]
    ]

/// Where you are in the work: which part (a book of the Iliad, a speech), and
/// for a long part which page of it. Parts were reached from the sidebar
/// before it was retired; this puts them above the text.
let private readerNav (rm: ReaderModel) (dispatch: Msg -> unit) (d: AlignedText) (chunk: Chunk) (currentPage: int) : ReactElement option =
    let unit =
        match rm.Grc.Refs with
        | first :: _ :: _ when first <> "" -> first.Substring(0, 1).ToUpperInvariant() + first.Substring 1
        | _ -> "Part"
    let parts =
        if d.Chunks.Length <= 1 then None
        else
            let ci = d.Chunks |> List.tryFindIndex (fun c -> c.Ref = chunk.Ref) |> Option.defaultValue 0
            let options = d.Chunks |> List.mapi (fun i c -> c.Ref, sprintf "%s of %d" c.Ref d.Chunks.Length)
            Some(stepper "parts" unit (unit.ToLowerInvariant()) options ci (fun i -> dispatch (Reader_(ShowChunk(d.Chunks.[i].Ref, None, None)))))
    let npages = int (ceil (float chunk.Segments.Length / float PAGE_SIZE))
    let pages =
        if npages <= 1 then None
        else
            let options = [ for i in 0 .. npages - 1 -> string i, sprintf "%d of %d, from %s" (i + 1) npages chunk.Segments.[i * PAGE_SIZE].Ref ]
            Some(stepper "pages-of-part" "Page" "page" options currentPage (fun i -> dispatch (Reader_(ShowChunk(chunk.Ref, None, Some i)))))
    match parts, pages with
    | None, None -> None
    | a, b -> Some(Html.div [ prop.className "reader-nav"; prop.children (Option.toList a @ Option.toList b) ])

let private coverNote (coverage: Coverage option) : ReactElement option =
    match coverage with
    | Some c when c.Covered < c.Total ->
        Some(
            Html.div [
                prop.className "cover-note"
                prop.children (
                    if c.Covered > 0 then
                        [ Html.text "This translation covers "
                          Html.b [ prop.text (string c.Covered + " of " + string c.Total) ]
                          Html.text (
                              " passages, from "
                              + (c.First |> Option.defaultValue "")
                              + " to "
                              + (c.Last |> Option.defaultValue "")
                              + ". Elsewhere you'll see the Greek on its own."
                          ) ]
                    else
                        [ Html.text "This translation couldn't be lined up with the Greek. The two editions probably divide the work differently." ]
                )
            ]
        )
    | _ -> None

let private columnHead (rm: ReaderModel) : ReactElement =
    Html.div [
        prop.className "colhead"
        prop.children (
            [ Html.span [ prop.text ("Greek · " + urnSuffix rm.Grc.Urn) ] ]
            @ (rm.Eng |> Option.map (fun e -> Html.span [ prop.text (langName e.Lang + " · " + urnSuffix e.Urn) ]) |> Option.toList)
        )
    ]

/// The quiet row of study tools above the text: a switch for scansion marks
/// (verse only) and one entry to each lens, about the passage on screen.
let private lensBar (rm: ReaderModel) (isVerse: bool) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "lens-bar"
        prop.children [
            yield Html.span [ prop.className "lb-label"; prop.text "Study" ]
            for k in Lens.allLenses do
                yield Html.button [
                    prop.key (Lens.lensName k)
                    prop.className (if rm.Lens |> Option.exists (fun l -> l.Kind = k) then "on" else "")
                    prop.text (Lens.lensName k)
                    prop.onClick (fun _ ->
                        match rm.Lens with
                        | Some l when l.Kind = k -> dispatch (Reader_ CloseLens)
                        | Some l -> dispatch (Reader_(OpenLens(k, l.Seg)))
                        | None -> dispatch (Reader_(OpenLens(k, None))))
                ]
            if isVerse then
                yield Html.label [
                    prop.className "lb-meter"
                    prop.title "Mark every syllable long or short"
                    prop.children [
                        Html.input [ prop.type'.checkbox; prop.isChecked rm.MeterOn; prop.onChange (fun (_: bool) -> dispatch (Reader_ ToggleMeter)) ]
                        Html.text " Scansion"
                    ]
                ]
        ]
    ]

// ---------------------------------------------------------------------------
// bookmark box (read-only note + backlinks) and editor
// ---------------------------------------------------------------------------

let private markBox (model: Model) (dispatch: Msg -> unit) (mark: Mark option) (backlinks: Mark list) : ReactElement =
    let hasNoteContent = mark |> Option.map (fun m -> m.Note <> "" || not (List.isEmpty m.Links)) |> Option.defaultValue false
    if not hasNoteContent && List.isEmpty backlinks then
        Html.none
    else
        Html.div [
            prop.className "mk-box"
            prop.children [
                match mark with
                | Some m when hasNoteContent ->
                    Html.div [
                        prop.className "mk-note"
                        prop.children (
                            (Shared.noteBody model.Catalog dispatch m.Note)
                            @ (if List.isEmpty m.Links then
                                   []
                               else
                                   [ Html.div [
                                         prop.className "mk-links"
                                         prop.children ([ Html.text "See also: " ] @ interspersed (Html.text " ") (m.Links |> List.map (Shared.linkChip model.Catalog dispatch None)))
                                     ] ])
                            // Opens the same editor as the bookmark button, with
                            // links and "Remove bookmark", in place of this box.
                            @ [ Html.div [
                                    prop.className "note-edit-row"
                                    prop.children [
                                        Shared.editNoteButton (m.Note <> "") (fun () ->
                                            dispatch (Library_(OpenMarkEditor(m.Work, m.Ref, m.Label, m.Snippet))))
                                    ]
                                ] ]
                        )
                    ]
                | _ -> Html.none
                if not (List.isEmpty backlinks) then
                    Html.div [
                        prop.className "mk-back"
                        prop.children (
                            [ Html.text "Referenced from: " ]
                            @ interspersed
                                (Html.text " · ")
                                (backlinks
                                 |> List.map (fun b ->
                                     let title = Catalog.workTitle model.Catalog b.Work |> Option.defaultValue b.Work
                                     let hash = Router.toHash (ReaderRoute(b.Work, "", "", None, (if b.Ref = "" then None else Some b.Ref)))
                                     Html.a [
                                         prop.className "xref"
                                         prop.href "#"
                                         prop.text (title + " " + (if b.Label <> "" then b.Label else b.Ref))
                                         prop.onClick (fun e ->
                                             e.preventDefault ()
                                             dispatch (Navigate(hash, false)))
                                     ]))
                        )
                    ]
            ]
        ]

let private workOptionsFor (model: Model) (current: string) : ReactElement list =
    let recentIds = LibraryData.recentWorkIds (Some current) model.Library
    let label (w: Work) =
        let authorName = Catalog.authorOf model.Catalog w.Id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
        authorName + " — " + w.Title
    let opt (w: Work) =
        Html.option [ prop.value w.Id; prop.text (label w + (if w.Id = current then " (this work)" else "")) ]
    let recentWorks = recentIds |> List.choose model.Catalog.WorkById.TryFind
    let allWorks = model.Catalog.Authors |> List.collect (fun a -> a.Works)
    (if List.isEmpty recentWorks then
         []
     else
         [ Html.optgroup [ prop.label "This work, favourites & recent"; prop.children [ for w in recentWorks -> opt w ] ] ])
    @ [ Html.optgroup [ prop.label "All works"; prop.children [ for w in allWorks -> opt w ] ] ]

let private markEditor (model: Model) (dispatch: Msg -> unit) (workId: string) (segRef: string) (mark: Mark option) : ReactElement =
    let note = mark |> Option.map (fun m -> m.Note) |> Option.defaultValue ""
    let links = mark |> Option.map (fun m -> m.Links) |> Option.defaultValue []
    let others =
        model.Library.Marks
        |> List.filter (fun m -> not (m.Work = workId && m.Ref = segRef))
        |> List.sortByDescending (fun m -> m.Ts)

    let mutable workSelectEl: HTMLSelectElement = Unchecked.defaultof<_>
    let mutable refInputEl: HTMLInputElement = Unchecked.defaultof<_>

    let addLink (linkWork: string) (linkRef: string) (label: string option) =
        dispatch (Library_(AddMarkLink(workId, segRef, { Work = linkWork; Ref = linkRef; Label = label })))

    Html.div [
        prop.className "mk-edit"
        prop.children [
            Html.textarea [
                prop.rows 4
                prop.placeholder "Write a note about this passage."
                prop.defaultValue note
                prop.onChange (fun (v: string) -> dispatch (Library_(SetMarkNote(workId, segRef, v))))
                // Saved as you type; Enter closes the editor like "Done".
                Shared.onEnterSave (fun () -> dispatch (Library_ CloseMarkEditor))
            ]
            Shared.enterHint "to finish"
            Html.div [
                prop.className "mk-linked"
                prop.children [
                    Html.span [ prop.className "sc-label"; prop.text "Linked passages" ]
                    Html.div [
                        prop.className "mk-chips"
                        prop.children (
                            if List.isEmpty links then
                                [ Html.span [ prop.className "quiet"; prop.text "None yet." ] ]
                            else
                                interspersed (Html.text " ") (links |> List.map (Shared.linkChip model.Catalog dispatch (Some(workId, segRef))))
                        )
                    ]
                ]
            ]
            Html.div [
                prop.className "mk-tools"
                prop.children [
                    Html.label [
                        prop.className "mk-lbl"
                        prop.children [
                            Html.text "Add a link to "
                            Html.select [
                                prop.className "mk-saved"
                                prop.onChange (fun (v: string) ->
                                    if v <> "" then
                                        match v.Split('|') with
                                        | [| w; r; l |] -> addLink w r (if l = "" then None else Some l)
                                        | _ -> ())
                                prop.children (
                                    [ Html.option [
                                          prop.value ""
                                          prop.text (if List.isEmpty others then "(no other saved passages yet)" else "one of your saved passages…")
                                      ] ]
                                    @ [ for x in others ->
                                            let title = Catalog.workTitle model.Catalog x.Work |> Option.defaultValue x.Work
                                            let lbl = title + " " + (if x.Label <> "" then x.Label else x.Ref)
                                            Html.option [ prop.value (x.Work + "|" + x.Ref + "|" + lbl); prop.text lbl ] ]
                                )
                            ]
                        ]
                    ]
                    Html.details [
                        prop.className "mk-more"
                        prop.children [
                            Html.summary [ prop.text "…or to a passage by its number" ]
                            Html.div [
                                prop.className "mk-row"
                                prop.children [
                                    Html.select [
                                        prop.className "mk-work"
                                        prop.defaultValue workId
                                        prop.ref (fun el -> if not (isNullOrUndefined el) then workSelectEl <- el :?> HTMLSelectElement)
                                        prop.children (workOptionsFor model workId)
                                    ]
                                    Html.input [
                                        prop.className "mk-ref"
                                        prop.placeholder "e.g. 1.33"
                                        prop.ariaLabel "Passage reference"
                                        prop.ref (fun el -> if not (isNullOrUndefined el) then refInputEl <- el :?> HTMLInputElement)
                                    ]
                                    Html.button [
                                        prop.className "btn mk-ins"
                                        prop.text "Add"
                                        prop.onClick (fun _ ->
                                            let r = refInputEl.value.Trim()
                                            if r = "" then
                                                dispatch (ShowToast "Type the passage number first")
                                            else
                                                let w = workSelectEl.value
                                                let title = Catalog.workTitle model.Catalog w |> Option.defaultValue w
                                                addLink w r (Some(title + " " + r))
                                                refInputEl.value <- "")
                                    ]
                                ]
                            ]
                            Html.div [
                                prop.className "quiet"
                                prop.text "The work you're reading is already selected. Just type the passage number shown beside the text."
                            ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "mk-actions"
                prop.children [
                    Html.button [ prop.className "btn primary mk-save"; prop.text "Done"; prop.onClick (fun _ -> dispatch (Library_ CloseMarkEditor)) ]
                    Html.text " "
                    Html.button [ prop.className "btn danger mk-del"; prop.text "Remove bookmark"; prop.onClick (fun _ -> dispatch (Library_(RemoveMark(workId, segRef)))) ]
                    Html.button [
                        prop.className "btn mk-discuss"
                        prop.title "Start a thread about this passage in the forum"
                        prop.onClick (fun _ -> dispatch (Forum_(StartThread("passages", workId, segRef))))
                        prop.children [ Shared.icon "forum"; Html.text " Discuss" ]
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// one segment
// ---------------------------------------------------------------------------

let private segmentView (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (coverage: Coverage option) (initial: bool) (seg: Segment) : ReactElement =
    let workId = rm.Work.Id
    let mark = LibraryData.markFor model.Library workId seg.Ref
    let backlinks = LibraryData.backlinks model.Library workId seg.Ref
    let isEditing = rm.OpenEditor = Some(workId, seg.Ref)
    let isFlash = rm.FlashSeg = Some seg.Ref
    let urnText = rm.Grc.Urn + ":" + seg.Ref
    // Verse already has a line-number gutter carrying its structure, so it does
    // not also need a rule under every segment — see `.seg[data-verse]`.
    let isVerse = seg.Grc |> List.exists (function Verse _ -> true | _ -> false)

    Html.section [
        prop.key seg.Ref
        prop.id ("s-" + seg.Ref)
        prop.custom ("data-ref", seg.Ref)
        prop.custom ("data-verse", (if isVerse then "1" else ""))
        prop.tabIndex -1
        prop.className ("seg" + (if isFlash then " flash" else "") + (if isMarkerOnly seg && mark.IsNone && List.isEmpty backlinks && not isEditing then " seg-empty" else ""))
        prop.children [
            Html.div [
                prop.className "ref"
                prop.children [
                    // The number copies the citation too: the URN beside it
                    // only shows on hover, which phones do not have.
                    Html.b [
                        prop.role "button"
                        prop.tabIndex 0
                        prop.title ("Copy the citation: " + urnText)
                        prop.text (segLabel seg)
                        prop.onClick (fun _ -> dispatch (Reader_(CopyUrn urnText)))
                        prop.onKeyDown (fun e ->
                            if e.key = "Enter" || e.key = " " then
                                e.preventDefault ()
                                e.stopPropagation ()
                                dispatch (Reader_(CopyUrn urnText)))
                    ]
                    Html.span [
                        prop.className "urn"
                        prop.title "Copy CTS URN"
                        prop.text urnText
                        prop.onClick (fun _ -> dispatch (Reader_(CopyUrn urnText)))
                    ]
                    Html.button [
                        prop.className ("mk-btn" + (if mark.IsSome then " on" else ""))
                        prop.title (if mark.IsSome then "Bookmarked — click for note" else "Bookmark this passage")
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            if isEditing then
                                dispatch (Library_ CloseMarkEditor)
                            else
                                dispatch (Library_(OpenMarkEditor(workId, seg.Ref, segLabel seg, plainGrcText seg.Grc))))
                        prop.children [ Shared.icon "bookmark" ]
                    ]
                    // Opens the study rail on this passage, in whichever lens
                    // was last used (Echoes the first time).
                    let lensHere = rm.Lens |> Option.exists (fun l -> l.Seg = Some seg.Ref)
                    Html.button [
                        prop.className ("ln-btn" + (if lensHere then " on" else ""))
                        prop.title (if lensHere then "Close the study lenses" else "Study this passage — echoes, words, meter, map, manuscript")
                        prop.ariaLabel "Study this passage"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            dispatch (Reader_(StudyPassage seg.Ref)))
                        prop.children [ Shared.icon "study" ]
                    ]
                ]
            ]
            Html.div [
                prop.className "col grc"
                prop.lang "grc"
                prop.custom ("data-sans", (if model.Settings.Face = Sans then "1" else ""))
                prop.tabIndex 0
                prop.ariaDescribedBy "wordNavHint"
                prop.onKeyDown (wordKeys dispatch)
                prop.children (
                    let meter =
                        if rm.MeterOn && isVerse then
                            match Lens.chunkMeters rm with
                            | [] -> None
                            | ms -> Some { Meters = ms; SegRef = seg.Ref; Playhead = rm.Playhead }
                        else None
                    renderBlocksWith dispatch true meter (initial && meter.IsNone) seg.Grc
                )
            ]
            (match seg.Eng, rm.Eng with
             | Some engBlocks, Some engMeta -> Html.div [ prop.className "col eng"; prop.lang engMeta.Lang; prop.children (renderBlocks dispatch false engBlocks) ]
             | None, Some _ ->
                 let hint =
                     match coverage with
                     | Some c when c.Covered > 0 -> "It runs from " + (c.First |> Option.defaultValue "") + " to " + (c.Last |> Option.defaultValue "") + "."
                     | _ -> "It has nothing that lines up with this passage."
                 Html.div [
                     prop.className "col eng none"
                     prop.children [ Html.b [ prop.text "Not in this translation" ]; Html.text hint ]
                 ]
             | _ -> Html.none)
            if not isEditing then markBox model dispatch mark backlinks
            if isEditing then markEditor model dispatch workId seg.Ref mark
        ]
    ]

// ---------------------------------------------------------------------------
// memoised passages
// ---------------------------------------------------------------------------

/// What a passage's rendering depends on. `Model` and `Rm` are carried so the
/// passage can render, but equality is judged on the fields below them: the
/// reader re-renders on every scroll report, and without this each report
/// rebuilt all 250 passages on the page — several thousand word spans, and
/// with scansion showing, several thousand more.
type private SegProps =
    { Model    : Model
      Rm       : ReaderModel
      Dispatch : Msg -> unit
      Coverage : Coverage option
      Seg      : Segment
      Mark     : Mark option
      Backs    : Mark list
      Editing  : bool
      Flash    : bool
      LensHere : bool
      MeterOn  : bool
      Meters   : string list
      Playhead : (string * int * int) option     // only when it is in this passage
      Face     : Typeface
      EngUrn   : string
      Initial  : bool }                           // first passage of the part: ochre initial

let private sameSeg (a: SegProps) (b: SegProps) : bool =
    // An open editor holds live input; never skip rendering it.
    not a.Editing && not b.Editing
    && obj.ReferenceEquals(a.Seg, b.Seg)
    && a.Mark = b.Mark && a.Backs = b.Backs
    && a.Flash = b.Flash && a.LensHere = b.LensHere
    && a.MeterOn = b.MeterOn && a.Meters = b.Meters && a.Playhead = b.Playhead
    && a.Face = b.Face && a.EngUrn = b.EngUrn && a.Coverage = b.Coverage
    && a.Initial = b.Initial

let private SegMemo =
    React.memo ((fun (p: SegProps) -> segmentView p.Model p.Rm p.Dispatch p.Coverage p.Initial p.Seg), sameSeg)

let private segment (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (coverage: Coverage option) (meters: string list) (initial: bool) (seg: Segment) : ReactElement =
    let props =
        { Initial = initial
          Model = model
          Rm = rm
          Dispatch = dispatch
          Coverage = coverage
          Seg = seg
          Mark = LibraryData.markFor model.Library rm.Work.Id seg.Ref
          Backs = LibraryData.backlinks model.Library rm.Work.Id seg.Ref
          Editing = (rm.OpenEditor = Some(rm.Work.Id, seg.Ref))
          Flash = (rm.FlashSeg = Some seg.Ref)
          LensHere = rm.Lens |> Option.exists (fun l -> l.Seg = Some seg.Ref)
          MeterOn = rm.MeterOn
          Meters = meters
          Playhead = (match rm.Playhead with Some(r, _, _) when r = seg.Ref -> rm.Playhead | _ -> None)
          Face = model.Settings.Face
          EngUrn = rm.Eng |> Option.map (fun e -> e.Urn) |> Option.defaultValue "" }
    React.memoRender (SegMemo, props, withKey = (fun p -> p.Seg.Ref))

// ---------------------------------------------------------------------------
// scroll rail + passage readout
// ---------------------------------------------------------------------------

/// The passage you are on, shown while the page is moving and faded out once it
/// settles — so it answers "where am I?" during a scroll without sitting over
/// the text the rest of the time.
let private passageReadout (model: Model) (rm: ReaderModel) (pageSegs: Segment array) : ReactElement =
    let current = rm.ScrollRef |> Option.bind (fun r -> pageSegs |> Array.tryFind (fun s -> s.Ref = r))
    match current with
    | None -> Html.none
    | Some seg ->
        let mark = LibraryData.markFor model.Library rm.Work.Id seg.Ref
        Html.div [
            prop.classes [ "scroll-readout"; if rm.ScrollLive then "show" ]
            prop.ariaLive.polite
            prop.children (
                [ Html.span [ prop.className "ro-label"; prop.text (segLabel seg) ] ]
                @ (match mark with
                   | Some m ->
                       [ Html.span [
                             prop.className "ro-mark"
                             prop.title (if m.Note <> "" then "You have a note here" else "You bookmarked this passage")
                             prop.text (if m.Note <> "" then "note" else "bookmarked")
                         ] ]
                   | None -> [])
            )
        ]

// ---------------------------------------------------------------------------
// pager
// ---------------------------------------------------------------------------

let private pager (dispatch: Msg -> unit) (d: AlignedText) (chunk: Chunk) (page: int) : ReactElement =
    let npages = int (ceil (float chunk.Segments.Length / float PAGE_SIZE))
    let ci = d.Chunks |> List.tryFindIndex (fun c -> c.Ref = chunk.Ref) |> Option.defaultValue 0
    let prev = if ci > 0 then Some d.Chunks.[ci - 1] else None
    let next = if ci < d.Chunks.Length - 1 then Some d.Chunks.[ci + 1] else None
    let multi = d.Chunks.Length > 1
    let label (r: string) = if r = "all" then "" else r
    Html.div [
        prop.className "pager"
        prop.children [
            Html.button [
                prop.id "prevC"
                prop.disabled (not (prev.IsSome || page > 0))
                prop.text ("← " + (if page > 0 then "Previous page" else (match prev with Some p -> label p.Ref | None -> "Start")))
                prop.onClick (fun _ -> if page > 0 then dispatch (Reader_(ShowChunk(chunk.Ref, None, Some(page - 1)))) else prev |> Option.iter (fun p -> dispatch (Reader_(ShowChunk(p.Ref, None, None)))))
            ]
            Html.span [ prop.text (string chunk.Segments.Length + " passages" + (if multi then " · " + string d.Chunks.Length + " parts" else "")) ]
            Html.button [
                prop.id "nextC"
                prop.disabled (not (next.IsSome || page < npages - 1))
                prop.text ((if page < npages - 1 then "Next page" else (match next with Some n -> label n.Ref | None -> "End")) + " →")
                prop.onClick (fun _ ->
                    if page < npages - 1 then dispatch (Reader_(ShowChunk(chunk.Ref, None, Some(page + 1))))
                    else next |> Option.iter (fun n -> dispatch (Reader_(ShowChunk(n.Ref, None, None)))))
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// top-level assembly
// ---------------------------------------------------------------------------

/// Verse and prose want different column widths: a hexameter needs about 57
/// characters and no more, while a prose translation reads better given the
/// full measure. Deciding this once for the whole chunk — rather than per
/// segment — keeps every segment's columns on the same grid lines as the
/// heading above them, which is the whole point of an aligned layout.
let private chunkShape (rm: ReaderModel) : string =
    match rm.Phase, rm.Data with
    | Ready, Some d ->
        let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = rm.Chunk) |> Option.defaultValue d.Chunks.Head
        let sample = chunk.Segments |> Array.truncate 40
        let isVerse (s: Segment) = s.Grc |> List.exists (function Verse _ -> true | _ -> false)
        let verseCount = sample |> Array.filter isVerse |> Array.length
        if sample.Length > 0 && verseCount * 2 > sample.Length then "verse" else "prose"
    | _ -> "prose"

/// The width, in ems of the Greek face, of this text's longest verse lines,
/// estimated from their length in characters (Gentium averages 0.43em a
/// letter). The stylesheet shrinks verse to fit that width in the column,
/// so a hexameter stays on one line on a phone instead of wrapping. The
/// 99.8th percentile ignores a stray over-long line (an editor's note run
/// into the verse), which may still wrap. Worked out once per text: the
/// whole work is thousands of lines.
let private verseWidths = System.Collections.Generic.Dictionary<string, float>()

let private markerRx = System.Text.RegularExpressions.Regex("⟦[^⟧]*⟧")

let private verseEm (rm: ReaderModel) (d: AlignedText) : float =
    let key = rm.Grc.Urn + "|" + string d.Segments.Length
    match verseWidths.TryGetValue key with
    | true, v -> v
    | _ ->
        let lengths =
            [| for s in d.Segments do
                   for b in s.Grc do
                       match b with
                       | Verse(_, lines) ->
                           for (_, t) in lines do
                               let n = markerRx.Replace(t, "").Trim().Length
                               if n > 0 then yield n
                       | _ -> () |]
            |> Array.sort
        let at (q: float) = float lengths.[min (lengths.Length - 1) (int (float lengths.Length * q))]
        // In a single metre (Homer, Hesiod) nearly every line is close to the
        // longest, so fit them all. Where long lines are a different metre
        // (lyric in tragedy, tetrameters in comedy), fitting them would shrink
        // the whole play for them; they wrap instead.
        let v = if lengths.Length = 0 then 0.0 else min (at 0.998) (at 0.95 * 1.12) * 0.43
        verseWidths.[key] <- v
        v

let render (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) : ReactElement =
    let shape = chunkShape rm
    let fit =
        match rm.Phase, rm.Data with
        | Ready, Some d -> verseEm rm d
        | _ -> 0.0
    Html.div [
        prop.className ("reader" + (if rm.Lens.IsSome then " with-lens" else "") + (if rm.MeterOn && shape = "verse" then " meter-on" else ""))
        prop.custom ("data-shape", shape)
        if fit > 0.0 then prop.style [ style.custom ("--verse-em", sprintf "%.2f" fit) ]
        prop.children (
            [ workHead model rm dispatch ]
            @ (statusPane model rm dispatch |> Option.toList)
            @ (match rm.Phase, rm.Data with
               | Ready, Some d ->
                   let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = rm.Chunk) |> Option.defaultValue d.Chunks.Head
                   let page = rm.Page
                   let pageSegs = chunk.Segments |> Array.skip (page * PAGE_SIZE) |> Array.truncate PAGE_SIZE
                   (readerNav rm dispatch d chunk page |> Option.toList)
                   @ (coverNote d.Coverage |> Option.toList)
                   @ [ lensBar rm (shape = "verse") dispatch ]
                   @ [ columnHead rm ]
                   @ (let meters = if rm.MeterOn then Lens.chunkMeters rm else []
                      // The initial goes on the first passage with words in it, not on
                      // a bare page marker such as Plato's "17" before 17a.
                      let firstReal = pageSegs |> Array.tryFindIndex (isMarkerOnly >> not) |> Option.defaultValue 0
                      [ for i, seg in Array.indexed pageSegs -> segment model rm dispatch d.Coverage meters (page = 0 && i = firstReal) seg ])
                   @ [ Html.p [
                           prop.id "wordNavHint"
                           prop.hidden true
                           prop.text "Use the left and right arrow keys to move between words, and Enter to look one up."
                       ]
                       pager dispatch d chunk page
                       passageReadout model rm pageSegs
                       Lens.render model rm dispatch ]
               | _ -> [])
        )
    ]
