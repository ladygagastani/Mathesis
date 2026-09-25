module Views.Lens

// The study rail: one panel beside the reader that holds all five lenses, so
// none of them adds its own furniture to the reading columns. Everything shown
// here is derived from the texts already loaded, except the map (Wikidata and
// Pleiades) and manuscript images (the holding library's IIIF server), which
// are fetched only when their lens is opened.

open Feliz
open Fable.Core
open Types
open Lenses

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

let lensName (k: LensKind) =
    match k with
    | LensEchoes -> "Echoes"
    | LensWords -> "Words"
    | LensManuscript -> "Manuscript"
    | LensMeter -> "Meter"
    | LensMap -> "Map"

let allLenses = [ LensEchoes; LensWords; LensMeter; LensMap; LensManuscript ]

// ---------------------------------------------------------------------------
// shared helpers
// ---------------------------------------------------------------------------

let private urnSuffix (urn: string) = urn.Split('.') |> Array.last

let private chunkOfRef (rm: ReaderModel) (segRef: string) : string option =
    rm.Data
    |> Option.bind (fun d -> d.Chunks |> List.tryFind (fun c -> c.Segments |> Array.exists (fun s -> s.Ref = segRef)))
    |> Option.map (fun c -> c.Ref)

/// A link to a passage: in this work it moves the reader; in another work it
/// opens that work at the passage, in the edition the match was found in.
let private passageLink (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (urn: string) (segRef: string) (text: string) : ReactElement =
    let workId = Greek.workOfUrn urn
    Html.a [
        prop.className "xref"
        prop.href "#"
        prop.text text
        prop.onClick (fun e ->
            e.preventDefault ()
            if workId = rm.Work.Id then
                match chunkOfRef rm segRef with
                | Some c -> dispatch (Reader_(ShowChunk(c, Some segRef, None)))
                | None -> dispatch (ShowToast("Passage " + segRef + " isn't in this edition"))
            else
                dispatch (Navigate(Router.toHash (ReaderRoute(workId, urnSuffix urn, "", None, Some segRef)), false)))
    ]

let private workLabel (model: Model) (urn: string) : string =
    let wid = Greek.workOfUrn urn
    let title = Catalog.workTitle model.Catalog wid |> Option.defaultValue wid
    match Catalog.authorOf model.Catalog wid with
    | Some a -> a.Name + ", " + title
    | None -> title

let private isGreekText (model: Model) (urn: string) : bool =
    model.Catalog.WorkById.TryFind(Greek.workOfUrn urn)
    |> Option.bind (fun w -> w.Texts |> List.tryFind (fun t -> t.Urn = urn))
    |> Option.map (fun t -> t.Lang = "grc")
    |> Option.defaultValue false

/// The Greek texts loaded this session other than the one being read.
let private otherIndexes (model: Model) (rm: ReaderModel) : Corpus.TextIndex list =
    model.TextCache
    |> Map.toList
    |> List.filter (fun (urn, _) -> urn <> rm.Grc.Urn && isGreekText model urn)
    |> List.map (fun (urn, segs) -> Corpus.index urn segs)

let private currentIndex (_model: Model) (rm: ReaderModel) : Corpus.TextIndex option =
    rm.Data
    |> Option.map (fun d -> Corpus.indexAligned rm.Grc.Urn (rm.Eng |> Option.map (fun e -> e.Urn) |> Option.defaultValue "none") d.Segments)

let private section (title: string) (children: ReactElement list) =
    Html.section [ prop.className "ln-sec"; prop.children (Html.h4 [ prop.text title ] :: children) ]

let private quiet (text: string) = Html.p [ prop.className "ln-quiet"; prop.text text ]

let private findSeg (rm: ReaderModel) (segRef: string) : Segment option =
    rm.Data |> Option.bind (fun d -> d.Segments |> Array.tryFind (fun s -> s.Ref = segRef))

let private wordsWithHits (words: string array) (hit: bool array) : ReactElement list =
    words
    |> Array.mapi (fun i w ->
        [ if i > 0 then Html.text " "
          if hit.[i] then Html.mark [ prop.key (string i); prop.text w ] else Html.text w ])
    |> List.concat

// ---------------------------------------------------------------------------
// Echoes
// ---------------------------------------------------------------------------

let private echoes (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (segRef: string) : ReactElement list =
    let mark = LibraryData.markFor model.Library rm.Work.Id segRef
    let links = mark |> Option.map (fun m -> m.Links) |> Option.defaultValue []
    let backs = LibraryData.backlinks model.Library rm.Work.Id segRef
    let others = otherIndexes model rm
    let here, there =
        match currentIndex model rm with
        | Some ix -> Corpus.echoes ix segRef others
        | None -> [], []
    let echoItem (e: Corpus.Echo) (label: string) =
        Html.li [
            prop.key (e.Urn + e.Ref)
            prop.className "ln-echo"
            prop.children [
                Html.div [ prop.className "ln-echo-h"; prop.children [ passageLink model rm dispatch e.Urn e.Ref label ] ]
                Html.div [ prop.className "ln-grc"; prop.lang "grc"; prop.children (Html.text "…" :: wordsWithHits e.Words e.Hit @ [ Html.text "…" ]) ]
                Html.div [ prop.className "ln-shared"; prop.text ("shares: " + (e.Shared |> List.truncate 3 |> String.concat " · ")) ]
            ]
        ]
    [ section "Your links" [
          if links.IsEmpty && backs.IsEmpty then
              quiet "You haven't linked this passage to any other yet."
          else
              Html.ul [
                  prop.className "ln-list"
                  prop.children (
                      (links |> List.map (fun l ->
                          Html.li [ prop.key ("o" + l.Work + l.Ref); prop.children [ Html.text "→ "; Shared.linkChip model.Catalog dispatch None l ] ]))
                      @ (backs |> List.map (fun b ->
                          let title = Catalog.workTitle model.Catalog b.Work |> Option.defaultValue b.Work
                          Html.li [
                              prop.key ("b" + b.Work + b.Ref)
                              prop.children [
                                  Html.text "← "
                                  Html.a [
                                      prop.className "xref"; prop.href "#"
                                      prop.text (title + " " + (if b.Label <> "" then b.Label else b.Ref))
                                      prop.onClick (fun e ->
                                          e.preventDefault ()
                                          dispatch (Navigate(Router.toHash (ReaderRoute(b.Work, "", "", None, Some b.Ref)), false)))
                                  ]
                              ]
                          ]))
                  )
              ]
          Html.button [
              prop.className "btn small"
              prop.text "Link this passage to another…"
              prop.onClick (fun _ ->
                  let snippet = findSeg rm segRef |> Option.map (fun s -> Greek.blockText s.Grc) |> Option.defaultValue ""
                  let snippet = if snippet.Length > 140 then snippet.Substring(0, 140) else snippet
                  dispatch (Library_(OpenMarkEditor(rm.Work.Id, segRef, segRef, snippet))))
          ]
      ]
      section "Repeated phrases in this work" [
          if here.IsEmpty then quiet "No three-word phrase here recurs elsewhere in this text."
          else Html.ol [ prop.className "ln-list"; prop.children [ for e in here -> echoItem e e.Ref ] ]
      ]
      section "In other texts you have opened" [
          if others.IsEmpty then
              quiet "Open another Greek text — Homer, Hesiod, a tragedian — and its shared phrases with this passage will be listed here. Echoes are searched across every Greek text you have opened this session."
          elif there.IsEmpty then
              quiet ("None of the " + string others.Length + " other Greek text" + (if others.Length = 1 then "" else "s") + " you have open shares a three-word phrase with this passage.")
          else Html.ol [ prop.className "ln-list"; prop.children [ for e in there -> echoItem e (workLabel model e.Urn + " " + e.Ref) ] ]
      ]
      Html.p [
          prop.className "ln-note"
          prop.text "Matches are three-word phrases with at least two content words, compared without accents or breathings. Formulae that recur more than sixty times in a text are ranked low, so rarer, more deliberate echoes come first."
      ] ]

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------

let private bars (rows: (string * float * string) list) (onPick: (string -> unit) option) : ReactElement =
    let top = rows |> List.map (fun (_, v, _) -> v) |> List.fold max 0.0
    Html.div [
        prop.className "ln-bars"
        prop.children [
            for (label, v, note) in rows ->
                Html.div [
                    prop.key label
                    prop.className ("ln-bar" + (if onPick.IsSome && v > 0.0 then " pick" else ""))
                    prop.title note
                    prop.onClick (fun _ -> match onPick with Some f when v > 0.0 -> f label | _ -> ())
                    prop.children [
                        Html.span [ prop.className "ln-bar-l"; prop.text label ]
                        Html.span [
                            prop.className "ln-bar-t"
                            prop.children [ Html.span [ prop.className "ln-bar-f"; prop.style [ style.width (length.percent (if top > 0.0 then 100.0 * v / top else 0.0)) ] ] ]
                        ]
                        Html.span [ prop.className "ln-bar-v"; prop.text note ]
                    ]
                ]
        ]
    ]

let private words (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (lens: LensState) : ReactElement list =
    match lens.Word with
    | None ->
        [ quiet "Click any Greek word in the text, then choose “Trace this word” to see where it — or any word sharing its stem — occurs: part by part in this work, and era by era across the texts you have opened." ]
    | Some word ->
        let stem = lens.Stem
        let ixOpt = currentIndex model rm
        let hits = ixOpt |> Option.map (fun ix -> Corpus.occurrences ix stem) |> Option.defaultValue []
        let chunkRows =
            match rm.Data with
            | Some d when d.Chunks.Length > 1 ->
                let refToChunk = d.Chunks |> List.collect (fun c -> c.Segments |> Array.toList |> List.map (fun s -> s.Ref, c.Ref)) |> Map.ofList
                let counts = hits |> List.countBy (fun h -> refToChunk.TryFind h.Ref |> Option.defaultValue "") |> Map.ofList
                d.Chunks |> List.map (fun c -> let n = counts.TryFind c.Ref |> Option.defaultValue 0 in c.Ref, float n, string n)
            | _ -> []
        // Era by era across every Greek text in memory, per 10,000 words so a
        // long text doesn't outweigh a short one just by being long.
        let eraRows =
            let texts = (ixOpt |> Option.toList) @ otherIndexes model rm
            let eraOf (ix: Corpus.TextIndex) =
                let wid = Greek.workOfUrn ix.Urn
                model.Catalog.WorkById.TryFind wid
                |> Option.bind (fun w -> model.Meta.Authors.TryFind w.AuthorId)
                |> Option.bind (fun a -> a.Era)
                |> Option.defaultValue "undated"
            let perEra =
                texts
                |> List.map (fun ix -> eraOf ix, ix.WordCount, (Corpus.occurrences ix stem).Length, workLabel model ix.Urn)
                |> List.groupBy (fun (e, _, _, _) -> e)
                |> Map.ofList
            model.Meta.Eras
            |> List.choose (fun era ->
                perEra.TryFind era.Id
                |> Option.map (fun xs ->
                    let words = xs |> List.sumBy (fun (_, w, _, _) -> w)
                    let n = xs |> List.sumBy (fun (_, _, n, _) -> n)
                    let rate = if words > 0 then 10000.0 * float n / float words else 0.0
                    era.Name, rate, sprintf "%.1f per 10k words (%d in %s)" rate n (xs |> List.map (fun (_, _, _, l) -> l) |> String.concat "; ")))
        let forms = hits |> List.countBy (fun h -> h.Form.Normalize(System.Text.NormalizationForm.FormC)) |> List.sortByDescending snd |> List.truncate 14
        [ Html.div [
              prop.className "ln-word"
              prop.children [
                  Html.span [ prop.className "ln-hw"; prop.lang "grc"; prop.text word ]
                  Html.label [
                      prop.className "ln-stem"
                      prop.children [
                          Html.text "matching words beginning "
                          Html.input [
                              prop.lang "grc"
                              prop.value stem
                              prop.custom ("spellCheck", false)
                              prop.ariaLabel "Stem to search for"
                              prop.onChange (fun (v: string) -> dispatch (Reader_(SetLensStem v)))
                          ]
                          Html.text "-"
                      ]
                  ]
              ]
          ]
          quiet "Accents and breathings are ignored. Shorten the stem to catch more forms; lengthen it if unrelated words creep in."
          section ("In this work — " + string hits.Length + " occurrence" + (if hits.Length = 1 then "" else "s")) [
              if not chunkRows.IsEmpty then
                  bars chunkRows (Some(fun c -> dispatch (Reader_(ShowChunk(c, None, None)))))
              if not forms.IsEmpty then
                  Html.div [
                      prop.className "ln-forms"
                      prop.lang "grc"
                      prop.children [ for (f, n) in forms -> Html.span [ prop.key f; prop.text (f + " " + string n) ] ]
                  ]
              match ixOpt with
              | Some ix when not hits.IsEmpty ->
                  Html.ol [
                      prop.className "ln-kwic"
                      prop.children [
                          for h in hits |> List.truncate 40 ->
                              let ws, at = Corpus.context ix h
                              Html.li [
                                  prop.key (h.Ref + ":" + string h.Pos)
                                  prop.children [
                                      passageLink model rm dispatch ix.Urn h.Ref h.Ref
                                      Html.span [
                                          prop.className "ln-grc"
                                          prop.lang "grc"
                                          prop.children (wordsWithHits ws (Array.init ws.Length (fun i -> i = at)))
                                      ]
                                  ]
                              ]
                      ]
                  ]
              | _ -> Html.none
          ]
          section "Across eras" [
              if eraRows.Length <= 1 then
                  quiet "Only one era is represented among the texts you have open. Open works from other periods — say Homer, Plato and Plutarch — and this becomes a timeline of the word's use."
              bars eraRows None
          ]
          Html.div [
              prop.className "ln-ext"
              prop.children [
                  Html.a [ prop.href ("https://logeion.uchicago.edu/" + enc word); prop.target "_blank"; prop.rel "noopener"; prop.text "LSJ and other lexica on Logeion ↗" ]
                  Html.a [ prop.href ("https://www.perseus.tufts.edu/hopper/morph?l=" + enc word + "&la=greek"); prop.target "_blank"; prop.rel "noopener"; prop.text "Perseus word study ↗" ]
              ]
          ] ]

// ---------------------------------------------------------------------------
// Meter
// ---------------------------------------------------------------------------

let private meterCache = System.Collections.Generic.Dictionary<string, string list>()

/// Metres of the chunk on screen, judged once per edition and chunk.
let chunkMeters (rm: ReaderModel) : string list =
    match rm.Data with
    | Some d ->
        let chunk = d.Chunks |> List.tryFind (fun c -> Some c.Ref = rm.Chunk) |> Option.defaultValue d.Chunks.Head
        let key = rm.Grc.Urn + "|" + chunk.Ref
        match meterCache.TryGetValue key with
        | true, v -> v
        | _ ->
            let lines =
                chunk.Segments
                |> Array.truncate 60
                |> Array.toList
                |> List.collect (fun s -> s.Grc |> List.collect (function Verse(_, ls) -> ls |> List.map snd | _ -> []))
            let v = Prosody.detect lines
            meterCache.[key] <- v
            v
    | None -> []

let private meter (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (segRef: string) : ReactElement list =
    let meters = chunkMeters rm
    let lines =
        findSeg rm segRef
        |> Option.map (fun s -> s.Grc |> List.collect (function Verse(_, ls) -> ls | _ -> []))
        |> Option.defaultValue []
    let toggle =
        Html.label [
            prop.className "ln-check"
            prop.children [
                Html.input [ prop.type'.checkbox; prop.isChecked rm.MeterOn; prop.onChange (fun (_: bool) -> dispatch (Reader_ ToggleMeter)) ]
                Html.text " Show scansion over every line of the text"
            ]
        ]
    if lines.IsEmpty then
        [ quiet "This passage is prose. Choose a passage of verse — epic, elegy or the spoken parts of drama — to see it scanned."; toggle ]
    elif meters.IsEmpty then
        [ quiet "These lines don't fit dactylic hexameter, elegiac couplets or iambic trimeter, so they are probably lyric. Lyric metres need a scholar's colometry to scan and are not attempted here."; toggle ]
    else
        [ Html.p [ prop.className "ln-meter-name"; prop.text ((meters |> String.concat " and ") |> fun s -> s.Substring(0, 1).ToUpper() + s.Substring 1) ]
          toggle
          Html.ol [
              prop.className "ln-scan"
              prop.children [
                  for (li, (n, text)) in List.indexed lines ->
                      let scan = Prosody.scanLine meters (Greek.words text)
                      let playing = match rm.Playhead with Some(r, l, s) when r = segRef && l = li -> Some s | _ -> None
                      Html.li [
                          prop.key (string li)
                          prop.children [
                              Html.div [
                                  prop.className "ln-scan-h"
                                  prop.children [
                                      Html.span [ prop.className "ln-scan-n"; prop.text (n |> Option.defaultValue "") ]
                                      match scan.Scan with
                                      | Some s ->
                                          Html.span [
                                              prop.className "ln-scan-m"
                                              prop.text (
                                                  s.Meter
                                                  + (match s.Caesura with Some(_, c) -> " · " + c | None -> "")
                                                  + (if s.Bucolic.IsSome then " · bucolic diaeresis" else "")
                                                  + (if scan.Syls |> Array.exists (fun y -> y.Merged) then " · synizesis" else "")
                                              )
                                          ]
                                          Html.button [
                                              prop.className "ln-play"
                                              prop.title (if playing.IsSome then "Stop" else "Play the rhythm and pitch of this line")
                                              prop.ariaLabel (if playing.IsSome then "Stop" else "Play line")
                                              prop.text (if playing.IsSome then "■" else "▶")
                                              prop.onClick (fun _ -> dispatch (Reader_(if playing.IsSome then StopPlayback else PlayLine(segRef, li))))
                                          ]
                                      | None -> Html.span [ prop.className "ln-scan-m bad"; prop.text "does not scan as read — perhaps a textual problem, or a licence not modelled here" ]
                                  ]
                              ]
                              Html.div [
                                  prop.className "ln-syls"
                                  prop.lang "grc"
                                  prop.children [
                                      for (k, syl) in Array.indexed scan.Syls ->
                                          let long = scan.Scan |> Option.map (fun s -> s.Long.[k])
                                          let footEnd = scan.Scan |> Option.map (fun s -> s.FootEnd.[k]) |> Option.defaultValue false
                                          let caes = scan.Scan |> Option.bind (fun s -> s.Caesura) |> Option.map fst = Some k
                                          Html.span [
                                              prop.key (string k)
                                              prop.classes [
                                                  "ln-syl"
                                                  match long with Some true -> "long" | Some false -> "short" | None -> "open"
                                                  if footEnd then "fe"
                                                  if caes then "cz"
                                                  if syl.WordEnd then "we"
                                                  if playing = Some k then "now"
                                              ]
                                              prop.title (match long with Some l -> Prosody.reason syl l | None -> "")
                                              prop.children [
                                                  Html.span [ prop.className "q"; prop.text (match long with Some l -> Prosody.mark l | None -> "·") ]
                                                  Html.span [ prop.className "t"; prop.text syl.Text ]
                                              ]
                                          ]
                                  ]
                              ]
                          ]
                      ]
              ]
          ]
          Html.p [
              prop.className "ln-note"
              prop.text "Scanned from the letters alone: η, ω, diphthongs and circumflexed vowels are long by nature, a vowel before two consonants long by position, and α, ι, υ are left for the metre to decide. Playback gives a long twice the time of a short, and lifts the acute a fifth above the level pitch, as Dionysius of Halicarnassus describes; the circumflex rises and falls within its syllable."
          ] ]

// ---------------------------------------------------------------------------
// Map
// ---------------------------------------------------------------------------

let private map (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) : ReactElement list =
    let chunkLabel = rm.Chunk |> Option.filter (fun c -> c <> "all") |> Option.map (fun c -> " " + c) |> Option.defaultValue ""
    match rm.Places with
    | PlacesFailed(_, "no-translation") ->
        [ quiet "Places are found by reading the translation, where names are easy to pick out. Choose a translation above the text to map this part." ]
    | PlacesFailed(_, err) ->
        [ quiet ("Couldn't reach Wikidata to look the places up (" + err + "). The map needs a network connection.")
          Html.button [ prop.className "btn small"; prop.text "Try again"; prop.onClick (fun _ -> dispatch (Reader_(OpenLens(LensMap, None)))) ] ]
    | PlacesIdle
    | PlacesLoading _ -> [ quiet ("Looking up the places named in" + (if chunkLabel = "" then " this text" else chunkLabel) + "…") ]
    | PlacesReady(_, hits) ->
        let here = rm.Lens |> Option.bind (fun l -> l.Seg) |> Option.orElse rm.ScrollRef
        [ Html.div [ prop.key "map"; prop.id "lensMap"; prop.className "ln-map" ]
          if hits.IsEmpty then
              quiet "No ancient place named in this part of the translation could be identified."
          else
              section (string hits.Length + " place" + (if hits.Length = 1 then "" else "s") + " in order of first mention") [
                  Html.ol [
                      prop.className "ln-places"
                      prop.children [
                          for h in hits ->
                              let isHere = match here with Some r -> List.contains r h.Refs | None -> false
                              let saved = LibraryData.placeFor model.Library h.Qid
                              let savedHere =
                                  saved |> Option.exists (fun p -> h.Refs |> List.forall (fun r -> List.contains (rm.Work.Id, r) p.Seen))
                              Html.li [
                                  prop.key h.Pleiades
                                  prop.className (if isHere then "here" else "")
                                  prop.children [
                                      Html.button [
                                          prop.className ("ln-save" + (if savedHere then " on" else ""))
                                          prop.title (if savedHere then "Saved in My library › Places" else "Save this place, with these passages, to My library")
                                          prop.ariaLabel ((if savedHere then "Saved: " else "Save ") + h.Label)
                                          prop.custom ("aria-pressed", savedHere)
                                          prop.disabled savedHere
                                          prop.onClick (fun _ -> dispatch (Library_(SavePlace h)))
                                          prop.children [ Shared.icon "place"; Html.span [ prop.text (if savedHere then "Saved" else "Save") ] ]
                                      ]
                                      Html.b [ prop.text h.Label ]
                                      if h.Label <> h.Name then Html.span [ prop.className "ln-quiet"; prop.text (" “" + h.Name + "”") ]
                                      Html.div [
                                          prop.className "ln-refs"
                                          prop.children (
                                              (h.Refs
                                               |> List.truncate 10
                                               |> List.map (fun r ->
                                                   Html.a [
                                                       prop.key r
                                                       prop.href "#"
                                                       prop.className "xref"
                                                       prop.text r
                                                       prop.onClick (fun e ->
                                                           e.preventDefault ()
                                                           dispatch (Reader_(PlacePicked r)))
                                                   ]))
                                              @ (if h.Refs.Length > 10 then [ Html.text (" +" + string (h.Refs.Length - 10)) ] else [])
                                          )
                                      ]
                                  ]
                              ]
                      ]
                  ]
              ]
          Html.p [
              prop.className "ln-note"
              prop.children [
                  Html.text "Names are taken from the translation and matched to ancient places through "
                  Html.a [ prop.href "https://www.wikidata.org/"; prop.target "_blank"; prop.rel "noopener"; prop.text "Wikidata" ]
                  Html.text ", keeping only those with a "
                  Html.a [ prop.href "https://pleiades.stoa.org/"; prop.target "_blank"; prop.rel "noopener"; prop.text "Pleiades" ]
                  Html.text " gazetteer entry. A name shared by several places goes to the one nearest the others mentioned. The dashed line joins places in the order the text first names them."
              ]
          ] ]

// ---------------------------------------------------------------------------
// Manuscript
// ---------------------------------------------------------------------------

let private manuscript (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) (lens: LensState) (segRef: string) : ReactElement list =
    let text = findSeg rm segRef |> Option.map (fun s -> Greek.blockText s.Grc) |> Option.defaultValue ""
    let shown, cls, note =
        match lens.MsView with
        | Diplomatic ->
            Paleography.diplomatic text, "ln-uncial",
            "As a scribe of late antiquity would have written it: capitals only, no accents or breathings, no spaces between words, the lunate sigma (Ϲ), iota written in the line, and only the high point for punctuation."
        | Minuscule ->
            Paleography.minuscule text, "ln-minus",
            "As a 9th–10th-century Byzantine scribe would have set it: lower case, with breathings and accents, but iota still written beside the vowel rather than beneath it."
        | Normalized -> text, "ln-edited", "The modern edited text, with editorial punctuation and iota subscript."
    let viewBtn (v: MsView) (label: string) =
        Html.button [
            prop.className (if lens.MsView = v then "on" else "")
            prop.custom ("aria-pressed", (lens.MsView = v))
            prop.text label
            prop.onClick (fun _ -> dispatch (Reader_(SetMsView v)))
        ]
    let known = Paleography.witnesses.TryFind rm.Work.AuthorId |> Option.defaultValue []
    let mutable urlInput: Browser.Types.HTMLInputElement = Unchecked.defaultof<_>
    [ section "The passage, before print" [
          Html.div [ prop.className "segbtn ln-seg"; prop.children [ viewBtn Diplomatic "Uncial"; viewBtn Minuscule "Minuscule"; viewBtn Normalized "Edited" ] ]
          Html.div [ prop.className ("ln-ms " + cls); prop.lang "grc"; prop.text shown ]
          Html.p [ prop.className "ln-note"; prop.text note ]
      ]
      section "Manuscript images" [
          if not known.IsEmpty then
              Html.ul [
                  prop.className "ln-list"
                  prop.children [
                      for w in known ->
                          Html.li [
                              prop.key w.Manifest
                              prop.children [
                                  Html.a [
                                      prop.className "xref"; prop.href "#"
                                      prop.text w.Shelfmark
                                      prop.onClick (fun e -> e.preventDefault (); dispatch (Reader_(LoadManifest w.Manifest)))
                                  ]
                                  Html.span [ prop.className "ln-quiet"; prop.text (" · " + w.Date) ]
                                  Html.div [ prop.className "ln-quiet"; prop.text w.Note ]
                              ]
                          ]
                  ]
              ]
          Html.form [
              prop.className "ln-url"
              prop.onSubmit (fun e ->
                  e.preventDefault ()
                  if not (isNull urlInput) then dispatch (Reader_(LoadManifest urlInput.value)))
              prop.children [
                  Html.input [
                      prop.type'.url
                      prop.placeholder "IIIF manifest URL"
                      prop.ariaLabel "IIIF manifest URL"
                      prop.ref (fun el -> if not (isNull el) then urlInput <- el :?> Browser.Types.HTMLInputElement)
                  ]
                  Html.button [ prop.className "btn small"; prop.type'.submit; prop.text "Open" ]
              ]
          ]
          match rm.Manifest with
          | ManifestNone ->
              quiet "No digitised manuscript of this author is listed here yet. Libraries such as the Vatican (digi.vatlib.it), Heidelberg and the Bodleian publish IIIF manifests for their Greek codices — paste one above to read it beside the text."
          | ManifestLoading _ -> quiet "Opening the manuscript…"
          | ManifestFailed(url, err) -> quiet ("Couldn't open " + url + ": " + err + ". The library's server has to allow this page to read it.")
          | ManifestReady(url, title, canvases, i) ->
              Html.div [
                  prop.className "ln-iiif"
                  prop.children [
                      Html.div [ prop.className "ln-iiif-t"; prop.text title ]
                      Html.div [ prop.key ("osd"); prop.id "lensImage"; prop.className "ln-image" ]
                      Html.div [
                          prop.className "ln-iiif-nav"
                          prop.children [
                              Html.button [ prop.className "btn small"; prop.disabled ((i = 0)); prop.text "← Prev"; prop.onClick (fun _ -> dispatch (Reader_(ManifestPage(i - 1)))) ]
                              Html.select [
                                  prop.ariaLabel "Go to image"
                                  prop.value (string i)
                                  prop.onChange (fun (v: string) -> dispatch (Reader_(ManifestPage(int v))))
                                  prop.children [
                                      for (k, c) in List.indexed canvases ->
                                          Html.option [ prop.key (string k); prop.value (string k); prop.text (if c.Label <> "" then c.Label else string (k + 1)) ]
                                  ]
                              ]
                              Html.button [ prop.className "btn small"; prop.disabled (i >= canvases.Length - 1); prop.text "Next →"; prop.onClick (fun _ -> dispatch (Reader_(ManifestPage(i + 1)))) ]
                          ]
                      ]
                      Html.a [ prop.className "ln-quiet"; prop.href url; prop.target "_blank"; prop.rel "noopener"; prop.text "IIIF manifest ↗" ]
                  ]
              ]
      ] ]

// ---------------------------------------------------------------------------
// the rail
// ---------------------------------------------------------------------------

let render (model: Model) (rm: ReaderModel) (dispatch: Msg -> unit) : ReactElement =
    match rm.Lens with
    | None -> Html.none
    | Some lens ->
        let segRef = lens.Seg |> Option.defaultValue ""
        let segLabel = if segRef = "" then "" else " · " + segRef
        Html.aside [
            prop.className "lens-rail"
            prop.ariaLabel "Study lenses"
            prop.children [
                Html.div [
                    prop.className "ln-bar-top"
                    prop.children [
                        Html.div [
                            prop.className "ln-tabs"
                            prop.role "tablist"
                            prop.children [
                                for k in allLenses ->
                                    Html.button [
                                        prop.key (lensName k)
                                        prop.role "tab"
                                        prop.ariaSelected ((k = lens.Kind))
                                        prop.className (if k = lens.Kind then "on" else "")
                                        prop.text (lensName k)
                                        prop.onClick (fun _ -> dispatch (Reader_(OpenLens(k, lens.Seg))))
                                    ]
                            ]
                        ]
                        Html.button [
                            prop.className "panel-close"
                            prop.ariaLabel "Close study lenses"
                            prop.title "Close"
                            prop.text "×"
                            prop.onClick (fun _ -> dispatch (Reader_ CloseLens))
                        ]
                    ]
                ]
                Html.div [
                    prop.className "ln-where"
                    prop.text (
                        match lens.Kind with
                        | LensMap -> rm.Work.Title + (rm.Chunk |> Option.filter (fun c -> c <> "all") |> Option.map (fun c -> " · " + c) |> Option.defaultValue "")
                        | LensWords -> rm.Work.Title
                        | _ -> rm.Work.Title + segLabel
                    )
                ]
                Html.div [
                    prop.className "ln-body"
                    prop.children (
                        match lens.Kind with
                        | LensEchoes when segRef <> "" -> echoes model rm dispatch segRef
                        | LensWords -> words model rm dispatch lens
                        | LensMeter when segRef <> "" -> meter model rm dispatch segRef
                        | LensMap -> map model rm dispatch
                        | LensManuscript when segRef <> "" -> manuscript model rm dispatch lens segRef
                        | _ -> [ quiet "Choose a passage with its ◇ button to study it here." ]
                    )
                ]
                // Study turns into conversation: take the passage to the forum.
                if segRef <> "" && lens.Kind <> LensMap && lens.Kind <> LensWords then
                    Html.div [
                        prop.className "ln-foot"
                        prop.children [
                            Html.button [
                                prop.className "btn small"
                                prop.onClick (fun _ -> dispatch (Forum_(StartThread("passages", rm.Work.Id, segRef))))
                                prop.children [ Shared.icon "forum"; Html.text (" Discuss " + segRef + " in the forum") ]
                            ]
                        ]
                    ]
            ]
        ]
