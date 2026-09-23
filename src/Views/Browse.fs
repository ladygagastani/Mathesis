module Views.Browse

open Feliz
open Types

let private openWork (dispatch: Msg -> unit) (workId: string) (_: Browser.Types.MouseEvent) =
    dispatch (Reader_(OpenWork(workId, None, None, None, None)))

/// Search results row — mirrors `homeToolsAndResults`'s `.hr` rows exactly
/// (this is the same `bindWorkSearch` binding the original reuses on browsePage).
let private searchResults (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let results = Catalog.searchWorks model.Catalog model.Filter model.BrowseQuery
    let hasQuery = model.BrowseQuery.Trim() <> ""
    Html.div [
        prop.id "browseResults"
        prop.className "home-results"
        prop.children [
            if hasQuery && List.isEmpty results then
                Html.div [ prop.className "quiet"; prop.text "Nothing matches that. Try an author's name, a title, or part of either." ]
            for a, w in results do
                let grcTitle = Catalog.titleGrc w
                Html.button [
                    prop.key w.Id
                    prop.className "hr"
                    prop.onClick (openWork dispatch w.Id)
                    prop.children [
                        Html.span [
                            prop.children [
                                Html.b [ prop.text w.Title ]
                                match grcTitle with
                                | Some g when g <> w.Title -> Html.span [ prop.className "grc"; prop.text g ]
                                | _ -> Html.none
                            ]
                        ]
                        Html.span [ prop.className "who"; prop.text (a.Name + (if Catalog.hasTranslation w then "" else " · Greek only")) ]
                    ]
                ]
            if results.Length >= 40 then
                Html.div [ prop.className "quiet"; prop.text "Showing the 40 best matches. Keep typing to narrow them down." ]
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let sections =
        Content.browse
        |> List.map (fun s -> s.Title, s.WorkIds |> List.choose model.Catalog.WorkById.TryFind |> List.filter (Catalog.passesFilter model.Filter))
        |> List.filter (fun (_, works) -> not (List.isEmpty works))
    let total = model.Catalog.Authors |> List.sumBy (fun a -> a.Works.Length)
    Html.div [
        prop.className "page browse"
        prop.children (
            [ Html.h1 [ prop.className "ph"; prop.text "Where to start" ]
              Html.p [
                  prop.className "quiet"
                  prop.text (sprintf "Well-known works, grouped by kind. All %s works are listed in the library (☰), where you can also filter by genre, or search for one below." (total.ToString("N0")))
              ]
              Html.input [
                  prop.id "browseQ"
                  prop.className "search"
                  prop.placeholder "Search all works by author or title"
                  prop.autoComplete "off"
                  prop.value model.BrowseQuery
                  prop.onChange (fun (v: string) -> dispatch (SetBrowseQuery v))
              ]
              searchResults model dispatch ]
            @ [ for title, works in sections do
                    Html.h2 [ prop.key title; prop.className "sh"; prop.text title ]
                    Html.div [ prop.key (title + "-picks"); prop.className "picks"; prop.children [ for w in works -> Shared.workCard model.Catalog dispatch w ] ] ]
        )
    ]
