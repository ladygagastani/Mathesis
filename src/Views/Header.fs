module Views.Header

open Feliz
open Fable.Core
open Types

[<Emit("matchMedia('(max-width: 900px)').matches")>]
let private isPhoneWidth (): bool = jsNative

let private iconSvg (shapes: Content.IconShape list) : ReactElement = Shared.iconShapes shapes

// ---------------------------------------------------------------------------
// individual header controls
// ---------------------------------------------------------------------------

/// When the sidebar is collapsed — which the reader now does by default — a
/// bare hamburger is not enough of a signal that 1,800 works are one click
/// away, so the button grows a label. CSS drops the label back to an icon on
/// phones, where the same control opens the drawer instead.
let navToggleButton (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let hidden = Router.navHiddenOn model.Route model.NavHidden model.NavHiddenReader
    Html.button [
        prop.classes [ "ib"; if hidden then "nav-show"; if model.Route = Browse then "nav-none" ]
        prop.id "navToggle"
        prop.ariaLabel (if hidden then "Show contents" else "Hide contents")
        prop.title ((if hidden then "Show contents" else "Hide contents") + " (\\)")
        prop.custom ("aria-expanded", not hidden)
        prop.onClick (fun e ->
            e.stopPropagation ()
            if isPhoneWidth () then dispatch (ToggleSide(not model.SideOpen))
            else dispatch ToggleNavHidden)
        prop.children [
            Shared.icon "contents"
            if hidden then Html.span [ prop.className "nav-show-label"; prop.text "Contents" ]
        ]
    ]

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
        prop.href "#"
        prop.onClick (fun e ->
            e.preventDefault ()
            dispatch (Navigate("#", false)))
        prop.children [ Html.text "Μάθησις"; Html.small [ prop.text "Ancient Greek reader" ] ]
    ]

/// On phones this row becomes the bottom tab bar, and gains two tabs that the
/// header carries on wider screens (the library drawer and My library), so the
/// two things reached for most sit under the thumb rather than at the top of
/// the screen. Order on phones: Library · Contents · Wiki · Forum · My library
/// (far right). `.tab-phone` hides them above the phone breakpoint.
let topNav (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let route = model.Route
    let active = Router.navKey route
    Html.nav [
        prop.className "topnav"
        prop.id "topnav"
        prop.children [
            Html.a [
                prop.href "#library"
                prop.custom ("data-nav", "library")
                prop.classes [ if active = "library" then "active" ]
                prop.title "Every text in the collection"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#library", false)))
                prop.children [
                    Shared.icon "texts"
                    Html.text "Library"
                ]
            ]
            Html.button [
                prop.custom ("data-nav", "contents")
                prop.classes [ "tab-phone"; if model.SideOpen then "active" ]
                prop.custom ("aria-expanded", model.SideOpen)
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    dispatch (ToggleSide(not model.SideOpen)))
                prop.children [ Shared.icon "contents"; Html.text "Contents" ]
            ]
            Html.a [
                prop.href "#wiki"
                prop.custom ("data-nav", "wiki")
                prop.classes [ if active = "wiki" then "active" ]
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#wiki", false)))
                prop.children [
                    Shared.icon "wiki"
                    Html.text "Wiki"
                ]
            ]
            Html.a [
                prop.href "#forum"
                prop.custom ("data-nav", "forum")
                prop.classes [ if active = "forum" then "active" ]
                prop.title "The town hall: discuss passages, debate, ask, report bugs"
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#forum", false)))
                prop.children [
                    Shared.icon "forum"
                    Html.text "Forum"
                ]
            ]
            Html.a [
                prop.href "#lib"
                prop.custom ("data-nav", "lib")
                prop.classes [ "tab-phone"; if active = "lib" then "active" ]
                prop.onClick (fun e ->
                    e.preventDefault ()
                    dispatch (Navigate("#lib", false)))
                prop.children [ Shared.icon "library"; Html.text "My library" ]
            ]
        ]
    ]

/// Author › work (› chunk, for multi-part works) — empty outside the reader,
/// mirrors the header's `#crumbs` (distinct from `Views.Shared.wikiCrumbs`,
/// which is the wiki pages' own in-content breadcrumb).
let crumbs (model: Model) : ReactElement =
    match model.Reader with
    | None -> Html.div [ prop.className "crumbs" ]
    | Some rm ->
        let authorName =
            Catalog.authorOf model.Catalog rm.Work.Id
            |> Option.map (fun a -> a.Name)
            |> Option.defaultValue ""
        let multi = rm.Data |> Option.map (fun d -> d.Chunks.Length > 1) |> Option.defaultValue false
        let chunkCrumb =
            match (if multi then rm.Chunk else None) with
            | Some c -> [ Html.span [ prop.className "sep"; prop.text "›" ]; Html.span [ prop.text c ] ]
            | None -> []
        Html.div [
            prop.className "crumbs"
            prop.children (
                [ Html.span [ prop.text authorName ]
                  Html.span [ prop.className "sep"; prop.text "›" ]
                  Html.span [ prop.text rm.Work.Title ] ]
                @ chunkCrumb
            )
        ]

let jumpBox (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.div [
        prop.className "jump"
        prop.id "jump"
        prop.hidden model.Reader.IsNone
        prop.children [
            Html.input [
                prop.id "jumpIn"
                prop.placeholder "Passage…"
                prop.ariaLabel "Go to reference"
                prop.value model.JumpInput
                prop.onChange (fun (v: string) -> dispatch (Reader_(SetJumpInput v)))
                prop.onKeyDown (fun e -> if e.key = "Enter" then dispatch (Reader_ GotoRefSubmitted))
            ]
            Html.button [
                prop.id "jumpBtn"
                prop.text "Go"
                prop.onClick (fun _ -> dispatch (Reader_ GotoRefSubmitted))
            ]
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
        prop.href "#account"
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
            accountButton model dispatch
            Html.a [
                prop.className ("libbtn" + (if Router.navKey route = "lib" then " active" else ""))
                prop.id "libBtn"
                prop.href "#lib"
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
        | SourceGitHub -> "GitHub", "Texts are downloaded from GitHub as you open them"
        | SourceUrl -> "URL", "Texts are read from " + (if model.Source.BaseUrl = "" then "this folder" else model.Source.BaseUrl)
        | SourceLocal when not connected -> "No local copy", "Local is selected but nothing is connected — texts will come from GitHub"
        | SourceLocal when not (List.isEmpty model.Source.NeedsReconnect) ->
            "Reconnect", String.concat ", " model.Source.NeedsReconnect + " needs permission again"
        | SourceLocal ->
            let what =
                model.Source.Local
                |> Map.toList
                |> List.map (fun (r, _) -> Sources.repoName r)
                |> String.concat " and "
            "Local", "Reading from your computer — " + what

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
                option_ SourceGitHub "GitHub" "Downloaded as you open them" (fun () ->
                    dispatch (Source_(SetSourceMode SourceGitHub)))
                option_ SourceLocal "Your computer" localHint (fun () ->
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
            navToggleButton model dispatch
            backButton model dispatch
            brand dispatch
            topNav model dispatch
            crumbs model
            jumpBox model dispatch
            sourceChip model dispatch
            notesButton model dispatch
            tools model dispatch
        ]
    ]
