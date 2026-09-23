module Views.Nav

open Feliz
open Types

let private chevron: ReactElement =
    Svg.svg [
        svg.className "chev"
        svg.viewBox (0, 0, 24, 24)
        svg.children [ Svg.path [ svg.d "M6 9l6 6 6-6" ] ]
    ]

/// Mirrors the sidebar search's exact (slightly asymmetric) matching: author
/// name and work title are lowercased before comparison, but the Greek name,
/// text labels and work id are compared as-is against the already-lowercased
/// query — faithfully reproduced, not "fixed".
let private searchMatches (q: string) (author: Author) (work: Work) : bool =
    if q = "" then
        true
    else
        author.Name.ToLowerInvariant().Contains q
        || (author.Grc |> Option.defaultValue "").Contains q
        || work.Title.ToLowerInvariant().Contains q
        || work.Texts |> List.exists (fun t -> t.Label.Contains q)
        || work.Id.Contains q

/// The work button, plus (only for the currently-open reader work, when it
/// has more than one chunk) the parts-toggle and its chunk list.
let private workRow (model: Model) (dispatch: Msg -> unit) (currentWorkId: string option) (w: Work) : ReactElement list =
    let isActive = currentWorkId = Some w.Id
    let hasT = Catalog.hasTranslation w
    let grcLabel = w.Texts |> List.tryFind (fun t -> t.Lang = "grc") |> Option.map (fun t -> t.Label)
    let workBtn =
        Html.button [
            prop.key w.Id
            prop.className ("work" + (if isActive then " active" else ""))
            prop.onClick (fun _ -> dispatch (Reader_(OpenWork(w.Id, None, None, None, None))))
            prop.children [
                Html.text w.Title
                if not hasT then Html.span [ prop.className "tag"; prop.text "Greek only" ]
                match grcLabel with
                | Some g when g <> w.Title -> Html.span [ prop.className "grc"; prop.text g ]
                | _ -> Html.none
            ]
        ]
    let partsRows =
        match model.Reader with
        | Some rm when isActive ->
            match rm.Data with
            | Some d when d.Chunks.Length > 1 ->
                [ Html.button [
                      prop.key (w.Id + "-parts")
                      prop.className "parts-toggle"
                      prop.custom ("aria-expanded", model.PartsOpen)
                      prop.onClick (fun _ -> dispatch ToggleParts)
                      prop.children [ chevron; Html.text (string d.Chunks.Length + " parts") ]
                  ]
                  Html.div [
                      prop.key (w.Id + "-chunks")
                      prop.className "chunks"
                      prop.children [
                          for c in d.Chunks ->
                              Html.button [
                                  prop.key c.Ref
                                  prop.text c.Ref
                                  prop.className (if rm.Chunk = Some c.Ref then "active" else "")
                                  prop.onClick (fun _ -> dispatch (Reader_(ShowChunk(c.Ref, None, None))))
                              ]
                      ]
                  ] ]
            | _ -> []
        | _ -> []
    workBtn :: partsRows

/// The author/work accordion body (mirrors `renderNav`'s DOM building, minus
/// the imperative lazy-build/scroll-preservation bookkeeping React doesn't need).
/// Authors with the works that pass the search box and the translation filter,
/// before the genre chip is applied (the chips count from this).
let private candidateRows (model: Model) : (Author * Work list) list =
    let q = model.NavQuery.Trim().ToLowerInvariant()
    model.Catalog.Authors
    |> List.choose (fun a ->
        let ws = a.Works |> List.filter (fun w -> Catalog.passesFilter model.Filter w && searchMatches q a w)
        if List.isEmpty ws then None else Some(a, ws))

let authorList (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let hasQuery = model.NavQuery.Trim() <> ""
    let currentWorkId = model.Reader |> Option.map (fun rm -> rm.Work.Id)

    let rows =
        candidateRows model
        |> List.filter (fun (a, _) ->
            match model.Genre with
            | Some g -> WikiData.genreOfAuthor model.Meta a.Id = g
            | None -> true)

    if List.isEmpty rows then
        let narrowed =
            [ if model.Filter <> FilterAll then "this filter"
              if model.Genre.IsSome then "this genre" ]
        [ Html.div [
              prop.className "nav-empty"
              prop.text (
                  "Nothing matches"
                  + (if List.isEmpty narrowed then "" else " with " + String.concat " and " narrowed)
                  + ". Try an author, a title, or a TLG number such as tlg0012."
              )
          ] ]
    else
        rows
        |> List.map (fun (a, ws) ->
            let openA = if hasQuery then not (model.ClosedAuthors.Contains a.Id) else model.OpenAuthors.Contains a.Id
            Html.div [
                prop.key a.Id
                prop.className ("author" + (if openA then " open" else ""))
                prop.children [
                    Html.button [
                        prop.custom ("aria-expanded", openA)
                        prop.onClick (fun _ -> dispatch (ToggleAuthor a.Id))
                        prop.children [
                            chevron
                            Html.span [ prop.text a.Name ]
                            match a.Grc with
                            | Some g -> Html.span [ prop.className "grc"; prop.text g ]
                            | None -> Html.none
                            Html.span [ prop.className "cnt"; prop.text (string ws.Length) ]
                        ]
                    ]
                    Html.div [
                        prop.className "works"
                        prop.children (if openA then ws |> List.collect (workRow model dispatch currentWorkId) else [])
                    ]
                ]
            ])

let private filterButton (model: Model) (dispatch: Msg -> unit) (value: WorksFilter) (dataV: string) (label: string) : ReactElement =
    Html.button [
        prop.custom ("data-v", dataV)
        prop.custom ("aria-pressed", model.Filter = value)
        prop.text label
        prop.onClick (fun _ -> dispatch (SetFilter value))
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.nav [
        prop.className ("side" + (if model.SideOpen then " open" else ""))
        prop.id "side"
        prop.ariaLabel "Library"
        prop.children [
            Html.div [
                prop.className "side-head"
                prop.children [
                    Html.span [ prop.text "Library" ]
                    Html.button [
                        prop.id "sideClose"
                        prop.text "Done"
                        prop.onClick (fun _ -> dispatch (ToggleSide false))
                    ]
                ]
            ]
            Html.input [
                prop.className "search"
                prop.id "q"
                prop.type' "search"
                prop.placeholder "Search 370+ authors and 1,800+ works"
                prop.autoComplete "off"
                prop.value model.NavQuery
                prop.onChange (fun (v: string) -> dispatch (SetNavQuery v))
            ]
            Html.div [
                prop.className "filter"
                prop.role "group"
                prop.ariaLabel "Show"
                prop.children [
                    filterButton model dispatch FilterAll "all" "All"
                    filterButton model dispatch FilterTranslated "trans" "With translation"
                    filterButton model dispatch FilterGreekOnly "grc" "Greek only"
                ]
            ]
            Shared.genreChips
                "Genre"
                // counted in works, like the numbers beside each author
                (candidateRows model
                 |> List.groupBy (fun (a, _) -> WikiData.genreOfAuthor model.Meta a.Id)
                 |> List.map (fun (g, rows) -> g, rows |> List.sumBy (fun (_, ws) -> ws.Length)))
                model.Genre
                dispatch
            Html.div [ prop.id "authors"; prop.children (authorList model dispatch) ]
        ]
    ]
