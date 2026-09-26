/// `#study`: the front page of the Study section. The word the site is named
/// after and what the Greeks made of learning (text in `WikiData.wikiIntro`),
/// then the "Start here" guide, two references after it (the alphabet at a
/// glance and "five things to know", both moved here from the home page; after
/// the guide so they don't push it down on phones), the practice lessons
/// (Views.Learn), then a short essay on learning with three quotations.
module Views.Study

open Feliz
open Types

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

/// A passage link in the prose or the quotations: into the reader, at that
/// passage (and that part, for works read part by part).
let private passageLink (dispatch: Msg -> unit) (cls: string) (label: string) (workId: string) (chunk: string option) (ref: string) : ReactElement =
    let hash = Router.toHash (ReaderRoute(workId, "", "", chunk, Some ref))
    Html.a [ prop.className cls; prop.href (Router.href hash); prop.title "Read it in context"; prop.text label; prop.onClick (navigateTo dispatch hash) ]

let private runs (dispatch: Msg -> unit) (xs: WikiData.IntroRun list) : ReactElement list =
    xs
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

/// The word and its family: the page's title block.
let private hero (dispatch: Msg -> unit) : ReactElement =
    let intro = WikiData.wikiIntro
    Html.section [
        prop.className "study-hero"
        prop.children [
            Html.div [
                prop.className "sh-main"
                prop.children [
                    Html.p [ prop.className "wh-kicker"; prop.text "Study" ]
                    Html.h1 [
                        prop.className "ph wh-title"
                        prop.children [
                            Html.span [ prop.className "grc"; prop.lang "grc"; prop.text "μάθησις" ]
                            Html.span [ prop.className "wh-gloss"; prop.text "máthēsis · learning, the act of learning" ]
                        ]
                    ]
                    Html.p [ prop.className "wh-lead"; prop.children (runs dispatch intro.Lead) ]
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

/// The alphabet at a glance: moved here from the home page. It follows the
/// guide, whose first step (linked just above it) teaches the sounds, so it
/// carries no link of its own.
let private alphabet : ReactElement =
    Html.section [
        prop.className "study-block"
        prop.ariaLabel "The alphabet at a glance"
        prop.children [
            Html.h2 [ prop.className "sh"; prop.text "The alphabet at a glance" ]
            Html.div [
                prop.className "alpha"
                prop.children [
                    for row in Content.alphabet ->
                        Html.div [
                            prop.key row.Name
                            prop.className "al"
                            prop.children [
                                Html.span [ prop.className "grc"; prop.text (row.Upper + " " + row.Lower) ]
                                Html.b [ prop.text row.Name ]
                                Html.span [ prop.className "t"; prop.text row.Translit ]
                            ]
                        ]
                ]
            ]
        ]
    ]

/// "Reading Greek: five things to know": the marks over and under the
/// letters, in short (moved here from the home page). Step 3 of the guide,
/// listed just above, teaches them in full, so no link of its own.
let private tips : ReactElement =
    Html.section [
        prop.className "study-block"
        prop.ariaLabel "Reading Greek: five things to know"
        prop.children [
            Html.h2 [ prop.className "sh"; prop.text "Reading Greek: five things to know" ]
            Html.div [
                prop.className "tips"
                prop.children [
                    for t in Content.tips ->
                        Html.div [
                            prop.key t.Label
                            prop.children [
                                Html.span [ prop.className "grc"; prop.lang "grc"; prop.text t.Grc ]
                                Html.b [ prop.text t.Label ]
                                Html.span [ prop.text t.Text ]
                            ]
                        ]
                ]
            ]
        ]
    ]

let private startHere (dispatch: Msg -> unit) : ReactElement =
    let first = GuideData.hashOf (snd GuideData.steps.Head).Slug
    Html.section [
        prop.className "study-block"
        prop.ariaLabel "Start here"
        prop.children [
            Html.h2 [ prop.className "sh"; prop.text "Start here: learn to read the Greek" ]
            Html.p [
                prop.className "sh-lede"
                prop.text "A short guide in eight steps, from the letters and their sounds to the life story of a word. You need no Greek to begin; steps 1 to 3 take about half an hour."
            ]
            Guide.path dispatch
            Html.div [
                prop.className "g-begin"
                prop.children [
                    Html.a [ prop.className "btn primary"; prop.href (Router.href first); prop.text "Begin with the alphabet →"; prop.onClick (navigateTo dispatch first) ]
                ]
            ]
        ]
    ]

let private practise (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.section [
        prop.className "study-block study-practice"
        prop.ariaLabel "Practise"
        prop.children [
            Html.h2 [ prop.className "sh"; prop.text "Practise" ]
            Html.p [
                prop.className "sh-lede"
                prop.text "Lessons with flashcards, matching, letters to trace and a line of Homer to read, for the steps above."
            ]
            Html.p [
                prop.className "study-fun"
                prop.children [
                    Html.b [ prop.text "Just for fun. " ]
                    Html.text "These exercises are a game to help things stick, not a test or a formal assessment: nothing is graded, and your progress stays in this browser."
                ]
            ]
            Html.div [ prop.className "learn study-lessons"; prop.children [ Learn.contents model.Learn dispatch ] ]
        ]
    ]

/// What the Greeks said about learning, three quotations, and why it is worth it.
let private onLearning (dispatch: Msg -> unit) : ReactElement =
    let intro = WikiData.wikiIntro
    Html.section [
        prop.className "study-block study-essay"
        prop.children [
            Html.h2 [ prop.className "sh"; prop.text "On learning" ]
            Html.div [
                prop.className "se-cols"
                prop.children [
                    Html.p [ prop.className "wh-text"; prop.children (runs dispatch intro.Philosophy) ]
                    Html.p [ prop.className "wh-text"; prop.children (runs dispatch intro.Living) ]
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
            Html.p [
                prop.className "tr-note"
                prop.children [
                    Html.text "The English on this page is our own translation. Open a passage and the reader shows it with the published translation that comes with the text, from Perseus or First1KGreek, which may read differently. "
                    Html.span [ prop.className "tr-star"; prop.text "*" ]
                    Html.text " marks a rendering we are unsure of, or one that scholars dispute."
                ]
            ]
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "page study"
        prop.children [ hero dispatch; startHere dispatch; alphabet; tips; practise model dispatch; onLearning dispatch ]
    ]
