/// The Library (`#library`): the whole catalogue on one page, in alphabetical
/// order by author or by title, or in time order by era; narrowed by search,
/// letter, era, genre and whether a translation exists. The header search is
/// the quick way to a text; this is the place to look around.
module Views.Browse

open Feliz
open Types

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

let private fmtYear (v: int) : string = if v < 0 then string (-v) + " BCE" else string v + " CE"

/// "c. 428 BCE – 348 BCE", "fl. 120 CE", or "".
let private dateSpan (m: AuthorMeta) : string =
    if m.Birth.IsSome || m.Death.IsSome then
        (m.Birth |> Option.map (fun b -> "c. " + fmtYear b) |> Option.defaultValue "?")
        + " – "
        + (m.Death |> Option.map fmtYear |> Option.defaultValue "?")
    else
        m.Floruit |> Option.map (fun f -> "fl. " + fmtYear f) |> Option.defaultValue ""

/// The letter a name is filed under: accents and breathings dropped, so
/// "Ælius" and "Aelius" file together and a Greek name files by its letter.
let private fileLetter (name: string) : string =
    let bare = System.Text.RegularExpressions.Regex.Replace(name.Normalize(System.Text.NormalizationForm.FormD), "[̀-ͯ]", "")
    let c = bare.TrimStart([| '['; '('; '"'; '\''; ' ' |])
    if c = "" then "#"
    else
        let up = c.Substring(0, 1).ToUpperInvariant()
        if up >= "A" && up <= "Z" then up else "#"

let private sortName (name: string) : string =
    System.Text.RegularExpressions.Regex.Replace(name.Normalize(System.Text.NormalizationForm.FormD), "[̀-ͯ]", "").ToLowerInvariant()

let private matches (q: string) (a: Author) (w: Work) : bool =
    q = ""
    || a.Name.ToLowerInvariant().Contains q
    || (a.Grc |> Option.defaultValue "").ToLowerInvariant().Contains q
    || w.Title.ToLowerInvariant().Contains q
    || w.Texts |> List.exists (fun t -> t.Label.ToLowerInvariant().Contains q)
    || w.Id.Contains q

let private metaOf (model: Model) (a: Author) : AuthorMeta option = model.Meta.Authors.TryFind a.Id

let private eraOf (model: Model) (a: Author) : string =
    metaOf model a |> Option.bind (fun m -> m.Era) |> Option.defaultValue "undated"

/// Authors with the works that pass every control except the letter bar
/// (which is counted from this).
let private candidates (model: Model) : (Author * Work list) list =
    let q = model.BrowseQuery.Trim().ToLowerInvariant()
    model.Catalog.Authors
    |> List.filter (fun a ->
        (match model.Genre with
         | Some g -> WikiData.genreOfAuthor model.Meta a.Id = g
         | None -> true)
        && (match model.Shelf.Era with
            | Some e -> eraOf model a = e
            | None -> true))
    |> List.choose (fun a ->
        match a.Works |> List.filter (fun w -> Catalog.passesFilter model.Filter w && matches q a w) with
        | [] -> None
        | ws -> Some(a, ws))

// ---------------------------------------------------------------------------
// rows
// ---------------------------------------------------------------------------

let private favToggle (model: Model) (dispatch: Msg -> unit) (w: Work) : ReactElement =
    let on = LibraryData.isFav model.Library w.Id
    Html.button [
        prop.className ("shelf-fav" + (if on then " on" else ""))
        prop.title (if on then "Remove from favourites" else "Add to favourites")
        prop.ariaLabel ((if on then "Remove " else "Add ") + w.Title + (if on then " from favourites" else " to favourites"))
        prop.custom ("aria-pressed", on)
        prop.onClick (fun e ->
            e.stopPropagation ()
            dispatch (Library_(ToggleFav w.Id)))
        prop.children [ Shared.icon "olive" ]
    ]

let private workRow (model: Model) (dispatch: Msg -> unit) (showAuthor: Author option) (w: Work) : ReactElement =
    let hash = Router.toHash (ReaderRoute(w.Id, "", "", None, None))
    let grc = Catalog.titleGrc w |> Option.filter (fun g -> g <> w.Title)
    let marks = model.Library.Marks |> List.filter (fun m -> m.Work = w.Id) |> List.length
    Html.li [
        prop.key w.Id
        prop.className "shelf-work"
        prop.children [
            Html.a [
                prop.className "sw-open"
                prop.href (Router.href hash)
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Reader_(OpenWork(w.Id, None, None, None, None))))
                prop.children [
                    Html.span [ prop.className "sw-title"; prop.children [ Shared.titleText w.Title ] ]
                    match grc with
                    | Some g -> Html.span [ prop.className "grc"; prop.lang "grc"; prop.text g ]
                    | None -> Html.none
                    match showAuthor with
                    | Some a -> Html.span [ prop.className "sw-who"; prop.text a.Name ]
                    | None -> Html.none
                ]
            ]
            Html.span [
                prop.className "sw-tags"
                prop.children [
                    if marks > 0 then
                        Html.span [
                            prop.className "tag mine"
                            prop.title "Passages you have saved in this work"
                            prop.text (string marks + (if marks = 1 then " bookmark" else " bookmarks"))
                        ]
                    if not (Catalog.hasTranslation w) then Html.span [ prop.className "tag"; prop.text "Greek only" ]
                ]
            ]
            favToggle model dispatch w
        ]
    ]

let private authorBlock (model: Model) (dispatch: Msg -> unit) (a: Author, ws: Work list) : ReactElement =
    let m = metaOf model a
    let hash = "#author/" + a.Id
    Html.div [
        prop.key a.Id
        prop.className "shelf-author"
        prop.children [
            Html.div [
                prop.className "sa-head"
                prop.children [
                    Html.a [
                        prop.className "sa-name"
                        prop.href (Router.href hash)
                        prop.title ("About " + a.Name)
                        prop.onClick (navigateTo dispatch hash)
                        prop.text a.Name
                    ]
                    match a.Grc with
                    | Some g -> Html.span [ prop.className "grc"; prop.lang "grc"; prop.text g ]
                    | None -> Html.none
                    match m |> Option.map dateSpan with
                    | Some d when d <> "" -> Html.span [ prop.className "sa-dates"; prop.text d ]
                    | _ -> Html.none
                ]
            ]
            Html.ul [
                prop.className "shelf-works"
                prop.children [ for w in ws |> List.sortBy (fun w -> sortName w.Title) -> workRow model dispatch None w ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// controls
// ---------------------------------------------------------------------------

let private segButton (on: bool) (label: string) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.key label
        prop.custom ("aria-pressed", on)
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

let private letterBar (model: Model) (dispatch: Msg -> unit) (have: Set<string>) : ReactElement =
    Html.nav [
        prop.className "shelf-letters"
        prop.ariaLabel "Jump to a letter"
        prop.children [
            yield
                Html.button [
                    prop.key "all"
                    prop.className "sl-all"
                    prop.custom ("aria-pressed", model.Shelf.Letter.IsNone)
                    prop.text "All"
                    prop.onClick (fun _ -> dispatch (Shelf_(SetShelfLetter None)))
                ]
            for c in [ 'A' .. 'Z' ] do
                let l = string c
                let on = model.Shelf.Letter = Some l
                yield Html.button [
                    prop.key l
                    prop.custom ("aria-pressed", on)
                    prop.disabled (not (have.Contains l) && not on)
                    prop.text l
                    prop.onClick (fun _ -> dispatch (Shelf_(SetShelfLetter(if on then None else Some l))))
                ]
        ]
    ]

let private controls (model: Model) (dispatch: Msg -> unit) (genreCounts: (string * int) list) : ReactElement =
    let eras = model.Meta.Eras |> List.filter (fun e -> model.Meta.Authors |> Map.exists (fun _ m -> m.Era = Some e.Id))
    Html.div [
        prop.className "shelf-controls"
        prop.children [
            Html.input [
                prop.id "browseQ"
                prop.className "search"
                prop.type' "search"
                prop.placeholder "Search authors, titles or TLG numbers"
                prop.ariaLabel "Search the library"
                prop.autoComplete "off"
                prop.value model.BrowseQuery
                prop.onChange (fun (v: string) -> dispatch (SetBrowseQuery v))
            ]
            Html.div [
                prop.className "shelf-row"
                prop.children [
                    Html.div [
                        prop.className "segbtn"
                        prop.role "group"
                        prop.ariaLabel "Order"
                        prop.children [
                            segButton (model.Shelf.Sort = ByAuthor) "Author A–Z" (fun () -> dispatch (Shelf_(SetShelfSort ByAuthor)))
                            segButton (model.Shelf.Sort = ByTitle) "Title A–Z" (fun () -> dispatch (Shelf_(SetShelfSort ByTitle)))
                            segButton (model.Shelf.Sort = ByDate) "By era" (fun () -> dispatch (Shelf_(SetShelfSort ByDate)))
                        ]
                    ]
                    Html.div [
                        prop.className "segbtn"
                        prop.role "group"
                        prop.ariaLabel "Translation"
                        prop.children [
                            segButton (model.Filter = FilterAll) "All" (fun () -> dispatch (SetFilter FilterAll))
                            segButton (model.Filter = FilterTranslated) "With translation" (fun () -> dispatch (SetFilter FilterTranslated))
                            segButton (model.Filter = FilterGreekOnly) "Greek only" (fun () -> dispatch (SetFilter FilterGreekOnly))
                        ]
                    ]
                    Html.label [
                        prop.className "shelf-era"
                        prop.children [
                            Html.span [ prop.text "Era" ]
                            Html.select [
                                prop.value (model.Shelf.Era |> Option.defaultValue "")
                                prop.onChange (fun (v: string) -> dispatch (Shelf_(SetShelfEra(if v = "" then None else Some v))))
                                prop.children [
                                    yield Html.option [ prop.key "all"; prop.value ""; prop.text "Every era" ]
                                    for e in eras do
                                        yield Html.option [ prop.key e.Id; prop.value e.Id; prop.text e.Name ]
                                    yield Html.option [ prop.key "undated"; prop.value "undated"; prop.text "Undated" ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            Shared.genreChips "Genre" genreCounts model.Genre dispatch
        ]
    ]

/// Well-known works by kind, for a first visit: folded away once you are
/// searching or filtering, since then you know what you want.
let private whereToStart (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let sections =
        Content.browse
        |> List.map (fun s -> s.Title, s.WorkIds |> List.choose model.Catalog.WorkById.TryFind)
        |> List.filter (fun (_, works) -> not (List.isEmpty works))
    Html.details [
        prop.className "shelf-start"
        prop.children [
            Html.summary [ prop.text "Not sure where to begin? Well-known works, by kind" ]
            for title, works in sections do
                Html.h3 [ prop.key title; prop.className "sh"; prop.text title ]
                Html.div [ prop.key (title + "-picks"); prop.className "picks"; prop.children [ for w in works -> Shared.workCard model.Catalog dispatch w ] ]
        ]
    ]

// ---------------------------------------------------------------------------
// page
// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let cands = candidates model
    let genreCounts =
        let q = model.BrowseQuery.Trim().ToLowerInvariant()
        model.Catalog.Authors
        |> List.filter (fun a -> match model.Shelf.Era with Some e -> eraOf model a = e | None -> true)
        |> List.map (fun a -> a, a.Works |> List.filter (fun w -> Catalog.passesFilter model.Filter w && matches q a w) |> List.length)
        |> List.filter (fun (_, n) -> n > 0)
        |> List.groupBy (fun (a, _) -> WikiData.genreOfAuthor model.Meta a.Id)
        |> List.map (fun (g, xs) -> g, xs |> List.sumBy snd)
    let letterOfEntry =
        match model.Shelf.Sort with
        | ByTitle -> fun (_: Author, w: Work) -> fileLetter w.Title
        | _ -> fun (a: Author, _: Work) -> fileLetter a.Name
    let flat = cands |> List.collect (fun (a, ws) -> ws |> List.map (fun w -> a, w))
    let lettersHave = flat |> List.map letterOfEntry |> Set.ofList
    let inLetter (entry: Author * Work) =
        match model.Shelf.Letter with
        | Some l -> letterOfEntry entry = l
        | None -> true
    let shown = flat |> List.filter inLetter
    let nWorks = shown.Length
    let nAuthors = shown |> List.map (fun (a, _) -> a.Id) |> List.distinct |> List.length
    let total = model.Catalog.WorkById.Count
    let filtering =
        model.BrowseQuery.Trim() <> "" || model.Genre.IsSome || model.Shelf.Era.IsSome || model.Filter <> FilterAll || model.Shelf.Letter.IsSome

    let byAuthor () =
        shown
        |> List.groupBy (fun (a, _) -> a.Id)
        |> List.map (fun (_, xs) -> fst xs.Head, xs |> List.map snd)
        |> List.sortBy (fun (a, _) -> sortName a.Name)
        |> List.groupBy (fun (a, _) -> fileLetter a.Name)
        |> List.map (fun (letter, blocks) ->
            Html.section [
                prop.key letter
                prop.className "shelf-letter"
                prop.children [
                    Html.h2 [ prop.className "sl-h"; prop.text letter ]
                    yield! blocks |> List.map (authorBlock model dispatch)
                ]
            ])

    let byTitle () =
        shown
        |> List.sortBy (fun (_, w) -> sortName w.Title)
        |> List.groupBy (fun (_, w) -> fileLetter w.Title)
        |> List.map (fun (letter, rows) ->
            Html.section [
                prop.key letter
                prop.className "shelf-letter"
                prop.children [
                    Html.h2 [ prop.className "sl-h"; prop.text letter ]
                    Html.ul [ prop.className "shelf-works flat"; prop.children [ for a, w in rows -> workRow model dispatch (Some a) w ] ]
                ]
            ])

    let byDate () =
        let eraName id =
            model.Meta.Eras |> List.tryFind (fun e -> e.Id = id) |> Option.map (fun e -> e.Name) |> Option.defaultValue "Undated"
        let eraOrder id =
            model.Meta.Eras |> List.tryFindIndex (fun e -> e.Id = id) |> Option.defaultValue 999
        shown
        |> List.groupBy (fun (a, _) -> a.Id)
        |> List.map (fun (_, xs) -> fst xs.Head, xs |> List.map snd)
        |> List.groupBy (fun (a, _) -> eraOf model a)
        |> List.sortBy (fun (e, _) -> eraOrder e)
        |> List.map (fun (e, blocks) ->
            let sorted =
                blocks
                |> List.sortBy (fun (a, _) ->
                    (metaOf model a |> Option.bind (fun m -> m.Year) |> Option.defaultValue System.Int32.MaxValue), sortName a.Name)
            Html.section [
                prop.key e
                prop.className "shelf-letter"
                prop.children [
                    Html.h2 [ prop.className "sl-h era"; prop.text (eraName e) ]
                    yield! sorted |> List.map (authorBlock model dispatch)
                ]
            ])

    Html.div [
        prop.className "page shelf"
        prop.children [
            Html.h1 [ prop.className "ph"; prop.text "Library" ]
            Html.p [
                prop.className "shelf-lede"
                prop.text (
                    sprintf "%s works by %s authors, from Homer to the Byzantine chroniclers. Open any one to read it beside its translation, where there is one."
                        (total.ToString("N0")) (model.Catalog.Authors.Length.ToString("N0"))
                )
            ]
            controls model dispatch genreCounts
            if not filtering then whereToStart model dispatch
            if model.Shelf.Sort <> ByDate then letterBar model dispatch lettersHave
            Html.p [
                prop.className "shelf-count"
                prop.custom ("aria-live", "polite")
                prop.children [
                    // Unfiltered, the count would repeat the sentence at the top.
                    if filtering || nWorks = 0 then
                        Html.text (
                            if nWorks = 0 then "Nothing matches."
                            else sprintf "%s %s by %s %s" (nWorks.ToString("N0")) (if nWorks = 1 then "work" else "works") (nAuthors.ToString("N0")) (if nAuthors = 1 then "author" else "authors")
                        )
                    if filtering then
                        Html.button [
                            prop.className "linkbtn"
                            prop.text "Clear filters"
                            prop.onClick (fun _ ->
                                dispatch (SetBrowseQuery "")
                                dispatch (SetGenre None)
                                dispatch (SetFilter FilterAll)
                                dispatch (Shelf_(SetShelfEra None))
                                dispatch (Shelf_(SetShelfLetter None)))
                        ]
                ]
            ]
            if nWorks = 0 then
                Html.p [ prop.className "quiet"; prop.text "Try fewer filters, or search for part of a name, a title, or a TLG number such as tlg0012." ]
            else
                Html.div [
                    prop.className "shelf-list"
                    prop.children (
                        match model.Shelf.Sort with
                        | ByAuthor -> byAuthor ()
                        | ByTitle -> byTitle ()
                        | ByDate -> byDate ()
                    )
                ]
        ]
    ]
