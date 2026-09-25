/// `#account`: signing in by emailed code, the name shown in the forum, and
/// the state of library sync.
module Views.Account

open Feliz
open Fable.Core
open Types

[<Emit("new Date($0).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })")>]
let private fmtTime (ts: float) : string = jsNative

let private why: ReactElement =
    Html.ul [
        prop.className "acct-why"
        prop.children [
            Html.li [
                Html.b [ prop.text "Your library everywhere. " ]
                Html.text "Bookmarks, notes, words, places and favourites sync between every device you sign in on, and survive a cleared browser."
            ]
            Html.li [
                Html.b [ prop.text "A voice in the forum. " ]
                Html.text "Start discussions, reply, and report bugs."
            ]
            Html.li [
                Html.b [ prop.text "No password. " ]
                Html.text "Each time, we email you a one-time code. We keep your email address and what you save, nothing else, and never share either."
            ]
        ]
    ]

let private errorLine (acc: AccountState) : ReactElement =
    match acc.Error with
    | Some e -> Html.p [ prop.className "acct-err"; prop.role "alert"; prop.text e ]
    | None -> Html.none

let private signInForm (acc: AccountState) (dispatch: Msg -> unit) : ReactElement list =
    match acc.Stage with
    | EnterEmail ->
        [ Html.form [
              prop.className "acct-form"
              prop.onSubmit (fun e ->
                  e.preventDefault ()
                  dispatch (Account_ SendCode))
              prop.children [
                  Html.label [
                      prop.htmlFor "acctEmail"
                      prop.text "Your email address"
                  ]
                  Html.input [
                      prop.id "acctEmail"
                      prop.type' "email"
                      prop.autoComplete "email"
                      prop.required true
                      prop.value acc.EmailInput
                      prop.onChange (fun (v: string) -> dispatch (Account_(SetEmailInput v)))
                  ]
                  errorLine acc
                  Html.button [
                      prop.type' "submit"
                      prop.className "btn primary"
                      prop.disabled acc.Busy
                      prop.text (if acc.Busy then "Sending…" else "Email me a sign-in code")
                  ]
                  Html.p [ prop.className "quiet"; prop.text "New here? The same button creates your account." ]
                  // for a code from an earlier email (say, when no new email can be sent)
                  if acc.EmailInput.Trim().Contains "@" then
                      Html.p [
                          prop.className "quiet"
                          prop.children [
                              Html.button [
                                  prop.type' "button"
                                  prop.className "linkbtn"
                                  prop.text "I already have a code"
                                  prop.onClick (fun _ -> dispatch (Account_(CodeSentOk(acc.EmailInput.Trim()))))
                              ]
                          ]
                      ]
              ]
          ] ]
    | CodeSent _
    | Verifying ->
        let email = match acc.Stage with CodeSent e -> e | _ -> acc.EmailInput.Trim()
        [ Html.form [
              prop.className "acct-form"
              prop.onSubmit (fun e ->
                  e.preventDefault ()
                  dispatch (Account_ VerifyCode))
              prop.children [
                  Html.p [
                      Html.text "We've sent a code to "
                      Html.b [ prop.text email ]
                      Html.text ". Type it here, or simply open the sign-in link in the same email."
                  ]
                  Html.label [ prop.htmlFor "acctCode"; prop.text "The code from the email" ]
                  Html.input [
                      prop.id "acctCode"
                      prop.className "acct-code"
                      prop.inputMode.numeric
                      prop.autoComplete "one-time-code"
                      prop.maxLength 12
                      prop.autoFocus true
                      prop.value acc.CodeInput
                      prop.onChange (fun (v: string) -> dispatch (Account_(SetCodeInput v)))
                  ]
                  errorLine acc
                  Html.button [
                      prop.type' "submit"
                      prop.className "btn primary"
                      prop.disabled acc.Busy
                      prop.text (if acc.Busy then "Checking…" else "Sign in")
                  ]
                  Html.p [
                      prop.className "quiet"
                      prop.children [
                          Html.text "Nothing arrived after a minute or two? Look in your spam folder, "
                          Html.button [ prop.type' "button"; prop.className "linkbtn"; prop.text "send a new code"; prop.onClick (fun _ -> dispatch (Account_ SendCode)) ]
                          Html.text ", or "
                          Html.button [ prop.type' "button"; prop.className "linkbtn"; prop.text "use a different address"; prop.onClick (fun _ -> dispatch (Account_ UseDifferentEmail)) ]
                          Html.text "."
                      ]
                  ]
              ]
          ] ]

let private signedIn (model: Model) (s: Session) (dispatch: Msg -> unit) : ReactElement list =
    let acc = model.Account
    let lib = model.Library
    [ Html.p [
          prop.className "acct-who"
          prop.children [
              Html.text "Signed in as "
              Html.b [ prop.text s.Email ]
              if acc.IsAdmin then Html.span [ prop.className "tag"; prop.text "moderator" ]
          ]
      ]
      Html.section [
          prop.className "acct-block"
          prop.children [
              Html.h2 [ prop.className "sh"; prop.text "Your name in the forum" ]
              Html.div [
                  prop.className "acct-row"
                  prop.children [
                      Html.input [
                          prop.ariaLabel "Name shown on your posts"
                          prop.value acc.NameInput
                          prop.maxLength 40
                          prop.placeholder "e.g. Sappho's fan"
                          prop.onChange (fun (v: string) -> dispatch (Account_(SetNameInput v)))
                          prop.onKeyDown (fun e -> if e.key = "Enter" then dispatch (Account_ SaveName))
                      ]
                      Html.button [
                          prop.className "btn"
                          prop.disabled (acc.Busy || acc.NameInput.Trim() = acc.DisplayName)
                          prop.text "Save"
                          prop.onClick (fun _ -> dispatch (Account_ SaveName))
                      ]
                  ]
              ]
              if not acc.DeleteAsk then errorLine acc
              Html.p [ prop.className "quiet"; prop.text "Shown beside everything you post. Changing it changes it on your old posts too." ]
          ]
      ]
      Html.section [
          prop.className "acct-block"
          prop.children [
              Html.h2 [ prop.className "sh"; prop.text "Library sync" ]
              Html.p [
                  prop.className ("acct-sync" + (match acc.Sync with SyncError _ -> " err" | _ -> ""))
                  prop.custom ("aria-live", "polite")
                  prop.text (
                      match acc.Sync with
                      | Syncing -> "Syncing…"
                      | Synced t -> "Up to date. Last synced at " + fmtTime t + "."
                      | SyncError e -> "The last sync failed: " + e + " Your changes are safe here and will be sent next time."
                      | SyncOff -> "Not synced yet."
                  )
              ]
              Html.p [
                  prop.className "quiet"
                  prop.text (
                      sprintf "%d bookmarks, %d words, %d places and %d favourites. Changes sync a moment after you make them, and whenever you come back to this tab. If you change the same thing on two devices, the later change wins."
                          lib.Marks.Length lib.Words.Length lib.Places.Length lib.Favs.Length
                  )
              ]
              Html.div [
                  prop.className "acct-row"
                  prop.children [
                      Html.button [ prop.className "btn"; prop.text "Sync now"; prop.disabled (acc.Sync = Syncing); prop.onClick (fun _ -> dispatch (Account_ SyncSoon)) ]
                      Html.button [ prop.className "btn"; prop.text "Sign out"; prop.onClick (fun _ -> dispatch (Account_(SignOut false))) ]
                      Html.button [
                          prop.className "btn danger"
                          prop.title "On a shared computer: your library stays in your account, but leaves this browser"
                          prop.text "Sign out and clear this browser"
                          prop.onClick (fun _ -> dispatch (Account_(SignOut true)))
                      ]
                  ]
              ]
          ]
      ]
      Html.section [
          prop.className "acct-block acct-delete"
          prop.children [
              Html.h2 [ prop.className "sh"; prop.text "Delete your account" ]
              Html.p [
                  prop.className "quiet"
                  prop.text
                      "Removes your email address, your name, the synced copy of your library, and everything you posted in the forum (a thread goes with the replies in it), at once and for good. The library in this browser stays."
              ]
              if not acc.DeleteAsk then
                  Html.button [
                      prop.className "btn danger"
                      prop.text "Delete my account…"
                      prop.onClick (fun _ -> dispatch (Account_(AskDeleteAccount true)))
                  ]
              else
                  Html.div [
                      prop.className "acct-row"
                      prop.children [
                          Html.label [
                              prop.className "acct-confirm"
                              prop.children [
                                  Html.span [ prop.text "Type delete to confirm" ]
                                  Html.input [
                                      prop.value acc.DeleteText
                                      prop.autoFocus true
                                      prop.autoComplete "off"
                                      prop.onChange (fun (v: string) -> dispatch (Account_(SetDeleteConfirm v)))
                                      prop.onKeyDown (fun e ->
                                          if e.key = "Enter" then dispatch (Account_ DeleteAccount)
                                          elif e.key = "Escape" then dispatch (Account_(AskDeleteAccount false)))
                                  ]
                              ]
                          ]
                          Html.button [
                              prop.className "btn danger"
                              prop.disabled (acc.Busy || acc.DeleteText.Trim().ToLower() <> "delete")
                              prop.text (if acc.Busy then "Deleting…" else "Delete for good")
                              prop.onClick (fun _ -> dispatch (Account_ DeleteAccount))
                          ]
                          Html.button [ prop.className "btn"; prop.text "Cancel"; prop.onClick (fun _ -> dispatch (Account_(AskDeleteAccount false))) ]
                      ]
                  ]
                  errorLine acc
          ]
      ] ]

/// The forum authors this reader has hidden, with a way to bring each back.
let private hiddenPeople (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    match model.Forum.Blocked with
    | [] -> []
    | people ->
        [ Html.section [
              prop.className "acct-block"
              prop.children [
                  Html.h2 [ prop.className "sh"; prop.text "People you've hidden in the forum" ]
                  Html.p [ prop.className "quiet"; prop.text "Their posts are folded away for you, in this browser only. They aren't told." ]
                  Html.ul [
                      prop.className "acct-hidden"
                      prop.children [
                          for id, name in people ->
                              Html.li [
                                  prop.key id
                                  prop.children [
                                      Html.span [ prop.text name ]
                                      Html.button [ prop.className "btn small"; prop.text "Show again"; prop.onClick (fun _ -> dispatch (Forum_(Unblock id))) ]
                                  ]
                              ]
                      ]
                  ]
              ]
          ] ]

let private privacyLink (dispatch: Msg -> unit) : ReactElement =
    Html.p [
        prop.className "quiet acct-privacy"
        prop.children [
            Html.text "What is stored, where, and who can see it: "
            Html.a [
                prop.href (Router.href "#privacy")
                prop.text "Privacy"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#privacy", false)))
            ]
            Html.text "."
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let acc = model.Account
    Html.div [
        prop.className "page account"
        prop.children (
            [ Html.h1 [ prop.className "ph"; prop.text (if acc.Session.IsSome then "Your account" else "Sign in") ] ]
            @ (if not acc.Configured then
                   [ Html.div [
                         prop.className "notice"
                         prop.children [
                             Html.p [
                                 Html.b [ prop.text "Accounts aren't switched on for this copy of the site yet. " ]
                                 Html.text "Everything you save stays in this browser; use Back up in My library to move it to another."
                             ]
                         ]
                     ]
                     why ]
               else
                   match acc.Session with
                   | Some s -> signedIn model s dispatch
                   | None -> signInForm acc dispatch @ [ why ])
            @ hiddenPeople model dispatch
            @ [ privacyLink dispatch ]
        )
    ]
