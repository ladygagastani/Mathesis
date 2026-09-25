module LibraryData

open System.Text.RegularExpressions
open Fable.Core
open Thoth.Json
open Types

[<Emit("Math.random().toString(36).slice(2,9) + Date.now().toString(36)")>]
let private newId (): string = jsNative

[<Emit("Date.now()")>]
let private now (): float = jsNative

// ---------------------------------------------------------------------------
// change stamps — every change to an item records when it happened, under the
// item's key, so two copies of a library (two devices) can be merged item by
// item: whichever side changed an item last wins, and a deletion is a change.
// ---------------------------------------------------------------------------

let favKey (workId: string) = "fav:" + workId
let markKey (work: string) (ref_: string) = "mark:" + work + "|" + ref_
let anoteKey (authorId: string) = "anote:" + authorId
let wordKey (id: string) = "word:" + id
let placeKey (qid: string) = "place:" + qid

let private stamp (key: string) (lib: Library) : Library =
    { lib with Stamps = Map.add key (now ()) lib.Stamps }

// ---------------------------------------------------------------------------
// favourites
// ---------------------------------------------------------------------------

let isFav (lib: Library) (workId: string) : bool = List.contains workId lib.Favs

let toggleFav (lib: Library) (workId: string) : Library =
    if isFav lib workId then
        { lib with Favs = lib.Favs |> List.filter (fun x -> x <> workId) }
    else
        { lib with Favs = lib.Favs @ [ workId ] }
    |> stamp (favKey workId)

// ---------------------------------------------------------------------------
// marks (bookmarks)
// ---------------------------------------------------------------------------

let markFor (lib: Library) (work: string) (ref_: string) : Mark option =
    lib.Marks |> List.tryFind (fun m -> m.Work = work && m.Ref = ref_)

/// Adds a bookmark if one doesn't already exist for this passage (mirrors
/// `addMark`); returns the (possibly pre-existing) mark alongside the library.
let addMark (lib: Library) (work: string) (ref_: string) (label: string) (snippet: string) (now: float) : Library * Mark =
    match markFor lib work ref_ with
    | Some m -> lib, m
    | None ->
        let m =
            { Id = newId ()
              Work = work
              Ref = ref_
              Label = label
              Snippet = snippet
              Note = ""
              Links = []
              Ts = now }
        stamp (markKey work ref_) { lib with Marks = lib.Marks @ [ m ] }, m

let removeMark (lib: Library) (work: string) (ref_: string) : Library =
    { lib with Marks = lib.Marks |> List.filter (fun m -> not (m.Work = work && m.Ref = ref_)) }
    |> stamp (markKey work ref_)

let private updateMark (lib: Library) (work: string) (ref_: string) (f: Mark -> Mark) : Library =
    { lib with
        Marks = lib.Marks |> List.map (fun m -> if m.Work = work && m.Ref = ref_ then f m else m) }
    |> stamp (markKey work ref_)

let setMarkNote (lib: Library) (work: string) (ref_: string) (note: string) : Library =
    updateMark lib work ref_ (fun m -> { m with Note = note })

let addMarkLink (lib: Library) (work: string) (ref_: string) (link: MarkLink) : Library =
    updateMark lib work ref_ (fun m ->
        if m.Links |> List.exists (fun l -> l.Work = link.Work && l.Ref = link.Ref) then
            m
        else
            { m with Links = m.Links @ [ link ] })

let removeMarkLink (lib: Library) (work: string) (ref_: string) (linkWork: string) (linkRef: string) : Library =
    updateMark lib work ref_ (fun m ->
        { m with Links = m.Links |> List.filter (fun l -> not (l.Work = linkWork && l.Ref = linkRef)) })

// ---------------------------------------------------------------------------
// cross-references — `[[tlg0012.tlg001]]`, `[[tlg0012.tlg001:1.33]]`,
// `[[tlg0012.tlg001:1.33|a label]]`, `[[tlg0012.tlg001|a label]]`
// ---------------------------------------------------------------------------

let private linkRe = Regex(@"\[\[([a-z]{3}\d{4}\.[a-z]{3}\d{3})(?::([^\]|]+))?(?:\|([^\]]+))?\]\]")

let linksIn (text: string) : MarkLink list =
    linkRe.Matches text
    |> Seq.map (fun m ->
        { Work = m.Groups.[1].Value
          Ref = if m.Groups.[2].Success then m.Groups.[2].Value else ""
          Label = if m.Groups.[3].Success then Some m.Groups.[3].Value else None })
    |> List.ofSeq

/// A mark's explicit links plus any inline `[[...]]` cross-references in its note.
let linksOf (m: Mark) : MarkLink list = m.Links @ linksIn m.Note

/// A run of plain text, or a parsed cross-reference, in the order they appear —
/// what `renderNote` needs to interleave text and `<a class="xref">` links
/// without resorting to raw HTML.
type NoteToken =
    | NoteText of string
    | NoteLink of MarkLink

let tokenize (text: string) : NoteToken list =
    let matches = linkRe.Matches text
    let tokens = ResizeArray<NoteToken>()
    let mutable pos = 0
    for m in matches do
        if m.Index > pos then tokens.Add(NoteText(text.Substring(pos, m.Index - pos)))
        tokens.Add(
            NoteLink
                { Work = m.Groups.[1].Value
                  Ref = if m.Groups.[2].Success then m.Groups.[2].Value else ""
                  Label = if m.Groups.[3].Success then Some m.Groups.[3].Value else None }
        )
        pos <- m.Index + m.Length
    if pos < text.Length then tokens.Add(NoteText(text.Substring pos))
    List.ofSeq tokens

/// Marks (anywhere in the library) that reference this passage, explicitly or inline.
let backlinks (lib: Library) (work: string) (ref_: string) : Mark list =
    lib.Marks |> List.filter (fun m -> linksOf m |> List.exists (fun l -> l.Work = work && l.Ref = ref_))

// ---------------------------------------------------------------------------
// author notes
// ---------------------------------------------------------------------------

let setAuthorNote (lib: Library) (authorId: string) (text: string) : Library =
    { lib with AuthorNotes = Map.add authorId text lib.AuthorNotes } |> stamp (anoteKey authorId)

// ---------------------------------------------------------------------------
// #tags — any #word in a bookmark's note tags it (#simile, #Achilles, #exam)
// ---------------------------------------------------------------------------

// explicit ranges rather than \p{L}: Fable's regexes are JS ones, without the u flag
let private tagRe = Regex(@"(?:^|[\s(\[,;])#([A-Za-z0-9_\-À-ɏͰ-Ͽἀ-῿]{2,40})")

let tagsOf (note: string) : string list =
    tagRe.Matches note |> Seq.map (fun m -> m.Groups.[1].Value.ToLowerInvariant()) |> Seq.distinct |> List.ofSeq

/// Every tag in the library with how many bookmarks carry it, commonest first.
let allTags (lib: Library) : (string * int) list =
    lib.Marks
    |> List.collect (fun m -> tagsOf m.Note)
    |> List.countBy id
    |> List.sortBy (fun (t, n) -> -n, t)

// ---------------------------------------------------------------------------
// words — saved from the word popover, reviewed as flashcards on a Leitner
// schedule: a card you know moves up a box and waits longer; one you miss
// goes back to box 1.
// ---------------------------------------------------------------------------

let private dayMs = 86400000.0

/// Days before a card in each box is due again (box 1 = tomorrow).
let boxDays = [| 0.0; 1.0; 3.0; 7.0; 16.0; 35.0 |]

let wordFor (lib: Library) (word: string) : WordCard option =
    lib.Words |> List.tryFind (fun w -> w.Word = word)

let addWord (lib: Library) (word: string) (work: string) (ref_: string) (context: string) : Library * bool =
    match wordFor lib word with
    | Some _ -> lib, false
    | None ->
        let t = now ()
        let card: WordCard =
            { Id = newId (); Word = word; Lemma = ""; Gloss = ""; Work = work; Ref = ref_
              Context = context; Ts = t; Box = 0; Due = t }
        stamp (wordKey card.Id) { lib with Words = lib.Words @ [ card ] }, true

let private updateWord (lib: Library) (id: string) (f: WordCard -> WordCard) : Library =
    { lib with Words = lib.Words |> List.map (fun w -> if w.Id = id then f w else w) } |> stamp (wordKey id)

let editWord (lib: Library) (id: string) (lemma: string) (gloss: string) : Library =
    updateWord lib id (fun w -> { w with Lemma = lemma; Gloss = gloss })

let removeWord (lib: Library) (id: string) : Library =
    { lib with Words = lib.Words |> List.filter (fun w -> w.Id <> id) } |> stamp (wordKey id)

/// `relearned`: known now, but missed earlier in the same review, so it
/// starts again from box 1 rather than climbing.
let gradeWord (lib: Library) (id: string) (knew: bool) (relearned: bool) : Library =
    updateWord lib id (fun w ->
        let box = if knew && not relearned then min 5 (w.Box + 1) else 1
        // a missed card comes back in ten minutes, not tomorrow
        let wait = if knew then boxDays.[box] * dayMs else 600000.0
        { w with Box = box; Due = now () + wait })

/// Cards due now, the most overdue first.
let dueWords (lib: Library) (at: float) : WordCard list =
    lib.Words |> List.filter (fun w -> w.Due <= at) |> List.sortBy (fun w -> w.Due)

// ---------------------------------------------------------------------------
// places — saved from the Map lens, with every passage you saved them from
// ---------------------------------------------------------------------------

let placeFor (lib: Library) (qid: string) : SavedPlace option =
    lib.Places |> List.tryFind (fun p -> p.Qid = qid)

let addPlace (lib: Library) (hit: PlaceHit) (work: string) : Library =
    let seen = hit.Refs |> List.map (fun r -> work, r)
    let places =
        match placeFor lib hit.Qid with
        | Some p ->
            lib.Places
            |> List.map (fun q -> if q.Qid = hit.Qid then { q with Seen = (q.Seen @ seen) |> List.distinct } else q)
        | None ->
            lib.Places
            @ [ { Qid = hit.Qid; Label = hit.Label; Pleiades = hit.Pleiades; Lat = hit.Lat; Lon = hit.Lon
                  Seen = seen; Note = ""; Ts = now () } ]
    stamp (placeKey hit.Qid) { lib with Places = places }

let removePlace (lib: Library) (qid: string) : Library =
    { lib with Places = lib.Places |> List.filter (fun p -> p.Qid <> qid) } |> stamp (placeKey qid)

let setPlaceNote (lib: Library) (qid: string) (note: string) : Library =
    { lib with Places = lib.Places |> List.map (fun p -> if p.Qid = qid then { p with Note = note } else p) }
    |> stamp (placeKey qid)

// ---------------------------------------------------------------------------
// merging two copies of a library (sync)
// ---------------------------------------------------------------------------

let private keysPresent (lib: Library) : Set<string> =
    Set.unionMany [
        lib.Favs |> List.map favKey |> Set.ofList
        lib.Marks |> List.map (fun m -> markKey m.Work m.Ref) |> Set.ofList
        lib.AuthorNotes |> Map.toList |> List.map (fst >> anoteKey) |> Set.ofList
        lib.Words |> List.map (fun w -> wordKey w.Id) |> Set.ofList
        lib.Places |> List.map (fun p -> placeKey p.Qid) |> Set.ofList
    ]

/// Merges two copies item by item. For each item the copy that changed it
/// last wins, whether that change was an edit, an addition or a deletion;
/// items neither copy has a stamp for (saved before stamps existed) are kept
/// if either copy has them. Order follows `a`, then what only `b` has.
let merge (a: Library) (b: Library) : Library =
    let presentA, presentB = keysPresent a, keysPresent b
    let stampOf (l: Library) k = l.Stamps.TryFind k |> Option.defaultValue 0.0
    let fromA k =
        let sa, sb = stampOf a k, stampOf b k
        if sa > sb then true elif sb > sa then false else presentA.Contains k
    let keep k = if fromA k then presentA.Contains k else presentB.Contains k
    let pick (keyOf: 'T -> string) (xa: 'T list) (xb: 'T list) : 'T list =
        let byKeyB = xb |> List.map (fun x -> keyOf x, x) |> Map.ofList
        let byKeyA = xa |> List.map (fun x -> keyOf x, x) |> Map.ofList
        let order = (xa |> List.map keyOf) @ (xb |> List.map keyOf) |> List.distinct
        order
        |> List.filter keep
        |> List.choose (fun k ->
            if fromA k then byKeyA.TryFind k |> Option.orElse (byKeyB.TryFind k)
            else byKeyB.TryFind k |> Option.orElse (byKeyA.TryFind k))
    let favs = pick favKey a.Favs b.Favs
    let marks = pick (fun (m: Mark) -> markKey m.Work m.Ref) a.Marks b.Marks
    let words = pick (fun (w: WordCard) -> wordKey w.Id) a.Words b.Words
    let places = pick (fun (p: SavedPlace) -> placeKey p.Qid) a.Places b.Places
    let anotes =
        pick (fun (id: string, _: string) -> anoteKey id) (Map.toList a.AuthorNotes) (Map.toList b.AuthorNotes)
        |> Map.ofList
    let stamps = b.Stamps |> Map.fold (fun acc k v -> if v > (acc |> Map.tryFind k |> Option.defaultValue 0.0) then Map.add k v acc else acc) a.Stamps
    { Favs = favs; Marks = marks; AuthorNotes = anotes; Words = words; Places = places; Stamps = stamps }

/// Everything removed, with each removal stamped so it also reaches the
/// library's other copies when they sync.
let clearAll (lib: Library) : Library =
    let t = now ()
    let stamps = keysPresent lib |> Set.fold (fun acc k -> Map.add k t acc) lib.Stamps
    { Favs = []; Marks = []; AuthorNotes = Map.empty; Words = []; Places = []; Stamps = stamps }

/// Two libraries hold the same things (stamps aside).
let sameContent (a: Library) (b: Library) : bool =
    a.Favs = b.Favs && a.Marks = b.Marks && a.AuthorNotes = b.AuthorNotes && a.Words = b.Words && a.Places = b.Places

// ---------------------------------------------------------------------------
// misc
// ---------------------------------------------------------------------------

/// Candidate work ids for the "add a link" work picker: the current work first,
/// then favourites, then the works of existing bookmarks (most recent first),
/// deduplicated (mirrors `workOptions`'s recency list).
let recentWorkIds (current: string option) (lib: Library) : string list =
    (Option.toList current) @ lib.Favs @ (lib.Marks |> List.map (fun m -> m.Work) |> List.rev)
    |> List.filter (fun s -> s <> "")
    |> List.distinct

// ---------------------------------------------------------------------------
// export / import
//
// Distinct from Storage.fs's (private, trust-your-own-localStorage) codec:
// import must *reject* a pasted blob that isn't a real export, which the
// original enforces by requiring a `marks` field to be present.
// ---------------------------------------------------------------------------

let exportJson (lib: Library) : string = Storage.libraryToJson lib

let private decodeMarkLinkImport: Decoder<MarkLink> =
    Decode.object (fun get ->
        { Work = get.Required.Field "work" Decode.string
          Ref = get.Optional.Field "ref" Decode.string |> Option.defaultValue ""
          Label = get.Optional.Field "label" Decode.string })

let private decodeMarkImport: Decoder<Mark> =
    Decode.object (fun get ->
        { Id = get.Optional.Field "id" Decode.string |> Option.defaultValue ""
          Work = get.Required.Field "work" Decode.string
          Ref = get.Required.Field "ref" Decode.string
          Label = get.Optional.Field "label" Decode.string |> Option.defaultValue ""
          Snippet = get.Optional.Field "snippet" Decode.string |> Option.defaultValue ""
          Note = get.Optional.Field "note" Decode.string |> Option.defaultValue ""
          Links = get.Optional.Field "links" (Decode.list decodeMarkLinkImport) |> Option.defaultValue []
          Ts = get.Optional.Field "ts" Decode.float |> Option.defaultValue 0.0 })

let private decodeImportedLibrary: Decoder<Library> =
    Decode.object (fun get ->
        // `marks` must be present: that is what marks a real export
        let marks = get.Required.Field "marks" (Decode.list decodeMarkImport)
        let rest =
            get.Required.Raw Storage.decodeLibrary
        { rest with Marks = marks |> List.map (fun m -> if m.Id = "" then { m with Id = newId () } else m) })

/// Parses a pasted export blob; `Error` for anything that isn't recognizably
/// one (mirrors the original's `if(!v.marks) throw 0`).
let parseImport (json: string) : Result<Library, string> =
    match Decode.fromString decodeImportedLibrary json with
    | Ok lib -> Ok lib
    | Error _ -> Error "That is not a valid export"

/// Merges an imported library into the current one: favourites are unioned,
/// author notes are overwritten by the import on conflict, and marks are only
/// added where no existing mark already occupies that work+ref (existing
/// marks always win) — mirrors the original's merge exactly.
let mergeImport (current: Library) (imported: Library) : Library =
    let favs = (current.Favs @ imported.Favs) |> List.distinct
    let existingKeys = current.Marks |> List.map (fun m -> m.Work, m.Ref) |> Set.ofList
    let newMarks = imported.Marks |> List.filter (fun m -> not (existingKeys.Contains(m.Work, m.Ref)))
    let authorNotes = imported.AuthorNotes |> Map.fold (fun acc k v -> Map.add k v acc) current.AuthorNotes
    let words = imported.Words |> List.filter (fun w -> (wordFor current w.Word).IsNone)
    let places = imported.Places |> List.filter (fun p -> (placeFor current p.Qid).IsNone)
    let merged =
        { current with
            Favs = favs
            Marks = current.Marks @ newMarks
            AuthorNotes = authorNotes
            Words = current.Words @ words
            Places = current.Places @ places }
    // whatever the import added counts as changed now, so it syncs
    let t = now ()
    let added = Set.difference (keysPresent merged) (keysPresent current)
    { merged with Stamps = added |> Set.fold (fun acc k -> Map.add k t acc) merged.Stamps }
