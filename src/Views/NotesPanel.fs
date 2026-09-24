module Views.NotesPanel

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Types

// Pointer capture keeps every move and the release going to the title bar that
// started the drag, even when the pointer outruns the panel. Without it a fast
// drag hands the events to whatever is underneath and the panel gets stuck.
[<Emit("$0.setPointerCapture && $0.setPointerCapture($1)")>]
let private setPointerCapture (el: obj) (pointerId: float) : unit = jsNative

[<Emit("$0.releasePointerCapture && $0.hasPointerCapture && $0.hasPointerCapture($1) && $0.releasePointerCapture($1)")>]
let private releasePointerCapture (el: obj) (pointerId: float) : unit = jsNative

// The pointer's offset into the element, and where the drag began, are stashed
// on the element itself. Measuring off the live element avoids re-deriving the
// stylesheet's `right`/`bottom` in code, and avoids being out by the scrollbar:
// CSS resolves those against the client width, not innerWidth.
[<Emit("(() => { const r = $0.getBoundingClientRect(); const d = $0.dataset; d.gx = $1 - r.left; d.gy = $2 - r.top; d.sx = $1; d.sy = $2; })()")>]
let private beginDrag (el: obj) (x: float) (y: float) : unit = jsNative

/// `[newLeft; newTop; movedFarEnough]` for a pointer now at `x`,`y`. The last
/// value guards the button's click: a press that never travelled more than a
/// few pixels is a tap, not a drag, and must still open the panel.
[<Emit("(() => { const d = $0.dataset; const nx = $1 - (+d.gx), ny = $2 - (+d.gy); const far = (Math.abs($1 - (+d.sx)) + Math.abs($2 - (+d.sy))) > 4; return [nx, ny, far ? 1 : 0]; })()")>]
let private dragTo (el: obj) (x: float) (y: float) : float array = jsNative

/// Notes saved against the work on screen, newest first — the panel is about
/// the text you are reading, so it needs no picking or searching. The note
/// being edited stays listed even while its text is empty, so clearing it to
/// retype doesn't make the editor vanish mid-word.
let notesFor (library: Library) (editing: string option) (workId: string) : Mark list =
    library.Marks
    |> List.filter (fun m -> m.Work = workId && (m.Note <> "" || not (List.isEmpty m.Links) || editing = Some m.Id))
    |> List.sortByDescending (fun m -> m.Ts)

/// The chunk holding a passage, so jumping to a note lands in the right part of
/// a multi-part work rather than defaulting to the first.
let private chunkHolding (rm: ReaderModel) (segRef: string) : string option =
    rm.Data
    |> Option.bind (fun d -> d.Chunks |> List.tryFind (fun c -> c.Segments |> Array.exists (fun s -> s.Ref = segRef)))
    |> Option.map (fun c -> c.Ref)

let private noteRow (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (mark: Mark) : ReactElement =
    let editing = model.EditingNote = Some mark.Id
    Html.div [
        prop.key mark.Id
        prop.className ("np-note" + (if editing then " editing" else ""))
        prop.children [
            Html.button [
                prop.className "np-ref"
                prop.title "Go to this passage"
                // Already inside this work, so this is a jump within the open
                // text rather than a navigation — ShowChunk flashes the passage.
                // If the passage is gone (a different edition divides the work
                // differently) the note still reads; only the jump is inert.
                prop.disabled (chunkHolding rm mark.Ref |> Option.isNone)
                prop.onClick (fun _ ->
                    match chunkHolding rm mark.Ref with
                    | Some chunkRef -> dispatch (Reader_(ShowChunk(chunkRef, Some mark.Ref, None)))
                    | None -> ())
                prop.text (if mark.Label <> "" then mark.Label else mark.Ref)
            ]
            if editing then
                Shared.noteEditor dispatch mark
            elif mark.Note <> "" then
                Html.div [ prop.className "np-body"; prop.children (Shared.noteBody model.Catalog dispatch mark.Note) ]
            if not (List.isEmpty mark.Links) then
                Html.div [
                    prop.className "np-links"
                    prop.children (mark.Links |> List.map (Shared.linkChip model.Catalog dispatch None))
                ]
            if not editing then
                Html.div [
                    prop.className "note-edit-row"
                    prop.children [
                        Shared.editNoteButton (mark.Note <> "") (fun () -> dispatch (Library_(EditNote(Some mark.Id))))
                    ]
                ]
        ]
    ]

/// Explicit coordinates once dragged; until then the stylesheet's own corner is
/// left in charge, so `right`/`bottom` anchoring still applies.
let private placement (d: Draggable) : IStyleAttribute list =
    match d.X, d.Y with
    | Some x, Some y ->
        [ style.left (length.px x); style.top (length.px y); style.right length.auto; style.bottom length.auto ]
    | _ -> []

/// The button that opens the panel: floating and draggable rather than parked
/// in the header, so it can sit wherever it is least in the way of the text.
let private floatingButton (model: Model) (dispatch: Msg -> unit) : ReactElement =
    Html.button [
        prop.className (
            "notes-fab"
            + (if model.Notes.Open then " on" else "")
            + (if model.Notes.Button.Dragging then " dragging" else "")
        )
        prop.id "notesFab"
        prop.title "Notes on this text — drag to move"
        prop.ariaLabel "Notes on this text"
        prop.custom ("aria-pressed", model.Notes.Open)
        prop.style (placement model.Notes.Button)
        prop.onPointerDown (fun e ->
            e.preventDefault ()
            setPointerCapture (box e.currentTarget) e.pointerId
            beginDrag (box e.currentTarget) e.clientX e.clientY
            dispatch (NotesDrag("button", "grab", 0.0, 0.0)))
        prop.onPointerMove (fun e ->
            if model.Notes.Button.Dragging then
                let r = dragTo (box e.currentTarget) e.clientX e.clientY
                // Below the threshold this is still a tap in progress, so the
                // button must not budge — otherwise every click nudges it.
                if r.[2] = 1.0 then dispatch (NotesDrag("button", "move", r.[0], r.[1])))
        prop.onPointerUp (fun e ->
            releasePointerCapture (box e.currentTarget) e.pointerId
            // A press that never travelled is a click: open or close the panel.
            if not model.Notes.ButtonMoved then dispatch ToggleNotesPanel
            dispatch (NotesDrag("button", "drop", 0.0, 0.0)))
        prop.children [
            Shared.icon "notes"
            Html.span [ prop.className "fab-label"; prop.text "Notes" ]
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    match model.Reader with
    | None -> Html.none
    | Some rm ->
        let notes = notesFor model.Library model.EditingNote rm.Work.Id

        let panel =
            Html.div [
                prop.className ("notes-panel" + (if model.Notes.Open then " show" else "") + (if model.Notes.Panel.Dragging then " dragging" else ""))
                prop.id "notesPanel"
                prop.role "dialog"
                prop.ariaLabel ("Notes on " + rm.Work.Title)
                prop.style (placement model.Notes.Panel)
                prop.children [
                Html.div [
                    prop.className "np-bar"
                    prop.onPointerDown (fun e ->
                        // Buttons in the bar keep their own clicks.
                        let target = box e.target
                        if isNullOrUndefined (target?closest "button") then
                            e.preventDefault ()
                            setPointerCapture (box e.currentTarget) e.pointerId
                            beginDrag (box e.currentTarget?parentElement) e.clientX e.clientY
                            dispatch (NotesDrag("panel", "grab", 0.0, 0.0)))
                    prop.onPointerMove (fun e ->
                        if model.Notes.Panel.Dragging then
                            let r = dragTo (box e.currentTarget?parentElement) e.clientX e.clientY
                            dispatch (NotesDrag("panel", "move", r.[0], r.[1])))
                    prop.onPointerUp (fun e ->
                        releasePointerCapture (box e.currentTarget) e.pointerId
                        dispatch (NotesDrag("panel", "drop", 0.0, 0.0)))
                    prop.children [
                        Html.span [ prop.className "np-title"; prop.text "Notes" ]
                        Html.span [ prop.className "np-work"; prop.text rm.Work.Title ]
                        Html.button [
                            prop.className "np-close"
                            prop.ariaLabel "Close notes"
                            prop.title "Close"
                            prop.text "×"
                            prop.onClick (fun e ->
                                e.stopPropagation ()
                                dispatch ToggleNotesPanel)
                        ]
                    ]
                ]
                Html.div [
                    prop.className "np-list"
                    prop.children (
                        if List.isEmpty notes then
                            [ Html.p [
                                  prop.className "np-empty"
                                  prop.text "No notes on this text yet. Press the bookmark beside any passage, then write in the box that opens."
                              ] ]
                        else
                            notes |> List.map (noteRow model rm dispatch)
                    )
                ]
                Html.div [
                    prop.className "np-foot"
                    prop.children [
                        Html.span [
                            prop.className "np-count"
                            prop.text (
                                match List.length notes with
                                | 0 -> ""
                                | 1 -> "1 note here"
                                | n -> string n + " notes here"
                            )
                        ]
                        Html.button [
                            prop.className "btn small np-lib"
                            prop.text "Notes & favourites →"
                            prop.title "Open My library — every note, bookmark and favourite"
                            prop.onClick (fun _ -> dispatch (Navigate("#lib", false)))
                        ]
                    ]
                ]
                ]
            ]

        React.Fragment [ floatingButton model dispatch; panel ]
