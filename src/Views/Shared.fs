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
            prop.href "#"
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
            prop.href "#"
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
            ]
            Html.div [
                prop.className "note-edit-foot"
                prop.children [
                    Html.span [ prop.className "quiet"; prop.text "Saved as you type." ]
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
                prop.href "#wiki"
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
                        prop.href h
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
/// is the number of works in the library by its authors. The open-ended ends
/// of the first and last eras are drawn from where the corpus starts (Homer,
/// c. 800 BCE) and to 1600 CE; eras with no works are left out.
let erasBand (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let eraOfAuthor (a: Author) = model.Meta.Authors.TryFind a.Id |> Option.bind (fun m -> m.Era)
    let worksIn (eraId: string option) =
        model.Catalog.Authors |> List.filter (fun a -> eraOfAuthor a = eraId) |> List.sumBy (fun a -> a.Works.Length)
    let eras =
        model.Meta.Eras
        |> List.map (fun e ->
            let from = if e.From = -9999 then -800 else e.From
            let to_ = if e.To = 9999 then 1600 else e.To
            e, from, to_, worksIn (Some e.Id))
        |> List.filter (fun (_, from, to_, n) -> n > 0 && to_ > from)
    if List.isEmpty eras then Html.none
    else
        let first = eras |> List.map (fun (_, f, _, _) -> f) |> List.min
        let last = eras |> List.map (fun (_, _, t, _) -> t) |> List.max
        let span = float (last - first)
        let most = eras |> List.map (fun (_, _, _, n) -> n) |> List.max |> float
        let total = eras |> List.sumBy (fun (_, _, _, n) -> n)
        let undated = worksIn None
        let pct (x: float) = System.Math.Round(x * 100.0, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%"
        let widthOf from to_ = "calc(" + pct (float (to_ - from) / span) + " - 3px)"
        let name (e: Era) = System.Text.RegularExpressions.Regex.Replace(e.Name, @" \(.*\)", "")
        let go (id: string) (ev: Browser.Types.MouseEvent) =
            ev.preventDefault ()
            dispatch (Navigate("#wiki/eras/" + id, false))
        Html.div [
            prop.className "eras-band"
            prop.children [
                Html.div [
                    prop.className "eb-cols"
                    prop.children [
                        for e, from, to_, n in eras ->
                            Html.a [
                                prop.key e.Id
                                prop.href ("#wiki/eras/" + e.Id)
                                prop.style [ style.custom ("flexBasis", widthOf from to_) ]
                                prop.ariaLabel (name e + ", " + eraRange from to_ + ": " + string n + " works")
                                prop.onClick (go e.Id)
                                prop.children [
                                    Html.b [ prop.text (string n) ]
                                    Html.i [ prop.style [ style.custom ("height", pct (max 0.015 (float n / most))) ] ]
                                ]
                            ]
                    ]
                ]
                Html.div [
                    prop.className "eb-labels"
                    prop.ariaHidden true
                    prop.children [
                        for e, from, to_, _ in eras ->
                            Html.div [
                                prop.key e.Id
                                prop.style [ style.custom ("flexBasis", widthOf from to_) ]
                                prop.children [ Html.span [ prop.text (name e) ]; Html.text (eraRange from to_) ]
                            ]
                    ]
                ]
                // Phones: the same data turned on its side, years running down.
                Html.div [
                    prop.className "eb-rows"
                    prop.children [
                        for e, from, to_, n in eras ->
                            Html.a [
                                prop.key e.Id
                                prop.href ("#wiki/eras/" + e.Id)
                                prop.onClick (go e.Id)
                                prop.children [
                                    Html.span [
                                        prop.children [
                                            Html.b [ prop.text (name e) ]
                                            Html.small [ prop.text (" " + eraRange from to_ + " · " + string n + " works") ]
                                        ]
                                    ]
                                    Html.i [ prop.style [ style.custom ("width", pct (max 0.01 (float n / most))) ] ]
                                ]
                            ]
                    ]
                ]
                Html.p [
                    prop.className "eb-note"
                    prop.text (
                        "Width is the length of each era; height, the works in the library by its authors ("
                        + string total
                        + " in all"
                        + (if undated > 0 then ", plus " + string undated + " by authors of uncertain date" else "")
                        + ")."
                    )
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
