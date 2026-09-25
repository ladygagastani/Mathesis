module Views.Learn

// The Learn section (#learn): beginner's lessons ported from the "Arche"
// design, set in the Stoichedon design language. Each page is one `.lx-leaf`,
// the unit the page turn swings away (LearnFx); elements marked `data-ink`
// blur into focus as a leaf opens, and `data-reveal` blocks do the same when
// an answer appears. Messages that turn a leaf are dispatched through
// `turning`, which snapshots the old leaf first — only those, or a snapshot
// would sit over the page with nothing to turn it.

open Feliz
open Browser.Types
open Types
open LearnData

type private Dispatch = Msg -> unit

// ---------------------------------------------------------------------------
// building blocks
// ---------------------------------------------------------------------------

let private ink = prop.custom ("data-ink", "1")
let private grc = prop.custom ("lang", "grc")

let private turning (dispatch: Dispatch) (msg: Msg) =
    fun (_: MouseEvent) ->
        LearnFx.snapshot ()
        dispatch msg

let private navTo (page: LearnPage) : Msg = Navigate(Router.learnHash page, false)

/// A link to another Learn page that turns the leaf on the way.
let private pageLink (dispatch: Dispatch) (page: LearnPage) (className: string) (children: ReactElement list) =
    Html.a [
        prop.className className
        prop.href (Router.href (Router.learnHash page))
        prop.onClick (fun e ->
            e.preventDefault ()
            LearnFx.snapshot ()
            dispatch (navTo page))
        prop.children children
    ]

let private kicker (text: string) = Html.p [ prop.className "lx-kicker"; ink; prop.text text ]
/// A section label: the ochre marker of the design language.
let private label (text: string) = Html.p [ prop.className "lx-kicker lx-label"; ink; prop.text text ]
let private labelRow (left: string) (right: string) =
    Html.div [
        prop.className "lx-labelrow"
        ink
        prop.children [ Html.span [ prop.className "lx-label"; prop.text left ]; Html.span [ prop.className "lx-count"; prop.text right ] ]
    ]
let private title (text: string) = Html.h1 [ prop.className "lx-title"; ink; prop.text text ]
let private heading (children: ReactElement list) = Html.h2 [ prop.className "lx-h2"; ink; prop.children children ]
let private prose (children: ReactElement list) = Html.p [ prop.className "lx-prose"; ink; prop.children children ]
let private initial (letter: string) = Html.span [ prop.className "lx-initial"; prop.custom ("aria-hidden", "true"); prop.text letter ]
/// A paragraph opening with an ochre drop initial (the design language's third ornament).
let private opening (text: string) =
    let first = text.Substring(0, 1)
    Html.p [
        prop.className "lx-prose"
        ink
        prop.children [ initial first; Html.span [ prop.className "sr-only"; prop.text first ]; Html.text (text.Substring 1) ]
    ]
let private note (children: ReactElement list) = Html.p [ prop.className "lx-note"; ink; prop.children children ]
let private g (text: string) = Html.span [ prop.className "grc"; grc; prop.text text ]

let private primary (text: string) (enabled: bool) (onClick: MouseEvent -> unit) =
    Html.button [
        prop.className "btn big primary lx-cta"
        prop.disabled (not enabled)
        prop.onClick onClick
        prop.text text
    ]

let private linkButton (text: string) (onClick: MouseEvent -> unit) =
    Html.button [ prop.className "link-btn lx-link"; prop.onClick onClick; prop.text text ]

let private actions (children: ReactElement list) = Html.div [ prop.className "lx-actions"; prop.children children ]

let private runhead (left: string) (right: string) =
    Html.div [
        prop.className "lx-runhead"
        prop.children [ Html.span [ prop.text left ]; Html.span [ prop.text right ] ]
    ]

/// Feedback states shared by every answer button: chosen, right, wrong,
/// dimmed (the other options once answered), done (a pair already found).
type private Opt =
    | Idle
    | Sel
    | Ok
    | Bad
    | Dim
    | Done

let private optClass (o: Opt) =
    match o with
    | Idle -> "lx-opt"
    | Sel -> "lx-opt sel"
    | Ok -> "lx-opt ok"
    | Bad -> "lx-opt bad"
    | Dim -> "lx-opt dim"
    | Done -> "lx-opt done"

/// The state of option `i` once `picked` (if anything) against `answer`.
let private answered (picked: int option) (answer: int) (i: int) =
    match picked with
    | None -> Idle
    | Some _ when i = answer -> Ok
    | Some p when p = i -> Bad
    | Some _ -> Dim

let private reveal (name: string) (children: ReactElement list) =
    Html.div [ prop.className "lx-reveal"; prop.custom ("data-reveal", name); prop.custom ("aria-live", "polite"); prop.children children ]

let private verdict (ok: bool) (text: string) =
    Html.p [ prop.classes [ "lx-verdict"; if ok then "ok" else "bad" ]; ink; prop.text text ]

let private leaf (className: string) (children: ReactElement list) =
    Html.div [ prop.className ("lx-leaf " + className); prop.children children ]

let private tabs (dispatch: Dispatch) (current: LearnPage) =
    Html.nav [
        prop.className "lx-tabs"
        prop.custom ("aria-label", "Practice")
        prop.children [
            for text, page in [ "Overview", LearnContents; "Letters", LearnLetters; "Sounds", LearnSounds; "Iliad", LearnIliad; "Myth", LearnMyth ] do
                Html.a [
                    prop.key text
                    prop.href (Router.href (Router.learnHash page))
                    prop.classes [ if page = current then "on" ]
                    if page = current then prop.custom ("aria-current", "page")
                    prop.onClick (fun e ->
                        e.preventDefault ()
                        if page <> current then
                            LearnFx.snapshot ()
                            dispatch (navTo page))
                    prop.text text
                ]
        ]
    ]

/// The top of a lesson: close, one tick per leaf, and "II · 3/6".
let private lessonBar (dispatch: Dispatch) (closeTo: LearnPage) (n: int) (total: int) (leafLabel: string) =
    Html.div [
        prop.className "lx-lessonbar"
        prop.children [
            Html.a [
                prop.className "ib"
                prop.href (Router.href (Router.learnHash closeTo))
                prop.custom ("aria-label", "Close the lesson")
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (navTo closeTo))
                prop.children [ Shared.icon "close" ]
            ]
            Html.div [
                prop.className "lx-ticks"
                prop.role "progressbar"
                prop.custom ("aria-valuemin", 1)
                prop.custom ("aria-valuemax", total)
                prop.custom ("aria-valuenow", n + 1)
                prop.children [ for i in 0 .. total - 1 do Html.span [ prop.key i; prop.classes [ if i <= n then "on" ] ] ]
            ]
            Html.span [ prop.className "lx-leafno"; prop.text leafLabel ]
        ]
    ]

let private page (children: ReactElement list) = Html.div [ prop.className "page learn"; prop.children children ]

// ---------------------------------------------------------------------------
// onboarding
// ---------------------------------------------------------------------------

let private welcome (dispatch: Dispatch) =
    leaf "lx-welcome" [
        kicker "An introduction to Ancient Greek"
        Html.div [
            prop.className "lx-arche"
            grc
            prop.custom ("aria-label", "ΑΡΧΗ")
            prop.children [ for c in [ "Α"; "Ρ"; "Χ"; "Η" ] do Html.span [ prop.key c; ink; prop.custom ("aria-hidden", "true"); prop.text c ] ]
        ]
        Html.p [ prop.className "lx-sense"; ink; prop.text "beginning; origin; first principle" ]
        Html.span [ prop.className "lx-dash"; ink ]
        prose [ Html.text "Every line of Homer was first a sound in someone’s mouth. Learn the letters, then the words, then the song." ]
        actions [
            pageLink dispatch LearnPreface "btn big primary lx-cta" [ Html.text "Begin" ]
            linkButton "I already know the letters" (turning dispatch (Learn_(FinishOnboarding(Router.learnHash LearnContents))))
        ]
        Html.p [ prop.className "lx-foot"; prop.text "Attic & Homeric · Book I of V" ]
    ]

let private preface (lm: LearnModel) (dispatch: Dispatch) =
    leaf "" [
        runhead "ΠΡΟΟΙΜΙΟΝ · Preface" "ii"
        title "How long will you read each day?"
        prose [ Html.text "A little every day serves better than a great deal once a week. The Muses are patient." ]
        Html.div [
            prop.className "lx-paces"
            prop.role "radiogroup"
            prop.custom ("aria-label", "Daily reading")
            prop.children [
                for i, (num, name, sub) in Array.indexed paces do
                    Html.button [
                        prop.key num
                        prop.role "radio"
                        prop.custom ("aria-checked", (lm.Progress.Pace = i))
                        prop.classes [ "lx-pace"; if lm.Progress.Pace = i then "on" ]
                        ink
                        prop.onClick (fun _ -> dispatch (Learn_(SetPace i)))
                        prop.children [
                            Html.span [ prop.className "lx-num"; prop.text num ]
                            Html.span [ prop.className "lx-pace-t"; prop.children [ Html.b [ prop.text name ]; Html.span [ prop.text sub ] ] ]
                            Html.span [ prop.className "lx-radio" ]
                        ]
                    ]
            ]
        ]
        note [ Html.text "You can change this later on the Study page." ]
        actions [ primary "Open the book" true (turning dispatch (Learn_(FinishOnboarding(Router.learnHash LearnContents)))) ]
    ]

// ---------------------------------------------------------------------------
// contents
// ---------------------------------------------------------------------------

/// The lessons' own contents leaf; the Study page (Views.Study) shows it as
/// its "Practise" section.
let contents (lm: LearnModel) (dispatch: Msg -> unit) =
    let step = lm.Progress.Step
    let finished = step >= lessonLeaves
    let books =
        [ "I", "Letters", "Τὰ γράμματα", (if lm.Progress.AlphaDone then "read" else "begin here"), Some LearnAlphabet, false
          "II", "The First Declension", "Ἡ πρώτη κλίσις", (if finished then "lesson 3 read" else "lesson 3 of 6"), Some LearnLesson, true
          "III", "The Article", "Τὸ ἄρθρον", "in preparation", None, false
          "IV", "Verbs in -ω", "Τὰ ῥήματα", "in preparation", None, false
          "V", "The Wrath", "Μῆνις · Iliad I.1–7", "", Some LearnIliad, false ]
    leaf "lx-contents" [
        tabs dispatch LearnContents
        runhead "ΓΥΜΝΑΣΙΑ · Practice" ""
        Html.section [
            prop.className "lx-now"
            prop.children [
                label "Now reading · Book II, Lesson 3"
                title "The First Declension"
                Html.p [ prop.className "lx-sub"; ink; prop.children [ g "ἡ τιμή, τῆς τιμῆς"; Html.text " · honour" ] ]
                Html.div [
                    prop.className "lx-progress"
                    prop.children [
                        Html.div [
                            prop.className "lx-bar"
                            prop.children [ Html.span [ prop.style [ style.width (length.percent (float (min step lessonLeaves) / float lessonLeaves * 100.0)) ] ] ]
                        ]
                        Html.span [ prop.text (if finished then "All six leaves read" else sprintf "Leaf %d of %d" (step + 1) lessonLeaves) ]
                    ]
                ]
                primary
                    (if step = 0 then "Begin the lesson" elif finished then "Read it again" else "Continue reading")
                    true
                    (turning dispatch (Learn_ StartLesson))
            ]
        ]
        pageLink dispatch LearnIliad "lx-card lx-lineofday" [
            Html.span [ prop.className "lx-kicker"; ink; prop.text "Line of the day · Iliad 1.1" ]
            Html.span [ prop.className "lx-verse"; grc; ink; prop.text "Μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος" ]
            Html.span [ prop.className "lx-tr"; ink; prop.text "Sing, goddess, of the wrath of Achilles, Peleus’ son" ]
        ]
        Html.h2 [ prop.className "lx-kicker lx-books-h"; prop.text "Books" ]
        Html.ol [
            prop.className "lx-books"
            prop.children [
                for num, name, greek, status, target, current in books do
                    let inner =
                        [ Html.span [ prop.className "lx-num"; prop.text num ]
                          Html.span [ prop.className "lx-book-t"; prop.children [ Html.b [ prop.text name ]; Html.span [ grc; prop.text greek ] ] ]
                          Html.span [ prop.className "lx-status"; prop.text status ] ]
                    Html.li [
                        prop.key num
                        ink
                        prop.children [
                            match target with
                            | Some LearnLesson ->
                                Html.button [
                                    prop.classes [ "lx-book"; if current then "current" ]
                                    prop.onClick (turning dispatch (Learn_ StartLesson))
                                    prop.children inner
                                ]
                            | Some p -> pageLink dispatch p (if current then "lx-book current" else "lx-book") inner
                            | None -> Html.div [ prop.className "lx-book off"; prop.children inner ]
                        ]
                    ]
            ]
        ]
        pageLink dispatch LearnMyth "lx-card lx-mythcard" [
            Html.span [ prop.className "lx-medallion"; grc; prop.text "Ο" ]
            Html.span [
                prop.className "lx-book-t"
                prop.children [
                    Html.span [ prop.className "lx-kicker"; prop.text "Μῦθος · A story" ]
                    Html.b [ prop.text "Nobody: Odysseus in the Cyclops’ cave" ]
                ]
            ]
        ]
        Html.p [
            prop.className "lx-note lx-pace-note"
            prop.children [
                let _, name, sub = paces.[lm.Progress.Pace]
                Html.text (sprintf "Your pace: %s, %s. " (name.ToLower()) sub)
                pageLink dispatch LearnPreface "" [ Html.text "Change" ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// Book II, Lesson 3 — the first declension
// ---------------------------------------------------------------------------

let private lessonIntro (dispatch: Dispatch) =
    [ label "Book II · Lesson 3"
      title "The First Declension"
      Html.div [
          prop.className "lx-headword"
          ink
          prop.children [ Html.span [ prop.className "grc"; grc; prop.text "ἡ τιμή" ]; Html.i [ prop.text "honour" ] ]
      ]
      Html.p [
          prop.className "lx-prose"
          ink
          prop.children [
              initial "A"
              Html.text " Greek noun changes its ending to show what it does in a sentence. Nouns that share one set of endings form a "
              Html.em [ prop.text "declension" ]
              Html.text ". The first is the gentlest: its nouns end in -η or -α, and nearly all are feminine."
              Html.sup [ prop.className "lx-fn"; prop.text "1" ]
          ]
      ]
      prose [ Html.text "On these leaves you will meet five nouns of the -η type and write out all ten forms of "; g "τιμή"; Html.text "." ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              note [ Html.sup [ prop.className "lx-fn"; prop.text "1" ]; Html.text " Masculine nouns such as "; g "ὁ πολίτης"; Html.text ", citizen, come later in Book II." ]
              primary "Turn the leaf" true (turning dispatch (Learn_(LeafTo 1)))
          ]
      ] ]

let private flashcardLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let card = flashcards.[lm.Queue.[min lm.QueuePos (lm.Queue.Length - 1)]]
    let last = lm.QueuePos + 1 >= lm.Queue.Length
    [ labelRow "Vocabulary" (sprintf "%d / %d" (min (lm.QueuePos + 1) lm.Queue.Length) lm.Queue.Length)
      heading [ Html.text "Five nouns in -η" ]
      Html.button [
          prop.className "lx-flashcard"
          prop.custom ("data-card", "1")
          prop.custom ("aria-label", (if lm.Flipped then card.G + ": " + card.En else card.G + ". Tap to turn the card."))
          prop.onClick (fun _ -> dispatch (Learn_ Flip))
          prop.children [
              if not lm.Flipped then
                  Html.span [ prop.className "lx-fc-g"; grc; ink; prop.text card.G ]
                  Html.span [ prop.className "lx-kicker"; ink; prop.text "Tap to turn" ]
              else
                  Html.span [
                      prop.className "lx-fc-back"
                      prop.custom ("data-reveal", "card")
                      prop.children [
                          Html.span [ prop.className "lx-fc-small"; grc; ink; prop.text card.G ]
                          Html.span [ prop.className "lx-fc-en"; ink; prop.text card.En ]
                          Html.span [ prop.className "lx-dash"; ink ]
                          Html.span [ prop.className "lx-fc-gen"; grc; ink; prop.text card.Gen ]
                          Html.span [ prop.className "lx-fc-note"; ink; prop.text card.Note ]
                      ]
                  ]
          ]
      ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              if lm.Flipped then
                  Html.div [
                      prop.className "lx-pair"
                      prop.children [
                          Html.button [ prop.className "btn ghost"; prop.onClick (fun _ -> dispatch (Learn_(NextCard true))); prop.text "Again" ]
                          Html.button [
                              prop.className "btn big primary"
                              prop.onClick (if last then turning dispatch (Learn_(NextCard false)) else fun _ -> dispatch (Learn_(NextCard false)))
                              prop.text "I know it"
                          ]
                      ]
                  ]
              else
                  Html.p [ prop.className "lx-hint"; prop.text "Say the word aloud before you turn the card." ]
          ]
      ] ]

let private matchLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let all = lm.Matched.Count = matchGreek.Length
    let state (selected: bool) (matched: bool) (wrong: bool) =
        if matched then Done elif wrong then Bad elif selected then Sel else Idle
    let wrongG i = lm.MatchWrong |> Option.exists (fun (g, _) -> g = i)
    let wrongE i = lm.MatchWrong |> Option.exists (fun (_, e) -> e = i)
    [ label "Match"
      heading [ Html.text "Pair each word with its meaning" ]
      Html.div [
          prop.className "lx-match"
          prop.children [
              Html.div [
                  prop.children [
                      for i, w in Array.indexed matchGreek do
                          let st = state (lm.MatchG = Some i) (lm.Matched.Contains i) (wrongG i)
                          Html.button [
                              prop.key w
                              prop.className (optClass st + " lx-opt-g")
                              prop.custom ("data-m", sprintf "g%d" i)
                              prop.custom ("aria-pressed", (st = Sel))
                              prop.disabled ((st = Done))
                              grc
                              ink
                              prop.onClick (fun _ -> dispatch (Learn_(TapMatch(true, i))))
                              prop.text w
                          ]
                  ]
              ]
              Html.div [
                  prop.children [
                      for i, (en, gi) in Array.indexed matchEnglish do
                          let st = state (lm.MatchE = Some i) (lm.Matched.Contains gi) (wrongE i)
                          Html.button [
                              prop.key en
                              prop.className (optClass st + " lx-opt-e")
                              prop.custom ("data-m", sprintf "e%d" i)
                              prop.custom ("aria-pressed", (st = Sel))
                              prop.disabled ((st = Done))
                              ink
                              prop.onClick (fun _ -> dispatch (Learn_(TapMatch(false, i))))
                              prop.text en
                          ]
                  ]
              ]
          ]
      ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              Html.p [ prop.className "lx-hint"; prop.custom ("aria-live", "polite"); prop.text (if all then "All four pairs found." else "Tap a Greek word, then its meaning.") ]
              primary "Turn the leaf" all (turning dispatch (Learn_(LeafTo 3)))
          ]
      ] ]

let private mcLeaf (lm: LearnModel) (dispatch: Dispatch) =
    [ label "Question"
      heading [ Html.text "Which form means "; Html.em [ prop.text "of honour" ]; Html.text "?" ]
      Html.div [
          prop.className "lx-grid2"
          prop.children [
              for i, o in Array.indexed mcOptions do
                  Html.button [
                      prop.key o
                      prop.className (optClass (answered lm.Mc mcAnswer i) + " lx-opt-big")
                      grc
                      ink
                      prop.onClick (fun _ -> dispatch (Learn_(PickMc i)))
                      prop.text o
                  ]
          ]
      ]
      match lm.Mc with
      | Some pick ->
          reveal "mc" [
              verdict (pick = mcAnswer) (if pick = mcAnswer then "Rightly chosen." else "Not this time.")
              prose [ g "τιμῆς"; Html.text " is the genitive singular. The genitive answers the question “of what?”, and in this declension it is marked by -ῆς." ]
          ]
      | None -> Html.none
      Html.div [ prop.className "lx-end"; prop.children [ primary "Turn the leaf" lm.Mc.IsSome (turning dispatch (Learn_(LeafTo 4))) ] ] ]

let private composeLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let ok = lm.Built = Some true
    let tile (id: string) (fromBank: bool) =
        let st =
            match fromBank, lm.Built with
            | false, Some true -> " ok"
            | false, Some false -> " bad"
            | _ -> ""
        Html.div [
            prop.key id
            prop.className ((if fromBank then "lx-tile" else "lx-tile on-line") + st)
            if not fromBank then prop.custom ("data-lt", "1")
            grc
            prop.role "button"
            prop.tabIndex 0
            prop.custom ("aria-label", tiles.[id] + (if fromBank then ", add to the line" else ", take off the line"))
            prop.onPointerDown (fun (e: PointerEvent) -> LearnFx.dragDown e)
            prop.onPointerMove (fun (e: PointerEvent) -> LearnFx.dragMove e)
            prop.onPointerUp (fun (e: PointerEvent) ->
                let d = LearnFx.dragUp e
                if not d.moved then dispatch (Learn_(TapTile(id, fromBank)))
                elif d.toLine then dispatch (Learn_(MoveTile(id, true, d.index)))
                elif not fromBank then dispatch (Learn_(MoveTile(id, false, 0))))
            prop.onPointerCancel (fun (e: PointerEvent) -> LearnFx.dragUp e |> ignore)
            prop.onKeyDown (fun (e: KeyboardEvent) ->
                if e.key = "Enter" || e.key = " " then
                    e.preventDefault ()
                    dispatch (Learn_(TapTile(id, fromBank))))
            prop.text tiles.[id]
        ]
    [ label "Compose"
      heading [ Html.em [ prop.text "“Victory brings honour.”" ] ]
      note [ Html.text "Drag the words onto the line, or tap them. One word does not belong." ]
      Html.div [
          prop.className "lx-line"
          prop.custom ("data-line", "1")
          prop.custom ("aria-label", "The line")
          prop.children [
              if List.isEmpty lm.Line then Html.span [ prop.className "lx-empty"; prop.text "The line is empty." ]
              for id in lm.Line do tile id false
          ]
      ]
      Html.div [
          prop.className "lx-bank"
          prop.children [ for id in bankOrder do if not (List.contains id lm.Line) then yield tile id true ]
      ]
      match lm.Built with
      | Some built ->
          reveal "build" [
              Html.p [ prop.classes [ "lx-verdict"; if built then "ok" else "bad" ]; grc; ink; prop.text (if built then "ἡ νίκη τιμὴν φέρει." else "Not quite.") ]
              prose [
                  if built then
                      g "τιμήν"
                      Html.text " is accusative: it is the thing that victory brings. "
                      g "τιμῆς"
                      Html.text ", the genitive, would mean “of honour”."
                  else
                      Html.text "The subject takes its article, and the object of "
                      g "φέρει"
                      Html.text " must be accusative. Drag a word back to the bank to change it."
              ]
          ]
      | None -> Html.none
      Html.div [
          prop.className "lx-end"
          prop.children [
              primary
                  (if ok then "Turn the leaf" else "Check the line")
                  (not (List.isEmpty lm.Line))
                  (if ok then turning dispatch (Learn_(LeafTo 5)) else fun _ -> dispatch (Learn_ CheckLine))
          ]
      ] ]

let private paradigmLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let filled = slotOrder |> List.forall lm.Cells.ContainsKey
    let right = lm.TableChecked && slotOrder |> List.forall (fun k -> lm.Cells.TryFind k = answers.TryFind k)
    let cell (slot: string) =
        match given.TryFind slot with
        | Some ending ->
            Html.span [ prop.className "lx-cell given"; grc; prop.children [ Html.text "τιμ"; Html.span [ prop.className "end"; prop.text ending ] ] ]
        | None ->
            let v = lm.Cells.TryFind slot
            let active = lm.Active = Some slot && not lm.TableChecked
            let st =
                if not lm.TableChecked then (if active then " active" else "")
                elif v = answers.TryFind slot then " ok"
                else " bad"
            Html.button [
                prop.className ("lx-cell" + st + (if v.IsSome then " filled" else ""))
                grc
                prop.custom ("aria-pressed", active)
                prop.onClick (fun _ -> dispatch (Learn_(TapCell slot)))
                prop.children [ Html.text "τιμ"; Html.span [ prop.className "end"; prop.text (v |> Option.defaultValue " ·") ] ]
            ]
    let used = lm.Cells |> Map.toList |> List.map snd
    [ label "Paradigm"
      heading [ Html.text "Complete the declension of "; g "τιμή" ]
      Html.div [
          prop.className "lx-paradigm"
          ink
          prop.role "table"
          prop.children [
              Html.div [
                  prop.className "lx-prow head"
                  prop.role "row"
                  prop.children [ Html.span []; Html.span [ prop.text "Singular" ]; Html.span [ prop.text "Plural" ] ]
              ]
              for case, sg, pl in paradigmRows do
                  Html.div [
                      prop.key case
                      prop.className "lx-prow"
                      prop.role "row"
                      prop.children [ Html.span [ prop.className "lx-case"; prop.text case ]; cell sg; cell pl ]
                  ]
          ]
      ]
      Html.div [
          prop.className "lx-chips"
          prop.children [
              for c in chips do
                  Html.button [
                      prop.key c
                      prop.className "lx-chip"
                      grc
                      prop.disabled (List.contains c used || lm.Active.IsNone)
                      prop.onClick (fun _ -> dispatch (Learn_(TapChip c)))
                      prop.text ("-" + c)
                  ]
          ]
      ]
      if lm.TableChecked then
          reveal "table" [
              verdict right (if right then "The paradigm is complete." else "Some forms are struck through.")
              note [
                  Html.text (
                      if right then "Notice the circumflex on -ῆς, -ῇ and -ῶν. The long endings draw the accent’s pitch downward."
                      else "Tap a struck form to clear it, then choose again."
                  )
              ]
          ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              primary
                  (if right then "Finish the lesson" else "Check the table")
                  filled
                  (if right then turning dispatch (Learn_ FinishLesson) else fun _ -> dispatch (Learn_ CheckTable))
          ]
      ] ]

let private lesson (lm: LearnModel) (dispatch: Dispatch) =
    let step = min lm.Progress.Step (lessonLeaves - 1)
    leaf "lx-lesson" [
        lessonBar dispatch LearnContents step lessonLeaves (sprintf "II · %d/%d" (step + 1) lessonLeaves)
        Html.div [
            prop.className "lx-body"
            prop.children (
                match step with
                | 0 -> lessonIntro dispatch
                | 1 -> flashcardLeaf lm dispatch
                | 2 -> matchLeaf lm dispatch
                | 3 -> mcLeaf lm dispatch
                | 4 -> composeLeaf lm dispatch
                | _ -> paradigmLeaf lm dispatch
            )
        ]
    ]

let private finished (dispatch: Dispatch) =
    leaf "lx-done" [
        Html.div [
            prop.className "lx-arche lx-telos"
            grc
            prop.custom ("aria-label", "ΤΕΛΟΣ")
            prop.children [ for i, c in List.indexed [ "Τ"; "Ε"; "Λ"; "Ο"; "Σ" ] do Html.span [ prop.key i; ink; prop.custom ("aria-hidden", "true"); prop.text c ] ]
        ]
        Html.p [ prop.className "lx-sense"; ink; prop.text "the end, the fulfilment" ]
        Html.p [ prop.className "lx-kicker lx-center"; ink; prop.text "Lesson 3 complete · 5 nouns · 10 forms" ]
        Html.section [
            prop.className "lx-homer"
            prop.children [
                Html.p [ prop.className "lx-kicker"; ink; prop.text "Now you can read this in Homer" ]
                Html.p [
                    prop.className "lx-verse"
                    ink
                    prop.children [
                        Html.span [ prop.className "lx-lineno"; prop.text "3" ]
                        Html.span [
                            grc
                            prop.children [ Html.text "πολλὰς δ᾽ ἰφθίμους "; Html.mark [ prop.text "ψυχὰς" ]; Html.text " Ἄϊδι προΐαψεν" ]
                        ]
                    ]
                ]
                Html.p [ prop.className "lx-tr lx-indent"; ink; prop.text "and hurled down to Hades many mighty souls" ]
                note [ g "ψυχάς"; Html.text " ] "; g "ψυχή, -ῆς, ἡ"; Html.text ", soul. Accusative plural, first declension. "; Html.em [ prop.text "Iliad" ]; Html.text " 1.3." ]
            ]
        ]
        actions [
            pageLink dispatch LearnIliad "btn big primary lx-cta" [ Html.text "Read Iliad I.1–5" ]
            pageLink dispatch LearnContents "link-btn lx-link" [ Html.text "Return to contents" ]
        ]
    ]

// ---------------------------------------------------------------------------
// the letters
// ---------------------------------------------------------------------------

let private lettersPage (lm: LearnModel) (dispatch: Dispatch) =
    let l = letters.[lm.Letter]
    leaf "lx-letters" [
        tabs dispatch LearnLetters
        runhead "ΤΑ ΓΡΑΜΜΑΤΑ · Letters" "Book I"
        pageLink dispatch LearnAlphabet "lx-card lx-lessoncard" [
            Html.span [ prop.className "lx-num"; prop.text "I·1" ]
            Html.span [
                prop.className "lx-book-t"
                prop.children [ Html.span [ prop.className "lx-kicker"; prop.text (if lm.Progress.AlphaDone then "Lesson · read" else "Lesson") ]; Html.b [ prop.text "Friends and False Friends" ] ]
            ]
            Html.span [ prop.className "lx-chev"; prop.custom ("aria-hidden", "true"); prop.text "›" ]
        ]
        Html.div [
            prop.className "lx-letter"
            prop.custom ("data-reveal", "letter")
            prop.custom ("aria-live", "polite")
            prop.children [
                Html.div [ prop.className "lx-letter-tile"; grc; ink; prop.children [ Html.span [ prop.text l.Upper ]; Html.span [ prop.className "lo"; prop.text l.Lower ] ] ]
                Html.div [
                    prop.className "lx-letter-t"
                    prop.children [
                        Html.b [ grc; ink; prop.text l.Name ]
                        Html.span [ prop.className "lx-tr"; ink; prop.children [ Html.text (l.Translit + " · "); Html.span [ prop.className "ipa"; prop.text l.Ipa ] ] ]
                        Html.span [ prop.className "lx-hint-s"; ink; prop.text l.Hint ]
                        Html.span [ prop.className "lx-example"; ink; prop.children [ g l.Word; Html.text " "; Html.i [ prop.text l.Gloss ] ] ]
                    ]
                ]
            ]
        ]
        Html.div [
            prop.className "lx-listen"
            prop.children [
                Html.button [
                    prop.className "lx-play"
                    prop.custom ("aria-label", "Listen to " + l.Translit)
                    prop.onClick (fun _ -> dispatch (Learn_ Listen))
                    prop.children [ Shared.icon "play" ]
                ]
                Html.div [
                    prop.className "lx-bars"
                    prop.custom ("aria-hidden", "true")
                    prop.children [ for i, h in Array.indexed bars do Html.span [ prop.key i; prop.custom ("data-bar", "1"); prop.style [ style.height (length.px (round (h * 36.0))) ] ] ]
                ]
                Html.span [ prop.className "lx-hint-s"; prop.text "Listen" ]
            ]
        ]
        Html.div [
            prop.className "lx-alphabet"
            prop.children [
                for i, x in Array.indexed letters do
                    Html.button [
                        prop.key x.Translit
                        prop.classes [ "lx-abc"; if i = lm.Letter then "on" ]
                        prop.custom ("aria-pressed", (i = lm.Letter))
                        prop.custom ("aria-label", x.Translit)
                        grc
                        prop.onClick (fun _ -> dispatch (Learn_(PickLetter i)))
                        prop.children [ Html.text x.Upper; Html.span [ prop.text x.Lower ] ]
                    ]
            ]
        ]
        note [ Html.text "Sounds follow the restored Attic of the fifth century BCE. Playback uses your device’s modern Greek voice, where it has one." ]
    ]

// ---------------------------------------------------------------------------
// Book I, Lesson 1 — Friends and False Friends
// ---------------------------------------------------------------------------

let private alphaIntro (dispatch: Dispatch) =
    [ label "Book I · Lesson 1"
      title "Friends and False Friends"
      opening "Greek gave the Romans their alphabet, and the Romans gave it to us. Many Greek letters will look familiar, and most of them are honest. A few are liars."
      Html.div [
          prop.className "lx-friends"
          ink
          prop.children [
              Html.div [ prop.children [ Html.span [ prop.className "lx-kicker"; prop.text "Honest" ]; Html.span [ prop.className "lx-row"; grc; prop.text "Α Β Ε Ι Κ Μ Ο Τ" ] ] ]
              Html.div [ prop.className "liars"; prop.children [ Html.span [ prop.className "lx-kicker"; prop.text "Liars" ]; Html.span [ prop.className "lx-row"; grc; prop.text "Η Ρ Χ ν" ] ] ]
          ]
      ]
      prose [ Html.text "On these leaves you will ink three letters with your own hand, unmask the liars, and sound out your first two Greek words." ]
      Html.div [ prop.className "lx-end"; prop.children [ primary "Turn the leaf" true (turning dispatch (Learn_(AlphaTo 1))) ] ] ]

let private writeLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let w = writeLetters.[lm.WriteIdx]
    [ label "Write"
      heading [ Html.text "Ink the letter" ]
      Html.div [
          prop.className "lx-writebar"
          prop.children [
              Html.div [
                  prop.className "segbtn"
                  prop.children [
                      for i, x in Array.indexed writeLetters do
                          Html.button [
                              prop.key x.Ch
                              grc
                              prop.custom ("aria-pressed", (i = lm.WriteIdx))
                              prop.custom ("aria-label", x.Tr)
                              prop.onClick (fun _ -> dispatch (Learn_(PickWriteLetter i)))
                              prop.text x.Ch
                          ]
                  ]
              ]
              Html.span [ prop.className "lx-grow" ]
              linkButton "Watch" (fun _ -> dispatch (Learn_ WatchLetter))
              linkButton "Clear" (fun _ -> dispatch (Learn_ ClearInk))
          ]
      ]
      Html.div [
          prop.className "lx-pad"
          ink
          prop.children [
              Svg.svg [
                  svg.viewBox (0, 0, 300, 290)
                  svg.custom ("aria-hidden", "true")
                  svg.custom ("preserveAspectRatio", "xMidYMid meet")
                  svg.children [
                      Svg.line [ svg.x1 18; svg.y1 206; svg.x2 282; svg.y2 206; svg.className "base" ]
                      Svg.line [ svg.x1 18; svg.y1 118; svg.x2 282; svg.y2 118; svg.className "mid" ]
                      Svg.text [
                          svg.key w.Ch
                          svg.custom ("data-ghost", "1")
                          svg.className "ghost"
                          svg.x 150
                          svg.y 206
                          svg.custom ("text-anchor", "middle")
                          svg.text w.Ch
                      ]
                  ]
              ]
              Html.canvas [
                  prop.custom ("data-canvas", "1")
                  prop.custom ("aria-label", "Trace the letter " + w.Name)
                  prop.onPointerDown (fun (e: PointerEvent) -> LearnFx.penDown e)
                  prop.onPointerMove (fun (e: PointerEvent) ->
                      if LearnFx.penMove e > inkThreshold && not lm.Written then dispatch (Learn_ Inked))
                  prop.onPointerUp (fun (e: PointerEvent) -> LearnFx.penUp e)
                  prop.onPointerCancel (fun (e: PointerEvent) -> LearnFx.penUp e)
              ]
          ]
      ]
      Html.p [ prop.className "lx-letter-name"; ink; prop.children [ Html.span [ grc; prop.text w.Name ]; Html.i [ prop.text w.Tr ] ] ]
      Html.p [
          prop.classes [ "lx-hint"; if lm.Written then "ok" ]
          prop.custom ("aria-live", "polite")
          prop.text (if lm.Written then "Well inked. Try the next letter, or turn the leaf." else "Watch the letter drawn, then trace it with your finger.")
      ]
      Html.div [ prop.className "lx-end"; prop.children [ primary "Turn the leaf" true (turning dispatch (Learn_(AlphaTo 2))) ] ] ]

let private falseFriendLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let ff = falseFriends.[lm.FfIdx]
    let last = lm.FfIdx = falseFriends.Length - 1
    [ labelRow "Unmask the liars" (sprintf "%d / %d" (lm.FfIdx + 1) falseFriends.Length)
      heading [ Html.text "What sound does this letter make?" ]
      Html.div [ prop.className "lx-bigglyph"; prop.custom ("data-ff", "1"); ink; grc; prop.text ff.Glyph ]
      Html.div [
          prop.className "lx-stack"
          prop.children [
              for i, o in Array.indexed ff.Options do
                  Html.button [
                      prop.key (string lm.FfIdx + o)
                      prop.className (optClass (answered lm.FfPick ff.Answer i))
                      prop.onClick (fun _ -> dispatch (Learn_(PickFf i)))
                      prop.text o
                  ]
          ]
      ]
      match lm.FfPick with
      | Some p -> reveal "ff" [ verdict (p = ff.Answer) (if p = ff.Answer then "Unmasked." else "A liar indeed."); prose [ Html.text ff.Note ] ]
      | None -> Html.none
      Html.div [
          prop.className "lx-end"
          prop.children [
              primary
                  (if last then "Turn the leaf" else "Next letter")
                  lm.FfPick.IsSome
                  (if last then turning dispatch (Learn_ NextFf) else fun _ -> dispatch (Learn_ NextFf))
          ]
      ] ]

let private decodeLeaf (lm: LearnModel) (dispatch: Dispatch) =
    let dw = decodeWords.[lm.DcWord]
    let complete = lm.DcShown.Length = dw.Glyphs.Length
    let last = lm.DcWord = decodeWords.Length - 1
    [ labelRow "Sound it out" (sprintf "%d / %d" (lm.DcWord + 1) decodeWords.Length)
      heading [ Html.text "Tap each letter to read the word" ]
      Html.div [
          prop.className "lx-decode"
          prop.custom ("data-dc", "1")
          ink
          prop.children [
              for i, glyph in Array.indexed dw.Glyphs do
                  let shown = List.contains i lm.DcShown
                  Html.button [
                      prop.key (sprintf "%d-%d" lm.DcWord i)
                      prop.classes [ "lx-dcl"; if shown then "on" ]
                      prop.custom ("aria-label", (if shown then glyph + ": " + dw.Sounds.[i] else glyph))
                      prop.onClick (fun _ -> dispatch (Learn_(ShowDcLetter i)))
                      prop.children [
                          Html.span [ prop.className "g"; grc; prop.text glyph ]
                          Html.span [ prop.className "s"; prop.custom ("data-dcl", string i); prop.text (if shown then dw.Sounds.[i] else "·") ]
                      ]
                  ]
          ]
      ]
      if complete then
          reveal "dc" [
              Html.p [ prop.className "lx-dc-word"; grc; ink; prop.text dw.Word ]
              Html.p [ prop.className "lx-sense"; ink; prop.text dw.En ]
              note [ Html.text dw.Note ]
          ]
      else
          Html.p [ prop.className "lx-hint"; prop.text "Say each sound aloud as it appears." ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              primary
                  (if last then "Turn the leaf" else "Next word")
                  complete
                  (if last then turning dispatch (Learn_ NextDcWord) else fun _ -> dispatch (Learn_ NextDcWord))
          ]
      ] ]

let private nameLeaf (lm: LearnModel) (dispatch: Dispatch) =
    [ Html.div [
          prop.className "lx-finale"
          prop.children [
              label "Lesson 1 complete"
              Html.p [ prop.className "lx-sense"; ink; prop.text "One last word. Read this name." ]
              Html.button [
                  prop.className "lx-name"
                  prop.custom ("aria-expanded", lm.NameShown)
                  prop.onClick (fun _ -> dispatch (Learn_ ShowName))
                  prop.children [
                      Html.span [ prop.className "g"; grc; ink; prop.text "ΟΔΥΣΣΕΥΣ" ]
                      if lm.NameShown then
                          Html.span [ prop.custom ("data-reveal", "ody"); prop.children [ Html.span [ prop.className "en"; ink; prop.text "Odysseus" ] ] ]
                      else
                          Html.span [ prop.className "lx-kicker"; prop.text "Tap when you have it" ]
                  ]
              ]
              if lm.NameShown then
                  reveal "ody" [ prose [ Html.text "Υ is written y, and ΕΥ sounds like eu. The Romans heard the name as Ulixes, and we still call him Ulysses." ] ]
          ]
      ]
      Html.div [
          prop.className "lx-end"
          prop.children [
              primary "Continue to Book II" true (turning dispatch (Learn_ ToBookTwo))
              pageLink dispatch LearnLetters "link-btn lx-link" [ Html.text "Back to the letters" ]
          ]
      ] ]

let private alphabet (lm: LearnModel) (dispatch: Dispatch) =
    leaf "lx-lesson" [
        lessonBar dispatch LearnLetters lm.AStep alphaLeaves (sprintf "I · %d/%d" (lm.AStep + 1) alphaLeaves)
        Html.div [
            prop.className "lx-body"
            prop.children (
                match lm.AStep with
                | 0 -> alphaIntro dispatch
                | 1 -> writeLeaf lm dispatch
                | 2 -> falseFriendLeaf lm dispatch
                | 3 -> decodeLeaf lm dispatch
                | _ -> nameLeaf lm dispatch
            )
        ]
    ]

// ---------------------------------------------------------------------------
// Sounds
// ---------------------------------------------------------------------------

let private sounds (lm: LearnModel) (dispatch: Dispatch) =
    leaf "lx-sounds" [
        tabs dispatch LearnSounds
        runhead "ΑΚΡΟΑΣΙΣ · Listening" "Book I"
        label "Pronunciation"
        title "The Music of the Accent"
        opening "Greek accents marked pitch, not stress. The voice rose on the acute, and on the circumflex it rose and fell within one long vowel. Dionysius of Halicarnassus put the range at about a fifth."
        Html.ul [
            prop.className "lx-accents"
            prop.children [
                for i, a in Array.indexed accents do
                    Html.li [
                        prop.key a.Word
                        ink
                        prop.children [
                            Html.button [
                                prop.className "lx-play"
                                prop.custom ("aria-label", "Play " + a.Word)
                                prop.onClick (fun _ -> dispatch (Learn_(PlayAccent i)))
                                prop.children [ Shared.icon "play" ]
                            ]
                            Html.div [
                                prop.className "lx-acc-t"
                                prop.children [
                                    Html.span [
                                        prop.className "lx-syl"
                                        grc
                                        prop.children [
                                            for k, y in Array.indexed a.Syllables do
                                                Html.span [ prop.key k; prop.classes [ if lm.Playing = Some(i, k) then "on" ]; prop.text y.T ]
                                        ]
                                    ]
                                    Html.span [ prop.className "lx-hint-s"; prop.text a.Label ]
                                ]
                            ]
                            Svg.svg [
                                svg.className "lx-contour"
                                svg.viewBox (0, 0, 110, 50)
                                svg.custom ("aria-hidden", "true")
                                svg.children [
                                    Svg.line [ svg.x1 2; svg.y1 44; svg.x2 108; svg.y2 44; svg.className "base" ]
                                    Svg.path [ svg.custom ("data-contour", string i); svg.d (contourPath a); svg.className "pitch" ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]
        Html.section [
            prop.className "lx-card lx-exercise"
            prop.children [
                Html.p [ prop.className "lx-kicker lx-label"; prop.text "Which did you hear?" ]
                Html.button [
                    prop.className "btn ghost lx-cta"
                    prop.onClick (fun _ -> dispatch (Learn_ PlayExercise))
                    prop.children [ Shared.icon "play"; Html.text (if lm.ExPlayed then "Play again" else "Play the sound") ]
                ]
                Html.div [
                    prop.className "lx-grid2"
                    prop.children [
                        for i in [ 0; 1 ] do
                            let st =
                                match lm.ExPick with
                                | None -> if lm.ExPlayed then Idle else Dim
                                | p -> answered p lm.ExWord i
                            Html.button [
                                prop.key i
                                prop.className (optClass st + " lx-opt-big")
                                prop.disabled (not lm.ExPlayed && lm.ExPick.IsNone)
                                grc
                                prop.onClick (fun _ -> dispatch (Learn_(PickExercise i)))
                                prop.text accents.[i].Word
                            ]
                    ]
                ]
                match lm.ExPick with
                | Some p ->
                    reveal "ex" [
                        verdict (p = lm.ExWord) (if p = lm.ExWord then "Well heard." else "Listen once more.")
                        prose [
                            if lm.ExWord = 1 then
                                Html.text "It was "; g "τιμῆς"; Html.text ": the pitch climbs and falls back inside the last syllable."
                            else
                                Html.text "It was "; g "τιμή"; Html.text ": the pitch climbs on the last syllable and stays up."
                        ]
                        linkButton "Another sound →" (fun _ -> dispatch (Learn_ AnotherSound))
                    ]
                | None -> Html.none
            ]
        ]
        note [ Html.text "Tones are synthesised to show pitch alone. They are not a voice." ]
    ]

// ---------------------------------------------------------------------------
// Iliad 1.1–5
// ---------------------------------------------------------------------------

let private iliad (lm: LearnModel) (dispatch: Dispatch) =
    leaf "lx-iliad" [
        tabs dispatch LearnIliad
        runhead "ΙΛΙΑΔΟΣ Α · Iliad I" "1–5"
        label "Homer"
        Html.h1 [ prop.className "lx-title"; ink; prop.children [ Html.em [ prop.text "The Wrath" ] ] ]
        note [ Html.text "Tap any word for its gloss. Words marked with a dotted rule are first declension." ]
        Html.div [
            prop.className "lx-passage"
            grc
            prop.children [
                for li, words in Array.indexed iliadLines do
                    Html.p [
                        prop.key li
                        prop.className "lx-vline"
                        ink
                        prop.children [
                            Html.span [ prop.className "lx-lineno"; prop.text (if li = 0 || li = 4 then string (li + 1) else "") ]
                            Html.span [
                                prop.children [
                                    for wi, w in Array.indexed words do
                                        Html.button [
                                            prop.key wi
                                            prop.classes [ "lx-w"; (if w.FirstDecl then "d1" else ""); (if lm.Gloss = Some(li, wi) then "on" else "") ]
                                            prop.custom ("aria-pressed", (lm.Gloss = Some(li, wi)))
                                            prop.onClick (fun _ -> dispatch (Learn_(PickGloss(li, wi))))
                                            prop.text w.W
                                        ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]
        Html.section [
            prop.className "lx-apparatus"
            prop.custom ("aria-live", "polite")
            prop.children [
                Html.p [ prop.className "lx-kicker"; prop.text "Apparatus" ]
                match lm.Gloss with
                | Some(li, wi) ->
                    let w = iliadLines.[li].[wi]
                    reveal "gloss" [
                        Html.p [ prop.className "lx-lemma"; ink; prop.children [ Html.b [ grc; prop.text w.W ]; Html.text " ] "; g w.Lemma ] ]
                        Html.p [ prop.className "lx-parse"; ink; prop.children [ Html.em [ prop.text w.Sense ]; Html.text (". " + w.Parse + ".") ] ]
                        if w.FirstDecl then Html.p [ prop.className "lx-verdict ok lx-small"; ink; prop.children [ Html.text "First declension, the pattern of "; g "τιμή"; Html.text "." ] ]
                    ]
                | None -> Html.p [ prop.className "lx-hint"; prop.text "Choose a word from the text above." ]
            ]
        ]
        Html.section [
            prop.className "lx-trans"
            prop.children [
                linkButton (if lm.ShowTrans then "Hide translation" else "Show translation") (fun _ -> dispatch (Learn_ ToggleTrans))
                if lm.ShowTrans then
                    reveal "trans" [ for i, t in List.indexed iliadTranslation do Html.p [ prop.key i; prop.className "lx-tr"; ink; prop.text t ] ]
            ]
        ]
        Html.p [
            prop.className "lx-note"
            prop.children [
                Html.a [
                    prop.className "link-btn"
                    prop.href (Router.href iliadWorkHash)
                    prop.onClick (fun e ->
                        e.preventDefault ()
                        dispatch (Navigate(iliadWorkHash, false)))
                    prop.text "Read on in the Iliad, with the Perseus text and translation →"
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// Myth — Odysseus in the Cyclops' cave
// ---------------------------------------------------------------------------

let private quote (greek: string) (english: string) =
    Html.blockquote [
        prop.className "lx-quote"
        ink
        prop.children [ Html.p [ prop.className "q-grc"; grc; prop.text greek ]; Html.p [ prop.className "q-tr"; prop.text english ] ]
    ]

let private apparatus (entries: (string * string) list) =
    note [
        for i, (lemma, sense) in List.indexed entries do
            if i > 0 then Html.text " "
            g lemma
            Html.text (" ] " + sense)
    ]

let private myth (lm: LearnModel) (dispatch: Dispatch) =
    let n = lm.MythLeaf
    let head (greek: string) (english: string) =
        Html.h1 [ prop.className "lx-title lx-mythhead"; ink; prop.children [ Html.span [ grc; prop.text greek ]; Html.em [ prop.text english ] ] ]
    leaf "lx-myth" [
        tabs dispatch LearnMyth
        runhead "ΜΥΘΟΣ · Odyssey IX" (sprintf "%d / %d" (n + 1) mythLeaves)
        match n with
        | 0 ->
            head "Ἄντρον" "The Cave"
            opening "Far across the wine-dark sea, Odysseus climbed with twelve companions to a cave above the shore. Cheeses lay stacked in baskets and lambs were penned in the dark. At dusk the owner came home: Polyphemus, one-eyed son of Poseidon, driving his flocks before him. He sealed the door with a stone no mortal could move."
            apparatus [ "τὸ ἄντρον", "cave, cavern. Second declension, neuter." ]
        | 1 ->
            head "Οὖτις" "Nobody"
            prose [ Html.text "He ate two men that night and two more at dawn. When evening came again, Odysseus poured him dark wine, unmixed with water, and the giant drank and asked his guest’s name. Odysseus answered:" ]
            quote "Οὖτις ἐμοί γ᾽ ὄνομα" "“Nobody is my name.” Odyssey 9.366"
            prose [ Html.text "The Cyclops laughed and promised him a gift for it: Nobody would be eaten last." ]
            apparatus [ "οὔτις", "no one, nobody."; "τὸ ὄνομα", "name." ]
        | _ ->
            head "Βοή" "The Cry"
            prose [ Html.text "While he slept they drove a sharpened olive stake into his eye. He roared for his neighbours, and they gathered outside the stone and asked who was hurting him." ]
            quote "Οὖτίς με κτείνει δόλῳ οὐδὲ βίηφιν" "“Nobody is killing me, by trickery, not by force.” 9.408"
            heading [ Html.text "Why did the other Cyclopes go home?" ]
            Html.div [
                prop.className "lx-stack"
                prop.children [
                    for i, o in Array.indexed mythOptions do
                        Html.button [
                            prop.key i
                            prop.className (optClass (answered lm.MythPick mythAnswer i) + " lx-opt-text")
                            prop.onClick (fun _ -> dispatch (Learn_(PickMyth i)))
                            prop.text o
                        ]
                ]
            ]
            if lm.MythPick.IsSome then
                reveal "myth" [ prose [ Html.text "They called back: if nobody is harming you, your pain must come from Zeus. Pray to your father Poseidon. The trick was in a single word." ] ]
        Html.div [
            prop.className "lx-leafnav"
            prop.children [
                Html.button [
                    prop.className "link-btn lx-link"
                    prop.disabled ((n = 0))
                    prop.onClick (turning dispatch (Learn_(MythTo(n - 1))))
                    prop.text "← Previous leaf"
                ]
                if n < mythLeaves - 1 then
                    Html.button [ prop.className "link-btn lx-link"; prop.onClick (turning dispatch (Learn_(MythTo(n + 1)))); prop.text "Next leaf →" ]
                else
                    pageLink dispatch LearnContents "link-btn lx-link" [ Html.text "Close the book →" ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) (p: LearnPage) : ReactElement =
    let lm = model.Learn
    page [
        match p with
        | LearnWelcome -> welcome dispatch
        | LearnPreface -> preface lm dispatch
        | LearnContents -> contents lm dispatch
        | LearnLesson -> lesson lm dispatch
        | LearnDone -> finished dispatch
        | LearnLetters -> lettersPage lm dispatch
        | LearnAlphabet -> alphabet lm dispatch
        | LearnSounds -> sounds lm dispatch
        | LearnIliad -> iliad lm dispatch
        | LearnMyth -> myth lm dispatch
    ]
