module Views.Header

open Feliz
open Fable.Core
open Types

let private iconSvg (shapes: Content.IconShape list) : ReactElement = Shared.iconShapes shapes

// ---------------------------------------------------------------------------
// individual header controls
// ---------------------------------------------------------------------------

let backButton (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.button [
        prop.className "ib"
        prop.id "btnBack"
        prop.hidden (List.isEmpty model.History)
        prop.ariaLabel "Back"
        prop.title "Back"
        prop.onClick (fun _ -> dispatch BackClicked)
        prop.children [ Shared.icon "back" ]
    ]

let brand (dispatch: Msg -> unit) : ReactElement =
    Html.a [
        prop.className "brand"
        prop.id "home"
        prop.href (Router.href "#")
        prop.onClick (fun e ->
            e.preventDefault ()
            dispatch (Navigate("#", false)))
        prop.children [ Html.text "Μάθησις"; Html.small [ prop.text "Ancient Greek reader" ] ]
    ]

/// Top bar: Library · Study · Wiki · Forum. On phones this row becomes the
/// bottom tab bar, with Search in the middle and My library (which the header
/// carries on wider screens) at the far right: Library · Study · Search ·
/// Wiki · My library. Forum moves to an icon in the phone header (`.tab-desk`
/// hides it from the tab bar), so the bar keeps five tabs and a middle.
let topNav (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let active = Router.navKey model.Route
    let tab (hash: string) (key: string) (iconName: string) (label: string) (title: string) (phoneOnly: bool) =
        Html.a [
            prop.href (Router.href hash)
            prop.custom ("data-nav", key)
            prop.classes [
                if phoneOnly then "tab-phone"
                if key = "forum" then "tab-desk"
                if active = key && not model.Search.Open then "active"
            ]
            if active = key then prop.custom ("aria-current", "page")
            if title <> "" then prop.title title
            prop.onClick (fun e ->
                e.preventDefault ()
                if model.Search.Open then dispatch (Search_ CloseSearch)
                dispatch (Navigate(hash, false)))
            prop.children [ Shared.icon iconName; Html.text label ]
        ]
    Html.nav [
        prop.className "topnav"
        prop.id "topnav"
        prop.ariaLabel "Sections"
        prop.children [
            tab "#library" "library" "texts" "Library" "Every text in the collection" false
            tab "#study" "learn" "learn" "Study" "Start here: the beginner's guide and practice exercises" false
            Html.button [
                prop.custom ("data-nav", "search")
                prop.classes [ "tab-phone"; "tab-search"; if model.Search.Open then "active" ]
                prop.custom ("aria-expanded", model.Search.Open)
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    dispatch (Search_(if model.Search.Open then CloseSearch else OpenSearch)))
                prop.children [ Shared.icon "search"; Html.text "Search" ]
            ]
            tab "#wiki" "wiki" "wiki" "Wiki" "" false
            tab "#forum" "forum" "forum" "Forum" "The town hall: discuss passages, debate, ask, report bugs" false
            tab "#lib" "lib" "library" "My library" "" true
        ]
    ]

/// Sign in, or who is signed in and whether their library is synced.
let accountButton (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let acc = model.Account
    let label, title, cls =
        match acc.Session with
        | None -> "Sign in", "Sign in to sync your library and post in the forum", ""
        | Some s ->
            let who = if acc.DisplayName <> "" then acc.DisplayName else s.Email
            match acc.Sync with
            | SyncError e -> who, "Signed in as " + who + ". Sync failed: " + e, " warn"
            | Syncing -> who, "Signed in as " + who + ". Syncing…", " on"
            | _ -> who, "Signed in as " + who + ". Your library is synced.", " on"
    Html.a [
        prop.className ("acct-btn" + cls + (if model.Route = AccountRoute then " active" else ""))
        prop.href (Router.href "#account")
        prop.title title
        prop.onClick (fun e ->
            e.preventDefault ()
            dispatch (Navigate("#account", false)))
        prop.children [
            Shared.icon "account"
            Html.span [ prop.className "acct-label"; prop.text label ]
            match acc.Sync with
            | Syncing when acc.Session.IsSome -> Html.span [ prop.className "acct-dot busy"; prop.ariaHidden true ]
            | SyncError _ when acc.Session.IsSome -> Html.span [ prop.className "acct-dot err"; prop.ariaHidden true ]
            | _ -> Html.none
        ]
    ]

let tools (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let route = model.Route
    Html.div [
        prop.className "tools"
        prop.children [
            // Phones only: the Forum's place in the tab bar went to Study.
            Html.a [
                prop.className ("ib forum-ib" + (if Router.navKey route = "forum" then " active" else ""))
                prop.href (Router.href "#forum")
                prop.title "Forum"
                prop.ariaLabel "Forum"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#forum", false)))
                prop.children [ Shared.icon "forum" ]
            ]
            accountButton model dispatch
            Html.a [
                prop.className ("libbtn" + (if Router.navKey route = "lib" then " active" else ""))
                prop.id "libBtn"
                prop.href (Router.href "#lib")
                prop.title "My library"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#lib", false)))
                prop.children [
                    Shared.icon "library"
                    Html.span [ prop.text "My library" ]
                ]
            ]
            Html.button [
                prop.className "ib"
                prop.id "btnSettings"
                prop.custom ("aria-expanded", model.SettingsOpen)
                prop.ariaLabel "Settings"
                prop.title "Settings"
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    dispatch (Settings_ ToggleSettingsPane))
                // Αα rather than a cog: what the sheet mostly sets is how the
                // text looks (columns, typeface, size, spacing, theme).
                prop.children [ Shared.icon "textset" ]
            ]
            Html.button [
                prop.className "ib"
                prop.id "btnTheme"
                prop.ariaLabel "Toggle dark mode"
                prop.title "Dark / light"
                prop.onClick (fun _ -> dispatch (Settings_ ToggleThemeQuick))
                // A clay oil lamp: light and dark.
                prop.children [ Shared.icon "lamp" ]
            ]
        ]
    ]

/// Always-visible statement of where texts are being read from. Local mode with
/// nothing actually connected is called out as a warning rather than left to
/// look fine until every text quietly arrives from GitHub instead.
let sourceChip (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let connected = not (Map.isEmpty model.Source.Local)
    let needsAttention = model.Source.Mode = SourceLocal && (not connected || not (List.isEmpty model.Source.NeedsReconnect))

    let label, title =
        match model.Source.Mode with
        | SourceGitHub -> "Online", "Texts are downloaded from GitHub as you open them"
        | SourceUrl -> "Web address", "Texts are read from " + (if model.Source.BaseUrl = "" then "this folder" else model.Source.BaseUrl)
        | SourceLocal when not connected -> "No local copy", "Local is selected but nothing is connected — texts will come from GitHub"
        | SourceLocal when not (List.isEmpty model.Source.NeedsReconnect) ->
            "Reconnect", String.concat ", " model.Source.NeedsReconnect + " needs permission again"
        | SourceLocal ->
            let what =
                model.Source.Local
                |> Map.toList
                |> List.map (fun (r, _) -> Sources.repoName r)
                |> String.concat " and "
            "This computer", "Reading from your computer — " + what

    // A cloud for the network, a drive for the disk: the icon carries the
    // distinction at a glance, the word confirms it.
    let iconPath =
        match model.Source.Mode with
        | SourceLocal when connected && not needsAttention -> "M4 7h16v10H4z M8 11h.01 M4 11h16"
        | SourceLocal -> "M12 9v4 M12 17h.01 M10.3 4.3 2.8 17a1.5 1.5 0 0 0 1.3 2.2h15.8a1.5 1.5 0 0 0 1.3-2.2L13.7 4.3a2 2 0 0 0-3.4 0z"
        | _ -> "M17.5 18a3.5 3.5 0 0 0 .3-7 5.5 5.5 0 0 0-10.7-1.3A4 4 0 0 0 7 18z"

    let chip =
        Html.button [
            prop.classes [ "src-chip"; "src-" + (match model.Source.Mode with
                                                 | SourceLocal -> "local"
                                                 | SourceUrl -> "url"
                                                 | SourceGitHub -> "github")
                           if needsAttention then "warn" ]
            prop.id "srcChip"
            prop.title (title + " — click to change")
            prop.custom ("aria-expanded", model.SourceMenuOpen)
            prop.onClick (fun e ->
                e.stopPropagation ()
                dispatch (Source_ ToggleSourceMenu))
            prop.children [ iconSvg [ Content.IPath iconPath ]; Html.span [ prop.text label ] ]
        ]

    // The chip owns this menu: choosing where texts come from is the one thing
    // it is about, so it answers in place instead of handing off to the settings
    // sheet that the button beside it already opens.
    let option_ (targetMode: SourceMode) (optLabel: string) (hint: string) (onPick: unit -> unit) =
        Html.button [
            prop.className ("src-opt" + (if model.Source.Mode = targetMode then " on" else ""))
            prop.custom ("aria-pressed", (model.Source.Mode = targetMode))
            prop.onClick (fun e ->
                e.stopPropagation ()
                onPick ())
            prop.children [
                Html.b [ prop.text optLabel ]
                Html.span [ prop.text hint ]
            ]
        ]

    let localHint =
        if connected then
            model.Source.Local |> Map.toList |> List.map (fun (r, _) -> Sources.repoName r) |> String.concat " and "
        else
            "Nothing connected yet — pick a folder or ZIP"

    let menu =
        Html.div [
            prop.className ("src-menu" + (if model.SourceMenuOpen then " show" else ""))
            prop.id "srcMenu"
            prop.role "dialog"
            prop.ariaLabel "Text source"
            prop.children [
                Html.h3 [ prop.text "Read texts from" ]
                option_ SourceGitHub "Online" "Downloaded from GitHub as you open them" (fun () ->
                    dispatch (Source_(SetSourceMode SourceGitHub)))
                option_ SourceLocal "This computer" localHint (fun () ->
                    dispatch (Source_(SetSourceMode SourceLocal))
                    // Choosing this with nothing connected is a request for the
                    // picker; the click is still a user gesture at this point.
                    if List.isEmpty (Sources.localHave ()) then Shared.connectDir dispatch)
                if model.Source.Mode = SourceLocal then
                    Html.div [
                        prop.className "src-menu-acts"
                        prop.children [
                            Html.button [
                                prop.className "btn small"
                                prop.text "Add ZIP…"
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    Shared.connectZip dispatch)
                            ]
                            Html.button [
                                prop.className "btn small"
                                prop.text "Choose folder…"
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    Shared.connectDir dispatch)
                            ]
                        ]
                    ]
                Html.button [
                    prop.className "src-more"
                    prop.text "More text-source settings…"
                    prop.onClick (fun e ->
                        e.stopPropagation ()
                        dispatch (Source_ ToggleSourceMenu)
                        dispatch (Settings_ ToggleSettingsPane))
                ]
            ]
        ]

    Html.div [ prop.className "src-wrap"; prop.children [ chip; menu ] ]

// ---------------------------------------------------------------------------
// top-level assembly
// ---------------------------------------------------------------------------

/// Phones only (the stylesheet hides it above 900px): the notes toggle, which
/// on wider screens is the draggable floating button. On a phone that button
/// sat over the ends of lines, so there it lives in the header instead.
let notesButton (model: Model) (dispatch: Msg -> unit) : ReactElement =
    match model.Reader with
    | None -> Html.none
    | Some _ ->
        Html.button [
            prop.classes [ "ib"; "notes-ib" ]
            prop.ariaLabel "Notes on this text"
            prop.title "Notes on this text"
            prop.custom ("aria-pressed", model.Notes.Open)
            prop.onClick (fun e ->
                e.stopPropagation ()
                dispatch ToggleNotesPanel)
            // The label shows on wide screens; phones show the icon alone.
            prop.children [ Shared.icon "notes"; Html.span [ prop.className "nb-label"; prop.text "Notes" ] ]
        ]

/// `reading` lets the stylesheet quiet the header while a text is open: the
/// crumbs say where you are, so the tagline and the section labels step back.
let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.header [
        prop.className (if Router.isReader model.Route then "reading" else "")
        prop.children [
            Html.div [
                prop.className "h-left"
                prop.children [ backButton model dispatch; brand dispatch; topNav model dispatch ]
            ]
            SearchBox.render model dispatch
            Html.div [
                prop.className "h-right"
                prop.children [ sourceChip model dispatch; notesButton model dispatch; tools model dispatch ]
            ]
        ]
    ]
