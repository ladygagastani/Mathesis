/// The wiki's "Everyday life" (`#wiki/life`, `#wiki/life/<slug>`): an index of
/// the articles, grouped, as rows like the wiki's own contents; and each
/// article, rendered from its Markdown (LifeData) as the guide is, beside a
/// rail with the other articles and the texts and authors it draws on.
module Views.Life

open Feliz
open Types

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

let private link (dispatch: Msg -> unit) (cls: string) (hash: string) (children: ReactElement list) : ReactElement =
    Html.a [ prop.className cls; prop.href hash; prop.onClick (navigateTo dispatch hash); prop.children children ]

/// One row of the index, in the style of the wiki's contents.
let private row (dispatch: Msg -> unit) (p: LifeData.LifePage) : ReactElement =
    let h = LifeData.hashOf p.Slug
    Html.div [
        prop.key p.Slug
        prop.className "wcat"
        prop.children [
            Html.span [ prop.className "wcat-grc"; prop.lang "grc"; prop.ariaHidden true; prop.text p.Greek ]
            link dispatch "wcat-main" h [ Html.b [ prop.text p.Title ]; Html.span [ prop.text p.Summary ] ]
        ]
    ]

let private translationNote : ReactElement =
    Html.p [
        prop.className "tr-note"
        prop.children [
            Html.text "Translations in these articles are our own. The reader shows each text with the published translation from Perseus or First1KGreek, which may read differently. "
            Html.span [ prop.className "tr-star"; prop.text "*" ]
            Html.text " marks a rendering we are unsure of, or one that scholars dispute."
        ]
    ]

/// "Author, Title" for a quoted work, from the catalogue.
let private workLabel (model: Model) (workId: string) : (string * string) option =
    model.Catalog.WorkById.TryFind workId
    |> Option.map (fun w ->
        let author = model.Catalog.AuthorOfWork.TryFind workId |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
        author, w.Title)

let private index (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let nWorks = LifeData.pages |> List.collect (fun p -> p.Works) |> List.distinct |> List.length
    Html.div [
        prop.className "page life"
        prop.children [
            Shared.wikiCrumbs dispatch [ "Everyday life", None ]
            Html.div [
                prop.className "wiki-top"
                prop.children [
                    Html.p [ prop.className "wh-kicker"; prop.text "Wiki" ]
                    Html.h1 [ prop.className "ph"; prop.text "Everyday life" ]
                    Html.p [
                        prop.className "wh-lead"
                        prop.text "How the Greeks lived: what they ate and drank, how they married, raised children, worshipped, played, fell ill and were buried. The good and the grim, from the texts themselves, with a link to every passage quoted."
                    ]
                ]
            ]
            Html.div [
                prop.className "life-cols"
                prop.children [
                    Html.div [
                        prop.children [
                            for g in LifeData.groups do
                                Html.h2 [ prop.key ("h" + g); prop.className "sh life-group"; prop.text g ]
                                Html.div [
                                    prop.key ("g" + g)
                                    prop.className "wiki-cats"
                                    prop.children [ for p in LifeData.pages |> List.filter (fun p -> p.Group = g) -> row dispatch p ]
                                ]
                        ]
                    ]
                    Html.aside [
                        prop.className "wiki-side life-side"
                        prop.children [
                            Html.h2 [ prop.className "sh"; prop.text "How to read these" ]
                            Html.p [
                                prop.className "life-how"
                                prop.text (sprintf "Every quotation opens its passage in the reader, in Greek beside a translation: %d texts from the library are quoted. Where a story is legend, or scholars disagree, the article says so." nWorks)
                            ]
                            Html.p [
                                prop.className "life-how"
                                prop.text "Most of our evidence comes from men, from Athens, and from the well off. The articles point out where the picture is one-sided."
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private rail (model: Model) (dispatch: Msg -> unit) (p: LifeData.LifePage) : ReactElement =
    Html.aside [
        prop.className "life-rail"
        prop.children [
            if not (List.isEmpty p.Works) then
                Html.h2 [ prop.className "sh"; prop.text "Texts quoted" ]
                Html.ul [
                    prop.className "life-works"
                    prop.children [
                        for w in p.Works do
                            match workLabel model w with
                            | Some(author, title) ->
                                let h = Router.toHash (ReaderRoute(w, "", "", None, None))
                                Html.li [ prop.key w; prop.children [ link dispatch "" h [ Html.i [ prop.text title ] ]; Html.span [ prop.className "lw-a"; prop.text author ] ] ]
                            | None -> ()
                    ]
                ]
            Html.h2 [ prop.className "sh"; prop.text "Everyday life" ]
            Html.nav [
                prop.className "life-toc"
                prop.ariaLabel "Everyday life articles"
                prop.children [
                    for g in LifeData.groups do
                        Html.div [
                            prop.key g
                            prop.className "lt-group"
                            prop.children [
                                Html.b [ prop.text g ]
                                for q in LifeData.pages |> List.filter (fun q -> q.Group = g) do
                                    let h = LifeData.hashOf q.Slug
                                    if q.Slug = p.Slug then
                                        Html.span [ prop.key q.Slug; prop.className "on"; prop.custom ("aria-current", "page"); prop.text q.Title ]
                                    else
                                        Html.a [ prop.key q.Slug; prop.href h; prop.text q.Title; prop.onClick (navigateTo dispatch h) ]
                            ]
                        ]
                ]
            ]
        ]
    ]

let private article (model: Model) (dispatch: Msg -> unit) (p: LifeData.LifePage) : ReactElement =
    let pages = LifeData.pages
    let i = pages |> List.findIndex (fun q -> q.Slug = p.Slug)
    let pagerLink (j: int) (label: string) (cls: string) =
        link dispatch cls (LifeData.hashOf pages.[j].Slug) [ Html.text label ]
    Html.div [
        prop.className "page life life-article"
        prop.children [
            Shared.wikiCrumbs dispatch [ "Everyday life", Some(LifeData.hashOf ""); p.Title, None ]
            Html.p [
                prop.className "g-kicker life-kicker"
                prop.children [
                    Html.text p.Group
                    Html.span [ prop.className "grc"; prop.lang "grc"; prop.text (" · " + p.Greek) ]
                ]
            ]
            Html.div [
                prop.className "life-cols"
                prop.children [
                    Html.div [
                        prop.className "life-main"
                        prop.children [
                            Html.article [ prop.className "g-body"; prop.children (Guide.markdown dispatch p.Slug (Markdown.parse p.Markdown)) ]
                            if p.Markdown.Contains "](read:" then translationNote
                            Html.div [
                                prop.className "pager g-pager"
                                prop.children [
                                    (if i > 0 then pagerLink (i - 1) ("← " + pages.[i - 1].Title) "btn"
                                     else link dispatch "btn" (LifeData.hashOf "") [ Html.text "← Everyday life" ])
                                    (if i < pages.Length - 1 then pagerLink (i + 1) (pages.[i + 1].Title + " →") "btn primary"
                                     else link dispatch "btn" (LifeData.hashOf "") [ Html.text "All of Everyday life" ])
                                ]
                            ]
                        ]
                    ]
                    rail model dispatch p
                ]
            ]
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) (slug: string option) : ReactElement =
    match LifeData.tryFind slug with
    | Some p -> article model dispatch p
    | None -> index model dispatch
