module Views.LibraryPage

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Types

[<Emit("window.confirm($0)")>]
let private confirmDialog (msg: string) : bool = jsNative

[<Emit("navigator.clipboard && navigator.clipboard.writeText($0)")>]
let private clipboardWriteText (text: string) : JS.Promise<unit> option = jsNative

[<Emit("new Date($0).toLocaleDateString()")>]
let private toLocaleDateString (ts: float) : string = jsNative

let private interspersed (sep: ReactElement) (items: ReactElement list) : ReactElement list =
    items |> List.mapi (fun i x -> if i = 0 then [ x ] else [ sep; x ]) |> List.collect id

// ---------------------------------------------------------------------------
// summary line
// ---------------------------------------------------------------------------

let private plural (n: int) (one: string) (many: string) = string n + " " + (if n = 1 then one else many)

let private summaryLine (lib: Library) : ReactElement =
    let xrefCount = lib.Marks |> List.sumBy (fun m -> (LibraryData.linksOf m).Length)
    Html.p [
        prop.className "quiet lib-summary"
        prop.text (
            plural lib.Favs.Length "favourite work" "favourite works"
            + " and "
            + plural lib.Marks.Length "saved passage" "saved passages"
            + (if xrefCount > 0 then ", with " + plural xrefCount "cross-reference" "cross-references" else "")
            + ". Everything is saved in this browser only, so use Back up at the bottom of the page to keep a copy."
        )
    ]

/// Shown instead of the sections when nothing has been saved yet: what the
/// page is for, and how things get onto it.
let private emptyState: ReactElement =
    Html.div [
        prop.className "lib-empty"
        prop.children [
            Html.p [ prop.text "This is where your own reading collects. Nothing is here yet." ]
            Html.ul [
                prop.children [
                    Html.li [
                        prop.children [
                            Html.b [ prop.text "Favourite a work " ]
                            Html.text "with the star beside its title, and it will wait for you here."
                        ]
                    ]
                    Html.li [
                        prop.children [
                            Html.b [ prop.text "Save a passage " ]
                            Html.text "with the bookmark beside its number, and add a note if you like. Notes can link to other passages: type "
                            Html.code [ prop.text "[[tlg0012.tlg001:1.1]]" ]
                            Html.text " for Iliad 1.1."
                        ]
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// favourite works
// ---------------------------------------------------------------------------

let private favouritesSection (model: Model) (dispatch: Msg -> unit) (favs: string list) : ReactElement list =
    [ Html.h2 [ prop.className "sh"; prop.text "Favourite works" ]
      if List.isEmpty model.Library.Favs then
          Html.p [ prop.className "quiet"; prop.text "None yet. Star a work beside its title to keep it here." ]
      elif List.isEmpty favs then
          Html.p [ prop.className "quiet"; prop.text "No favourites in this genre." ]
      else
          Html.div [
              prop.className "picks"
              prop.children [
                  for id in favs ->
                      let title = Catalog.workTitle model.Catalog id |> Option.defaultValue id
                      let grc =
                          model.Catalog.WorkById.TryFind id
                          |> Option.bind Catalog.titleGrc
                          |> Option.filter (fun g -> g <> title)
                      let authorName = Catalog.authorOf model.Catalog id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
                      Html.button [
                          prop.key id
                          prop.className "pick"
                          prop.onClick (fun _ -> dispatch (Reader_(OpenWork(id, None, None, None, None))))
                          prop.children [
                              match grc with
                              | Some g -> Html.span [ prop.className "grc"; prop.text g ]
                              | None -> Html.none
                              Html.span [ prop.className "en"; prop.text title ]
                              Html.span [ prop.className "who"; prop.text authorName ]
                          ]
                      ]
              ]
          ] ]

// ---------------------------------------------------------------------------
// bookmarks and notes
// ---------------------------------------------------------------------------

/// Marks grouped by work, the work you saved something in most recently first;
/// within a work, passages keep the order they were saved in.
let private groupMarksByWork (marks: Mark list) : (string * Mark list) list =
    marks
    |> List.groupBy (fun m -> m.Work)
    |> List.sortByDescending (fun (_, ms) -> ms |> List.map (fun m -> m.Ts) |> List.max)
    |> List.map (fun (w, ms) -> w, ms |> List.sortBy (fun m -> m.Ts))

let private libMarkRow (model: Model) (dispatch: Msg -> unit) (m: Mark) : ReactElement =
    let backlinks = LibraryData.backlinks model.Library m.Work m.Ref
    let hasNoteContent = m.Note <> "" || not (List.isEmpty m.Links)
    let editing = model.EditingNote = Some m.Id
    Html.div [
        prop.key m.Id
        prop.className ("lib-mark" + (if editing then " editing" else ""))
        prop.custom ("data-id", m.Id)
        prop.children [
            Html.div [
                prop.className "lm-head"
                prop.children [
                    Html.button [
                        prop.className "lm-open"
                        prop.onClick (fun _ -> dispatch (Reader_(OpenWork(m.Work, None, None, None, Some m.Ref))))
                        prop.children [ Html.b [ prop.text (if m.Label <> "" then m.Label else m.Ref) ] ]
                    ]
                    Html.span [ prop.className "quiet"; prop.text (toLocaleDateString m.Ts) ]
                    if not editing then
                        Shared.editNoteButton (m.Note <> "") (fun () -> dispatch (Library_(EditNote(Some m.Id))))
                    Html.button [
                        prop.className "btn small lm-del"
                        prop.text "Remove"
                        prop.onClick (fun _ -> dispatch (Library_(RemoveMark(m.Work, m.Ref))))
                    ]
                ]
            ]
            Html.div [ prop.className "lm-snip"; prop.text m.Snippet ]
            if editing then
                Shared.noteEditor dispatch m
            if hasNoteContent && not editing then
                Html.div [
                    prop.className "mk-note"
                    prop.children (
                        (Shared.noteBody model.Catalog dispatch m.Note)
                        @ (if List.isEmpty m.Links then
                               []
                           else
                               [ Html.div [
                                     prop.className "mk-links"
                                     prop.children ([ Html.text "See also: " ] @ interspersed (Html.text " ") (m.Links |> List.map (Shared.linkChip model.Catalog dispatch None)))
                                 ] ])
                    )
                ]
            if not (List.isEmpty backlinks) then
                Html.div [
                    prop.className "mk-back"
                    prop.children (
                        [ Html.text "Referenced from: " ]
                        @ interspersed
                            (Html.text " · ")
                            (backlinks
                             |> List.map (fun b ->
                                 let title = Catalog.workTitle model.Catalog b.Work |> Option.defaultValue b.Work
                                 let hash = Router.toHash (ReaderRoute(b.Work, "", "", None, (if b.Ref = "" then None else Some b.Ref)))
                                 Html.a [
                                     prop.className "xref"
                                     prop.href "#"
                                     prop.text (title + " " + (if b.Label <> "" then b.Label else b.Ref))
                                     prop.onClick (fun e ->
                                         e.preventDefault ()
                                         dispatch (Navigate(hash, false)))
                                 ]))
                    )
                ]
        ]
    ]

let private bookmarksSection (model: Model) (dispatch: Msg -> unit) (marks: Mark list) : ReactElement list =
    let groups = groupMarksByWork marks
    [ Html.h2 [ prop.className "sh"; prop.text "Saved passages and notes" ]
      if List.isEmpty model.Library.Marks then
          Html.p [ prop.className "quiet"; prop.text "None yet. While reading, press the bookmark beside any passage's number." ]
      elif List.isEmpty marks then
          Html.p [ prop.className "quiet"; prop.text "No saved passages in this genre." ]
      else
          for workId, ms in groups do
              let title = Catalog.workTitle model.Catalog workId |> Option.defaultValue workId
              let authorName = Catalog.authorOf model.Catalog workId |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
              // A long library opens as a list of works to pick from, not a
              // wall of notes; with only a few works everything is open.
              Html.details [
                  prop.key workId
                  prop.className "lib-work"
                  if groups.Length <= 3 then prop.isOpen true
                  prop.children (
                      [ Html.summary [
                            prop.children [
                                Html.span [ prop.className "lw-title"; prop.text title ]
                                Html.span [ prop.className "quiet"; prop.text authorName ]
                                Html.span [ prop.className "lw-n"; prop.text (plural ms.Length "passage" "passages") ]
                            ]
                        ] ]
                      @ [ for m in ms -> libMarkRow model dispatch m ]
                  )
              ] ]

// ---------------------------------------------------------------------------
// author notes — written on author pages, gathered here so they can be found
// ---------------------------------------------------------------------------

let private authorNotesSection (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let notes =
        model.Library.AuthorNotes
        |> Map.toList
        |> List.filter (fun (_, t) -> t.Trim() <> "")
        |> List.map (fun (id, t) ->
            let name =
                model.Catalog.Authors |> List.tryFind (fun a -> a.Id = id) |> Option.map (fun a -> a.Name) |> Option.defaultValue id
            id, name, t)
        |> List.sortBy (fun (_, name, _) -> name)
    if List.isEmpty notes then
        []
    else
        [ Html.h2 [ prop.className "sh"; prop.text "Notes on authors" ]
          Html.div [
              prop.className "lib-anotes"
              prop.children [
                  for id, name, t in notes ->
                      let hash = "#author/" + id
                      let firstLine = (t.Split('\n').[0]).Trim()
                      Html.a [
                          prop.key id
                          prop.className "lib-anote"
                          prop.href hash
                          prop.onClick (fun e ->
                              e.preventDefault ()
                              dispatch (Navigate(hash, false)))
                          prop.children [
                              Html.b [ prop.text name ]
                              Html.span [ prop.text (if firstLine.Length > 80 then firstLine.Substring(0, 80) + "…" else firstLine) ]
                          ]
                      ]
              ]
          ] ]

// ---------------------------------------------------------------------------
// back up (export / import / clear) — visibility/mode is transient UI state,
// not part of the Elmish Model, so this is a real React component with hooks
// ---------------------------------------------------------------------------

type private IoMode =
    | IoNone
    | IoExport
    | IoImport

[<ReactComponent>]
let private BackupSection (model: Model) (dispatch: Msg -> unit) =
    let mode, setMode = React.useState IoNone
    let importText, setImportText = React.useState ""

    let currentValue =
        match mode with
        | IoExport -> LibraryData.exportJson model.Library
        | IoImport -> importText
        | IoNone -> ""

    Html.details [
        prop.className "lib-backup"
        prop.children [
            Html.summary [ prop.className "sh"; prop.text "Back up, restore or clear" ]
            Html.p [
                prop.className "quiet"
                prop.text "Export gives you your whole library as text you can save anywhere. Import restores it here or in another browser."
            ]
            Html.div [
                prop.className "lib-io"
                prop.children [
                    Html.button [
                        prop.className "btn"
                        prop.id "libExport"
                        prop.text "Export"
                        prop.onClick (fun _ ->
                            dispatch (Library_ ExportRequested)
                            setMode IoExport)
                    ]
                    Html.text " "
                    Html.button [
                        prop.className "btn"
                        prop.id "libImport"
                        prop.text "Import"
                        prop.onClick (fun _ ->
                            setImportText ""
                            setMode IoImport)
                    ]
                    Html.text " "
                    Html.button [
                        prop.className "btn danger"
                        prop.id "libClear"
                        prop.text "Clear everything"
                        prop.onClick (fun _ ->
                            if confirmDialog "Remove all favourites, bookmarks and notes from this browser?" then
                                dispatch (Library_ ClearLibraryConfirmed))
                    ]
                ]
            ]
            Html.textarea [
                prop.id "libJson"
                prop.rows 6
                prop.hidden ((mode = IoNone))
                prop.readOnly ((mode = IoExport))
                prop.placeholder (if mode = IoImport then "Paste an export here, then press Load this" else "")
                prop.value currentValue
                prop.onChange (fun (v: string) -> if mode = IoImport then setImportText v)
            ]
            Html.div [
                prop.id "libIoActions"
                prop.hidden ((mode = IoNone))
                prop.children [
                    Html.button [
                        prop.className "btn"
                        prop.id "libCopy"
                        prop.text "Copy"
                        prop.onClick (fun _ ->
                            match clipboardWriteText currentValue with
                            | Some p -> p |> Promise.map (fun () -> dispatch (ShowToast "Copied")) |> Promise.start
                            | None -> ())
                    ]
                    Html.text " "
                    Html.button [
                        prop.className "btn primary"
                        prop.id "libLoad"
                        prop.text "Load this"
                        prop.hidden (mode <> IoImport)
                        prop.onClick (fun _ ->
                            dispatch (Library_(ImportText importText))
                            dispatch (Library_ ImportConfirmed))
                    ]
                ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// top-level assembly
// ---------------------------------------------------------------------------

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let lib = model.Library
    let genreOf = Shared.genreOfWork model
    let inGenre (workId: string) =
        match model.Genre with
        | Some g -> genreOf workId = g
        | None -> true
    let favs = lib.Favs |> List.filter inGenre
    let marks = lib.Marks |> List.filter (fun m -> inGenre m.Work)
    let hasAuthorNotes = lib.AuthorNotes |> Map.exists (fun _ t -> t.Trim() <> "")
    let isEmpty = List.isEmpty lib.Favs && List.isEmpty lib.Marks && not hasAuthorNotes
    // Chips count works: every favourite, plus every work with a saved passage.
    let genreCounts =
        (lib.Favs @ (lib.Marks |> List.map (fun m -> m.Work)))
        |> List.distinct
        |> List.countBy genreOf
    Html.div [
        prop.className "page library-page"
        prop.children (
            [ Html.h1 [ prop.className "ph"; prop.text "My library" ] ]
            @ (if isEmpty then
                   [ emptyState; BackupSection model dispatch ]
               else
                   [ summaryLine lib
                     // One genre is not a choice; show chips only once there are two.
                     if genreCounts.Length > 1 || model.Genre.IsSome then
                         Shared.genreChips "Genre" genreCounts model.Genre dispatch
                     // Saved passages are what gets read here, so they take the
                     // main column; favourites, author notes and the back-up
                     // tools are consulted, so they sit in a rail beside it.
                     Html.div [
                         prop.className "lib-cols"
                         prop.children [
                             Html.div [ prop.className "lib-main"; prop.children (bookmarksSection model dispatch marks) ]
                             Html.aside [
                                 prop.className "lib-rail"
                                 prop.children (
                                     favouritesSection model dispatch favs
                                     @ authorNotesSection model dispatch
                                     @ [ BackupSection model dispatch ]
                                 )
                             ]
                         ]
                     ] ])
        )
    ]
