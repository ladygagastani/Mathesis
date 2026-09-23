module Views.WikiPages

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Types

// ---------------------------------------------------------------------------
// small local helpers (duplicated in a couple of view files by design — see
// the project's established pattern for tiny, self-contained UI glue)
// ---------------------------------------------------------------------------

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

let private stripParenSuffix (name: string) : string =
    System.Text.RegularExpressions.Regex.Replace(name, @" \(.*\)", "")

let private fmtYear (y: int option) : string =
    match y with
    | None -> ""
    | Some v -> if v < 0 then string (-v) + " BCE" else string v + " CE"

let private fmtYearOrEllipsis (y: int option) : string =
    match fmtYear y with
    | "" -> "…"
    | s -> s

let private yearRangeText (e: Era) : string =
    fmtYearOrEllipsis (if e.From = -9999 then None else Some e.From) + " – " + fmtYearOrEllipsis (if e.To = 9999 then None else Some e.To)

let private emptyAuthorMeta: AuthorMeta =
    { Name = ""; Grc = None; Birth = None; Death = None; Floruit = None; Year = None; Era = None; Desc = None; Wiki = None; Q = None; Place = None; Occ = []; NWorks = 0; Core = None }

let private metaFor (meta: Meta) (authorId: string) : AuthorMeta =
    meta.Authors.TryFind authorId |> Option.defaultValue emptyAuthorMeta

let private dateSpan (m: AuthorMeta) : string =
    if m.Birth.IsSome || m.Death.IsSome then
        (match m.Birth with
         | Some b -> "c. " + fmtYear (Some b)
         | None -> "?")
        + " – "
        + (match m.Death with
           | Some d -> fmtYear (Some d)
           | None -> "?")
    elif m.Floruit.IsSome then
        "fl. " + fmtYear m.Floruit
    else
        ""

let private sortKey (a: Author) (m: AuthorMeta) = (m.Year |> Option.defaultValue System.Int32.MaxValue), a.Name

let private erasWithCounts (meta: Meta) : (Era * int) list =
    meta.Eras
    |> List.map (fun e -> e, (meta.Authors |> Map.toList |> List.filter (fun (_, m) -> m.Era = Some e.Id) |> List.length))
    |> List.filter (fun (_, n) -> n > 0)

let private genresWithCounts (model: Model) : (WikiData.Genre * int) list =
    let authorIds = model.Catalog.Authors |> List.map (fun a -> a.Id)
    WikiData.genres
    |> List.map (fun g -> g, (authorIds |> List.filter (fun aid -> WikiData.genreOf (metaFor model.Meta aid) = g.Id) |> List.length))
    |> List.filter (fun (_, n) -> n > 0)

/// The author-list row shared by authorsIndex/eras/articleIndex — `showArticleTag`
/// controls the "article" chip, which only authorsIndex's rows show.
let private authorRow (dispatch: Msg -> unit) (showArticleTag: bool) (a: Author) (m: AuthorMeta) : ReactElement =
    let hash = "#author/" + a.Id
    Html.a [
        prop.key a.Id
        prop.className "author-row"
        prop.href hash
        prop.onClick (navigateTo dispatch hash)
        prop.children [
            Html.span [
                prop.className "ar-name"
                prop.children ([ Html.text a.Name ] @ (a.Grc |> Option.map (fun g -> Html.span [ prop.className "grc"; prop.text (" " + g) ]) |> Option.toList))
            ]
            Html.span [ prop.className "ar-dates"; prop.text (dateSpan m) ]
            Html.span [
                prop.className "ar-desc"
                prop.children (
                    [ Html.text (m.Desc |> Option.defaultValue "") ]
                    @ (if showArticleTag && m.Core.IsSome then [ Html.text " "; Html.span [ prop.className "tag"; prop.text "article" ] ] else [])
                )
            ]
            Html.span [ prop.className "ar-n"; prop.text (string a.Works.Length + " work" + (if a.Works.Length = 1 then "" else "s")) ]
        ]
    ]

// ---------------------------------------------------------------------------
// wiki home
// ---------------------------------------------------------------------------

let private firstWord (s: string) : string = s.Split(' ').[0].Replace(",", "")

/// One row of the wiki's table of contents: a Greek word for the subject in
/// the margin (decoration, so hidden from screen readers), the English name as
/// the link, one plain sentence, and any shortcuts beneath.
let private wcat (dispatch: Msg -> unit) (greek: string) (hash: string) (title: string) (desc: string) (subLinks: (string * string) list) : ReactElement =
    Html.div [
        prop.className "wcat"
        prop.children (
            [ Html.span [ prop.className "wcat-grc"; prop.lang "grc"; prop.ariaHidden true; prop.text greek ]
              Html.a [
                  prop.className "wcat-main"
                  prop.href hash
                  prop.onClick (navigateTo dispatch hash)
                  prop.children [
                      Html.b [ prop.text title ]
                      Html.span [ prop.text desc ]
                  ]
              ] ]
            @ (if List.isEmpty subLinks then
                   []
               else
                   [ Html.div [
                         prop.className "sub"
                         prop.children [ for h, l in subLinks -> Html.a [ prop.key h; prop.href h; prop.text l; prop.onClick (navigateTo dispatch h) ] ]
                     ] ])
        )
    ]

/// Says plainly that a page's translations are ours, not the published ones the
/// reader shows beside each text; and what the asterisk on some of them means.
let private ourTranslationNote (text: string) : ReactElement =
    Html.p [
        prop.className "tr-note"
        prop.children [
            Html.text (text + " ")
            Html.span [ prop.className "tr-star"; prop.text "*" ]
            Html.text " marks a rendering we are unsure of, or one that scholars dispute."
        ]
    ]

/// A passage link in the introduction's prose or quotations: into the reader,
/// at that passage (and that part, for works read part by part).
let private passageLink (dispatch: Msg -> unit) (cls: string) (label: string) (workId: string) (chunk: string option) (ref: string) : ReactElement =
    let hash = Router.toHash (ReaderRoute(workId, "", "", chunk, Some ref))
    Html.a [ prop.className cls; prop.href hash; prop.title "Read it in context"; prop.text label; prop.onClick (navigateTo dispatch hash) ]

let private introRuns (dispatch: Msg -> unit) (runs: WikiData.IntroRun list) : ReactElement list =
    runs
    |> List.mapi (fun i run ->
        match run with
        | WikiData.Plain t -> Html.text t
        | WikiData.Greek g -> Html.span [ prop.key i; prop.className "grc"; prop.lang "grc"; prop.text g ]
        | WikiData.Title t -> Html.i [ prop.key i; prop.text t ]
        | WikiData.Passage(label, workId, chunk, ref) ->
            Html.span [
                prop.key i
                prop.className "wh-cite"
                prop.children [ Html.text " ("; passageLink dispatch "wh-ref" label workId chunk ref; Html.text ")" ]
            ])

/// The wiki's own hero: the word the reader is named after, what the Greek
/// philosophers made of learning, and why an open reference and a living
/// language matter. Text in `WikiData.wikiIntro`.
let private wikiHero (dispatch: Msg -> unit) : ReactElement =
    let intro = WikiData.wikiIntro
    Html.section [
        prop.className "wiki-hero"
        prop.children [
            Html.div [
                prop.className "wh-top"
                prop.children [
                    Html.div [
                        prop.className "wh-main"
                        prop.children [
                            Html.p [ prop.className "wh-kicker"; prop.text "Wiki" ]
                            Html.h1 [
                                prop.className "ph wh-title"
                                prop.children [
                                    Html.span [ prop.className "grc"; prop.lang "grc"; prop.text "μάθησις" ]
                                    Html.span [ prop.className "wh-gloss"; prop.text "máthēsis · learning" ]
                                ]
                            ]
                            Html.p [ prop.className "wh-lead"; prop.children (introRuns dispatch intro.Lead) ]
                            Html.p [ prop.className "wh-text"; prop.children (introRuns dispatch intro.Philosophy) ]
                        ]
                    ]
                    Html.aside [
                        prop.className "wh-family"
                        prop.ariaLabel "Words from the same root"
                        prop.children [
                            Html.h2 [
                                prop.className "sh"
                                prop.children [ Html.text "One root, "; Html.span [ prop.className "grc"; prop.lang "grc"; prop.text "μαθ-" ] ]
                            ]
                            Html.dl [
                                prop.children [
                                    for w in intro.Family ->
                                        Html.div [
                                            prop.key w.Grc
                                            prop.className "wh-word"
                                            prop.children [
                                                Html.dt [
                                                    prop.children [
                                                        Html.span [ prop.className "grc"; prop.lang "grc"; prop.text w.Grc ]
                                                        Html.span [ prop.className "wh-tr"; prop.text w.Translit ]
                                                    ]
                                                ]
                                                Html.dd [ prop.text w.Gloss ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "wh-quotes"
                prop.children [
                    for q in intro.Quotes ->
                        Html.figure [
                            prop.key q.Ref
                            prop.className "wh-quote"
                            prop.children [
                                Html.blockquote [ prop.className "grc"; prop.lang "grc"; prop.text q.Grc ]
                                Html.figcaption [
                                    prop.children [
                                        Html.span [ prop.className "wh-en"; prop.text q.En ]
                                        passageLink dispatch "wh-who" (q.Who + " →") q.WorkId q.Chunk q.Ref
                                    ]
                                ]
                            ]
                        ]
                ]
            ]
            ourTranslationNote "The English on this page is our own translation. Open a passage and the reader shows it with the published translation that comes with the text, from Perseus or First1KGreek, which may read differently."
            Html.div [
                prop.className "wh-why"
                prop.children [
                    Html.div [
                        prop.children [
                            Html.h2 [ prop.className "sh"; prop.text "Why a wiki" ]
                            Html.p [ prop.children (introRuns dispatch intro.WhyWiki) ]
                        ]
                    ]
                    Html.div [
                        prop.children [
                            Html.h2 [ prop.className "sh"; prop.text "A language still spoken" ]
                            Html.p [ prop.children (introRuns dispatch intro.Living) ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let home (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let eras = erasWithCounts model.Meta
    let genres = genresWithCounts model
    let nCore = model.Meta.Authors |> Map.toList |> List.filter (fun (_, m) -> m.Core.IsSome) |> List.length
    let nAuthors = model.Catalog.Authors.Length
    Html.div [
        prop.className "page wiki-home"
        prop.children [
            wikiHero dispatch
            Html.h2 [ prop.className "sh wh-contents"; prop.text "In this wiki" ]
            Html.div [
                prop.className "wiki-home-cols"
                prop.children [
                    Html.div [
                        prop.className "wiki-cats"
                        prop.children [
                            wcat
                                dispatch
                                "Ἀρχή"
                                "#wiki/start"
                                "Start here"
                                "A beginner's guide: the alphabet and its sounds, using a Greek dictionary, and twelve words followed through three thousand years."
                                [ GuideData.hashOf "which-greek", "Which Greek"
                                  GuideData.hashOf "alphabet-and-sounds", "Letters and sounds"
                                  GuideData.hashOf "how-dictionaries-list-words", "Dictionaries"
                                  GuideData.hashOf "reading-a-word-study", "Word studies" ]
                            wcat
                                dispatch
                                "Συγγραφεῖς"
                                "#wiki/authors"
                                "Authors"
                                (sprintf "Biographies and timelines for %d authors." nAuthors)
                                ((eras |> List.map (fun (e, _) -> "#wiki/authors/era/" + e.Id, stripParenSuffix e.Name))
                                 @ (genres |> List.map (fun (g, _) -> "#wiki/authors/genre/" + g.Id, firstWord g.Name)))
                            wcat
                                dispatch
                                "Χρόνοι"
                                "#wiki/eras"
                                "Eras of Greek"
                                "From Homeric epic to Byzantine Greek: the periods and their language."
                                (eras |> List.map (fun (e, _) -> "#wiki/eras/" + e.Id, stripParenSuffix e.Name))
                            wcat dispatch "Παράδοσις" "#wiki/manuscripts" "Manuscripts & transmission" (sprintf "How the texts reached us: papyri, codices and the key witnesses, with %d author articles." nCore) []
                            wcat dispatch "Γραφαί" "#wiki/variants" "Textual variants" "Interpolations, athetized lines, disputed works and the main textual problems." []
                            wcat dispatch "Ἐκδόσεις" "#wiki/editions" "Editions & translations" "The printed sources behind this collection and the standard critical editions." []
                            wcat dispatch "Χάριτες" "#about" "About & acknowledgments" "The projects, scholars and licences this reader is built on." []
                        ]
                    ]
                    Html.aside [
                        prop.className "wiki-eras"
                        prop.children [
                            Html.h2 [ prop.className "sh"; prop.text "Greek through the centuries" ]
                            Shared.erasBand model dispatch
                        ]
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// authors index
// ---------------------------------------------------------------------------

let private subcatChips (dispatch: Msg -> unit) (label: string) (items: (string * string * int) list) (current: string option) (prefix: string) : ReactElement =
    Html.div [
        prop.className "subcats"
        prop.children [
            Html.span [ prop.className "sc-label"; prop.text label ]
            Html.div [
                prop.className "chips"
                prop.children [
                    for id, name, n in items do
                        let isCurrent = current = Some id
                        let hash = if isCurrent then "#wiki/authors" else "#wiki/authors/" + prefix + "/" + id
                        Html.a [
                            prop.key id
                            prop.className ("chip" + (if isCurrent then " on" else ""))
                            prop.href hash
                            prop.title (if isCurrent then "Show all authors" else "")
                            prop.onClick (navigateTo dispatch hash)
                            prop.children [ Html.text (stripParenSuffix name + " "); Html.span [ prop.text (string n) ] ]
                        ]
                ]
            ]
        ]
    ]

let authorsIndex (model: Model) (dispatch: Msg -> unit) (scope: AuthorScope option) (query: string) : ReactElement =
    let eras = erasWithCounts model.Meta
    let nUndated = model.Meta.Authors |> Map.toList |> List.filter (fun (_, m) -> m.Era.IsNone) |> List.length
    let genres = genresWithCounts model
    let nOther = model.Catalog.Authors |> List.filter (fun a -> WikiData.genreOf (metaFor model.Meta a.Id) = "other") |> List.length

    let title, note =
        match scope with
        | Some(ByEra "undated") -> "Anonymous & undated", Some "Anonymous collections, apocrypha, scholia and works whose authors cannot be dated."
        | Some(ByEra eraId) ->
            match eras |> List.tryFind (fun (e, _) -> e.Id = eraId) with
            | Some(e, _) -> e.Name, WikiData.eraNote.TryFind e.Id
            | None -> "Anonymous & undated", Some "Anonymous collections, apocrypha, scholia and works whose authors cannot be dated."
        | Some(ByGenre genreId) ->
            // Said plainly, because the sorting is mechanical and a reader
            // who finds a misfiled author should know why.
            let genreNote =
                Some "Authors are sorted into genres automatically, from the one-line description and occupations Wikidata gives for each, so a few may be filed under their second calling rather than their first."
            match genres |> List.tryFind (fun (g, _) -> g.Id = genreId) with
            | Some(g, _) -> g.Name, genreNote
            | None -> "Other", genreNote
        | None -> "All authors", None

    let matchesScope (m: AuthorMeta) =
        match scope with
        | Some(ByEra "undated") -> m.Era.IsNone
        | Some(ByEra eraId) -> m.Era = Some eraId
        | Some(ByGenre genreId) -> WikiData.genreOf m = genreId
        | None -> true

    let q = query.Trim().ToLowerInvariant()

    let rows =
        model.Catalog.Authors
        |> List.map (fun a -> a, metaFor model.Meta a.Id)
        |> List.filter (fun (a, m) ->
            matchesScope m
            && (q = "" || a.Name.ToLowerInvariant().Contains q || (a.Grc |> Option.defaultValue "").Contains q || (m.Desc |> Option.defaultValue "").ToLowerInvariant().Contains q))
        |> List.sortBy (fun (a, m) -> sortKey a m)

    Html.div [
        prop.className "page"
        prop.children (
            [ Shared.wikiCrumbs dispatch ((if scope.IsSome then [ "Authors", Some "#wiki/authors" ] else [ "Authors", None ]) @ (if scope.IsSome then [ title, None ] else []))
              Html.h1 [ prop.className "ph"; prop.text title ] ]
            @ (note |> Option.map (fun n -> Html.p [ prop.className "era-note"; prop.text n ]) |> Option.toList)
            @ [ subcatChips
                    dispatch
                    "By era"
                    ((eras |> List.map (fun (e, n) -> e.Id, e.Name, n)) @ (if nUndated > 0 then [ "undated", "Anonymous & undated", nUndated ] else []))
                    (match scope with
                     | Some(ByEra e) -> Some e
                     | _ -> None)
                    "era"
                subcatChips
                    dispatch
                    "By genre"
                    ((genres |> List.map (fun (g, n) -> g.Id, g.Name, n)) @ (if nOther > 0 then [ "other", "Other", nOther ] else []))
                    (match scope with
                     | Some(ByGenre g) -> Some g
                     | _ -> None)
                    "genre"
                Html.div [
                    prop.className "wiki-tools"
                    prop.children (
                        [ Html.input [
                              prop.id "wikiQ"
                              prop.className "search"
                              prop.placeholder "Search these authors…"
                              prop.value query
                              prop.onChange (fun (v: string) -> dispatch (SetWikiQuery v))
                          ] ]
                        @ (if scope.IsSome then
                               [ Html.a [ prop.className "btn"; prop.href "#wiki/authors"; prop.text "All authors"; prop.onClick (navigateTo dispatch "#wiki/authors") ] ]
                           else
                               [])
                    )
                ]
                Html.div [
                    prop.className "author-list"
                    prop.children (
                        if List.isEmpty rows then
                            [ Html.p [ prop.className "quiet"; prop.text "No authors match." ] ]
                        else
                            [ for a, m in rows -> authorRow dispatch true a m ]
                    )
                ] ]
        )
    ]

// ---------------------------------------------------------------------------
// eras
// ---------------------------------------------------------------------------

let eras (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let erasList = erasWithCounts model.Meta
    Html.div [
        prop.className "page"
        prop.children [
            Shared.wikiCrumbs dispatch [ "Eras of Greek", None ]
            Html.h1 [ prop.className "ph"; prop.text "Eras of Greek" ]
            Html.p [
                prop.className "ap-text"
                prop.text "Ancient Greek was never a single fixed language. It began as a family of regional dialects and kept changing for well over two thousand years. The periods below are the conventional ones, and their dates are convenient boundaries rather than sharp breaks."
            ]
            Html.div [
                prop.className "era-list"
                prop.children [
                    for e, n in erasList do
                        let hash = "#wiki/eras/" + e.Id
                        Html.a [
                            prop.key e.Id
                            prop.className "era-item"
                            prop.href hash
                            prop.custom ("data-letter", WikiData.eraLetter.TryFind e.Id |> Option.defaultValue "")
                            prop.onClick (navigateTo dispatch hash)
                            prop.children [
                                Html.div [
                                    prop.className "ei-head"
                                    prop.children [
                                        Html.b [ prop.text e.Name ]
                                        Html.span [ prop.className "yrs"; prop.text (yearRangeText e) ]
                                        Html.span [ prop.className "n"; prop.text (string n + " authors") ]
                                    ]
                                ]
                                Html.p [ prop.text (WikiData.eraNote.TryFind e.Id |> Option.defaultValue "") ]
                            ]
                        ]
                ]
            ]
        ]
    ]

let era (model: Model) (dispatch: Msg -> unit) (eraId: string) : ReactElement =
    let erasList = erasWithCounts model.Meta
    match erasList |> List.tryFindIndex (fun (e, _) -> e.Id = eraId) with
    | None -> eras model dispatch
    | Some idx ->
        let cur, _ = erasList.[idx]
        let rows =
            model.Catalog.Authors
            |> List.map (fun a -> a, metaFor model.Meta a.Id)
            |> List.filter (fun (_, m) -> m.Era = Some eraId)
            |> List.sortBy (fun (a, m) -> sortKey a m)
        let prev = if idx > 0 then Some(fst erasList.[idx - 1]) else None
        let next = if idx < erasList.Length - 1 then Some(fst erasList.[idx + 1]) else None
        Html.div [
            prop.className "page"
            prop.children [
                Shared.wikiCrumbs dispatch [ "Eras of Greek", Some "#wiki/eras"; cur.Name, None ]
                Html.h1 [ prop.className "ph"; prop.text cur.Name ]
                Html.p [ prop.className "quiet"; prop.text (yearRangeText cur) ]
                Html.p [ prop.className "ap-text"; prop.text (WikiData.eraNote.TryFind eraId |> Option.defaultValue "") ]
                Html.h2 [ prop.className "sh"; prop.text (sprintf "Authors of this era (%d)" rows.Length) ]
                Html.div [ prop.className "author-list"; prop.children [ for a, m in rows -> authorRow dispatch false a m ] ]
                Html.div [
                    prop.className "pager"
                    prop.children [
                        (match prev with
                         | Some p ->
                             let h = "#wiki/eras/" + p.Id
                             Html.a [ prop.className "btn"; prop.href h; prop.text ("← " + p.Name); prop.onClick (navigateTo dispatch h) ]
                         | None -> Html.span [])
                        (match next with
                         | Some n ->
                             let h = "#wiki/eras/" + n.Id
                             Html.a [ prop.className "btn"; prop.href h; prop.text (n.Name + " →"); prop.onClick (navigateTo dispatch h) ]
                         | None -> Html.span [])
                    ]
                ]
            ]
        ]

// ---------------------------------------------------------------------------
// manuscripts / variants article indexes
// ---------------------------------------------------------------------------

let articleIndex (model: Model) (dispatch: Msg -> unit) (kind: ArticleKind) : ReactElement =
    let title =
        match kind with
        | Manuscripts -> "Manuscripts & transmission"
        | Variants -> "Textual variants"
    let field = match kind with Manuscripts -> "manuscripts" | Variants -> "variants"
    let list =
        model.Catalog.Authors
        |> List.choose (fun a -> (metaFor model.Meta a.Id).Core |> Option.map (fun c -> a, metaFor model.Meta a.Id, c))
        |> List.sortBy (fun (a, m, _) -> sortKey a m)
    Html.div [
        prop.className "page"
        prop.children [
            Shared.wikiCrumbs dispatch [ title, None ]
            Html.h1 [ prop.className "ph"; prop.text title ]
            Html.p [ prop.className "ap-text"; prop.text (WikiData.articleIntro kind) ]
            Html.h2 [ prop.className "sh"; prop.text (sprintf "Articles (%d)" list.Length) ]
            Html.div [
                prop.className "art-list"
                prop.children [
                    for a, m, c in list do
                        let hash = "#author/" + a.Id + "/" + field
                        let text = match kind with Manuscripts -> c.Manuscripts | Variants -> c.Variants
                        let excerpt = if text.Length > 220 then text.Substring(0, 220) else text
                        Html.a [
                            prop.key a.Id
                            prop.className "art-item"
                            prop.href hash
                            prop.onClick (navigateTo dispatch hash)
                            prop.children [ Html.b [ prop.text a.Name ]; Html.span [ prop.className "ar-dates"; prop.text (dateSpan m) ]; Html.p [ prop.text (excerpt + "…") ] ]
                        ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// editions & translations
// ---------------------------------------------------------------------------

let editions (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let rows =
        model.Catalog.Authors
        |> List.collect (fun a -> a.Works |> List.collect (fun w -> w.Texts |> List.filter (fun t -> t.Desc.IsSome || t.Label <> "") |> List.map (fun t -> a, w, t)))
    let groups =
        rows
        |> List.groupBy (fun (_, _, t) -> WikiData.seriesOf (t.Desc |> Option.defaultValue t.Label))
        |> List.sortByDescending (fun (_, items) -> items.Length)
    let std =
        model.Catalog.Authors
        |> List.choose (fun a -> (metaFor model.Meta a.Id).Core |> Option.map (fun c -> a, metaFor model.Meta a.Id, c))
        |> List.sortBy (fun (a, m, _) -> sortKey a m)
    Html.div [
        // `editions-page` lets the stylesheet flow the per-series <details>
        // blocks into two columns on wide screens; as one stack they left the
        // right half of a very long page empty for its whole length.
        prop.className "page editions-page"
        prop.children (
            [ Shared.wikiCrumbs dispatch [ "Editions & translations", None ]
              Html.h1 [ prop.className "ph"; prop.text "Editions & translations" ]
              Html.p [ prop.className "ap-text"; prop.text WikiData.editionsIntro ]
              Html.h2 [ prop.className "sh"; prop.text "Standard editions, by author" ]
              Html.div [
                  prop.className "art-list"
                  prop.children [
                      for a, _, c in std do
                          let hash = "#author/" + a.Id + "/editions"
                          Html.a [
                              prop.key a.Id
                              prop.className "art-item"
                              prop.href hash
                              prop.onClick (navigateTo dispatch hash)
                              prop.children [ Html.b [ prop.text a.Name ]; Html.p [ prop.text (List.tryHead c.Editions |> Option.defaultValue "") ] ]
                          ]
                  ]
              ]
              Html.h2 [ prop.className "sh"; prop.text "Sources of this collection, by series" ] ]
            @ [ for seriesName, items in groups do
                    let sortedItems = items |> List.sortBy (fun (a, _, _) -> a.Name) |> List.truncate 400
                    Html.details [
                        prop.key seriesName
                        prop.children [
                            Html.summary [
                                prop.children [
                                    Html.b [ prop.text seriesName ]
                                    Html.span [ prop.className "quiet"; prop.text (sprintf " %d text%s" items.Length (if items.Length = 1 then "" else "s")) ]
                                ]
                            ]
                            Html.ul [
                                prop.className "ap-eds"
                                prop.children (
                                    [ for a, w, t in sortedItems ->
                                          let hash = "#author/" + a.Id
                                          Html.li [
                                              prop.children (
                                                  [ Html.a [ prop.href hash; prop.text a.Name; prop.onClick (navigateTo dispatch hash) ]
                                                    Html.text ", "
                                                    Html.b [ prop.text w.Title ]
                                                    Html.text (" — " + (t.Desc |> Option.defaultValue t.Label)) ]
                                                  @ (if t.Lang <> "grc" then [ Html.span [ prop.className "tag"; prop.text t.Lang ] ] else [])
                                              )
                                          ] ]
                                    @ (if items.Length > 400 then
                                           [ Html.li [ prop.className "quiet"; prop.text (sprintf "…and %d more" (items.Length - 400)) ] ]
                                       else
                                           [])
                                )
                            ]
                        ]
                    ] ]
        )
    ]

// ---------------------------------------------------------------------------
// author page
// ---------------------------------------------------------------------------

[<Emit("fetch($0)")>]
let private fetchUrl (url: string) : JS.Promise<obj> = jsNative

[<Emit("$0.ok")>]
let private responseOk (res: obj) : bool = jsNative

[<Emit("$0.json()")>]
let private responseJson (res: obj) : JS.Promise<obj> = jsNative

[<Emit("decodeURIComponent($0)")>]
let private decodeUriComponent (s: string) : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private encodeUriComponent (s: string) : string = jsNative

let private fetchWikipediaSummary (wikiUrl: string) : JS.Promise<string option> =
    promise {
        let parts = wikiUrl.Split([| "/wiki/" |], System.StringSplitOptions.None)
        let title = if parts.Length > 1 then decodeUriComponent parts.[1] else ""
        if title = "" then
            return None
        else
            try
                let! res = fetchUrl ("https://en.wikipedia.org/api/rest_v1/page/summary/" + encodeUriComponent title)
                if responseOk res then
                    let! json = responseJson res
                    let extract: string = json?extract
                    if isNullOrUndefined extract || extract = "" then return None else return Some extract
                else
                    return None
            with _ ->
                return None
    }

[<Emit("document.querySelector($0)?.scrollIntoView($1)")>]
let private scrollIntoViewSelector (selector: string) (opts: obj) : unit = jsNative

[<Emit("document.getElementById($0)?.scrollIntoView($1)")>]
let private scrollIntoViewId (id: string) (opts: obj) : unit = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeoutMs (f: unit -> unit) (ms: int) : unit = jsNative

/// `.ap-nav` links smooth-scroll to an in-page section rather than going
/// through the app's hash router (mirrors the original's own preventDefault
/// + scrollIntoView, since these hrefs share the `#` prefix with routes).
let private apNavLink (targetId: string) (label: string) : ReactElement =
    Html.a [
        prop.href ("#" + targetId)
        prop.text label
        prop.onClick (fun (e: Browser.Types.MouseEvent) ->
            e.preventDefault ()
            scrollIntoViewId targetId (createObj [ "behavior" ==> "smooth"; "block" ==> "start" ]))
    ]

[<ReactComponent>]
let private AuthorNotesEditor (model: Model) (dispatch: Msg -> unit) (authorId: string) =
    let mutable taEl: Browser.Types.HTMLTextAreaElement = Unchecked.defaultof<_>
    let currentNote = model.Library.AuthorNotes.TryFind authorId |> Option.defaultValue ""
    Html.div [
        prop.children [
            Html.textarea [
                prop.id "anote"
                prop.rows 5
                prop.placeholder "Anything you want to remember about this author."
                prop.defaultValue currentNote
                prop.ref (fun el -> if not (isNullOrUndefined el) then taEl <- el :?> Browser.Types.HTMLTextAreaElement)
            ]
            Html.div [
                prop.children [
                    Html.button [
                        prop.className "btn primary"
                        prop.id "anoteSave"
                        prop.text "Save"
                        prop.onClick (fun _ ->
                            dispatch (Library_(SetAuthorNote(authorId, taEl.value)))
                            dispatch (Library_(SaveAuthorNote authorId)))
                    ]
                ]
            ]
            Html.div [ prop.className "mk-note"; prop.id "anoteView"; prop.children (Shared.noteBody model.Catalog dispatch currentNote) ]
        ]
    ]

/// Article prose may run to several paragraphs, separated by a blank line.
/// `keyPrefix` keeps keys unique when several articles share one parent.
let private paragraphs (keyPrefix: string) (cls: string) (text: string) : ReactElement list =
    text.Split([| "\n\n" |], System.StringSplitOptions.RemoveEmptyEntries)
    |> Array.toList
    |> List.mapi (fun i p -> Html.p [ prop.key (keyPrefix + string i); prop.className cls; prop.text (p.Trim()) ])

/// An article on a single work (`WikiData.workArticles`). Its id is
/// "ap-<workId>", so `#author/<id>/<workId>` scrolls straight to it.
let private workArticleView (dispatch: Msg -> unit) (w: Work) (art: WikiData.WorkArticle) : ReactElement =
    let greek, english = art.Epigraph
    Html.article [
        prop.key w.Id
        prop.className "work-article"
        prop.id ("ap-" + w.Id)
        prop.children (
            [ Html.h2 [
                  prop.className "wa-title"
                  prop.children [ Html.text art.Title; Html.span [ prop.className "grc"; prop.lang "grc"; prop.text art.Grc ] ]
              ]
              Html.blockquote [
                  prop.className "wa-epigraph"
                  prop.children [
                      Html.span [ prop.className "grc"; prop.lang "grc"; prop.text greek ]
                      Html.span [ prop.className "tr"; prop.text english ]
                  ]
              ] ]
            @ (art.Sections
               |> List.mapi (fun i (heading, ps) ->
                   (heading |> Option.map (fun h -> Html.h3 [ prop.key ("h" + string i); prop.text h ]) |> Option.toList)
                   @ (ps |> List.mapi (fun j p -> Html.p [ prop.key (string i + "-" + string j); prop.className "ap-text"; prop.text p ])))
               |> List.concat)
            @ [ Html.p [
                    prop.className "wa-open"
                    prop.children [
                        Html.button [
                            prop.className "btn primary"
                            prop.text ("Read the " + art.Title)
                            prop.onClick (fun _ -> dispatch (Reader_(OpenWork(w.Id, None, None, None, None))))
                        ]
                    ]
                ] ]
        )
    ]

[<ReactComponent>]
let AuthorPage (model: Model) (dispatch: Msg -> unit) (authorId: string) (section: string option) =
    match model.Catalog.Authors |> List.tryFind (fun a -> a.Id = authorId) with
    | None -> authorsIndex model dispatch None ""
    | Some a ->
        let m = metaFor model.Meta authorId
        let era = model.Meta.Eras |> List.tryFind (fun e -> Some e.Id = m.Era)
        let genreId = WikiData.genreOf m
        let genreName = WikiData.genres |> List.tryFind (fun g -> g.Id = genreId) |> Option.map (fun g -> g.Name)

        let wikiExtract, setWikiExtract = React.useState<string option> None

        React.useEffect (
            (fun () ->
                setWikiExtract None
                if m.Core.IsNone then
                    match m.Wiki with
                    | Some wikiUrl ->
                        fetchWikipediaSummary wikiUrl
                        |> Promise.map (fun extract ->
                            match extract with
                            | Some e ->
                                setWikiExtract (Some e)
                                dispatch (WikipediaSummary(authorId, e))
                            | None -> ())
                        |> Promise.start
                    | None -> ()),
            [| box authorId |]
        )

        React.useEffect (
            (fun () ->
                match section with
                | Some s -> setTimeoutMs (fun () -> scrollIntoViewId ("ap-" + s) (createObj [ "block" ==> "start" ])) 50
                | None -> ()),
            [| box authorId; box section |]
        )

        let timeline =
            match m.Core with
            | Some c -> c.Timeline
            | None ->
                [ m.Birth |> Option.map (fun b -> b, "Born" + (m.Place |> Option.map (fun p -> " at " + p) |> Option.defaultValue ""))
                  m.Floruit |> Option.map (fun f -> f, "Active (floruit)")
                  m.Death |> Option.map (fun d -> d, "Died") ]
                |> List.choose id
            |> List.sortBy fst

        let editionsHere =
            a.Works
            |> List.collect (fun w -> w.Texts |> List.filter (fun t -> t.Desc.IsSome || t.Label <> "") |> List.map (fun t -> w, t))

        let articles = a.Works |> List.choose (fun w -> WikiData.workArticles.TryFind w.Id |> Option.map (fun art -> w, art))

        // What the article column opens with: the hand-written summary where
        // there is one, otherwise the Wikipedia extract once it has arrived.
        let overview =
            match m.Core, wikiExtract with
            | Some c, _ -> paragraphs "sum-" "ap-summary" c.Summary
            | None, Some extract -> [ Html.p [ prop.className "ap-text"; prop.text extract ]; Html.p [ prop.className "quiet"; prop.text "From Wikipedia (CC BY-SA)." ] ]
            | None, None -> []

        // Two columns, like a museum case: the article (the long read) at the
        // prose measure on the left, and a rail of facts on the right — works,
        // timeline, your notes. The rail comes first in the markup so that on
        // phones, where the columns stack, the works are the first thing found.
        Html.div [
            prop.className "page author-page"
            prop.children [
                Shared.wikiCrumbs dispatch [ "Authors", Some "#wiki/authors"; a.Name, None ]
                Html.div [
                    prop.className "ap-head"
                    prop.children [
                        Html.h1 [
                            prop.className "ph"
                            prop.children ([ Html.text a.Name ] @ (a.Grc |> Option.map (fun g -> Html.span [ prop.className "grc"; prop.text (" " + g) ]) |> Option.toList))
                        ]
                        Html.div [
                            prop.className "ap-meta"
                            prop.children (
                                (let ds = dateSpan m in if ds <> "" then [ Html.span [ prop.text ds ] ] else [])
                                @ (m.Place |> Option.map (fun p -> Html.span [ prop.text ("born " + p) ]) |> Option.toList)
                                @ (era
                                   |> Option.map (fun e ->
                                       let h = "#wiki/authors/era/" + e.Id
                                       Html.a [ prop.className "chip"; prop.href h; prop.text e.Name; prop.onClick (navigateTo dispatch h) ])
                                   |> Option.toList)
                                @ (genreName
                                   |> Option.map (fun gn ->
                                       let h = "#wiki/authors/genre/" + genreId
                                       Html.a [ prop.className "chip"; prop.href h; prop.text gn; prop.onClick (navigateTo dispatch h) ])
                                   |> Option.toList)
                                @ (m.Occ |> List.truncate 3 |> List.map (fun o -> Html.span [ prop.className "chip soft"; prop.text o ]))
                            )
                        ]
                        (match m.Desc with
                         | Some d when d <> "" -> Html.p [ prop.className "ap-desc"; prop.text (string (System.Char.ToUpper d.[0]) + d.Substring(1) + ".") ]
                         | _ -> Html.none)
                        Html.p [
                            prop.className "ap-links"
                            prop.children (
                                (m.Wiki |> Option.map (fun w -> Html.a [ prop.href w; prop.target "_blank"; prop.rel "noopener"; prop.text "Wikipedia ↗" ]) |> Option.toList)
                                @ (m.Q
                                   |> Option.map (fun q -> Html.a [ prop.href ("https://www.wikidata.org/wiki/" + q); prop.target "_blank"; prop.rel "noopener"; prop.text " Wikidata ↗" ])
                                   |> Option.toList)
                                @ [ Html.a [ prop.href "https://stephanus.tlg.uci.edu/"; prop.target "_blank"; prop.rel "noopener"; prop.text (" TLG " + authorId + " ↗") ] ]
                            )
                        ]
                        Html.nav [
                            prop.className "ap-nav"
                            prop.children (
                                (if List.isEmpty overview then [] else [ apNavLink "ap-overview" "Overview" ])
                                @ (articles |> List.map (fun (w, art) -> apNavLink ("ap-" + w.Id) art.Title))
                                @ [ apNavLink "ap-works" "Works" ]
                                @ (if List.isEmpty timeline then [] else [ apNavLink "ap-timeline" "Timeline" ])
                                @ (if m.Core.IsSome then
                                       [ apNavLink "ap-manuscripts" "Manuscripts"; apNavLink "ap-variants" "Variants"; apNavLink "ap-editions" "Editions" ]
                                   else
                                       [])
                                @ [ apNavLink "ap-notes" "My notes" ]
                            )
                        ]
                    ]
                ]
                Html.div [
                    prop.className "ap-cols"
                    prop.children [
                        Html.aside [
                            prop.className "ap-rail"
                            prop.children (
                                [ Html.h2 [ prop.className "sh"; prop.id "ap-works"; prop.text (sprintf "Works in this collection (%d)" a.Works.Length) ]
                                  Html.div [
                                      prop.className "ap-works"
                                      prop.children [
                                          for w in a.Works ->
                                              let grc = Catalog.titleGrc w |> Option.filter (fun t -> t <> w.Title)
                                              Html.button [
                                                  prop.key w.Id
                                                  prop.className "ap-work"
                                                  prop.onClick (fun _ -> dispatch (Reader_(OpenWork(w.Id, None, None, None, None))))
                                                  prop.children (
                                                      [ Html.b [ prop.text w.Title ] ]
                                                      @ (grc |> Option.map (fun g -> Html.span [ prop.className "grc"; prop.text g ]) |> Option.toList)
                                                      @ (if Catalog.hasTranslation w then [] else [ Html.span [ prop.className "tag"; prop.text "Greek only" ] ])
                                                      @ (if WikiData.workArticles.ContainsKey w.Id then [ Html.span [ prop.className "tag"; prop.text "article" ] ] else [])
                                                  )
                                              ]
                                      ]
                                  ] ]
                                @ (if List.isEmpty timeline then
                                       []
                                   else
                                       [ Html.h2 [ prop.className "sh"; prop.id "ap-timeline"; prop.text "Timeline" ]
                                         Html.ol [
                                             prop.className "timeline"
                                             prop.children [
                                                 for y, t in timeline -> Html.li [ prop.children [ Html.span [ prop.className "ty"; prop.text (fmtYear (Some y)) ]; Html.span [ prop.text t ] ] ]
                                             ]
                                         ] ])
                                @ [ Html.div [
                                        prop.className "ap-notes"
                                        prop.children [
                                            Html.h2 [ prop.className "sh"; prop.id "ap-notes"; prop.text ("My notes on " + a.Name) ]
                                            AuthorNotesEditor model dispatch authorId
                                        ]
                                    ] ]
                            )
                        ]
                        Html.div [
                            prop.className "ap-main"
                            prop.children (
                                [ ourTranslationNote "Translations quoted in these articles are our own. The reader shows each text with the published translation from Perseus or First1KGreek, which may read differently." ]
                                @ (if List.isEmpty overview then
                                     []
                                 else
                                     [ Html.div [
                                           prop.className "ap-summary-wrap"
                                           prop.id "ap-overview"
                                           prop.children overview
                                       ] ])
                                @ (articles |> List.map (fun (w, art) -> workArticleView dispatch w art))
                                @ (match m.Core with
                                   | Some c ->
                                       [ Html.h2 [ prop.className "sh"; prop.id "ap-manuscripts"; prop.text "How the text survived" ] ]
                                       @ paragraphs "ms-" "ap-text" c.Manuscripts
                                       @ [ Html.h2 [ prop.className "sh"; prop.id "ap-variants"; prop.text "Problems in the text" ] ]
                                       @ paragraphs "var-" "ap-text" c.Variants
                                       @ [ Html.h2 [ prop.className "sh"; prop.id "ap-editions"; prop.text "Standard editions" ]
                                           Html.ul [ prop.className "ap-eds"; prop.children [ for e in c.Editions -> Html.li [ prop.text e ] ] ] ]
                                   | None ->
                                       [ Html.h2 [ prop.className "sh"; prop.id "ap-manuscripts"; prop.text "How the text survived" ]
                                         Html.p [
                                             prop.className "quiet"
                                             prop.children [
                                                 Html.text "There is no article on this author's manuscripts yet. The major authors have one (see "
                                                 Html.a [
                                                     prop.href "#wiki/manuscripts"
                                                     prop.text "Manuscripts & transmission"
                                                     prop.onClick (navigateTo dispatch "#wiki/manuscripts")
                                                 ]
                                                 Html.text "). Below you'll find the printed edition behind each text here, and the Wikipedia link above covers the author's life."
                                             ]
                                         ] ])
                                @ (if List.isEmpty editionsHere then
                                       []
                                   else
                                       [ Html.h2 [ prop.className "sh"; prop.text "Editions in this collection" ]
                                         Html.details [
                                             if editionsHere.Length <= 8 then prop.isOpen true
                                             prop.children [
                                                 Html.summary [
                                                     prop.className "quiet"
                                                     prop.text (sprintf "%d printed source%s behind the texts here" editionsHere.Length (if editionsHere.Length = 1 then "" else "s"))
                                                 ]
                                                 Html.ul [
                                                     prop.className "ap-eds"
                                                     prop.children [
                                                         for w, t in editionsHere ->
                                                             Html.li [
                                                                 prop.children (
                                                                     [ Html.b [ prop.text w.Title ]; Html.text (" — " + (t.Desc |> Option.defaultValue t.Label)) ]
                                                                     @ (if t.Lang <> "grc" then [ Html.span [ prop.className "tag"; prop.text t.Lang ] ] else [])
                                                                 )
                                                             ]
                                                     ]
                                                 ]
                                             ]
                                         ] ])
                            )
                        ]
                    ]
                ]
            ]
        ]
