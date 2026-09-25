module LearnState

// init / enterPage / update for the Learn section. Kept apart from State.fs
// because none of it touches the reader, the catalogue or the library; State
// routes `Learn_` messages here and calls `enterPage` from `loadForRoute`.
//
// Any message that changes the leaf returns `turn` (back to the top; there is
// no page-turn animation). Messages that only reveal an answer return an `ink`
// of the part that appeared.

open Elmish
open Fable.Core
open Types
open LearnData

let private freshLesson (lm: LearnModel) : LearnModel =
    { lm with
        Queue = [ 0 .. flashcards.Length - 1 ]
        QueuePos = 0
        Flipped = false
        MatchG = None
        MatchE = None
        Matched = Set.empty
        MatchWrong = None
        Mc = None
        Line = []
        Built = None
        Cells = Map.empty
        Active = Some(List.head slotOrder)
        TableChecked = false }

let private freshAlpha (lm: LearnModel) : LearnModel =
    { lm with AStep = 0; WriteIdx = 0; Written = false; FfIdx = 0; FfPick = None; DcWord = 0; DcShown = []; NameShown = false }

let init (progress: LearnProgress) : LearnModel =
    { Progress = progress
      Queue = []; QueuePos = 0; Flipped = false
      MatchG = None; MatchE = None; Matched = Set.empty; MatchWrong = None; WrongSeq = 0
      Mc = None; Line = []; Built = None; Cells = Map.empty; Active = None; TableChecked = false
      Letter = 0; Gloss = None; ShowTrans = false; MythLeaf = 0; MythPick = None
      AStep = 0; WriteIdx = 0; Written = false; FfIdx = 0; FfPick = None; DcWord = 0; DcShown = []; NameShown = false
      Playing = None; PlayToken = 0; ExWord = 1; ExPick = None; ExPlayed = false }
    |> freshLesson

// ---------------------------------------------------------------------------
// commands
// ---------------------------------------------------------------------------

let private save (p: LearnProgress) : Cmd<Msg> = Cmd.ofEffect (fun _ -> Storage.saveLearn p)
let private turn: Cmd<Msg> = Cmd.ofEffect (fun _ -> LearnFx.turn ())
let private ink (selector: string) (delay: int) : Cmd<Msg> = Cmd.ofEffect (fun _ -> LearnFx.inkIn selector delay)
let private reveal (name: string) : Cmd<Msg> = ink (sprintf "[data-reveal=\"%s\"]" name) 0
let private go (page: LearnPage) : Cmd<Msg> = Cmd.ofMsg (Navigate(Router.learnHash page, false))
let private after (ms: int) (f: (Msg -> unit) -> unit) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch -> JS.setTimeout (fun () -> f dispatch) ms |> ignore)

/// The letter drawing itself: after the new leaf has inked in, or at once when
/// the reader picks another letter (after React has swapped the glyph).
let private watchAfter (ms: int) : Cmd<Msg> = after ms (fun _ -> LearnFx.watchLetter ())

let private withProgress (lm: LearnModel) (p: LearnProgress) : LearnModel * Cmd<Msg> =
    { lm with Progress = p }, save p

// ---------------------------------------------------------------------------
// entering a page
// ---------------------------------------------------------------------------

/// Called when a Learn page becomes the route. Returns the page actually shown
/// — a first visit to the contents goes to the welcome page instead — with the
/// model prepared for it and the command that turns the leaf in.
let enterPage (page: LearnPage) (lm: LearnModel) : LearnPage * LearnModel * Cmd<Msg> =
    // Study's front page holds the guide and the hero as well as the lessons,
    // so a first visit is no longer sent to the lessons' welcome leaf.
    match page with
    | LearnMyth -> page, { lm with MythLeaf = 0; MythPick = None }, turn
    | LearnAlphabet -> page, freshAlpha lm, turn
    | LearnLesson when lm.Progress.Step >= lessonLeaves ->
        let p = { lm.Progress with Step = 0 }
        page, freshLesson { lm with Progress = p }, Cmd.batch [ turn; save p ]
    | _ -> page, lm, turn

// ---------------------------------------------------------------------------
// update
// ---------------------------------------------------------------------------

let private startLesson (lm: LearnModel) : LearnModel * Cmd<Msg> =
    let lm2 =
        if lm.Progress.Step >= lessonLeaves then freshLesson { lm with Progress = { lm.Progress with Step = 0 } }
        else lm
    lm2, Cmd.batch [ save lm2.Progress; go LearnLesson ]

let private leafTo (n: int) (lm: LearnModel) : LearnModel * Cmd<Msg> =
    let p = { lm.Progress with Step = max 0 (min (lessonLeaves - 1) n) }
    { lm with Progress = p; Flipped = false }, Cmd.batch [ save p; turn ]

let private alphaTo (n: int) (lm: LearnModel) : LearnModel * Cmd<Msg> =
    let n = max 0 (min (alphaLeaves - 1) n)
    let lm2 = { lm with AStep = n; Written = false }
    let lm3, saveCmd =
        if n = alphaLeaves - 1 && not lm.Progress.AlphaDone then withProgress lm2 { lm.Progress with AlphaDone = true }
        else lm2, Cmd.none
    lm3, Cmd.batch [ turn; saveCmd; (if n = 1 then watchAfter 900 else Cmd.none) ]

let update (msg: LearnMsg) (lm: LearnModel) : LearnModel * Cmd<Msg> =
    match msg with
    // -- onboarding --------------------------------------------------------
    | SetPace i -> withProgress lm { lm.Progress with Pace = max 0 (min 2 i) }
    | FinishOnboarding hash ->
        let p = { lm.Progress with Onboarded = true }
        { lm with Progress = p }, Cmd.batch [ save p; Cmd.ofMsg (Navigate(hash, false)) ]

    // -- Book II, Lesson 3 ---------------------------------------------------
    | StartLesson -> startLesson lm
    | LeafTo n -> leafTo n lm
    | Flip ->
        if lm.Flipped then lm, Cmd.none
        else { lm with Flipped = true }, Cmd.ofEffect (fun _ -> LearnFx.flipCard ())
    | NextCard again ->
        let queue = if again then lm.Queue @ [ lm.Queue.[lm.QueuePos] ] else lm.Queue
        let pos = lm.QueuePos + 1
        if pos >= queue.Length then leafTo 2 { lm with Queue = queue; QueuePos = pos }
        else { lm with Queue = queue; QueuePos = pos; Flipped = false }, Cmd.ofEffect (fun _ -> LearnFx.slideCard ())
    | TapMatch(greek, i) ->
        let taken = if greek then lm.Matched.Contains i else lm.Matched.Contains(snd matchEnglish.[i])
        if taken then lm, Cmd.none
        else
            let g = if greek then Some i else lm.MatchG
            let e = if greek then lm.MatchE else Some i
            match g, e with
            | Some g, Some e when snd matchEnglish.[e] = g ->
                { lm with MatchG = None; MatchE = None; MatchWrong = None; Matched = lm.Matched.Add g }, Cmd.none
            | Some g, Some e ->
                let seq = lm.WrongSeq + 1
                { lm with MatchG = None; MatchE = None; MatchWrong = Some(g, e); WrongSeq = seq },
                Cmd.batch [
                    Cmd.ofEffect (fun _ -> LearnFx.shake (sprintf "[data-m=\"%s%d\"]" (if greek then "g" else "e") i))
                    after 750 (fun dispatch -> dispatch (Learn_(ClearMatchWrong seq)))
                ]
            | _ -> { lm with MatchG = g; MatchE = e; MatchWrong = None }, Cmd.none
    | ClearMatchWrong seq -> (if seq = lm.WrongSeq then { lm with MatchWrong = None } else lm), Cmd.none
    | PickMc i -> (if lm.Mc.IsSome then lm, Cmd.none else { lm with Mc = Some i }, reveal "mc")
    | TapTile(id, fromBank) ->
        let line = if fromBank then lm.Line @ [ id ] else lm.Line |> List.filter ((<>) id)
        { lm with Line = line; Built = None }, Cmd.none
    | MoveTile(id, toLine, index) ->
        let rest = lm.Line |> List.filter ((<>) id)
        let line = if toLine then List.insertAt (min index rest.Length) id rest else rest
        { lm with Line = line; Built = None }, Cmd.none
    | CheckLine ->
        if List.isEmpty lm.Line then lm, Cmd.none
        else { lm with Built = Some(lm.Line = lineAnswer) }, reveal "build"
    | TapCell slot ->
        if given.ContainsKey slot || (lm.TableChecked && lm.Cells.TryFind slot = answers.TryFind slot) then lm, Cmd.none
        else { lm with Active = Some slot; Cells = lm.Cells.Remove slot; TableChecked = false }, Cmd.none
    | TapChip ending ->
        match lm.Active with
        | Some slot when not (lm.Cells |> Map.exists (fun _ v -> v = ending)) ->
            let cells = lm.Cells.Add(slot, ending)
            { lm with Cells = cells; Active = slotOrder |> List.tryFind (fun k -> not (cells.ContainsKey k)); TableChecked = false }, Cmd.none
        | _ -> lm, Cmd.none
    | CheckTable ->
        if slotOrder |> List.forall lm.Cells.ContainsKey then { lm with TableChecked = true; Active = None }, reveal "table"
        else lm, Cmd.none
    | FinishLesson ->
        let p = { lm.Progress with Step = lessonLeaves }
        { lm with Progress = p }, Cmd.batch [ save p; go LearnDone ]

    // -- letters, Iliad, myth -------------------------------------------------
    | PickLetter i -> { lm with Letter = i }, reveal "letter"
    | Listen ->
        let name = letters.[lm.Letter].Name
        lm, Cmd.ofEffect (fun _ -> LearnFx.speak name; LearnFx.pulseBars ())
    | PickGloss(l, w) -> { lm with Gloss = Some(l, w) }, reveal "gloss"
    | ToggleTrans -> { lm with ShowTrans = not lm.ShowTrans }, (if lm.ShowTrans then Cmd.none else reveal "trans")
    | MythTo n -> { lm with MythLeaf = max 0 (min (mythLeaves - 1) n) }, turn
    | PickMyth i -> (if lm.MythPick.IsSome then lm, Cmd.none else { lm with MythPick = Some i }, reveal "myth")

    // -- Book I, Lesson 1 -----------------------------------------------------
    | AlphaTo n -> alphaTo n lm
    | PickWriteLetter i ->
        { lm with WriteIdx = i; Written = false }, Cmd.batch [ Cmd.ofEffect (fun _ -> LearnFx.clearInk ()); watchAfter 60 ]
    | Inked -> { lm with Written = true }, Cmd.none
    | ClearInk -> { lm with Written = false }, Cmd.ofEffect (fun _ -> LearnFx.clearInk ())
    | WatchLetter -> lm, Cmd.ofEffect (fun _ -> LearnFx.watchLetter ())
    | PickFf i -> (if lm.FfPick.IsSome then lm, Cmd.none else { lm with FfPick = Some i }, reveal "ff")
    | NextFf ->
        match lm.FfPick with
        | None -> lm, Cmd.none
        | Some _ when lm.FfIdx < falseFriends.Length - 1 -> { lm with FfIdx = lm.FfIdx + 1; FfPick = None }, ink "[data-ff]" 0
        | Some _ -> alphaTo 3 lm
    | ShowDcLetter i ->
        if List.contains i lm.DcShown then lm, Cmd.none
        else
            let shown = lm.DcShown @ [ i ]
            let complete = shown.Length = decodeWords.[lm.DcWord].Glyphs.Length
            { lm with DcShown = shown },
            Cmd.batch [ ink (sprintf "[data-dcl=\"%d\"]" i) 0; (if complete then ink "[data-reveal=\"dc\"]" 200 else Cmd.none) ]
    | NextDcWord ->
        let complete = lm.DcShown.Length = decodeWords.[lm.DcWord].Glyphs.Length
        if not complete then lm, Cmd.none
        elif lm.DcWord < decodeWords.Length - 1 then { lm with DcWord = lm.DcWord + 1; DcShown = [] }, ink "[data-dc]" 0
        else alphaTo 4 lm
    | ShowName -> (if lm.NameShown then lm, Cmd.none else { lm with NameShown = true }, reveal "ody")
    | ToBookTwo -> startLesson { lm with Progress = { lm.Progress with AlphaDone = true } }

    // -- Sounds ---------------------------------------------------------------
    | PlayAccent i ->
        let a = accents.[i]
        let token = lm.PlayToken + 1
        let starts = a.Syllables |> Array.scan (fun t y -> t + y.Dur + syllableGap) 60
        let total = starts.[starts.Length - 1]
        { lm with PlayToken = token; Playing = None },
        let sound =
            Cmd.ofEffect (fun _ ->
                LearnFx.tone (a.Syllables |> Array.map (fun y -> y.Pitch, y.Dur)) syllableGap
                LearnFx.drawContour i total)
        let marks = a.Syllables |> Array.mapi (fun k _ -> after starts.[k] (fun d -> d (Learn_(Playhead(token, Some(i, k)))))) |> List.ofArray
        let finish = after total (fun d -> d (Learn_(Playhead(token, None))))
        Cmd.batch (sound :: finish :: marks)
    | Playhead(token, at) -> (if token = lm.PlayToken then { lm with Playing = at } else lm), Cmd.none
    | PlayExercise ->
        let a = accents.[lm.ExWord]
        { lm with ExPlayed = true }, Cmd.ofEffect (fun _ -> LearnFx.tone (a.Syllables |> Array.map (fun y -> y.Pitch, y.Dur)) syllableGap)
    | PickExercise i ->
        if lm.ExPick.IsSome || not lm.ExPlayed then lm, Cmd.none
        else { lm with ExPick = Some i }, reveal "ex"
    | AnotherSound -> lm, Cmd.ofEffect (fun dispatch -> dispatch (Learn_(SetExercise(if JS.Math.random () < 0.5 then 0 else 1))))
    | SetExercise w -> { lm with ExWord = w; ExPick = None; ExPlayed = false }, Cmd.none
