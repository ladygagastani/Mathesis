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

/// Where the library lives: this browser only, or synced to an account.
let private whereSaved (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let acc = model.Account
    Html.p [
        prop.className "quiet lib-summary"
        prop.children [
            match acc.Session, acc.Sync with
            | Some s, SyncError e ->
                Html.text ("Signed in as " + (if acc.DisplayName <> "" then acc.DisplayName else s.Email) + ", but the last sync failed (" + e + "). Your changes are safe in this browser and will sync when it can. ")
            | Some s, _ ->
                Html.text ("Synced to your account (" + s.Email + "), so it is the same on every device you sign in on. ")
            | None, _ when acc.Configured ->
                Html.text "Saved in this browser only. "
                Html.a [
                    prop.href "#account"
                    prop.text "Sign in"
                    prop.onClick (fun e ->
                        e.preventDefault ()
                        dispatch (Navigate("#account", false)))
                ]
                Html.text " to keep it on every device, or use “Back up, restore or clear” to keep a copy. "
            | None, _ -> Html.text "Everything is saved in this browser only, so use “Back up, restore or clear” to keep a copy. "
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

/// A passage reference as a sortable key: numbers compare as numbers, so
/// 1.9 comes before 1.10 and Stephanus 17a before 17b.
let private refKey (r: string) : (int * string) list =
    r.Split([| '.'; ':' |])
    |> Array.map (fun part ->
        let digits = System.Text.RegularExpressions.Regex.Match(part, @"^\d+")
        if digits.Success then int digits.Value, part.Substring digits.Length else System.Int32.MaxValue, part)
    |> List.ofArray

/// Marks grouped by work. "recent": the work you saved in last first,
/// passages in the order saved. "work": works by author then title, passages
/// in reading order. "oldest": the work you began first.
let private groupMarks (model: Model) (sortBy: string) (marks: Mark list) : (string * Mark list) list =
    let groups = marks |> List.groupBy (fun m -> m.Work)
    match sortBy with
    | "work" ->
        groups
        |> List.sortBy (fun (w, _) ->
            (Catalog.authorOf model.Catalog w |> Option.map (fun a -> a.Name) |> Option.defaultValue "~"),
            (Catalog.workTitle model.Catalog w |> Option.defaultValue w))
        |> List.map (fun (w, ms) -> w, ms |> List.sortBy (fun m -> refKey m.Ref))
    | "oldest" ->
        groups
        |> List.sortBy (fun (_, ms) -> ms |> List.map (fun m -> m.Ts) |> List.min)
        |> List.map (fun (w, ms) -> w, ms |> List.sortBy (fun m -> m.Ts))
    | _ ->
        groups
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
    let q = model.MarkQuery.Trim().ToLowerInvariant()
    let tags = LibraryData.allTags model.Library
    let shown =
        marks
        |> List.filter (fun m ->
            (match model.MarkTag with
             | Some t -> List.contains t (LibraryData.tagsOf m.Note)
             | None -> true)
            && (q = ""
                || m.Label.ToLowerInvariant().Contains q
                || m.Ref.ToLowerInvariant().Contains q
                || m.Snippet.ToLowerInvariant().Contains q
                || m.Note.ToLowerInvariant().Contains q
                || (Catalog.workTitle model.Catalog m.Work |> Option.defaultValue "").ToLowerInvariant().Contains q))
    let groups = groupMarks model model.MarkSort shown
    [ if not (List.isEmpty model.Library.Marks) then
          Html.div [
              prop.className "lib-tools"
              prop.children [
                  Html.input [
                      prop.className "search"
                      prop.type' "search"
                      prop.placeholder "Search your passages and notes"
                      prop.ariaLabel "Search your passages and notes"
                      prop.value model.MarkQuery
                      prop.onChange (fun (v: string) -> dispatch (Library_(SetMarkQuery v)))
                  ]
                  Html.label [
                      prop.className "lib-sort"
                      prop.children [
                          Html.span [ prop.text "Order" ]
                          Html.select [
                              prop.value model.MarkSort
                              prop.onChange (fun (v: string) -> dispatch (Library_(SetMarkSort v)))
                              prop.children [
                                  Html.option [ prop.value "recent"; prop.text "Recently saved" ]
                                  Html.option [ prop.value "work"; prop.text "By author and work, in reading order" ]
                                  Html.option [ prop.value "oldest"; prop.text "Oldest first" ]
                              ]
                          ]
                      ]
                  ]
              ]
          ]
      if not (List.isEmpty tags) then
          Html.div [
              prop.className "subcats tag-chips"
              prop.role "group"
              prop.ariaLabel "Tags"
              prop.children [
                  Html.span [ prop.className "sc-label"; prop.text "Tags" ]
                  Html.div [
                      prop.className "chips"
                      prop.children [
                          for t, n in tags ->
                              let on = model.MarkTag = Some t
                              Html.button [
                                  prop.key t
                                  prop.className ("chip" + (if on then " on" else ""))
                                  prop.custom ("aria-pressed", on)
                                  prop.onClick (fun _ -> dispatch (Library_(SetMarkTag(if on then None else Some t))))
                                  prop.children [ Html.text ("#" + t + " "); Html.span [ prop.text (string n) ] ]
                              ]
                      ]
                  ]
              ]
          ]
      if List.isEmpty model.Library.Marks then
          Html.div [
              prop.className "lib-empty"
              prop.children [
                  Html.p [ prop.text "No saved passages yet." ]
                  Html.p [
                      prop.className "quiet"
                      prop.children [
                          Html.text "While reading, press the bookmark beside a passage's number to save it with a note. In a note, "
                          Html.code [ prop.text "#tags" ]
                          Html.text " group passages (#simile, #exam), and "
                          Html.code [ prop.text "[[tlg0012.tlg001:1.1]]" ]
                          Html.text " links to another passage (here, Iliad 1.1)."
                      ]
                  ]
              ]
          ]
      elif List.isEmpty shown then
          Html.p [ prop.className "quiet"; prop.text "Nothing saved matches. Try another search, tag or genre." ]
      else
          for workId, ms in groups do
              let title = Catalog.workTitle model.Catalog workId |> Option.defaultValue workId
              let authorName = Catalog.authorOf model.Catalog workId |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
              // A long library opens as a list of works to pick from, not a
              // wall of notes; with only a few works, or a search, all is open.
              Html.details [
                  prop.key workId
                  prop.className "lib-work"
                  if groups.Length <= 3 || q <> "" || model.MarkTag.IsSome then prop.isOpen true
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
// words: saved from the word popover, learned with flashcards
// ---------------------------------------------------------------------------

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

let private readerHash (work: string) (ref_: string) =
    Router.toHash (ReaderRoute(work, "", "", None, (if ref_ = "" then None else Some ref_)))

let private whereFrom (model: Model) (work: string) (ref_: string) : string =
    (Catalog.workTitle model.Catalog work |> Option.defaultValue work) + (if ref_ = "" then "" else " " + ref_)

/// How well a card is known: its Leitner box as five pips.
let private strength (box: int) : ReactElement =
    Html.span [
        prop.className "w-strength"
        prop.title (if box = 0 then "Not reviewed yet" else sprintf "Box %d of 5" box)
        prop.ariaLabel (if box = 0 then "new" else sprintf "known %d of 5" box)
        prop.children [ for i in 1..5 -> Html.i [ prop.key i; prop.className (if i <= box then "on" else "") ] ]
    ]

let private whenDue (due: float) : string =
    let hours = (due - nowMs ()) / 3600000.0
    if hours <= 0.0 then "due now"
    elif hours < 1.0 then "due within the hour"
    elif hours < 18.0 then sprintf "due in %.0f hours" (System.Math.Ceiling hours)
    elif hours < 42.0 then "due tomorrow"
    else sprintf "due in %.0f days" (System.Math.Round(hours / 24.0))

/// The context with the word itself picked out.
let private contextWithWord (ctx: string) (word: string) : ReactElement list =
    match ctx.IndexOf word with
    | i when i >= 0 && word <> "" ->
        [ Html.text (ctx.Substring(0, i))
          Html.mark [ prop.text word ]
          Html.text (ctx.Substring(i + word.Length)) ]
    | _ -> [ Html.text ctx ]

/// One word, with its dictionary form and meaning editable in place. Local
/// state holds the typing; leaving a field saves it.
[<ReactComponent>]
let private WordRow (model: Model) (dispatch: Msg -> unit) (w: WordCard) =
    let lemma, setLemma = React.useState w.Lemma
    let gloss, setGloss = React.useState w.Gloss
    let commit () = if lemma <> w.Lemma || gloss <> w.Gloss then dispatch (Library_(EditWord(w.Id, lemma.Trim(), gloss.Trim())))
    Html.div [
        prop.className "w-in"
        prop.children [
            Html.div [
                prop.className "w-head"
                prop.children [
                    Html.span [ prop.className "w-word grc"; prop.lang "grc"; prop.text w.Word ]
                    strength w.Box
                    Html.span [ prop.className "quiet w-due"; prop.text (if w.Box = 0 then "new" else whenDue w.Due) ]
                    Html.button [
                        prop.className "linkbtn w-del"
                        prop.ariaLabel ("Remove " + w.Word)
                        prop.title "Remove this word"
                        prop.text "Remove"
                        prop.onClick (fun _ -> dispatch (Library_(RemoveWord w.Id)))
                    ]
                ]
            ]
            Html.div [
                prop.className "w-fields"
                prop.children [
                    Html.label [
                        prop.children [
                            Html.span [ prop.text "Dictionary form" ]
                            Html.input [
                                prop.lang "grc"
                                prop.className "grc"
                                prop.value lemma
                                prop.placeholder "e.g. λόγος"
                                prop.onChange setLemma
                                prop.onBlur (fun _ -> commit ())
                                Shared.onEnterSave commit
                            ]
                        ]
                    ]
                    Html.label [
                        prop.children [
                            Html.span [ prop.text "Meaning" ]
                            Html.input [
                                prop.value gloss
                                prop.placeholder "e.g. word, speech, reason"
                                prop.onChange setGloss
                                prop.onBlur (fun _ -> commit ())
                                Shared.onEnterSave commit
                            ]
                        ]
                    ]
                    Html.a [
                        prop.className "w-look"
                        prop.href ("https://logeion.uchicago.edu/" + enc (if lemma <> "" then lemma else w.Word))
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.text "Look up on Logeion ↗"
                    ]
                ]
            ]
            if w.Context <> "" then
                Html.p [ prop.className "w-ctx grc"; prop.lang "grc"; prop.children (contextWithWord w.Context w.Word) ]
            if w.Work <> "" then
                let h = readerHash w.Work w.Ref
                Html.a [
                    prop.className "xref w-from"
                    prop.href h
                    prop.text (whereFrom model w.Work w.Ref)
                    prop.onClick (fun e ->
                        e.preventDefault ()
                        dispatch (Navigate(h, false)))
                ]
        ]
    ]

/// A review: the word in its passage; you recall it, then check.
let private reviewCard (model: Model) (dispatch: Msg -> unit) (r: ReviewState) : ReactElement =
    match r.Queue with
    | [] ->
        let next = model.Library.Words |> List.map (fun w -> w.Due) |> List.sort |> List.tryHead
        Html.div [
            prop.className "rv-done"
            prop.children [
                Html.h2 [ prop.className "rv-title"; prop.text "Review done" ]
                Html.p [ prop.text (sprintf "You knew %d of %d words on the first try." r.Right (r.Right + r.Missed.Length)) ]
                match next with
                | Some d -> Html.p [ prop.className "quiet"; prop.text ("The next card is " + whenDue d + ".") ]
                | None -> Html.none
                Html.button [ prop.className "btn primary"; prop.text "Back to my words"; prop.onClick (fun _ -> dispatch (Library_ EndReview)) ]
            ]
        ]
    | id :: _ ->
        match model.Library.Words |> List.tryFind (fun w -> w.Id = id) with
        | None -> Html.div [ prop.className "quiet"; prop.text "This card was removed." ]
        | Some w ->
            let total = r.Seen + r.Queue.Length
            Html.div [
                prop.className "rv-card"
                prop.role "region"
                prop.ariaLabel "Flashcard. Keys: Space shows the answer; 1 for again, 2 for got it."
                prop.children [
                    Html.div [
                        prop.className "rv-top"
                        prop.children [
                            Html.span [ prop.className "quiet"; prop.text (sprintf "Card %d of %d" (r.Seen + 1) total) ]
                            Html.button [ prop.className "linkbtn"; prop.text "End review"; prop.onClick (fun _ -> dispatch (Library_ EndReview)) ]
                        ]
                    ]
                    Html.div [ prop.className "rv-word grc"; prop.lang "grc"; prop.text w.Word ]
                    if w.Context <> "" then
                        Html.p [ prop.className "rv-ctx grc"; prop.lang "grc"; prop.children (contextWithWord w.Context w.Word) ]
                    if w.Work <> "" then Html.p [ prop.className "rv-from quiet"; prop.text (whereFrom model w.Work w.Ref) ]
                    if not r.Revealed then
                        Html.div [
                            prop.className "rv-acts"
                            prop.children [
                                Html.p [ prop.className "quiet"; prop.text "What does it mean here? Say it to yourself, then check." ]
                                Html.button [ prop.className "btn primary big"; prop.text "Show the answer"; prop.onClick (fun _ -> dispatch (Library_ RevealCard)) ]
                            ]
                        ]
                    else
                        Html.div [
                            prop.className "rv-answer"
                            prop.children [
                                if w.Lemma <> "" then Html.p [ prop.className "rv-lemma grc"; prop.lang "grc"; prop.text w.Lemma ]
                                if w.Gloss <> "" then Html.p [ prop.className "rv-gloss"; prop.text w.Gloss ]
                                if w.Lemma = "" && w.Gloss = "" then
                                    Html.p [
                                        prop.className "quiet"
                                        prop.text "You haven't written a meaning for this word yet. Look it up, then add it in the list below."
                                    ]
                                Html.a [
                                    prop.className "w-look"
                                    prop.href ("https://logeion.uchicago.edu/" + enc (if w.Lemma <> "" then w.Lemma else w.Word))
                                    prop.target "_blank"
                                    prop.rel "noopener"
                                    prop.text "Look up on Logeion ↗"
                                ]
                                Html.div [
                                    prop.className "rv-acts grade"
                                    prop.children [
                                        Html.button [ prop.className "btn big"; prop.text "Again  (1)"; prop.onClick (fun _ -> dispatch (Library_(GradeCard false))) ]
                                        Html.button [ prop.className "btn primary big"; prop.text "Got it  (2)"; prop.onClick (fun _ -> dispatch (Library_(GradeCard true))) ]
                                    ]
                                ]
                            ]
                        ]
                ]
            ]

let private wordsSection (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let words = model.Library.Words
    let due = LibraryData.dueWords model.Library (nowMs ())
    match model.Review with
    | Some r -> [ reviewCard model dispatch r ]
    | None ->
        if List.isEmpty words then
            [ Html.div [
                  prop.className "lib-empty"
                  prop.children [
                      Html.p [ prop.text "No words yet." ]
                      Html.p [
                          prop.className "quiet"
                          prop.text
                              "While reading, click a Greek word and press Save word. It comes here with the passage you met it in. Add its dictionary form and meaning, then review with flashcards: each card you know waits longer before it returns (1, 3, 7, 16, then 35 days), so a few minutes a day is enough."
                      ]
                  ]
              ] ]
        else
            [ Html.div [
                  prop.className "rv-bar"
                  prop.children [
                      Html.p [
                          prop.children [
                              Html.b [ prop.text (plural words.Length "word" "words") ]
                              Html.text (
                                  if List.isEmpty due then " · nothing due, well done"
                                  else " · " + plural due.Length "card" "cards" + " due for review"
                              )
                          ]
                      ]
                      Html.button [
                          prop.className ("btn" + (if List.isEmpty due then "" else " primary"))
                          prop.text (if List.isEmpty due then "Practise anyway" else "Review now")
                          prop.onClick (fun _ -> dispatch (Library_ StartReview))
                      ]
                  ]
              ]
              Html.ul [
                  prop.className "w-list"
                  prop.children [
                      for w in words |> List.sortByDescending (fun w -> w.Ts) ->
                          // keyed on the saved text too, so an edit that arrives by sync shows
                          Html.li [ prop.key (w.Id + "|" + w.Lemma + "|" + w.Gloss); prop.className "w-row"; prop.children [ WordRow model dispatch w ] ]
                  ]
              ] ]

// ---------------------------------------------------------------------------
// places: saved from the Map lens
// ---------------------------------------------------------------------------

[<ReactComponent>]
let private PlaceNote (dispatch: Msg -> unit) (p: SavedPlace) =
    let text, setText = React.useState p.Note
    Html.textarea [
        prop.className "pl-note"
        prop.rows 2
        prop.placeholder "A note on this place"
        prop.ariaLabel ("Note on " + p.Label)
        prop.value text
        prop.onChange setText
        prop.onBlur (fun _ -> if text <> p.Note then dispatch (Library_(SetPlaceNote(p.Qid, text))))
        // Enter saves (by leaving the box, which saves on blur)
        Shared.onEnterSave (fun () -> if text <> p.Note then dispatch (Library_(SetPlaceNote(p.Qid, text))))
    ]

let private placesSection (model: Model) (dispatch: Msg -> unit) : ReactElement list =
    let places = model.Library.Places
    if List.isEmpty places then
        [ Html.div [
              prop.className "lib-empty"
              prop.children [
                  Html.p [ prop.text "No places yet." ]
                  Html.p [
                      prop.className "quiet"
                      prop.text
                          "While reading a text with a translation, open the study lenses (the ◇ beside a passage) and choose Map. Press Save beside any place to keep it here, with the passages that name it, on a map of the ancient world."
                  ]
              ]
          ] ]
    else
        [ Html.div [ prop.key "map"; prop.id "libMap"; prop.className "ln-map lib-map"; prop.ariaLabel "Map of your saved places" ]
          Html.ul [
              prop.className "pl-list"
              prop.children [
                  for p in places |> List.sortBy (fun p -> p.Label) ->
                      Html.li [
                          prop.key p.Qid
                          prop.className "pl-row"
                          prop.children [
                              Html.div [
                                  prop.className "pl-head"
                                  prop.children [
                                      Html.b [ prop.text p.Label ]
                                      Html.a [
                                          prop.className "quiet"
                                          prop.href ("https://pleiades.stoa.org/places/" + p.Pleiades)
                                          prop.target "_blank"
                                          prop.rel "noopener"
                                          prop.text ("Pleiades " + p.Pleiades + " ↗")
                                      ]
                                      Html.button [
                                          prop.className "linkbtn"
                                          prop.text "Remove"
                                          prop.onClick (fun _ -> dispatch (Library_(RemovePlace p.Qid)))
                                      ]
                                  ]
                              ]
                              Html.div [
                                  prop.className "ln-refs"
                                  prop.children (
                                      [ Html.span [ prop.className "quiet"; prop.text "Named in " ] ]
                                      @ interspersed
                                          (Html.text ", ")
                                          (p.Seen
                                           |> List.truncate 12
                                           |> List.map (fun (w, r) ->
                                               let h = readerHash w r
                                               Html.a [
                                                   prop.key (w + r)
                                                   prop.className "xref"
                                                   prop.href h
                                                   prop.text (whereFrom model w r)
                                                   prop.onClick (fun e ->
                                                       e.preventDefault ()
                                                       dispatch (Navigate(h, false)))
                                               ]))
                                      @ (if p.Seen.Length > 12 then [ Html.text (sprintf " and %d more" (p.Seen.Length - 12)) ] else [])
                                  )
                              ]
                              Html.div [ prop.key p.Note; prop.children [ PlaceNote dispatch p ] ]
                          ]
                      ]
              ]
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
                            let msg =
                                if model.Account.Session.IsSome then
                                    "Remove all favourites, bookmarks, notes, words and places? You are signed in, so they will also be removed from your account and every device you sync."
                                else "Remove all favourites, bookmarks, notes, words and places from this browser?"
                            if confirmDialog msg then
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
                Shared.onEnterSave (fun () ->
                    if mode = IoImport && importText.Trim() <> "" then
                        dispatch (Library_(ImportText importText))
                        dispatch (Library_ ImportConfirmed))
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

let private tabs (model: Model) (dispatch: Msg -> unit) (current: LibTab) : ReactElement =
    let lib = model.Library
    let due = LibraryData.dueWords lib (nowMs ()) |> List.length
    let anotes = lib.AuthorNotes |> Map.filter (fun _ t -> t.Trim() <> "") |> Map.count
    let tab (t: LibTab) (label: string) (iconName: string) (n: int) (badge: string option) =
        let h = Router.toHash (LibraryRoute t)
        Html.a [
            prop.key label
            prop.className ("lib-tab" + (if t = current then " on" else ""))
            prop.href h
            if t = current then prop.custom ("aria-current", "page")
            prop.onClick (fun e ->
                e.preventDefault ()
                dispatch (Navigate(h, true)))
            prop.children [
                Shared.icon iconName
                Html.span [ prop.text label ]
                Html.span [ prop.className "lt-n"; prop.text (string n) ]
                match badge with
                | Some b -> Html.span [ prop.className "lt-due"; prop.text b ]
                | None -> Html.none
            ]
        ]
    Html.nav [
        prop.className "lib-tabs"
        prop.ariaLabel "My library"
        prop.children [
            tab LibMarks "Bookmarks" "bookmark" lib.Marks.Length None
            tab LibWords "Words" "words" lib.Words.Length (if due > 0 then Some(string due + " due") else None)
            tab LibPlaces "Places" "place" lib.Places.Length None
            tab LibFavs "Favourites" "olive" lib.Favs.Length None
            tab LibNotes "Author notes" "notes" anotes None
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) (tab: LibTab) : ReactElement =
    let lib = model.Library
    let genreOf = Shared.genreOfWork model
    let inGenre (workId: string) =
        match model.Genre with
        | Some g -> genreOf workId = g
        | None -> true
    // Chips count works: every favourite, plus every work with a saved passage.
    let genreCounts (works: string list) = works |> List.distinct |> List.countBy genreOf
    let chips (works: string list) =
        let counts = genreCounts works
        // One genre is not a choice; show chips only once there are two.
        if counts.Length > 1 || model.Genre.IsSome then [ Shared.genreChips "Genre" counts model.Genre dispatch ] else []
    let body =
        match tab with
        | LibMarks ->
            chips (lib.Marks |> List.map (fun m -> m.Work))
            @ bookmarksSection model dispatch (lib.Marks |> List.filter (fun m -> inGenre m.Work))
        | LibWords -> wordsSection model dispatch
        | LibPlaces -> placesSection model dispatch
        | LibFavs -> chips lib.Favs @ (favouritesSection model dispatch (lib.Favs |> List.filter inGenre) |> List.tail)
        | LibNotes ->
            match authorNotesSection model dispatch with
            | [] ->
                [ Html.div [
                      prop.className "lib-empty"
                      prop.children [
                          Html.p [ prop.text "No notes on authors yet." ]
                          Html.p [ prop.className "quiet"; prop.text "Every author page in the Wiki has a notes box at the foot of its right-hand column. What you write there collects here." ]
                      ]
                  ] ]
            | xs -> List.tail xs
    Html.div [
        prop.className "page library-page"
        prop.children [
            Html.h1 [ prop.className "ph"; prop.text "My library" ]
            whereSaved model dispatch
            tabs model dispatch tab
            // The list, with back-up and restore in the right rail (below it on phones).
            Html.div [
                prop.className "lib-cols"
                prop.children [
                    Html.div [ prop.className "lib-body"; prop.children body ]
                    Html.aside [ prop.className "lib-rail-io"; prop.children [ BackupSection model dispatch ] ]
                ]
            ]
        ]
    ]
