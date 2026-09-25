module Views.Home

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Types

[<Emit("document.getElementById($0).click()")>]
let private clickElementById (id: string) : unit = jsNative

[<Emit("$0.toLocaleString()")>]
let private toLocaleString (n: int) : string = jsNative

[<Emit("Math.floor(Date.now() / 86400000)")>]
let private daysSinceEpoch (): int = jsNative

let private connectZip (dispatch: Msg -> unit) () =
    if Sources.hasOpenFilePicker then
        Sources.pickZip ()
        |> Promise.map (function
            | Some(Ok(repo, name)) -> dispatch (Source_(ZipConnected(repo, name)))
            | Some(Error msg) -> dispatch (Source_(SourceFailed msg))
            | None -> ())
        |> Promise.start
    else
        clickElementById "zipInput"

let private connectDir (dispatch: Msg -> unit) () =
    if Sources.hasDirectoryPicker then
        Sources.pickDir ()
        |> Promise.map (function
            | Some(Ok(repos, name)) -> dispatch (Source_(DirConnected(repos, name)))
            | Some(Error msg) -> dispatch (Source_(SourceFailed msg))
            | None -> ())
        |> Promise.start
    else
        clickElementById "dirInput"

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

let private openWork (dispatch: Msg -> unit) (workId: string) (chunk: string option) (seg: string option) (_: Browser.Types.MouseEvent) =
    dispatch (Reader_(OpenWork(workId, None, None, chunk, seg)))

// ---------------------------------------------------------------------------
// small shared building blocks
// ---------------------------------------------------------------------------

let private cardHeader (dispatch: Msg -> unit) (iconName: string) (label: string) (moreLink: (string * string) option) : ReactElement list =
    [ Html.span [
          prop.children [
              Html.span [ prop.className "ic"; prop.children [ Shared.icon iconName ] ]
              Html.text label
          ]
      ]
      match moreLink with
      | Some(hash, linkLabel) ->
          Html.a [ prop.className "more"; prop.href hash; prop.text linkLabel; prop.onClick (navigateTo dispatch hash) ]
      | None -> Html.none ]

let private miniLink (dispatch: Msg -> unit) (hash: string) (label: string) (desc: string) : ReactElement =
    Html.a [
        prop.className "mini"
        prop.href hash
        prop.onClick (navigateTo dispatch hash)
        prop.children [ Html.b [ prop.text label ]; Html.span [ prop.text desc ] ]
    ]

let private stripParenSuffix (name: string) : string =
    System.Text.RegularExpressions.Regex.Replace(name, @" \(.*\)", "")

/// Mirrors `localSummary`'s wording (also used, separately, by SettingsPane's
/// own local-source pane) as real Feliz elements.
let private localSummaryText (source: SourceState) : ReactElement list =
    let have = source.Local |> Map.toList |> List.map fst
    if List.isEmpty have then
        []
    else
        let miss = [ Perseus; First1K ] |> List.filter (fun r -> not (List.contains r have))
        let howSuffix repo =
            match source.Local.TryFind repo with
            | Some(ZipArchive _) -> " (ZIP)"
            | _ -> ""
        let haveText = have |> List.map (fun r -> Sources.repoName r + howSuffix r) |> String.concat " and "
        [ Html.b [ prop.text "Reading from your computer:" ]
          Html.text (" " + haveText + ".")
          if not (List.isEmpty miss) then
              Html.text (" " + (miss |> List.map Sources.repoName |> String.concat " and ") + " isn't connected yet — its texts will come from GitHub until you add its ZIP too.")
          else
              Html.text " Everything is available offline." ]

// ---------------------------------------------------------------------------
// hero / stats / lead
// ---------------------------------------------------------------------------

let private statsBlock (catalog: Catalog) : ReactElement =
    let nWorks = catalog.WorkById.Count
    let nTranslated = catalog.WorkById |> Map.toList |> List.filter (fun (_, w) -> Catalog.hasTranslation w) |> List.length
    Html.div [
        prop.className "stats"
        prop.children [
            Html.div [ prop.children [ Html.b [ prop.text (string catalog.Authors.Length) ]; Html.text "authors" ] ]
            Html.div [ prop.children [ Html.b [ prop.text (toLocaleString nWorks) ]; Html.text "works" ] ]
            Html.div [ prop.children [ Html.b [ prop.text (toLocaleString nTranslated) ]; Html.text "with a translation" ] ]
        ]
    ]

let private leadBlock: ReactElement =
    Html.p [
        prop.className "lead"
        prop.children [
            Html.text "Every text in two open collections: the Perseus Digital Library's "
            Html.i [ prop.text "canonical-greekLit" ]
            Html.text " and Open Greek and Latin's "
            Html.i [ prop.text "First1KGreek" ]
            Html.text
                ". Each text is downloaded when you open it and lined up with its translation in your browser, passage by passage, using the references scholars cite: book and line for Homer, Stephanus pages for Plato, chapter and section for the historians."
        ]
    ]

// ---------------------------------------------------------------------------
// the motto, cut stoichedon
// ---------------------------------------------------------------------------

/// The motto as an Athenian mason after 403 BCE would have cut it: Ionic
/// capitals, no accents, no word spaces, thirteen letters to a row on an exact
/// grid — so μανθάνειν breaks across the line wherever the grid falls, as real
/// stoichedon texts do.
let private mottoCaps = "ΤΟΓΑΡΜΑΝΘΑΝΕΙΝΗΔΙΣΤΗΑΝΑΓΚΗ"

/// Each word of the accented text, with the span of capitals it came from.
let private mottoWords = [ "Τὸ", 0, 2; "γὰρ", 2, 5; "μανθάνειν", 5, 14; "ἡδίστη", 14, 20; "ἀνάγκη", 20, 26 ]

let private mottoWordAt (i: int) : int =
    mottoWords |> List.tryFindIndex (fun (_, a, b) -> i >= a && i < b) |> Option.defaultValue -1

/// Hovering or focusing a word in either version lights its letters in the
/// other: continuous capitals on one side, the edited, accented text on the
/// other. Which word is lit is momentary pointer state, so it lives here.
[<ReactComponent>]
let private StoichedonMotto () =
    let lit, setLit = React.useState -1
    Html.div [
        prop.className "hero-kicker stoi-wrap"
        prop.children [
            Html.div [
                prop.className "stoi"
                prop.role "img"
                prop.ariaLabel "ΤΟΓΑΡΜΑΝΘΑΝΕΙΝΗΔΙΣΤΗΑΝΑΓΚΗ: the motto in capitals, without accents or spaces, as an Athenian inscription would set it"
                prop.onMouseLeave (fun _ -> setLit -1)
                prop.children [
                    for i in 0 .. mottoCaps.Length - 1 ->
                        let w = mottoWordAt i
                        Html.span [
                            prop.key i
                            prop.className (if lit >= 0 && w = lit then "lit" else "")
                            prop.style [ style.custom ("--i", i) ]
                            prop.onMouseEnter (fun _ -> setLit w)
                            prop.text (string mottoCaps.[i])
                        ]
                ]
            ]
            Html.p [
                prop.className "stoi-m"
                prop.lang "grc"
                prop.children [
                    for k, (word, _, _) in List.indexed mottoWords do
                        Html.span [
                            prop.key k
                            prop.tabIndex 0
                            prop.className (if lit = k then "lit" else "")
                            prop.onMouseEnter (fun _ -> setLit k)
                            prop.onMouseLeave (fun _ -> setLit -1)
                            prop.onFocus (fun _ -> setLit k)
                            prop.onBlur (fun _ -> setLit -1)
                            prop.text word
                        ]
                        Html.text (if k = mottoWords.Length - 1 then "." else " ")
                ]
            ]
            Html.p [ prop.className "stoi-t"; prop.text "For learning is the sweetest necessity." ]
        ]
    ]

/// The hero leads with the plain-English promise and something to click; the
/// motto sits above it as the kicker. `startTarget` is whatever "Start
/// reading" should open — the most recent text if there is one, otherwise the
/// first suggested pick — so the primary action is never a dead end.
let private heroSection (catalog: Catalog) (startTarget: (string * string option) option) (pod: ReactElement option) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className ("hero-wrap" + (if pod.IsSome then " with-pod" else ""))
        prop.children [
            Html.div [
                prop.className "hero-body"
                prop.children [
                    StoichedonMotto ()
                    Html.h1 [ prop.className "hero"; prop.text "Read Ancient Greek with the translation beside it." ]
                    Html.p [
                        prop.className "hero-sub"
                        prop.text
                            "Greek and English side by side, passage by passage, with every word one click from a dictionary."
                    ]
                    Html.div [
                        prop.className "hero-cta"
                        prop.children [
                            match startTarget with
                            | Some(workId, chunk) ->
                                Html.button [
                                    prop.className "btn big"
                                    prop.text "Start reading →"
                                    prop.onClick (openWork dispatch workId chunk None)
                                ]
                            | None -> ()
                            Html.a [
                                prop.className "btn ghost"
                                prop.href "#browse"
                                prop.text ("Browse " + toLocaleString catalog.WorkById.Count + " works")
                                prop.onClick (navigateTo dispatch "#browse")
                            ]
                        ]
                    ]
                    statsBlock catalog
                ]
            ]
            yield! Option.toList pod
        ]
    ]

// ---------------------------------------------------------------------------
// continue reading
// ---------------------------------------------------------------------------

let private continueReadingSection (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    if List.isEmpty model.Recent then
        []
    else
        [ Html.section [
              prop.className "home-block"
              prop.id "contBlock"
              prop.children [
                  Html.h2 [
                      prop.className "sh"
                      prop.children [
                          Html.text "Continue reading "
                          Html.button [
                              prop.className "link-btn"
                              prop.id "contClear"
                              prop.text "Clear list"
                              prop.onClick (fun _ -> dispatch (ForgetRecent None))
                          ]
                      ]
                  ]
                  Html.div [
                      prop.className "cont"
                      prop.children [
                          for r in model.Recent ->
                              let title = Catalog.workTitle model.Catalog r.Id |> Option.defaultValue r.Id
                              let authorName =
                                  Catalog.authorOf model.Catalog r.Id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
                              let chunkSuffix =
                                  match r.Chunk with
                                  | Some c when c <> "all" -> " · part " + c
                                  | _ -> ""
                              Html.div [
                                  prop.key r.Id
                                  prop.className "cont-item"
                                  prop.children [
                                      Html.button [
                                          prop.className "cont-open"
                                          prop.onClick (openWork dispatch r.Id r.Chunk None)
                                          prop.children [ Html.b [ prop.text title ]; Html.span [ prop.text (authorName + chunkSuffix) ] ]
                                      ]
                                      Html.button [
                                          prop.className "cont-x"
                                          prop.ariaLabel ("Remove " + title + " from Continue reading")
                                          prop.title "Remove from list"
                                          prop.text "×"
                                          prop.onClick (fun e ->
                                              e.stopPropagation ()
                                              dispatch (ForgetRecent(Some r.Id)))
                                      ]
                                  ]
                              ]
                      ]
                  ]
              ]
          ] ]

// ---------------------------------------------------------------------------
// search tools + results
// ---------------------------------------------------------------------------

let private homeFilterBtn (model: Model) (dispatch: Msg -> unit) (value: WorksFilter) (dataV: string) (label: string) : ReactElement =
    Html.button [
        prop.custom ("data-v", dataV)
        prop.custom ("aria-pressed", (model.Filter = value))
        prop.text label
        prop.onClick (fun _ -> dispatch (SetFilter value))
    ]

let private homeToolsAndResults (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let results = Catalog.searchWorks model.Catalog model.Filter model.HomeQuery
    let hasQuery = model.HomeQuery.Trim() <> ""
    [ Html.div [
          prop.className "home-tools"
          prop.children [
              Html.span [ prop.className "lbl"; prop.text "Show" ]
              Html.div [
                  prop.className "filter"
                  prop.role "group"
                  prop.ariaLabel "Filter the library"
                  prop.children [
                      homeFilterBtn model dispatch FilterAll "all" "All"
                      homeFilterBtn model dispatch FilterTranslated "trans" "With translation"
                      homeFilterBtn model dispatch FilterGreekOnly "grc" "Greek only"
                  ]
              ]
              Html.input [
                  prop.className "search"
                  prop.id "homeQ"
                  prop.placeholder "Search by author or title"
                  prop.autoComplete "off"
                  prop.value model.HomeQuery
                  prop.onChange (fun (v: string) -> dispatch (SetHomeQuery v))
              ]
          ]
      ]
      Html.div [
          prop.className "home-results"
          prop.id "homeResults"
          prop.children [
              if hasQuery && List.isEmpty results then
                  Html.div [ prop.className "quiet"; prop.text "Nothing matches that. Try an author's name, a title, or part of either." ]
              for a, w in results do
                  let grcTitle = Catalog.titleGrc w
                  Html.button [
                      prop.key w.Id
                      prop.className "hr"
                      prop.onClick (openWork dispatch w.Id None None)
                      prop.children [
                          Html.span [
                              prop.children [
                                  Html.b [ prop.text w.Title ]
                                  match grcTitle with
                                  | Some g when g <> w.Title -> Html.span [ prop.className "grc"; prop.text g ]
                                  | _ -> Html.none
                              ]
                          ]
                          Html.span [
                              prop.className "who"
                              prop.text (a.Name + (if Catalog.hasTranslation w then "" else " · Greek only"))
                          ]
                      ]
                  ]
              if results.Length >= 40 then
                  Html.div [ prop.className "quiet"; prop.text "Showing the 40 best matches. Keep typing to narrow them down." ]
          ]
      ] ]

// ---------------------------------------------------------------------------
// suggested picks
// ---------------------------------------------------------------------------

let private preferredWorkIds =
    [ "tlg0012.tlg001"; "tlg0059.tlg002"; "tlg0086.tlg010"; "tlg0011.tlg002"; "tlg0016.tlg001"; "tlg0085.tlg003"
      "tlg0007.tlg012"; "tlg0062.tlg002"; "tlg0557.tlg002"; "tlg0032.tlg006"; "tlg0020.tlg002"; "tlg0003.tlg001"
      "tlg0562.tlg001"; "tlg0093.tlg001"; "tlg0057.tlg002"; "tlg0086.tlg025"; "tlg0059.tlg036"; "tlg0004.tlg001"
      "tlg0010.tlg011"; "tlg0005.tlg001"; "tlg0060.tlg001"; "tlg0087.tlg003"; "tlg2000.tlg001"; "tlg0018.tlg001" ]

let private suggestedPicks (catalog: Catalog) (filter: WorksFilter) : Work list =
    let fromPref =
        preferredWorkIds
        |> List.choose catalog.WorkById.TryFind
        |> List.filter (Catalog.passesFilter filter)
    let mutable picks = fromPref
    let mutable idSet = fromPref |> List.map (fun w -> w.Id) |> Set.ofList
    if picks.Length < 12 then
        for a in catalog.Authors do
            for w in a.Works do
                if picks.Length < 12 && Catalog.passesFilter filter w && not (idSet.Contains w.Id) && (w.Texts |> List.exists (fun t -> t.Lang = "grc")) then
                    picks <- picks @ [ w ]
                    idSet <- idSet.Add w.Id
    picks |> List.truncate 12

let private picksCardSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Shared.collapsibleSection dispatch model.Collapsed "home-card picks-card" "picks" "" (cardHeader dispatch "authors" "Suggested starting points" None) [
        Html.div [
            prop.className "picks"
            prop.children [ for w in suggestedPicks model.Catalog model.Filter -> Shared.workCard model.Catalog dispatch w ]
        ]
    ]

// ---------------------------------------------------------------------------
// wiki card
// ---------------------------------------------------------------------------

let private wikiCardSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let eraNames =
        model.Meta.Eras
        |> List.filter (fun e -> model.Meta.Authors |> Map.exists (fun _ m -> m.Era = Some e.Id))
        |> List.map (fun e -> stripParenSuffix e.Name)
    Shared.collapsibleSection dispatch model.Collapsed "home-card" "wiki" "" (cardHeader dispatch "wiki" "Wiki" None) [
        Html.div [
            prop.className "mini-list"
            prop.children [
                miniLink dispatch "#wiki/authors" "Authors" "Lives and timelines, by era and by genre"
                miniLink dispatch "#wiki/eras" "Eras of Greek" (String.concat " · " eraNames)
                miniLink dispatch "#wiki/manuscripts" "Manuscripts & transmission" "How the texts survived, and the manuscripts that matter"
                miniLink dispatch "#wiki/variants" "Textual variants" "Added lines, disputed works and other puzzles"
                miniLink dispatch "#wiki/editions" "Editions & translations" "The editions scholars use, and where these texts come from"
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// passage of the day
// ---------------------------------------------------------------------------

/// Sits beside the hero. The Greek opens with an ochre initial, after the gold
/// and red initials of Byzantine books (red is kept for the reader's own marks).
/// The capital is drawn for the eye only; the text itself stays as edited.
let private passageOfDaySection (model: Model) (dispatch: Msg -> unit) : ReactElement option =
    let avail = Content.passages |> List.filter (fun p -> model.Catalog.WorkById.ContainsKey p.Work)
    if List.isEmpty avail then
        None
    else
        let p = avail.[(daysSinceEpoch ()) % avail.Length]
        let greek =
            match Shared.initialOf p.Grc with
            | Some(cap, rest) ->
                [ Html.span [ prop.className "ini"; prop.ariaHidden true; prop.text cap ]
                  Html.span [ prop.className "sr-only"; prop.text (p.Grc.Substring(0, 1)) ]
                  Html.text rest ]
            | None -> [ Html.text p.Grc ]
        Some(
            Shared.collapsibleSection dispatch model.Collapsed "home-card pod" "pod" "" [ Html.span [ prop.className "pod-label"; prop.text "Passage of the day" ] ] [
                Html.blockquote [
                    prop.className "pod-q"
                    // The English is ours, and says so: "Read it in context"
                    // opens the reader, which shows the published translation.
                    prop.children [
                        Html.span [ prop.className "grc"; prop.lang "grc"; prop.children greek ]
                        if p.En <> "" then Html.span [ prop.className "en"; prop.text p.En ]
                        if p.En <> "" then
                            Html.span [
                                prop.className "tr-note"
                                prop.text (
                                    "Our translation; the reader shows the published one."
                                    + (if p.En.EndsWith "*" then " * marks a rendering we are unsure of." else "")
                                )
                            ]
                    ]
                ]
                Html.div [
                    prop.className "pod-foot"
                    prop.children [
                        Html.span [ prop.className "quiet"; prop.text p.Who ]
                        Html.button [
                            prop.className "btn small"
                            prop.text "Read it in context →"
                            prop.onClick (openWork dispatch p.Work None (Some p.Ref))
                        ]
                    ]
                ]
            ]
        )

// ---------------------------------------------------------------------------
// reading paths
// ---------------------------------------------------------------------------

let private readingPathsSection (model: Model) (dispatch: Msg -> unit) : ReactElement option =
    let paths =
        Content.paths
        |> List.map (fun p -> p, p.WorkIds |> List.choose model.Catalog.WorkById.TryFind)
        |> List.filter (fun (_, works) -> works.Length >= 2)
    if List.isEmpty paths then
        None
    else
        Some(
            Shared.collapsibleSection dispatch model.Collapsed "home-block" "paths" "sh" [ Html.text "Reading paths" ] [
                Html.div [
                    prop.className "paths"
                    prop.children [
                        for p, works in paths ->
                            Html.div [
                                prop.key p.Title
                                prop.className "path"
                                prop.children [
                                    Html.b [ prop.text p.Title ]
                                    Html.span [ prop.className "quiet"; prop.text p.Desc ]
                                    Html.ol [
                                        prop.children [
                                            for w in works ->
                                                Html.li [
                                                    prop.key w.Id
                                                    prop.children [
                                                        Html.button [
                                                            prop.onClick (openWork dispatch w.Id None None)
                                                            prop.children [
                                                                Html.text w.Title
                                                                Html.span [
                                                                    prop.className "who"
                                                                    prop.text (
                                                                        Catalog.authorOf model.Catalog w.Id
                                                                        |> Option.map (fun a -> a.Name)
                                                                        |> Option.defaultValue ""
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
                    ]
                ]
            ]
        )

// ---------------------------------------------------------------------------
// eras strip
// ---------------------------------------------------------------------------

/// The eras drawn to scale (see `Shared.erasBand`): the strip used to give
/// every era the same box, which hid the one thing worth seeing at a glance.
let private erasStripSection (model: Model) (dispatch: Msg -> unit) : ReactElement option =
    if List.isEmpty model.Meta.Eras then
        None
    else
        Some(
            Shared.collapsibleSection dispatch model.Collapsed "home-block" "eras" "sh" [ Html.text "Greek through the centuries" ] [
                Shared.erasBand model dispatch
            ]
        )

// ---------------------------------------------------------------------------
// alphabet / tips / how-it-works (static content)
// ---------------------------------------------------------------------------

/// The home page's alphabet and tips are the short version of the "Start here"
/// guide, which follows them at the foot of the page; each links to its step.
let private deeperLink (dispatch: Msg -> unit) (slug: string) (label: string) : ReactElement =
    let h = GuideData.hashOf slug
    Html.p [
        prop.className "deeper"
        prop.children [ Html.a [ prop.href h; prop.text label; prop.onClick (navigateTo dispatch h) ] ]
    ]

let private alphabetSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Shared.collapsibleSection dispatch model.Collapsed "home-block beginners" "alphabet" "sh" [ Html.text "New to Greek? The alphabet at a glance" ] [
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
        deeperLink dispatch "alphabet-and-sounds" "How to say each letter: step 1 of the guide below →"
    ]

let private tipsSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Shared.collapsibleSection dispatch model.Collapsed "home-block beginners" "tips" "sh" [ Html.text "Reading Greek: five things to know" ] [
        Html.div [
            prop.className "tips"
            prop.children [
                for t in Content.tips ->
                    Html.div [
                        prop.key t.Label
                        prop.children [
                            Html.span [ prop.className "grc"; prop.text t.Grc ]
                            Html.b [ prop.text t.Label ]
                            Html.span [ prop.text t.Text ]
                        ]
                    ]
            ]
        ]
        deeperLink dispatch "breathings-accents-punctuation" "Breathings, accents and punctuation: step 3 of the guide →"
    ]

/// The beginner's guide, at the foot of the page: its steps from the alphabet
/// to a word study, and a way in. Never folds: it is the page's last word.
let private startHereSection (dispatch: Msg -> unit) : ReactElement =
    let first = GuideData.hashOf (snd GuideData.steps.Head).Slug
    Shared.fixedSection "home-block start-here" "sh" [ Html.text "Start here: learn to read the Greek" ] [
        Html.p [
            prop.className "sh-lede"
            prop.text
                "A short guide in eight steps, from the letters and their sounds to the life story of a word. You need no Greek to begin; steps 1 to 3 take about half an hour."
        ]
        Views.Guide.path dispatch
        Html.div [
            prop.className "g-begin"
            prop.children [
                Html.a [ prop.className "btn primary"; prop.href first; prop.text "Begin with the alphabet →"; prop.onClick (navigateTo dispatch first) ]
            ]
        ]
    ]

let private howToSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    // Never collapses (a standing decision, like the hero and the offline setup).
    Shared.fixedSection "home-block" "sh" [ Html.text "How it works" ] [
        Html.div [
            prop.className "howto"
            prop.children [
                Html.div [
                    prop.children [
                        Html.span [ prop.className "num"; prop.text "1" ]
                        Html.b [ prop.text "Choose a text" ]
                        Html.span [
                            prop.text "Open the library (☰) or search below. Where there's a translation you'll see it beside the Greek; otherwise the Greek has the page to itself."
                        ]
                    ]
                ]
                Html.div [
                    prop.children [
                        Html.span [ prop.className "num"; prop.text "2" ]
                        Html.b [ prop.text "Tap any Greek word" ]
                        Html.span [ prop.text "A small card opens with links to look the word up in Logeion, Perseus and Wiktionary." ]
                    ]
                ]
                Html.div [
                    prop.children [
                        Html.span [ prop.className "num"; prop.text "3" ]
                        Html.b [ prop.text "Bookmark and note" ]
                        Html.span [
                            prop.text
                                "Press the bookmark beside a passage to save it with a note, and link it to your other saved passages. Everything you save is waiting under My library."
                        ]
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// offline setup block
// ---------------------------------------------------------------------------

let private offlineSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "offline"
        prop.children [
            Html.h2 [ prop.text "Want it to work without internet?" ]
            Html.p [ prop.text "Texts normally load from GitHub as you open them. To read from your own computer instead, it takes two clicks:" ]
            Html.ol [
                prop.children [
                    Html.li [
                        prop.children [
                            Html.b [ prop.text "Download" ]
                            Html.text " a collection: "
                            Html.a [
                                prop.className "dl"
                                prop.href "https://github.com/PerseusDL/canonical-greekLit/archive/refs/heads/master.zip"
                                prop.text "canonical-greekLit (Perseus, ~80 MB)"
                                prop.onClick (fun _ -> dispatch (ShowToast "Downloading… when it finishes, press “Add downloaded ZIP”"))
                            ]
                            Html.text " or "
                            Html.a [
                                prop.className "dl"
                                prop.href "https://github.com/OpenGreekAndLatin/First1KGreek/archive/refs/heads/master.zip"
                                prop.text "First1KGreek (~210 MB)"
                                prop.onClick (fun _ -> dispatch (ShowToast "Downloading… when it finishes, press “Add downloaded ZIP”"))
                            ]
                            Html.text ". Either one is fine on its own."
                        ]
                    ]
                    Html.li [
                        prop.children [
                            Html.b [ prop.text "Add the ZIP" ]
                            Html.text
                                " you just downloaded: press the button and choose it, or drag it onto this page. There's no need to unzip it; the reader opens texts straight from the archive."
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "offline-actions"
                prop.children [
                    Html.button [ prop.className "btn big"; prop.id "landZip"; prop.text "Add downloaded ZIP"; prop.onClick (fun _ -> connectZip dispatch ()) ]
                    Html.text " "
                    Html.button [
                        prop.className "btn"
                        prop.id "landPick"
                        prop.text "…or choose an unzipped folder"
                        prop.onClick (fun _ -> connectDir dispatch ())
                    ]
                ]
            ]
            Html.div [
                prop.className "srcinfo"
                prop.id "landLocal"
                prop.children (
                    let content = localSummaryText model.Source
                    if List.isEmpty content then
                        []
                    else
                        let have = model.Source.Local |> Map.toList |> List.map fst
                        let missing = have.Length < 2
                        content
                        @ [ if missing then
                                Html.button [
                                    prop.className "btn"
                                    prop.id "addMore"
                                    prop.text "Add the other ZIP"
                                    prop.onClick (fun _ -> connectZip dispatch ())
                                ] ]
                )
            ]
            Html.p [
                prop.className "fine"
                prop.text
                    "Nothing is uploaded anywhere; the browser only reads the file. Chrome and Edge remember the ZIP for next time; other browsers ask for it again each session."
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// sources footer
// ---------------------------------------------------------------------------

let private sourcesFooter (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "sources"
        prop.children [
            Html.a [ prop.href "#about"; prop.text "About & acknowledgments"; prop.onClick (navigateTo dispatch "#about") ]
            Html.text
                " · Texts from the Perseus Digital Library and Open Greek and Latin's First1KGreek, both CC BY-SA 4.0. Word lookups link to Logeion (University of Chicago) and the Perseus word study tool. Every passage has a Canonical Text Services (CTS) URN, a permanent citation you can see and copy by hovering over the passage."
        ]
    ]

// ---------------------------------------------------------------------------
// the corpus — scale and provenance, which is the trust signal this page has
// ---------------------------------------------------------------------------

/// The repositories behind the catalogue, carried at reading weight instead of
/// only as small print in the footer. `leadBlock` moves here out of the hero,
/// where it was competing with the primary action.
let private corpusSection (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Shared.collapsibleSection dispatch model.Collapsed "home-block corpus" "corpus" "sh" [ Html.text "Where the texts come from" ] [
        Html.div [
            prop.className "provenance"
            prop.children [
                Html.a [
                    prop.href "https://www.perseus.tufts.edu/"
                    prop.target "_blank"
                    prop.rel "noopener"
                    prop.text "Perseus Digital Library"
                ]
                Html.a [
                    prop.href "https://opengreekandlatin.org/"
                    prop.target "_blank"
                    prop.rel "noopener"
                    prop.text "Open Greek and Latin"
                ]
                Html.a [
                    prop.href "https://logeion.uchicago.edu/"
                    prop.target "_blank"
                    prop.rel "noopener"
                    prop.text "Logeion, University of Chicago"
                ]
            ]
        ]
        leadBlock
    ]

// ---------------------------------------------------------------------------
// top-level assembly
// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    // Whatever the primary CTA should open: the text in hand, else the first
    // suggested pick. Computed here because `suggestedPicks` is defined below
    // the hero in compilation order.
    let startTarget =
        match model.Recent with
        | r :: _ -> Some(r.Id, r.Chunk)
        | [] -> suggestedPicks model.Catalog model.Filter |> List.tryHead |> Option.map (fun w -> w.Id, None)

    Html.div [
        prop.className "landing"
        prop.children (
            // Returning readers never scroll past a pitch: the rail comes first
            // when there is one, and simply is not there when there is not.
            continueReadingSection model dispatch
            // 1. the promise, with a passage of real Greek beside it ...
            @ [ heroSection model.Catalog startTarget (passageOfDaySection model dispatch) dispatch ]
            // 2. ... then straight to choosing something to read ...
            @ [ Html.section [
                    prop.className "home-block find"
                    prop.children (
                        [ Html.h2 [ prop.className "sh"; prop.text "Find something to read" ] ]
                        @ homeToolsAndResults model dispatch
                        @ [ picksCardSection model dispatch ]
                    )
                ] ]
            @ (readingPathsSection model dispatch |> Option.toList)
            // 3. the shape of the whole collection, to scale.
            @ (erasStripSection model dispatch |> Option.toList)
            @ [ Html.div [
                    prop.className "home-bottom"
                    prop.children [ howToSection model dispatch ]
                ]
                corpusSection model dispatch
                Html.div [
                    prop.className "home-top"
                    prop.children [ wikiCardSection model dispatch ]
                ]
                offlineSection model dispatch
                // 4. the beginner lane at the foot of the page: the alphabet and
                // the marks at a glance, then the guide that teaches them.
                alphabetSection model dispatch
                tipsSection model dispatch
                startHereSection dispatch
                sourcesFooter dispatch ]
        )
    ]
