module Views.SettingsPane

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Types

[<Emit("document.getElementById($0).click()")>]
let private clickElementById (id: string) : unit = jsNative

[<Emit("Array.from($0)")>]
let private toFileArray (fl: obj) : File array = jsNative

// ---------------------------------------------------------------------------
// generic segmented-button group
// ---------------------------------------------------------------------------

let private segBtn (dataV: string) (label: string) (pressed: bool) (onClick: unit -> unit) : ReactElement =
    Html.button [
        prop.custom ("data-v", dataV)
        prop.custom ("aria-pressed", pressed)
        prop.text label
        prop.onClick (fun _ -> onClick ())
    ]

/// Width and button flex are the stylesheet's business — every segmented group
/// inside the settings sheet fills its row, so there is nothing to vary here.
let private segGroup (id: string) (children: ReactElement list) : ReactElement =
    Html.div [ prop.className "segbtn"; prop.id id; prop.children children ]

let private stepper (idPrefix: string) (label: string) (display: string) (downLabel: string) (upLabel: string) (onDown: unit -> unit) (onUp: unit -> unit) : ReactElement =
    Html.div [
        prop.className "grp"
        prop.children [
            Html.label [ prop.text label ]
            Html.div [
                prop.className "stepper"
                prop.children [
                    Html.button [
                        prop.id (idPrefix + "Down")
                        prop.ariaLabel downLabel
                        prop.text "−"
                        prop.onClick (fun _ -> onDown ())
                    ]
                    // `prop.text`, not `prop.value`: <output> renders its children,
                    // so setting the value attribute left the steppers blank.
                    Html.output [ prop.id (idPrefix + "Out"); prop.text display ]
                    Html.button [
                        prop.id (idPrefix + "Up")
                        prop.ariaLabel upLabel
                        prop.text "+"
                        prop.onClick (fun _ -> onUp ())
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// "reading from your computer" summary (mirrors `localSummary`, no raw HTML)
// ---------------------------------------------------------------------------

/// A per-repository status list rather than a paragraph: at a glance, which of
/// the two repositories is connected, by what means, and which one is still
/// coming over the network.
let private localStatus (source: SourceState) : ReactElement =
    let row (repo: RepoKey) =
        let name = Sources.repoName repo
        let stale = not (List.isEmpty source.NeedsReconnect)
        let state, detail =
            match source.Local.TryFind repo with
            | Some(ZipArchive zipName) -> (if stale then "stale" else "on"), "ZIP · " + zipName
            | Some(DirHandle dirName) -> (if stale then "stale" else "on"), "folder · " + dirName
            | Some FileList -> "session", "chosen folder, this tab only"
            | None -> "off", "from GitHub"
        Html.div [
            prop.key name
            prop.className ("srcrow " + state)
            prop.children [
                Html.span [ prop.className "dot" ]
                Html.b [ prop.text name ]
                Html.span [ prop.className "detail"; prop.text detail ]
            ]
        ]
    Html.div [ prop.className "srcrows"; prop.children [ row Perseus; row First1K ] ]

let private localSummary (source: SourceState) : ReactElement =
    let have = source.Local |> Map.toList |> List.map fst
    if List.isEmpty have then
        Html.text "Add the ZIP you downloaded from GitHub (canonical-greekLit or First1KGreek), without unzipping it, or choose an unzipped folder."
    elif not (List.isEmpty source.NeedsReconnect) then
        React.Fragment [
            Html.b [ prop.text "Permission needed. " ]
            Html.text (
                "Browsers forget folder access between visits. Press Reconnect to restore "
                + String.concat ", " source.NeedsReconnect
                + " without picking it again."
            )
        ]
    else
        let miss = [ Perseus; First1K ] |> List.filter (fun r -> not (List.contains r have))
        if List.isEmpty miss then
            Html.text "Both collections are connected, so everything is read from your computer and no internet connection is needed."
        else
            Html.text (
                (miss |> List.map Sources.repoName |> String.concat " and ")
                + " isn't connected yet, so its texts still come from GitHub. Add its ZIP too for a fully offline library."
            )

// ---------------------------------------------------------------------------
// text source pane
// ---------------------------------------------------------------------------

let private sourcePane (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let mode = model.Source.Mode
    let connectZip () = Shared.connectZip dispatch
    let connectDir () = Shared.connectDir dispatch
    [ Html.div [
          prop.className "grp"
          prop.children [
              segGroup "srcBtns" [
                  Html.button [
                      prop.custom ("data-v", "github")
                      prop.custom ("aria-pressed", (mode = SourceGitHub))
                      prop.text "Online"
                      prop.title "Downloaded from GitHub as you open each text"
                      prop.onClick (fun _ -> dispatch (Source_(SetSourceMode SourceGitHub)))
                  ]
                  Html.button [
                      prop.custom ("data-v", "local")
                      prop.custom ("aria-pressed", (mode = SourceLocal))
                      prop.text "This computer"
                      // Going straight to the folder picker is right only when
                      // there is nothing to fall back on. If a ZIP or folder is
                      // remembered, the picker would be pre-empting Reconnect —
                      // the very button meant to spare you choosing it again.
                      prop.onClick (fun _ ->
                          dispatch (Source_(SetSourceMode SourceLocal))
                          if List.isEmpty (Sources.localHave ()) && not (Sources.hasRemembered ()) then connectDir ())
                  ]
                  Html.button [
                      prop.custom ("data-v", "url")
                      prop.custom ("aria-pressed", (mode = SourceUrl))
                      prop.text "Web address"
                      prop.onClick (fun _ -> dispatch (Source_(SetSourceMode SourceUrl)))
                  ]
              ]
          ]
      ]
      // Reconnect belongs to the Text source section itself, not to the Local
      // sub-pane: that pane is display:none until "Local folder" is already
      // selected, so the button was invisible in exactly the situation it
      // exists for — coming back to a browser that has dropped folder access,
      // with the source still reading GitHub.
      Html.div [
          prop.className "grp src-reconnect"
          prop.children [
              Html.button [
                  prop.classes [ "btn"; if not (List.isEmpty model.Source.NeedsReconnect) then "primary" ]
                  prop.id "reconnectSrc"
                  prop.disabled (not (Sources.hasRemembered ()))
                  prop.title (
                      if Sources.hasRemembered () then
                          "Re-grant access to " + String.concat ", " (Sources.rememberedNames ())
                      else
                          "Nothing remembered yet — add a ZIP or choose a folder first"
                  )
                  prop.text "Reconnect local files"
                  prop.onClick (fun _ -> dispatch (Source_ ReconnectRemembered))
              ]
              Html.div [
                  prop.className "srcinfo"
                  prop.text (
                      if Sources.hasRemembered () then
                          "Browsers drop folder access between visits. This restores "
                          + String.concat ", " (Sources.rememberedNames ())
                          + " without picking it again."
                      else
                          "Once you have connected a ZIP or folder, this brings it back without picking it again."
                  )
              ]
          ]
      ]
      Html.div [
          prop.id "srcLocal"
          prop.className ("srcpane" + (if mode = SourceLocal then " show" else ""))
          prop.children [
              localStatus model.Source
              // A flex row rather than buttons separated by literal spaces, so the
              // pair shares the width evenly instead of wrapping raggedly.
              Html.div [
                  prop.className "src-actions"
                  prop.children [
                      Html.button [ prop.className "btn"; prop.id "pickZip"; prop.text "Add downloaded ZIP…"; prop.onClick (fun _ -> connectZip ()) ]
                      Html.button [ prop.className "btn"; prop.id "pickDir"; prop.text "Choose folder…"; prop.onClick (fun _ -> connectDir ()) ]
                  ]
              ]
              Html.div [ prop.className "srcinfo"; prop.id "localInfo"; prop.children [ localSummary model.Source ] ]
              Html.input [
                  prop.type' "file"
                  prop.id "dirInput"
                  prop.custom ("webkitdirectory", "")
                  prop.custom ("directory", "")
                  prop.multiple true
                  prop.hidden true
                  prop.onChange (fun (ev: Event) ->
                      let input = ev.target :?> HTMLInputElement
                      match Sources.connectFileList input.files with
                      | Ok count -> dispatch (Source_(FileListConnected count))
                      | Error msg -> dispatch (Source_(SourceFailed msg)))
              ]
              Html.input [
                  prop.type' "file"
                  prop.id "zipInput"
                  prop.accept ".zip,application/zip"
                  prop.hidden true
                  prop.onChange (fun (ev: Event) ->
                      let input = ev.target :?> HTMLInputElement
                      for f in toFileArray input.files do
                          Sources.addZipFile f None
                          |> Promise.map (function
                              | Ok(repo, name) -> dispatch (Source_(ZipConnected(repo, name)))
                              | Error msg -> dispatch (Source_(SourceFailed msg)))
                          |> Promise.start
                      input.value <- "")
              ]
          ]
      ]
      Html.div [
          prop.id "srcUrl"
          prop.className ("srcpane" + (if mode = SourceUrl then " show" else ""))
          prop.children [
              Html.input [
                  prop.id "baseUrl"
                  prop.placeholder "http://localhost:8000"
                  prop.defaultValue model.Source.BaseUrl
                  prop.onBlur (fun e ->
                      let input = e.target :?> HTMLInputElement
                      let trimmed = System.Text.RegularExpressions.Regex.Replace(input.value.Trim(), "/+$", "")
                      dispatch (Source_(SetBaseUrl trimmed)))
              ]
              Html.div [
                  prop.className "srcinfo"
                  prop.children [
                      Html.text "Base URL under which the repo folders sit. Served this page from the same folder? Use "
                      Html.code [ prop.text "." ]
                  ]
              ]
          ]
      ] ]

// ---------------------------------------------------------------------------
// top-level assembly
// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let s = model.Settings
    Html.div [
        prop.className ("settings" + (if model.SettingsOpen then " show" else ""))
        prop.id "settings"
        prop.role "dialog"
        prop.ariaLabel "Reading settings"
        prop.children (
            // The sheet is a scroll container, so the close button rides a
            // sticky bar rather than being positioned against the panel — an
            // absolutely-placed one would scroll away with the content.
            [ Html.div [
                  prop.className "panel-close-bar"
                  prop.children [
                      Html.button [
                          prop.className "panel-close"
                          prop.ariaLabel "Close settings"
                          prop.title "Close"
                          prop.text "×"
                          prop.onClick (fun e ->
                              e.stopPropagation ()
                              dispatch (Settings_ ToggleSettingsPane))
                      ]
                  ]
              ]
              Html.h3 [ prop.text "Reading" ]
              // A line of real Greek that follows the controls below, so a size
              // or spacing change can be judged without closing the sheet.
              Html.p [
                  prop.className "set-sample"
                  prop.lang "grc"
                  prop.ariaHidden true
                  prop.custom ("data-sans", (if s.Face = Sans then "1" else ""))
                  prop.text "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος"
              ]
              Html.div [
                  prop.className "grp"
                  prop.children [
                      Html.label [ prop.text "Columns" ]
                      segGroup "modeBtns" [
                          segBtn "both" "Both" (s.Mode = Both) (fun () -> dispatch (Settings_(SetMode Both)))
                          segBtn "grc" "Greek" (s.Mode = GrcOnly) (fun () -> dispatch (Settings_(SetMode GrcOnly)))
                          segBtn "eng" "Translation" (s.Mode = EngOnly) (fun () -> dispatch (Settings_(SetMode EngOnly)))
                      ]
                  ]
              ]
              Html.div [
                  prop.className "grp"
                  prop.children [
                      Html.label [ prop.text "Typeface" ]
                      segGroup "faceBtns" [
                          segBtn "serif" "Serif" (s.Face = Serif) (fun () -> dispatch (Settings_(SetFace Serif)))
                          segBtn "sans" "Sans" (s.Face = Sans) (fun () -> dispatch (Settings_(SetFace Sans)))
                      ]
                  ]
              ]
              // Shown relative to the default rather than as the raw rem value:
              // the control now scales the whole page, so "100%" means "the size
              // this site normally is", which 118% did not say.
              stepper "fs" "Text size" (string (System.Math.Round(s.FontSize / Storage.baseFontSize * 100.0)) + "%") "Smaller" "Larger"
                  (fun () -> dispatch (Settings_(BumpFontSize -0.06)))
                  (fun () -> dispatch (Settings_(BumpFontSize 0.06)))
              stepper "lh" "Line height" (s.LineHeight.ToString("F2")) "Tighter" "Looser"
                  (fun () -> dispatch (Settings_(BumpLineHeight -0.1)))
                  (fun () -> dispatch (Settings_(BumpLineHeight 0.1)))
              Html.div [
                  prop.className "grp"
                  prop.children [
                      Html.label [ prop.text "Theme" ]
                      segGroup "themeBtns" [
                          segBtn "auto" "Auto" (s.Theme = ThemeAuto) (fun () -> dispatch (Settings_(SetTheme ThemeAuto)))
                          segBtn "light" "Light" (s.Theme = ThemeLight) (fun () -> dispatch (Settings_(SetTheme ThemeLight)))
                          segBtn "dark" "Dark" (s.Theme = ThemeDark) (fun () -> dispatch (Settings_(SetTheme ThemeDark)))
                      ]
                  ]
              ]
              // `.sect` carries the rule and spacing that separates the two
              // halves of the sheet, in place of an inline margin.
              Html.h3 [ prop.className "sect"; prop.text "Text source" ] ]
            @ sourcePane model dispatch
        )
    ]
