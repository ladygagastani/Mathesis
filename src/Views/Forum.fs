/// The forum (`#forum`): the site's town hall. Four boards for discussion and
/// one, kept apart, for bug reports. Reading needs no account; posting does.
/// Posts are plain text: line breaks are kept, and `[[tlg0012.tlg001:1.1]]`
/// links to a passage, as in bookmark notes.
module Views.Forum

open Feliz
open Fable.Core
open Types

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

[<Emit("new Date($0).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })")>]
let private fmtDate (ts: float) : string = jsNative

[<Emit("new Date($0).toLocaleString()")>]
let private fmtFull (ts: float) : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

[<Emit("window.confirm($0)")>]
let private confirmDialog (msg: string) : bool = jsNative

let private go (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

let private link (dispatch: Msg -> unit) (cls: string) (hash: string) (children: ReactElement list) =
    Html.a [ prop.className cls; prop.href hash; prop.onClick (go dispatch hash); prop.children children ]

/// "just now", "5 minutes ago", "3 hours ago", "yesterday", then the date.
let private ago (ts: float) : string =
    let s = (nowMs () - ts) / 1000.0
    if s < 60.0 then "just now"
    elif s < 3600.0 then (let m = int (s / 60.0) in sprintf "%d minute%s ago" m (if m = 1 then "" else "s"))
    elif s < 86400.0 then (let h = int (s / 3600.0) in sprintf "%d hour%s ago" h (if h = 1 then "" else "s"))
    elif s < 172800.0 then "yesterday"
    elif s < 604800.0 then sprintf "%d days ago" (int (s / 86400.0))
    else fmtDate ts

let private boardName (id: string) = Content.forumBoard id |> Option.map (fun b -> b.Name) |> Option.defaultValue id

/// Where to report bugs when the forum isn't switched on: the code's own
/// issue tracker, with the page and browser filled in.
let private githubIssueUrl (fromHash: string) : string =
    let body =
        "What happened:\n\n\nSteps to make it happen:\n\n\nWhat I expected:\n\n\n—\nPage: " + fromHash
    "https://github.com/ladygagastani/Mathesis/issues/new?labels=bug&title=" + enc "Bug: " + "&body=" + enc body

let private statusLabel (s: string) =
    match s with
    | "open" -> "Open"
    | "confirmed" -> "Confirmed"
    | "fixed" -> "Fixed"
    | "wontfix" -> "Won't fix"
    | _ -> ""

let private statusBadge (s: string) : ReactElement =
    match statusLabel s with
    | "" -> Html.none
    | l -> Html.span [ prop.className ("f-status s-" + s); prop.text l ]

let private crumbs (dispatch: Msg -> unit) (parts: (string * string option) list) : ReactElement =
    Html.div [
        prop.className "wcrumbs"
        prop.children [
            link dispatch "" "#forum" [ Html.text "Forum" ]
            for label, hash in parts do
                Html.text " › "
                match hash with
                | Some h -> link dispatch "" h [ Html.text label ]
                | None -> Html.span [ prop.text label ]
        ]
    ]

/// The body of a post: paragraphs and line breaks as typed, passage links live.
let private body (model: Model) (dispatch: Msg -> unit) (text: string) : ReactElement =
    Html.div [ prop.className "f-body"; prop.children (Shared.noteBody model.Catalog dispatch text) ]

let private passageChip (model: Model) (dispatch: Msg -> unit) (t: ForumThread) : ReactElement =
    if t.Work = "" then Html.none
    else
        let title = Catalog.workTitle model.Catalog t.Work |> Option.defaultValue t.Work
        let h = Router.toHash (ReaderRoute(t.Work, "", "", None, (if t.Ref = "" then None else Some t.Ref)))
        link dispatch "f-passage" h [ Shared.icon "texts"; Html.text (" " + title + (if t.Ref = "" then "" else " " + t.Ref)) ]

/// What to show when there is no server to talk to.
let private notConfigured (dispatch: Msg -> unit) (model: Model) : ReactElement =
    Html.div [
        prop.className "notice f-off"
        prop.children [
            Html.p [
                Html.b [ prop.text "The forum isn't open on this copy of the site yet. " ]
                Html.text "It needs the site's server, which hasn't been connected. Everything else works as usual, and your library stays safe in this browser."
            ]
            Html.p [
                Html.text "Found a bug in the meantime? "
                Html.a [
                    prop.href (githubIssueUrl model.CurrentHash)
                    prop.target "_blank"
                    prop.rel "noopener"
                    prop.text "Report it on GitHub ↗"
                ]
                Html.text " (needs a free GitHub account)."
            ]
        ]
    ]

let private remoteView (r: Remote<'T>) (ok: 'T -> ReactElement list) (retry: unit -> unit) : ReactElement list =
    match r with
    | NotAsked
    | InFlight -> [ Html.p [ prop.className "quiet f-loading"; prop.text "Loading…" ] ]
    | Failed e ->
        [ Html.div [
              prop.className "notice"
              prop.children [
                  Html.p [ prop.text ("Couldn't load this: " + e) ]
                  Html.button [ prop.className "btn small"; prop.text "Try again"; prop.onClick (fun _ -> retry ()) ]
              ]
          ] ]
    | Loaded x -> ok x

let private threadList (model: Model) (dispatch: Msg -> unit) (showBoard: bool) (threads: ForumThread list) : ReactElement =
    if List.isEmpty threads then
        Html.p [ prop.className "quiet f-empty"; prop.text "Nothing here yet. Be the first to start a thread." ]
    else
        Html.ul [
            prop.className "f-threads"
            prop.children [
                for t in threads ->
                    Html.li [
                        prop.key t.Id
                        prop.className "f-thread"
                        prop.children [
                            link dispatch "f-t-title" ("#forum/t/" + t.Id) [ Html.text t.Title ]
                            Html.div [
                                prop.className "f-t-meta"
                                prop.children [
                                    statusBadge t.Status
                                    if showBoard then link dispatch "f-board-chip" ("#forum/" + t.Category) [ Html.text (boardName t.Category) ]
                                    passageChip model dispatch t
                                    Html.span [ prop.text ("by " + t.AuthorName) ]
                                    Html.span [ prop.text (sprintf "%d repl%s" t.Replies (if t.Replies = 1 then "y" else "ies")) ]
                                    Html.span [ prop.title (fmtFull t.LastPost); prop.text ("active " + ago t.LastPost) ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]

// ---------------------------------------------------------------------------
// home: the town hall
// ---------------------------------------------------------------------------

let private welcome (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "f-welcome"
        prop.children [
            Html.p [ prop.className "f-kicker grc"; prop.lang "grc"; prop.text "Βουλευτήριον" ]
            Html.h1 [ prop.className "ph"; prop.text "The town hall" ]
            Html.p [
                prop.className "f-lede"
                prop.children [
                    Html.text "Every Greek city had a council house, the "
                    Html.i [ prop.text "bouleutērion" ]
                    Html.text ", where the business of the day was argued out in the open. This is ours. Bring a passage you can't stop thinking about, a reading you are ready to defend, or the question you've been too shy to ask."
                ]
            ]
            Html.p [
                prop.className "f-rules"
                prop.text
                    "House rules, the Athenian ones minus the ostracism: speak freely, cite your text, disagree with the argument and not the person, and be kind to newcomers. Every one of us once read ρ as a p."
            ]
            if model.Account.Configured then
                Html.div [
                    prop.className "f-cta"
                    prop.children [
                        Html.button [
                            prop.className "btn primary"
                            prop.onClick (fun _ -> dispatch (Forum_(StartThread("square", "", ""))))
                            prop.text "Start a discussion"
                        ]
                    ]
                ]
        ]
    ]

let private boardsList (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "f-boards"
        prop.children [
            for b in Content.forumBoards |> List.filter (fun b -> b.Id <> "bugs" && b.Id <> "suggestions") ->
                link dispatch "f-board" ("#forum/" + b.Id) [
                    Html.span [ prop.className "f-b-grc grc"; prop.lang "grc"; prop.ariaHidden true; prop.text b.Grc ]
                    Html.b [ prop.text b.Name ]
                    Html.span [ prop.className "f-b-blurb"; prop.text b.Blurb ]
                ]
        ]
    ]

/// Bug reports and Suggestions: the two boards about the site itself, side by
/// side at the top of the forum, each a panel with its own button.
let private sitePanel (dispatch: Msg -> unit) (id: string) (how: string) (action: string) (seeAll: string) : ReactElement =
    let b = (Content.forumBoard id).Value
    Html.section [
        prop.className ("f-bugs f-site-" + id)
        prop.ariaLabel b.Name
        prop.children [
            Html.p [ prop.className "f-b-grc grc"; prop.lang "grc"; prop.ariaHidden true; prop.text b.Grc ]
            Html.h2 [ prop.className "sh"; prop.text b.Name ]
            Html.p [ prop.text b.Blurb ]
            Html.p [ prop.className "quiet"; prop.text how ]
            Html.div [
                prop.className "f-cta"
                prop.children [
                    Html.button [ prop.className "btn primary"; prop.text action; prop.onClick (fun _ -> dispatch (Forum_(StartThread(id, "", "")))) ]
                    link dispatch "btn" ("#forum/" + id) [ Html.text seeAll ]
                ]
            ]
        ]
    ]

let private sitePanels (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "f-site"
        prop.children [
            sitePanel dispatch "bugs"
                "A good report says what you did, what happened and what you expected. The page you were on and your browser are added for you. Each report is marked Open, Confirmed or Fixed as it is dealt with."
                "Report a bug" "See all reports"
            sitePanel dispatch "suggestions"
                "Say what you'd like and why it would help your reading. Other readers can add their voice in the replies, which is how we tell what matters most."
                "Make a suggestion" "See all suggestions"
        ]
    ]

let private home (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    [ welcome model dispatch
      if not model.Account.Configured then
          notConfigured dispatch model
      else
          sitePanels dispatch
          Html.div [
              prop.className "f-main"
              prop.children (
                  [ Html.h2 [ prop.className "sh"; prop.text "Boards" ]
                    boardsList dispatch
                    Html.h2 [ prop.className "sh"; prop.text "Latest discussions" ] ]
                  @ remoteView model.Forum.Board (fun ts -> [ threadList model dispatch true ts ]) (fun () -> dispatch (Forum_(LoadBoard "")))
              )
          ] ]

// ---------------------------------------------------------------------------
// a board
// ---------------------------------------------------------------------------

let private board (model: Model) (dispatch: Msg -> unit) (cat: string) : ReactElement list =
    match Content.forumBoard cat with
    | None ->
        [ crumbs dispatch [ "Not found", None ]
          Html.p [ prop.className "quiet"; prop.text "There is no board by that name." ] ]
    | Some b ->
        [ crumbs dispatch [ b.Name, None ]
          Html.div [
              prop.className "f-board-head"
              prop.children [
                  Html.p [ prop.className "f-kicker grc"; prop.lang "grc"; prop.text b.Grc ]
                  Html.h1 [ prop.className "ph"; prop.text b.Name ]
                  Html.p [ prop.className "f-lede"; prop.text b.Blurb ]
                  if model.Account.Configured then
                      Html.button [
                          prop.className "btn primary"
                          prop.text (if cat = "bugs" then "Report a bug" elif cat = "suggestions" then "Make a suggestion" else "New thread")
                          prop.onClick (fun _ -> dispatch (Forum_(StartThread(cat, "", ""))))
                      ]
              ]
          ]
          if not model.Account.Configured then notConfigured dispatch model
          else yield! remoteView model.Forum.Board (fun ts -> [ threadList model dispatch false ts ]) (fun () -> dispatch (Forum_(LoadBoard cat))) ]

// ---------------------------------------------------------------------------
// a thread
// ---------------------------------------------------------------------------

let private signInPrompt (dispatch: Msg -> unit) (what: string) : ReactElement =
    Html.div [
        prop.className "notice f-signin"
        prop.children [
            Html.text ("Sign in to " + what + ". It takes an email address and a code; no password. ")
            link dispatch "btn small primary" "#account" [ Html.text "Sign in" ]
        ]
    ]

/// Before the first post: the name other readers will see.
let private namePrompt (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "notice f-name"
        prop.children [
            Html.label [
                prop.children [
                    Html.span [ prop.text "First, the name to show on your posts" ]
                    Html.input [
                        prop.value model.Account.NameInput
                        prop.maxLength 40
                        prop.placeholder "e.g. Sappho's fan"
                        prop.onChange (fun (v: string) -> dispatch (Account_(SetNameInput v)))
                        prop.onKeyDown (fun e -> if e.key = "Enter" then dispatch (Account_ SaveName))
                    ]
                ]
            ]
            Html.button [ prop.className "btn small primary"; prop.text "Save name"; prop.onClick (fun _ -> dispatch (Account_ SaveName)) ]
        ]
    ]

let private postGate (model: Model) (dispatch: Msg -> unit) (what: string) : ReactElement option =
    if model.Account.Session.IsNone then Some(signInPrompt dispatch what)
    elif model.Account.DisplayName = "" then Some(namePrompt model dispatch)
    else None

let private thread (model: Model) (dispatch: Msg -> unit) (id: string) : ReactElement list =
    let me = model.Account.Session |> Option.map (fun s -> s.UserId) |> Option.defaultValue ""
    let isMod = model.Account.IsAdmin
    if not model.Account.Configured then
        [ crumbs dispatch []; notConfigured dispatch model ]
    else
        remoteView model.Forum.Thread (fun (t, posts) ->
            [ crumbs dispatch [ boardName t.Category, Some("#forum/" + t.Category); t.Title, None ]
              Html.article [
                  prop.className "f-post f-first"
                  prop.children [
                      Html.h1 [ prop.className "ph f-title"; prop.children [ Html.text t.Title; Html.text " "; statusBadge t.Status ] ]
                      Html.div [
                          prop.className "f-t-meta"
                          prop.children [
                              Html.span [ prop.className "f-author"; prop.text t.AuthorName ]
                              Html.span [ prop.title (fmtFull t.Created); prop.text (ago t.Created) ]
                              passageChip model dispatch t
                          ]
                      ]
                      body model dispatch t.Body
                      if t.AuthorId = me || isMod then
                          Html.div [
                              prop.className "f-post-acts"
                              prop.children [
                                  Html.button [
                                      prop.className "linkbtn"
                                      prop.text "Delete thread"
                                      prop.onClick (fun _ -> if confirmDialog "Delete this thread and all its replies?" then dispatch (Forum_(DeleteThread t.Id)))
                                  ]
                              ]
                          ]
                  ]
              ]
              if t.Category = "bugs" && isMod then
                  Html.label [
                      prop.className "f-mod"
                      prop.children [
                          Html.span [ prop.text "Moderator: status " ]
                          Html.select [
                              prop.value t.Status
                              prop.onChange (fun (v: string) -> dispatch (Forum_(SetBugStatus(t.Id, v))))
                              prop.children [
                                  for s in [ "open"; "confirmed"; "fixed"; "wontfix" ] -> Html.option [ prop.key s; prop.value s; prop.text (statusLabel s) ]
                              ]
                          ]
                      ]
                  ]
              Html.h2 [ prop.className "sh"; prop.text (sprintf "%d repl%s" posts.Length (if posts.Length = 1 then "y" else "ies")) ]
              Html.ol [
                  prop.className "f-replies"
                  prop.children [
                      for p in posts ->
                          Html.li [
                              prop.key p.Id
                              prop.className "f-post"
                              prop.children [
                                  Html.div [
                                      prop.className "f-t-meta"
                                      prop.children [
                                          Html.span [ prop.className "f-author"; prop.text p.AuthorName ]
                                          Html.span [ prop.title (fmtFull p.Created); prop.text (ago p.Created) ]
                                          if p.AuthorId = me || isMod then
                                              Html.button [
                                                  prop.className "linkbtn"
                                                  prop.text "Delete"
                                                  prop.onClick (fun _ -> if confirmDialog "Delete this reply?" then dispatch (Forum_(DeletePost(t.Id, p.Id))))
                                              ]
                                      ]
                                  ]
                                  body model dispatch p.Body
                              ]
                          ]
                  ]
              ]
              match postGate model dispatch "reply" with
              | Some gate -> gate
              | None ->
                  Html.div [
                      prop.className "f-reply"
                      prop.children [
                          Html.label [
                              prop.htmlFor "fReply"
                              prop.className "sh"
                              prop.text "Your reply"
                          ]
                          Html.textarea [
                              prop.id "fReply"
                              prop.rows 5
                              prop.maxLength 8000
                              prop.value model.Forum.Reply
                              prop.placeholder "Write a reply. Link a passage with [[tlg0012.tlg001:1.1]]."
                              prop.onChange (fun (v: string) -> dispatch (Forum_(SetReply v)))
                              Shared.onEnterSave (fun () ->
                                  if not model.Forum.Posting && model.Forum.Reply.Trim() <> "" then dispatch (Forum_ SubmitReply))
                          ]
                          Shared.enterHint "posts"
                          Html.button [
                              prop.className "btn primary"
                              prop.disabled (model.Forum.Posting || model.Forum.Reply.Trim() = "")
                              prop.text (if model.Forum.Posting then "Posting…" else "Post reply")
                              prop.onClick (fun _ -> dispatch (Forum_ SubmitReply))
                          ]
                      ]
                  ] ]) (fun () -> dispatch (Forum_(LoadThread id)))

// ---------------------------------------------------------------------------
// a new thread
// ---------------------------------------------------------------------------

/// Choosing the work a passage thread is about: type part of a title or name.
[<ReactComponent>]
let private WorkPicker (model: Model) (dispatch: Msg -> unit) (d: ForumDraft) =
    let q, setQ = React.useState ""
    let results = if q.Trim().Length < 2 then [] else Catalog.searchWorks model.Catalog FilterAll q |> List.truncate 8
    Html.div [
        prop.className "f-workpick"
        prop.children [
            if d.Work <> "" then
                Html.p [
                    prop.children [
                        Html.b [ prop.text ((Catalog.workTitle model.Catalog d.Work |> Option.defaultValue d.Work) + " ") ]
                        Html.span [ prop.className "quiet"; prop.text ((Catalog.authorOf model.Catalog d.Work |> Option.map (fun a -> a.Name) |> Option.defaultValue "") + " ") ]
                        Html.button [ prop.className "linkbtn"; prop.text "Change"; prop.onClick (fun _ -> dispatch (Forum_(SetDraft { d with Work = "" }))) ]
                    ]
                ]
            else
                Html.input [
                    prop.type' "search"
                    prop.placeholder "Find the work: part of a title or an author's name"
                    prop.ariaLabel "Find the work"
                    prop.value q
                    prop.onChange setQ
                ]
                Html.ul [
                    prop.className "f-wp-results"
                    prop.children [
                        for a, w in results ->
                            Html.li [
                                prop.key w.Id
                                prop.children [
                                    Html.button [
                                        prop.onClick (fun _ ->
                                            setQ ""
                                            dispatch (Forum_(SetDraft { d with Work = w.Id })))
                                        prop.children [ Html.b [ prop.text w.Title ]; Html.span [ prop.className "quiet"; prop.text (" " + a.Name) ] ]
                                    ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

let private field (label: string) (hint: string) (input: ReactElement) : ReactElement =
    Html.label [
        prop.className "f-field"
        prop.children [
            Html.span [ prop.className "f-f-label"; prop.text label ]
            if hint <> "" then Html.span [ prop.className "f-f-hint"; prop.text hint ]
            input
        ]
    ]

let private newThread (model: Model) (dispatch: Msg -> unit) (cat: string) : ReactElement list =
    let d0 = model.Forum.Draft
    // a new-thread link for another board carries that board
    let d = if d0.Category = cat then d0 else { d0 with Category = cat }
    let set (d: ForumDraft) = dispatch (Forum_(SetDraft d))
    let isBug = cat = "bugs"
    let boardInfo = Content.forumBoard cat
    [ crumbs dispatch [ boardName cat, Some("#forum/" + cat); (if isBug then "Report a bug" else "New thread"), None ]
      Html.h1 [ prop.className "ph"; prop.text (if isBug then "Report a bug" else "Start a discussion") ]
      if not model.Account.Configured then
          notConfigured dispatch model
      else
          let submit () = if not model.Forum.Posting then dispatch (Forum_ SubmitThread)
          Html.div [
              prop.className "f-form"
              prop.children [
                  if not isBug then
                      Html.div [
                          prop.className "f-field"
                          prop.role "group"
                          prop.ariaLabel "Board"
                          prop.children [
                              Html.span [ prop.className "f-f-label"; prop.text "Board" ]
                              Html.div [
                                  prop.className "chips"
                                  prop.children [
                                      for b in Content.forumBoards |> List.filter (fun b -> b.Id <> "bugs") ->
                                          let on = b.Id = cat
                                          Html.button [
                                              prop.key b.Id
                                              prop.className ("chip" + (if on then " on" else ""))
                                              prop.custom ("aria-pressed", on)
                                              prop.text b.Name
                                              prop.onClick (fun _ -> dispatch (Navigate(Router.toHash (ForumRoute(ForumNew b.Id)), true)))
                                          ]
                                  ]
                              ]
                              match boardInfo with
                              | Some b -> Html.span [ prop.className "f-f-hint"; prop.text b.Blurb ]
                              | None -> Html.none
                          ]
                      ]
                  field "Title" (if isBug then "A few words: “Map lens shows no places in the Odyssey”" else "") (
                      Html.input [
                          prop.value d.Title
                          prop.maxLength 140
                          prop.onChange (fun (v: string) -> set { d with Title = v })
                          Shared.onEnterNext
                      ]
                  )
                  if cat = "passages" then
                      Html.div [
                          prop.className "f-field"
                          prop.children [
                              Html.span [ prop.className "f-f-label"; prop.text "Passage" ]
                              WorkPicker model dispatch d
                              Html.input [
                                  prop.className "f-ref"
                                  prop.value d.Ref
                                  prop.maxLength 40
                                  prop.placeholder "Reference, e.g. 1.33 or 17a"
                                  prop.ariaLabel "Passage reference"
                                  prop.onChange (fun (v: string) -> set { d with Ref = v })
                                  Shared.onEnterNext
                              ]
                          ]
                      ]
                  if isBug then
                      field "What happened?" "" (
                          Html.textarea [ prop.rows 4; prop.value d.BugWhat; prop.onChange (fun (v: string) -> set { d with BugWhat = v }); Shared.onEnterNext ]
                      )
                      field "How can we make it happen?" "The steps, one per line (Shift+Enter starts a new line): which text, which button." (
                          Html.textarea [ prop.rows 4; prop.value d.BugSteps; prop.onChange (fun (v: string) -> set { d with BugSteps = v }); Shared.onEnterNext ]
                      )
                      field "What did you expect?" "" (
                          Html.textarea [ prop.rows 2; prop.value d.BugExpected; prop.onChange (fun (v: string) -> set { d with BugExpected = v }); Shared.onEnterSave submit ]
                      )
                      Html.details [
                          prop.className "f-attached"
                          prop.children [
                              Html.summary [ prop.text "Added to your report automatically" ]
                              Html.pre [ prop.text (let b = Features.bugBody d in b.Substring(b.IndexOf "—")) ]
                          ]
                      ]
                  else
                      field "Your post" "Plain text. Enter posts; Shift+Enter starts a new line, and a blank line a new paragraph. [[tlg0012.tlg001:1.1]] links a passage." (
                          Html.textarea [ prop.rows 9; prop.maxLength 8000; prop.value d.Body; prop.onChange (fun (v: string) -> set { d with Body = v }); Shared.onEnterSave submit ]
                      )
                  match postGate model dispatch (if isBug then "send a report" else "post") with
                  | Some gate -> gate
                  | None ->
                      Html.div [
                          prop.className "f-cta"
                          prop.children [
                              Html.button [
                                  prop.className "btn primary"
                                  prop.disabled model.Forum.Posting
                                  prop.text (if model.Forum.Posting then "Posting…" elif isBug then "Send report" else "Post")
                                  prop.onClick (fun _ -> dispatch (Forum_ SubmitThread))
                              ]
                              link dispatch "btn" ("#forum/" + cat) [ Html.text "Cancel" ]
                          ]
                      ]
                  if isBug then
                      Html.p [
                          prop.className "quiet f-gh"
                          prop.children [
                              Html.text "Prefer GitHub? "
                              Html.a [ prop.href (githubIssueUrl d.FromHash); prop.target "_blank"; prop.rel "noopener"; prop.text "Open an issue there instead ↗" ]
                          ]
                      ]
              ]
          ] ]

// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) (route: ForumRoute) : ReactElement =
    Html.div [
        prop.className "page forum"
        prop.children (
            match route with
            | ForumHome -> home model dispatch
            | ForumBoard cat -> board model dispatch cat
            | ForumThread id -> thread model dispatch id
            | ForumNew cat -> newThread model dispatch cat
        )
    ]
