module Views.Shared

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Types

// ---------------------------------------------------------------------------
// connecting a local copy
//
// Offered from two places — the settings sheet and the header's source menu —
// so the picker logic lives here rather than being written twice. Both must be
// called straight from a click: the File System Access API only opens a picker
// under a user gesture. Where it isn't available the hidden file inputs in the
// settings sheet stand in, which are in the DOM whether or not the sheet is open.
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// Enter finishes an edit
//
// In every box that can hold more than one line (notes, forum posts), Enter
// finishes the edit: saves, closes or posts. Shift+Enter is how to start a new
// line. Keys pressed while an input method is composing (Greek polytonic
// keyboards, Japanese…) are left alone, or the first Enter of a composition
// would post half a word.
// ---------------------------------------------------------------------------

[<Emit("($0.isComposing || $0.keyCode === 229)")>]
let private composing (e: Browser.Types.KeyboardEvent) : bool = jsNative

let onEnterSave (finish: unit -> unit) : IReactProperty =
    prop.onKeyDown (fun (e: Browser.Types.KeyboardEvent) ->
        if e.key = "Enter" && not e.shiftKey && not (composing e) then
            e.preventDefault ()
            e.stopPropagation ()
            finish ())

/// In a form of several boxes: Enter goes on to the next box of the form.
[<Emit("(() => { const t = $0.target, f = t.closest('form, .f-form, .mk-edit'); if (!f) return; const els = [...f.querySelectorAll('input:not([type=hidden]):not([disabled]), textarea')]; const i = els.indexOf(t); if (i >= 0 && i < els.length - 1) els[i + 1].focus(); })()")>]
let private focusNextIn (e: Browser.Types.KeyboardEvent) : unit = jsNative

let onEnterNext : IReactProperty =
    prop.onKeyDown (fun (e: Browser.Types.KeyboardEvent) ->
        if e.key = "Enter" && not e.shiftKey && not (composing e) then
            e.preventDefault ()
            focusNextIn e)

/// The line under a multi-line box that says how its keys work.
let enterHint (what: string) : ReactElement =
    Html.span [ prop.className "enter-hint"; prop.text ("Enter " + what + " · Shift+Enter for a new line") ]

[<Emit("document.getElementById($0)?.click()")>]
let private clickElementById (id: string) : unit = jsNative

let connectZip (dispatch: Msg -> unit) : unit =
    if Sources.hasOpenFilePicker then
        Sources.pickZip ()
        |> Promise.map (function
            | Some(Ok(repo, name)) -> dispatch (Source_(ZipConnected(repo, name)))
            | Some(Error msg) -> dispatch (Source_(SourceFailed msg))
            | None -> ())
        |> Promise.start
    else
        clickElementById "zipInput"

let connectDir (dispatch: Msg -> unit) : unit =
    if Sources.hasDirectoryPicker then
        Sources.pickDir ()
        |> Promise.map (function
            | Some(Ok(repos, name)) -> dispatch (Source_(DirConnected(repos, name)))
            | Some(Error msg) -> dispatch (Source_(SourceFailed msg))
            | None -> ())
        |> Promise.start
    else
        clickElementById "dirInput"

// ---------------------------------------------------------------------------
// icons — built from Content's structured shape data, never raw SVG markup
// ---------------------------------------------------------------------------

let iconShapes (shapes: Content.IconShape list) : ReactElement =
    Svg.svg [
        svg.viewBox (0, 0, 24, 24)
        svg.className "icon"
        svg.custom ("aria-hidden", "true")
        svg.children [
            for shape in shapes do
                match shape with
                | Content.IPath d -> Svg.path [ svg.d d ]
                | Content.ICircle(cx, cy, r) -> Svg.circle [ svg.cx cx; svg.cy cy; svg.r r ]
                | Content.IFill d -> Svg.path [ svg.d d; svg.className "fill" ]
        ]
    ]

/// An icon from the app's one set (`Content.icons`), by name.
let icon (name: string) : ReactElement =
    match Content.icons.TryFind name with
    | None -> Html.none
    | Some ic -> iconShapes ic.Shapes

// ---------------------------------------------------------------------------
// boot overlay
// ---------------------------------------------------------------------------

let bootOverlay (boot: BootState) : ReactElement =
    match boot with
    | Booted -> Html.none
    | Booting msg ->
        Html.div [
            prop.id "loading"
            prop.children [
                Html.div [
                    prop.className "box"
                    prop.children [
                        Html.div [ prop.className "g"; prop.text "Μάθησις" ]
                        Html.div [ prop.id "loadmsg"; prop.text msg ]
                    ]
                ]
            ]
        ]
    | BootFailed err ->
        Html.div [
            prop.id "loading"
            prop.children [
                Html.div [
                    prop.className "box"
                    prop.children [
                        Html.div [ prop.className "g"; prop.text "Μάθησις" ]
                        Html.div [
                            prop.id "loadmsg"
                            prop.children [
                                Html.div [ prop.className "err"; prop.text ("Could not open the catalogue: " + err) ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

// ---------------------------------------------------------------------------
// toast / backdrop
// ---------------------------------------------------------------------------

/// Dismissible, so a message that lingers long enough to be read can also be
/// got rid of early. The close button carries the toast's own id, so a click
/// landing just as a newer toast replaces this one cannot dismiss that one.
let toast (toastState: (int * string) option) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.id "toast"
        prop.className ("toast" + (if toastState.IsSome then " show" else ""))
        prop.role "status"
        prop.ariaLive.polite
        prop.children [
            Html.span [ prop.className "toast-msg"; prop.text (toastState |> Option.map snd |> Option.defaultValue "") ]
            match toastState with
            | Some(id, _) ->
                Html.button [
                    prop.className "toast-x"
                    prop.ariaLabel "Dismiss"
                    prop.title "Dismiss"
                    prop.text "×"
                    prop.onClick (fun _ -> dispatch (HideToast id))
                ]
            | None -> Html.none
        ]
    ]

// ---------------------------------------------------------------------------
// favourites
// ---------------------------------------------------------------------------

/// A work's title as text, marked as Greek when the catalogue has only a
/// Greek title for it (many of the grammarians' and Church Fathers' works),
/// so it is set in the Greek face rather than the interface's sans.
let isGreekScript (t: string) : bool =
    t.Length > 0
    && (let c = int t.[0] in (c >= 0x0370 && c <= 0x03FF) || (c >= 0x1F00 && c <= 0x1FFF))

let titleText (t: string) : ReactElement =
    if isGreekScript t then Html.span [ prop.className "t-grc"; prop.lang "grc"; prop.text t ] else Html.text t

let favButton (lib: Library) (workId: string) (dispatch: Msg -> unit) : ReactElement =
    let on = LibraryData.isFav lib workId
    Html.button [
        prop.className ("fav-btn" + (if on then " on" else ""))
        prop.custom ("data-w", workId)
        prop.title (if on then "Remove from favourites" else "Add to favourites")
        prop.onClick (fun e ->
            e.stopPropagation ()
            dispatch (Library_(ToggleFav workId)))
        prop.children [
            icon "olive"
            Html.span [ prop.text (if on then "Favourite" else "Add to favourites") ]
        ]
    ]

// ---------------------------------------------------------------------------
// cross-reference chips and inline note rendering
// ---------------------------------------------------------------------------

let private xrefHash (link: MarkLink) : string =
    Router.toHash (ReaderRoute(link.Work, "", "", None, (if link.Ref = "" then None else Some link.Ref)))

let private xrefLabel (catalog: Catalog) (link: MarkLink) : string =
    match link.Label with
    | Some l -> l
    | None ->
        match Catalog.workTitle catalog link.Work with
        | Some t -> if link.Ref <> "" then t + " " + link.Ref else t
        | None -> if link.Ref <> "" then link.Work + ":" + link.Ref else link.Work

/// A clickable cross-reference chip, with an optional remove button (mirrors
/// `linkChip`). Returned as a fragment (not wrapped) so `.xref.chip` and
/// `.chip-x` stay direct siblings, matching the CSS's flex/gap layout.
let linkChip (catalog: Catalog) (dispatch: Msg -> unit) (removeCtx: (string * string) option) (link: MarkLink) : ReactElement =
    React.Fragment [
        Html.a [
            prop.className "xref chip"
            prop.href (Router.href (Router.toHash (ReaderRoute(link.Work, "", "", None, (if link.Ref = "" then None else Some link.Ref)))))
            prop.text (xrefLabel catalog link)
            prop.onClick (fun e ->
                e.preventDefault ()
                dispatch (Navigate(xrefHash link, false)))
        ]
        match removeCtx with
        | Some(workId, segRef) ->
            Html.button [
                prop.className "chip-x"
                prop.ariaLabel "Remove link"
                prop.text "×"
                prop.onClick (fun _ -> dispatch (Library_(RemoveMarkLink(workId, segRef, link.Work, link.Ref))))
            ]
        | None -> Html.none
    ]

/// Renders note text with inline `[[work:ref|label]]` cross-references turned
/// into clickable links and newlines turned into `<br>` (mirrors `renderNote`,
/// without ever touching innerHTML).
let noteBody (catalog: Catalog) (dispatch: Msg -> unit) (text: string) : ReactElement list =
    let renderLink (link: MarkLink) : ReactElement =
        Html.a [
            prop.className "xref"
            prop.href (Router.href (Router.toHash (ReaderRoute(link.Work, "", "", None, (if link.Ref = "" then None else Some link.Ref)))))
            prop.text (xrefLabel catalog link)
            prop.onClick (fun e ->
                e.preventDefault ()
                dispatch (Navigate(xrefHash link, false)))
        ]
    let renderTextRun (t: string) : ReactElement list =
        t.Split '\n'
        |> Array.toList
        |> List.mapi (fun i line -> if i = 0 then [ Html.text line ] else [ Html.br []; Html.text line ])
        |> List.collect id
    text
    |> LibraryData.tokenize
    |> List.collect (function
        | LibraryData.NoteLink link -> [ renderLink link ]
        | LibraryData.NoteText t -> renderTextRun t)

/// The small note editor used where the reader's full editor (links, removing
/// the bookmark) would not fit: the floating notes panel and My library.
/// It saves as you type, like the reader's own editor, so "Done" only closes it.
/// Keyed by the mark, so switching to another note starts from that note's text.
let noteEditor (dispatch: Msg -> unit) (mark: Mark) : ReactElement =
    let where = if mark.Label <> "" then mark.Label else mark.Ref
    Html.div [
        prop.key ("ne-" + mark.Id)
        prop.className "mk-edit note-edit"
        prop.children [
            Html.textarea [
                prop.rows 4
                prop.autoFocus true
                prop.ariaLabel ("Note on " + where)
                prop.placeholder "Write a note about this passage."
                prop.defaultValue mark.Note
                prop.onChange (fun (v: string) -> dispatch (Library_(SetMarkNote(mark.Work, mark.Ref, v))))
                onEnterSave (fun () -> dispatch (Library_(EditNote None)))
            ]
            Html.div [
                prop.className "note-edit-foot"
                prop.children [
                    Html.span [ prop.className "quiet"; prop.text "Saved as you type. Enter to finish · Shift+Enter for a new line" ]
                    Html.button [
                        prop.className "btn small primary"
                        prop.text "Done"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            dispatch (Library_(EditNote None)))
                    ]
                ]
            ]
        ]
    ]

/// The "Edit note" button, the same everywhere a note is shown. With no note
/// written yet it offers to add one instead.
let editNoteButton (hasNote: bool) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.className "btn small note-edit-btn"
        prop.text (if hasNote then "Edit note" else "Add note")
        prop.onClick (fun e ->
            e.stopPropagation ()
            onClick ())
    ]

// ---------------------------------------------------------------------------
// wiki breadcrumbs
// ---------------------------------------------------------------------------

/// `parts` are (label, hash) pairs — a `None` hash renders as plain (current)
/// text, matching `crumbs()`.
let wikiCrumbs (dispatch: Msg -> unit) (parts: (string * string option) list) : ReactElement =
    Html.div [
        prop.className "wcrumbs"
        prop.children [
            Html.a [
                prop.href (Router.href "#wiki")
                prop.text "Wiki"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#wiki", false)))
            ]
            for label, hash in parts do
                Html.text " › "
                match hash with
                | Some h ->
                    Html.a [
                        prop.href (Router.href h)
                        prop.text label
                        prop.onClick (fun e ->
                            e.preventDefault ()
                            dispatch (Navigate(h, false)))
                    ]
                | None -> Html.span [ prop.text label ]
        ]
    ]

// ---------------------------------------------------------------------------
// work card (home / browse picks)
// ---------------------------------------------------------------------------

let workCard (catalog: Catalog) (dispatch: Msg -> unit) (work: Work) : ReactElement =
    let grc = Catalog.titleGrc work |> Option.filter (fun t -> t <> work.Title)
    let authorName = Catalog.authorOf catalog work.Id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
    Html.button [
        prop.className "pick"
        prop.onClick (fun _ -> dispatch (Navigate(Router.toHash (ReaderRoute(work.Id, "", "", None, None)), false)))
        prop.children [
            Html.span [ prop.className "grc"; prop.text (grc |> Option.defaultValue work.Title) ]
            match grc with
            | Some _ -> Html.span [ prop.className "en"; prop.text work.Title ]
            | None -> Html.none
            Html.span [
                prop.className "who"
                prop.text (authorName + (if Catalog.hasTranslation work then "" else " · Greek only"))
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// genre chips (the Library page + My library)
// ---------------------------------------------------------------------------

/// The genre a work belongs to, taken from its author (see `WikiData.genreOf`).
let genreOfWork (model: Model) (workId: string) : string =
    match Catalog.authorOf model.Catalog workId with
    | Some a -> WikiData.genreOfAuthor model.Meta a.Id
    | None -> "other"

/// One row of single-select filter chips, styled like the wiki's `.subcats`.
/// `counts` pairs a genre id with how many of the items on screen belong to it;
/// genres with nothing in them are left out, except the one currently chosen,
/// which stays so it can always be cleared. Choosing the current chip again,
/// or "All", clears the filter.
let genreChips (label: string) (counts: (string * int) list) (current: string option) (dispatch: Msg -> unit) : ReactElement =
    let countOf id = counts |> List.tryFind (fun (g, _) -> g = id) |> Option.map snd |> Option.defaultValue 0
    let items =
        (WikiData.genres |> List.map (fun g -> g.Id, g.Short)) @ [ "other", "Other" ]
        |> List.filter (fun (id, _) -> countOf id > 0 || current = Some id)
    let chip (key: string) (text: string) (n: int) (on: bool) (onClick: unit -> unit) =
        Html.button [
            prop.key key
            prop.className ("chip" + (if key = "all" then " all" else "") + (if on then " on" else ""))
            prop.custom ("aria-pressed", on)
            prop.title (if on && key <> "all" then "Show all genres" else "")
            prop.onClick (fun _ -> onClick ())
            prop.children [ Html.text (text + " "); Html.span [ prop.text (string n) ] ]
        ]
    Html.div [
        prop.className "subcats genre-chips"
        prop.role "group"
        prop.ariaLabel label
        prop.children [
            Html.span [ prop.className "sc-label"; prop.text label ]
            Html.div [
                prop.className "chips"
                prop.children (
                    chip "all" "All" (counts |> List.sumBy snd) current.IsNone (fun () -> dispatch (SetGenre None))
                    :: [ for id, text in items ->
                             let on = current = Some id
                             chip id text (countOf id) on (fun () -> dispatch (SetGenre(if on then None else Some id))) ]
                )
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// the ochre initial
// ---------------------------------------------------------------------------

/// Splits off a text's first letter as the capital a manuscript initial would
/// show. `toUpperCase` keeps a precomposed letter's breathing and accent
/// (ἄ → Ἄ), but turns a letter with iota subscript into two (ᾳ → ΑΙ); that one
/// stays as written rather than grow a letter.
let initialOf (text: string) : (string * string) option =
    if System.String.IsNullOrEmpty text then None
    else
        let first = text.Substring(0, 1)
        let upper = first.ToUpper()
        Some((if upper.Length = 1 then upper else first), text.Substring 1)

// ---------------------------------------------------------------------------
// the eras band (home + wiki home)
// ---------------------------------------------------------------------------

let private fmtEraYear (y: int) : string = if y < 0 then string (-y) + " BCE" else string y + " CE"

let private eraRange (from: int) (to_: int) : string =
    if to_ <= 0 then string (-from) + "–" + string (-to_) + " BCE"
    elif from < 0 then fmtEraYear from + " – " + fmtEraYear to_
    else string from + "–" + string to_ + " CE"

/// Every era as a column whose width is its length in years and whose height
/// is the number of works in the library by authors of that era, both to
/// scale. Works whose authors have no date form a column of their own, set
/// apart at the end (its width means nothing, only its height). The first era
/// has no agreed start; it is drawn from c. 800 BCE, where the library begins.
/// Every number is counted from the catalogue and the author data.
///
/// On phones the same chart is turned on its side: years run down the page
/// (each row as tall as its era is long) and the bars run across (as long as
/// the era has works).
let erasBand (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let eraOfAuthor (a: Author) = model.Meta.Authors.TryFind a.Id |> Option.bind (fun m -> m.Era)
    let eraIds = model.Meta.Eras |> List.map (fun e -> e.Id) |> Set.ofList
    let worksIn (eraId: string) =
        model.Catalog.Authors |> List.filter (fun a -> eraOfAuthor a = Some eraId) |> List.sumBy (fun a -> a.Works.Length)
    let eras =
        model.Meta.Eras
        |> List.map (fun e ->
            let from = if e.From = -9999 then -800 else e.From
            let to_ = if e.To = 9999 then 1600 else e.To
            e, from, to_, worksIn e.Id)
        |> List.filter (fun (_, from, to_, n) -> n > 0 && to_ > from)
    let allWorks = model.Catalog.Authors |> List.sumBy (fun a -> a.Works.Length)
    // no date, or a date outside the eras drawn
    let undated =
        model.Catalog.Authors
        |> List.filter (fun a ->
            match eraOfAuthor a with
            | Some id -> not (eraIds.Contains id) || not (eras |> List.exists (fun (e, _, _, _) -> e.Id = id))
            | None -> true)
        |> List.sumBy (fun a -> a.Works.Length)
    if List.isEmpty eras then Html.none
    else
        let first = eras |> List.map (fun (_, f, _, _) -> f) |> List.min
        let last = eras |> List.map (fun (_, _, t, _) -> t) |> List.max
        let span = float (last - first)
        let dated = eras |> List.sumBy (fun (_, _, _, n) -> n)
        let most = (undated :: (eras |> List.map (fun (_, _, _, n) -> n))) |> List.max |> float
        let inv = System.Globalization.CultureInfo.InvariantCulture
        let pct (x: float) = System.Math.Round(x * 100.0, 3).ToString(inv) + "%"
        let rem (x: float) = System.Math.Round(x, 3).ToString(inv) + "rem"
        // The years get 88% of the width; a gap and the undated column the rest.
        let yearsShare = if undated > 0 then 0.88 else 1.0
        let widthOf from to_ = pct (float (to_ - from) / span * yearsShare)
        let heightOf (n: int) = pct (float n / most)
        let name (e: Era) = System.Text.RegularExpressions.Regex.Replace(e.Name, @" \(.*\)", "")
        let works (n: int) = (if n = 1 then "1 work" else n.ToString("N0") + " works")
        let go (id: string) (ev: Browser.Types.MouseEvent) =
            ev.preventDefault ()
            dispatch (Navigate("#wiki/eras/" + id, false))
        // phones: 36rem of height for the whole span of years
        let rowHeight from to_ = rem (float (to_ - from) / span * 36.0)
        Html.figure [
            prop.className "eras-band"
            prop.children [
                Html.div [
                    prop.className "eb-cols"
                    prop.children [
                        for e, from, to_, n in eras do
                            Html.a [
                                prop.key e.Id
                                prop.className "eb-col"
                                prop.href (Router.href ("#wiki/eras/" + e.Id))
                                prop.style [ style.custom ("flexBasis", widthOf from to_) ]
                                prop.ariaLabel (name e + ", " + eraRange from to_ + ": " + works n)
                                prop.onClick (go e.Id)
                                prop.children [
                                    Html.b [ prop.style [ style.custom ("bottom", heightOf n) ]; prop.text (n.ToString("N0")) ]
                                    Html.i [ prop.style [ style.custom ("height", heightOf n) ] ]
                                ]
                            ]
                        if undated > 0 then
                            Html.span [ prop.key "gap"; prop.className "eb-gap"; prop.ariaHidden true ]
                            Html.div [
                                prop.key "undated"
                                prop.className "eb-col eb-undated"
                                prop.ariaLabel ("Authors of uncertain date: " + works undated)
                                prop.children [
                                    Html.b [ prop.style [ style.custom ("bottom", heightOf undated) ]; prop.text (undated.ToString("N0")) ]
                                    Html.i [ prop.style [ style.custom ("height", heightOf undated) ] ]
                                ]
                            ]
                    ]
                ]
                Html.div [
                    prop.className "eb-labels"
                    prop.ariaHidden true
                    prop.children [
                        for e, from, to_, _ in eras do
                            Html.div [
                                prop.key e.Id
                                prop.style [ style.custom ("flexBasis", widthOf from to_) ]
                                prop.children [ Html.span [ prop.text (name e) ]; Html.text (eraRange from to_) ]
                            ]
                        if undated > 0 then
                            Html.span [ prop.key "gap"; prop.className "eb-gap" ]
                            Html.div [ prop.key "undated"; prop.className "eb-undated-l"; prop.children [ Html.span [ prop.text "Undated" ]; Html.text "no span" ] ]
                    ]
                ]
                // Phones: the same chart on its side.
                Html.div [
                    prop.className "eb-rows"
                    prop.children [
                        for e, from, to_, n in eras do
                            Html.a [
                                prop.key e.Id
                                prop.href (Router.href ("#wiki/eras/" + e.Id))
                                prop.onClick (go e.Id)
                                prop.style [ style.custom ("height", rowHeight from to_) ]
                                prop.children [
                                    Html.span [
                                        prop.className "eb-r-l"
                                        prop.children [ Html.b [ prop.text (name e) ]; Html.small [ prop.text (eraRange from to_) ] ]
                                    ]
                                    Html.span [
                                        prop.className "eb-r-bar"
                                        prop.children [
                                            Html.i [ prop.style [ style.custom ("width", heightOf n) ] ]
                                            Html.small [ prop.text (n.ToString("N0")) ]
                                        ]
                                    ]
                                ]
                            ]
                        if undated > 0 then
                            Html.div [
                                prop.key "undated"
                                prop.className "eb-r-undated"
                                prop.children [
                                    Html.span [ prop.className "eb-r-l"; prop.children [ Html.b [ prop.text "Undated" ]; Html.small [ prop.text "no span of years" ] ] ]
                                    Html.span [
                                        prop.className "eb-r-bar"
                                        prop.children [ Html.i [ prop.style [ style.custom ("width", heightOf undated) ] ]; Html.small [ prop.text (undated.ToString("N0")) ] ]
                                    ]
                                ]
                            ]
                    ]
                ]
                let counts =
                    sprintf " the number of works in the library by its authors: %s dated works in all%s. The first era is drawn from c. 800 BCE, where the library begins."
                        (dated.ToString("N0"))
                        (if undated > 0 then sprintf ", and %s by authors of uncertain date, shown apart at the end" (undated.ToString("N0")) else "")
                Html.figcaption [
                    prop.className "eb-note"
                    prop.children [
                        Html.span [ prop.className "eb-note-cols"; prop.text ("Each column's width is the length of its era in years; its height," + counts) ]
                        Html.span [ prop.className "eb-note-rows"; prop.text ("Each row's height is the length of its era in years; the bar's length," + counts) ]
                        if dated + undated <> allWorks then
                            Html.text (sprintf " (%s works in the library altogether.)" (allWorks.ToString("N0")))
                    ]
                ]
            ]
        ]

// ---------------------------------------------------------------------------
// collapsible section (phone-width home/wiki sections)
// ---------------------------------------------------------------------------

/// Sections collapsed by default on phones unless the user has toggled them —
/// mirrors COL_DEFAULT (every other key, including any not listed here, opens
/// by default).
/// On phones the home page opens with its sections folded (a standing
/// decision). The hero with Passage of the day ("pod"), How it works and the
/// offline setup stay open; How it works and the offline setup cannot fold at
/// all. State.fs uses this same function, so a tap always does what it shows.
let collapsedByDefault (key: string) : bool =
    match key with
    | "picks"
    | "wiki"
    | "paths"
    | "eras"
    | "alphabet"
    | "tips"
    | "corpus" -> true
    | _ -> false

/// Note: despite the Model field's name, the stored bool means "is open" (it's
/// the exact value persisted to `anag:colState` historically), not "is collapsed".
///
/// `sectionClass` is the section's own class(es) beyond "mcol" (e.g. "home-block",
/// or "home-card picks-card"); `headerClass` is the `<h2>`'s own class(es) beyond
/// "mhead" ("sh" for home-block sections, "" for home-card sections, whose header
/// carries an icon + optional "more" link instead of plain text) — the two kinds
/// of home section differ in more than just the outer wrapper.
/// A home section that looks like a `collapsibleSection` but never folds.
let fixedSection (sectionClass: string) (headerClass: string) (headerContent: ReactElement list) (children: ReactElement list) : ReactElement =
    Html.section [
        prop.className sectionClass
        prop.children [ Html.h2 [ prop.className headerClass; prop.children headerContent ]; yield! children ]
    ]

let collapsibleSection
    (dispatch: Msg -> unit)
    (collapsedState: Map<string, bool>)
    (sectionClass: string)
    (key: string)
    (headerClass: string)
    (headerContent: ReactElement list)
    (children: ReactElement list)
    : ReactElement =
    let isOpen =
        match collapsedState.TryFind key with
        | Some open_ -> open_
        | None -> not (collapsedByDefault key)
    Html.section [
        prop.className (sectionClass + " mcol" + (if isOpen then "" else " collapsed"))
        prop.custom ("data-col", key)
        prop.children [
            Html.h2 [
                prop.className (headerClass + " mhead")
                prop.custom ("aria-expanded", isOpen)
                prop.onClick (fun e ->
                    let target = e.target :?> Browser.Types.Element
                    if isNullOrUndefined (target.closest "a,button") then
                        dispatch (ToggleCollapsed key))
                prop.children headerContent
            ]
            yield! children
        ]
    ]
