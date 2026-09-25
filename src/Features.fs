/// Update logic for the Library catalogue, My library's words and places,
/// accounts and sync, and the forum. State.update hands these messages here,
/// so State.fs keeps to the reader and the shell.
module Features

open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Types

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

[<Emit("setTimeout($0, $1)")>]
let private setTimeoutMs (f: unit -> unit) (ms: int) : unit = jsNative

[<Emit("navigator.userAgent")>]
let private userAgent () : string = jsNative

[<Emit("window.innerWidth + '×' + window.innerHeight")>]
let private viewport () : string = jsNative

// ---------------------------------------------------------------------------
// saving the library: to this browser always, and to the account if signed in
// ---------------------------------------------------------------------------

/// Saves locally and, when signed in, schedules a sync. Every change to the
/// library goes through here (State.saveLib adds the scrollbar repaint).
let saveLibrary (model: Model) (lib: Library) : Model * Cmd<Msg> =
    { model with Library = lib },
    Cmd.batch [
        Cmd.ofEffect (fun _ -> Storage.saveLibrary lib)
        (if model.Account.Session.IsSome then Cmd.ofMsg (Account_ SyncSoon) else Cmd.none)
    ]

// ---------------------------------------------------------------------------
// catalogue controls
// ---------------------------------------------------------------------------

let updateShelf (msg: ShelfMsg) (model: Model) : Model * Cmd<Msg> =
    let shelf = model.Shelf
    match msg with
    // a letter chosen for authors means something else for titles
    | SetShelfSort s -> { model with Shelf = { shelf with Sort = s; Letter = None } }, Cmd.none
    | SetShelfLetter l -> { model with Shelf = { shelf with Letter = l } }, Cmd.none
    | SetShelfEra e -> { model with Shelf = { shelf with Era = e } }, Cmd.none

// ---------------------------------------------------------------------------
// words and places
// ---------------------------------------------------------------------------

let private blockText (b: Block) : string =
    match b with
    | Heading _ -> ""
    | Prose(_, t) -> t
    | Verse(_, lines) -> lines |> List.map snd |> String.concat " / "

/// The Greek around a word in its passage, about a line either side, for the
/// front of its flashcard.
let private contextOf (model: Model) (segRef: string option) (word: string) : string =
    match model.Reader, segRef with
    | Some rm, Some r ->
        match rm.Data |> Option.bind (fun d -> d.Segments |> Array.tryFind (fun s -> s.Ref = r)) with
        | Some seg ->
            let text =
                seg.Grc
                |> List.map blockText
                |> String.concat " "
                |> fun t -> System.Text.RegularExpressions.Regex.Replace(t, "⟦[^⟧]*⟧", "")
                |> fun t -> System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ")
                |> fun t -> t.Trim().Normalize(System.Text.NormalizationForm.FormC)
            let i = text.IndexOf word
            if i < 0 then (if text.Length > 140 then text.Substring(0, 140) + " …" else text)
            else
                let a = max 0 (i - 60)
                let b = min text.Length (i + word.Length + 60)
                // widen to whole words
                let a = if a = 0 then 0 else (let sp = text.LastIndexOf(' ', a) in if sp < 0 then 0 else sp + 1)
                let b = if b = text.Length then b else (let sp = text.IndexOf(' ', b) in if sp < 0 then text.Length else sp)
                (if a > 0 then "… " else "") + text.Substring(a, b - a) + (if b < text.Length then " …" else "")
        | None -> ""
    | _ -> ""

let updateLibraryExtras (msg: LibraryMsg) (model: Model) : (Model * Cmd<Msg>) option =
    let lib = model.Library
    match msg with
    | SetMarkSort s -> Some({ model with MarkSort = s }, Cmd.none)
    | SetMarkTag t -> Some({ model with MarkTag = t }, Cmd.none)
    | SetMarkQuery q -> Some({ model with MarkQuery = q }, Cmd.none)
    | SaveWord(word, seg) ->
        let work = model.Reader |> Option.map (fun rm -> rm.Work.Id) |> Option.defaultValue ""
        let lib2, added = LibraryData.addWord lib word work (seg |> Option.defaultValue "") (contextOf model seg word)
        let m2, cmd = saveLibrary model lib2
        Some(m2, Cmd.batch [ cmd; Cmd.ofMsg (ShowToast(if added then "Saved to My library › Words" else "Already in your words")) ])
    | EditWord(id, lemma, gloss) -> Some(saveLibrary model (LibraryData.editWord lib id lemma gloss))
    | RemoveWord id ->
        let review = model.Review |> Option.map (fun r -> { r with Queue = r.Queue |> List.filter (fun x -> x <> id) })
        Some(saveLibrary { model with Review = review } (LibraryData.removeWord lib id))
    | StartReview ->
        let due = LibraryData.dueWords lib (nowMs ())
        // nothing due: practise everything, least-known first
        let cards = if List.isEmpty due then lib.Words |> List.sortBy (fun w -> w.Box, w.Due) else due
        let queue = cards |> List.truncate 30 |> List.map (fun w -> w.Id)
        Some({ model with Review = (if List.isEmpty queue then None else Some { Queue = queue; Revealed = false; Seen = 0; Right = 0; Missed = [] }) }, Cmd.none)
    | RevealCard -> Some({ model with Review = model.Review |> Option.map (fun r -> { r with Revealed = true }) }, Cmd.none)
    | GradeCard knew ->
        match model.Review with
        | Some({ Queue = id :: rest } as r) ->
            // a missed card comes round again at the end of this session
            let queue = if knew then rest else rest @ [ id ]
            let missedBefore = List.contains id r.Missed
            let r2 =
                { r with
                    Queue = queue
                    Revealed = false
                    Seen = r.Seen + 1
                    Right = r.Right + (if knew && not missedBefore then 1 else 0)
                    Missed = if knew || missedBefore then r.Missed else id :: r.Missed }
            Some(saveLibrary { model with Review = Some r2 } (LibraryData.gradeWord lib id knew missedBefore))
        | _ -> Some(model, Cmd.none)
    | EndReview -> Some({ model with Review = None }, Cmd.none)
    | SavePlace hit ->
        let work = model.Reader |> Option.map (fun rm -> rm.Work.Id) |> Option.defaultValue ""
        let isNew = (LibraryData.placeFor lib hit.Qid).IsNone
        let m2, cmd = saveLibrary model (LibraryData.addPlace lib hit work)
        Some(m2, Cmd.batch [ cmd; Cmd.ofMsg (ShowToast(if isNew then "Saved to My library › Places" else "Passages added to the saved place")) ])
    | RemovePlace qid -> Some(saveLibrary model (LibraryData.removePlace lib qid))
    | SetPlaceNote(qid, note) -> Some(saveLibrary model (LibraryData.setPlaceNote lib qid note))
    | _ -> None

// ---------------------------------------------------------------------------
// accounts and sync
// ---------------------------------------------------------------------------

let private emailRe = System.Text.RegularExpressions.Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")

let private setAccount (model: Model) (f: AccountState -> AccountState) = { model with Account = f model.Account }

/// After any sign-in: remember the session, fetch the profile, sync at once.
let private signedIn (model: Model) (s: Session) (greeting: bool) : Model * Cmd<Msg> =
    Server.setSession (Some s)
    let model =
        setAccount model (fun a ->
            { a with Session = Some s; Stage = EnterEmail; CodeInput = ""; Busy = false; Error = None; Sync = Syncing; SyncToken = a.SyncToken + 1 })
    model,
    Cmd.batch [
        Cmd.ofEffect (fun _ -> Storage.saveSession (Some s))
        Cmd.OfPromise.perform Server.loadProfile () (function
            | Ok(name, admin) -> Account_(ProfileLoaded(name, admin))
            | Error _ -> NoOp)
        Cmd.ofMsg (Account_(SyncNow model.Account.SyncToken))
        (if greeting then Cmd.ofMsg (ShowToast "Signed in. Your library will now sync.") else Cmd.none)
    ]

let updateAccount (msg: AccountMsg) (model: Model) : Model * Cmd<Msg> =
    let acc = model.Account
    match msg with
    | SetEmailInput v -> setAccount model (fun a -> { a with EmailInput = v; Error = None }), Cmd.none
    | SetCodeInput v -> setAccount model (fun a -> { a with CodeInput = v; Error = None }), Cmd.none
    | SetNameInput v -> setAccount model (fun a -> { a with NameInput = v }), Cmd.none
    | SendCode ->
        let email = acc.EmailInput.Trim()
        if not acc.Configured then model, Cmd.none
        elif not (emailRe.IsMatch email) then setAccount model (fun a -> { a with Error = Some "That doesn't look like an email address." }), Cmd.none
        else
            setAccount model (fun a -> { a with Busy = true; Error = None }),
            Cmd.OfPromise.perform Server.sendCode email (function
                | Ok() -> Account_(CodeSentOk email)
                | Error e -> Account_(AuthFailed e))
    | CodeSentOk email -> setAccount model (fun a -> { a with Busy = false; Stage = CodeSent email; CodeInput = "" }), Cmd.none
    | VerifyCode ->
        match acc.Stage with
        | CodeSent email ->
            let code = System.Text.RegularExpressions.Regex.Replace(acc.CodeInput, @"\s", "")
            if not (System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{6,10}$")) then
                setAccount model (fun a -> { a with Error = Some "The code is the 6-digit number in the email." }), Cmd.none
            else
                setAccount model (fun a -> { a with Busy = true; Error = None; Stage = Verifying }),
                Cmd.OfPromise.perform (fun () -> Server.verifyCode email code) () (function
                    | Ok s -> Account_(SignedIn s)
                    | Error e -> Account_(AuthFailed e))
        | _ -> model, Cmd.none
    | SignedIn s -> signedIn model s true
    | SessionFromUrl s ->
        let m2, cmd = signedIn model s true
        m2, Cmd.batch [ cmd; Cmd.ofEffect (fun _ -> Router.replaceState "#account") ]
    | AuthFailed e ->
        let stage =
            match acc.Stage with
            | Verifying -> CodeSent(acc.EmailInput.Trim())
            | s -> s
        setAccount model (fun a -> { a with Busy = false; Error = Some e; Stage = stage }), Cmd.none
    | ProfileLoaded(name, admin) ->
        setAccount model (fun a -> { a with DisplayName = name; IsAdmin = admin; NameInput = (if a.NameInput = "" then name else a.NameInput) }), Cmd.none
    | SaveName ->
        let name = acc.NameInput.Trim()
        if name.Length < 2 || name.Length > 40 then
            setAccount model (fun a -> { a with Error = Some "Choose a name of 2 to 40 characters." }), Cmd.none
        else
            setAccount model (fun a -> { a with Busy = true; Error = None }),
            Cmd.OfPromise.perform Server.saveProfile name (function
                | Ok() -> Account_(NameSaved name)
                | Error e -> Account_(AuthFailed e))
    | NameSaved name ->
        setAccount model (fun a -> { a with Busy = false; DisplayName = name; NameInput = name }), Cmd.ofMsg (ShowToast "Name saved")
    | UseDifferentEmail -> setAccount model (fun a -> { a with Stage = EnterEmail; CodeInput = ""; Error = None }), Cmd.none
    | SignOut forget ->
        let model =
            setAccount model (fun a ->
                { a with Session = None; DisplayName = ""; IsAdmin = false; NameInput = ""; Sync = SyncOff; Stage = EnterEmail; Error = None })
        // forgetting is not deleting: no stamps, so nothing is removed from the account
        let model = if forget then { model with Library = Storage.defaultLibrary; Review = None } else model
        model,
        Cmd.batch [
            Cmd.OfPromise.perform Server.signOut () (fun () -> NoOp)
            Cmd.ofEffect (fun _ ->
                Storage.saveSession None
                if forget then
                    Storage.saveLibrary Storage.defaultLibrary
                    Storage.saveLibOwner "")
            Cmd.ofMsg (ShowToast(if forget then "Signed out, and your library removed from this browser. It is safe in your account." else "Signed out. Your library stays in this browser."))
        ]
    | SessionRefreshed s ->
        match s with
        | Some s -> setAccount model (fun a -> { a with Session = Some s }), Cmd.ofEffect (fun _ -> Storage.saveSession (Some s))
        | None when acc.Session.IsSome ->
            setAccount model (fun a -> { a with Session = None; Sync = SyncOff }),
            Cmd.batch [
                Cmd.ofEffect (fun _ -> Storage.saveSession None)
                Cmd.ofMsg (ShowToastFor("Your sign-in has expired. Sign in again to keep syncing.", 8000))
            ]
        | None -> model, Cmd.none
    // A burst of changes (typing a note) syncs once, 1.5 s after the last.
    | SyncSoon ->
        if acc.Session.IsNone then model, Cmd.none
        else
            let token = acc.SyncToken + 1
            setAccount model (fun a -> { a with SyncToken = token }),
            Cmd.ofEffect (fun dispatch -> setTimeoutMs (fun () -> dispatch (Account_(SyncNow token))) 1500)
    | SyncNow token ->
        if acc.Session.IsNone || token <> acc.SyncToken then model, Cmd.none
        else
            setAccount model (fun a -> { a with Sync = Syncing }),
            Cmd.OfPromise.perform Server.pullLibrary () (fun r -> Account_(SyncPulled r))
    | SyncPulled(Ok remote) ->
        // Merged with whatever is here *now*, so changes made during the round
        // trip are not lost; but a library that belongs to another account
        // (someone else signed out on this computer) is replaced, not merged.
        let uid = acc.Session |> Option.map (fun s -> s.UserId) |> Option.defaultValue ""
        let owner = Storage.loadLibOwner ()
        let merged = if owner <> "" && owner <> uid then remote else LibraryData.merge model.Library remote
        let model2 = { model with Library = merged }
        let needPush = not (LibraryData.sameContent merged remote) || merged.Stamps <> remote.Stamps
        model2,
        Cmd.batch [
            Cmd.ofEffect (fun _ ->
                Storage.saveLibrary merged
                Storage.saveLibOwner uid)
            (if needPush then Cmd.OfPromise.perform Server.pushLibrary merged (fun r -> Account_(SyncPushed r))
             else Cmd.ofMsg (Account_(SyncPushed(Ok(nowMs ())))))
        ]
    | SyncPulled(Error e) -> setAccount model (fun a -> { a with Sync = SyncError e }), Cmd.none
    | SyncPushed(Ok t) -> setAccount model (fun a -> { a with Sync = Synced t }), Cmd.none
    | SyncPushed(Error e) -> setAccount model (fun a -> { a with Sync = SyncError e }), Cmd.none

// ---------------------------------------------------------------------------
// forum
// ---------------------------------------------------------------------------

let emptyDraft: ForumDraft =
    { Category = "square"; Title = ""; Body = ""; Work = ""; Ref = ""
      BugWhat = ""; BugSteps = ""; BugExpected = ""; FromHash = "" }

let private setForum (model: Model) (f: ForumState -> ForumState) = { model with Forum = f model.Forum }

/// A bug report's body: the reader's three answers, then what the app can
/// tell about where it happened.
let bugBody (d: ForumDraft) : string =
    let section (title: string) (text: string) = if text.Trim() = "" then "" else title + "\n" + text.Trim() + "\n\n"
    section "What happened:" d.BugWhat
    + section "Steps to make it happen:" d.BugSteps
    + section "What I expected:" d.BugExpected
    + "—\nPage: " + (if d.FromHash = "" then "(not recorded)" else d.FromHash)
    + "\nBrowser: " + userAgent ()
    + "\nWindow: " + viewport ()

let updateForum (msg: ForumMsg) (model: Model) : Model * Cmd<Msg> =
    let f = model.Forum
    let signedIn = model.Account.Session.IsSome
    match msg with
    | LoadBoard cat ->
        if not model.Account.Configured then model, Cmd.none
        else
            // keep showing the old list while a refresh of the same board loads
            let board = if f.BoardOf = cat then (match f.Board with Loaded _ as b -> b | _ -> InFlight) else InFlight
            setForum model (fun f -> { f with Board = board; BoardOf = cat }),
            Cmd.OfPromise.perform Server.listThreads cat (fun r -> Forum_(BoardLoaded(cat, r)))
    | BoardLoaded(cat, r) ->
        if cat <> f.BoardOf then model, Cmd.none
        else
            setForum model (fun f -> { f with Board = (match r with Ok ts -> Loaded ts | Error e -> Failed e) }), Cmd.none
    | LoadThread id ->
        if not model.Account.Configured then model, Cmd.none
        else
            let keep =
                match f.Thread with
                | Loaded(t, _) when t.Id = id -> f.Thread
                | _ -> InFlight
            setForum model (fun f -> { f with Thread = keep }),
            Cmd.OfPromise.perform Server.loadThread id (fun r -> Forum_(ThreadLoaded(id, r)))
    | ThreadLoaded(id, r) ->
        let wanted =
            match model.Route with
            | ForumRoute(ForumThread t) -> t = id
            | _ -> false
        if not wanted then model, Cmd.none
        else setForum model (fun f -> { f with Thread = (match r with Ok x -> Loaded x | Error e -> Failed e) }), Cmd.none
    | StartThread(cat, work, ref_) ->
        let fromHash =
            match model.Route with
            | ForumRoute _ -> (match model.History with h :: _ -> h | [] -> "")
            | _ -> model.CurrentHash
        let draft =
            if f.Draft.Category = cat && f.Draft.Work = work && f.Draft.Ref = ref_ && (f.Draft.Title <> "" || f.Draft.Body <> "") then
                { f.Draft with FromHash = fromHash }
            else
                let title =
                    if work = "" then ""
                    else
                        (Catalog.workTitle model.Catalog work |> Option.defaultValue work)
                        + (if ref_ = "" then "" else " " + ref_)
                        + ": "
                { emptyDraft with Category = cat; Work = work; Ref = ref_; Title = title; FromHash = fromHash }
        let hash = Router.toHash (ForumRoute(ForumNew cat))
        setForum model (fun f -> { f with Draft = draft }),
        (if model.Route = ForumRoute(ForumNew cat) then Cmd.none else Cmd.ofMsg (Navigate(hash, false)))
    | SetDraft d -> setForum model (fun f -> { f with Draft = d }), Cmd.none
    | SubmitThread ->
        let d = f.Draft
        let body = if d.Category = "bugs" then bugBody d else d.Body.Trim()
        let problem =
            if not signedIn then Some "Sign in to post."
            elif d.Title.Trim().Length < 3 then Some "Give it a title of at least three letters."
            elif d.Category = "bugs" && d.BugWhat.Trim() = "" then Some "Say what happened."
            elif d.Category <> "bugs" && body = "" then Some "Write something to start the discussion."
            elif body.Length > 8000 then Some "That is too long: 8,000 characters at most."
            else None
        match problem with
        | Some p -> model, Cmd.ofMsg (ShowToast p)
        | None ->
            setForum model (fun f -> { f with Posting = true }),
            Cmd.OfPromise.perform (fun () -> Server.createThread d body) () (fun r -> Forum_(ThreadPosted r))
    | ThreadPosted(Ok id) ->
        setForum model (fun f -> { f with Posting = false; Draft = { emptyDraft with Category = f.Draft.Category }; Board = NotAsked; BoardOf = "\u0000" }),
        Cmd.batch [ Cmd.ofMsg (Navigate("#forum/t/" + id, true)); Cmd.ofMsg (ShowToast "Posted") ]
    | ThreadPosted(Error e) -> setForum model (fun f -> { f with Posting = false }), Cmd.ofMsg (ShowToastFor(e, 6000))
    | SetReply v -> setForum model (fun f -> { f with Reply = v }), Cmd.none
    | SubmitReply ->
        match f.Thread with
        | Loaded(t, _) when signedIn && f.Reply.Trim() <> "" && not f.Posting ->
            setForum model (fun f -> { f with Posting = true }),
            Cmd.OfPromise.perform (fun () -> Server.createPost t.Id (f.Reply.Trim())) () (fun r -> Forum_(ReplyPosted r))
        | _ -> model, Cmd.none
    | ReplyPosted(Ok()) ->
        let id = match f.Thread with Loaded(t, _) -> t.Id | _ -> ""
        setForum model (fun f -> { f with Posting = false; Reply = "" }), Cmd.ofMsg (Forum_(LoadThread id))
    | ReplyPosted(Error e) -> setForum model (fun f -> { f with Posting = false }), Cmd.ofMsg (ShowToastFor(e, 6000))
    | SetBugStatus(threadId, status) ->
        model,
        Cmd.OfPromise.perform (fun () -> Server.setThreadStatus threadId status) () (fun r ->
            Forum_(ForumDone(r |> Result.map (fun () -> "Status changed"))))
    | DeletePost(_, postId) ->
        model,
        Cmd.OfPromise.perform Server.deletePost postId (fun r -> Forum_(ForumDone(r |> Result.map (fun () -> "Reply removed"))))
    | DeleteThread threadId ->
        let back =
            match f.Thread with
            | Loaded(t, _) -> "#forum/" + t.Category
            | _ -> "#forum"
        model,
        Cmd.OfPromise.perform Server.deleteThread threadId (fun r ->
            match r with
            | Ok() -> Navigate(back, true)
            | Error e -> ShowToastFor(e, 6000))
    | ForumDone(Ok text) ->
        let reload =
            match f.Thread with
            | Loaded(t, _) -> Cmd.ofMsg (Forum_(LoadThread t.Id))
            | _ -> Cmd.none
        model, Cmd.batch [ reload; Cmd.ofMsg (ShowToast text) ]
    | ForumDone(Error e) -> model, Cmd.ofMsg (ShowToastFor(e, 6000))
