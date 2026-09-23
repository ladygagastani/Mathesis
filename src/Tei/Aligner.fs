module Tei.Aligner

open System.Collections.Generic
open System.Text.RegularExpressions
open Types

/// Mirrors the original `truncateTo`: re-keys segments to a shared path depth,
/// merging the blocks of any segments that collapse onto the same truncated key.
let truncateTo (depth: int) (list: RawSegment list) : RawSegment list =
    let m = Dictionary<string, string list * ResizeArray<Block>>()
    let order = ResizeArray<string>()
    for s in list do
        let k = s.Key |> List.truncate depth
        let ks = String.concat "." k
        match m.TryGetValue ks with
        | true, (_, blocks) -> blocks.AddRange s.Blocks
        | false, _ ->
            m.[ks] <- (k, ResizeArray(s.Blocks))
            order.Add ks
    order
    |> Seq.map (fun ks -> let (k, blocks) = m.[ks] in { Key = k; Blocks = List.ofSeq blocks })
    |> List.ofSeq

let private depthOf (list: RawSegment list) : int =
    list |> List.fold (fun acc s -> max acc s.Key.Length) 0

/// One aligned pairing before immutable finalization — `Eng` is filled in during
/// the nearest-neighbour fallback pass below, mirroring the original's in-place mutation.
type private AlignSeg =
    { Ref: string; Key: string list; Grc: Block list; mutable Eng: Block list option }

type private EngEntry = { Blocks: Block list; mutable Taken: bool }

/// Mirrors the original `align`: truncates both sides to their shared depth, matches
/// segments by exact key, then — only for segments still unmatched — looks for a
/// single unambiguous English segment sitting between the nearest matched neighbours
/// (a fallback for one-off citation-boundary mismatches, e.g. card 1.33 vs 1.34).
let align (grcList: RawSegment list) (engList: RawSegment list option) : int * Segment array =
    let dRaw =
        match engList with
        | Some e -> min (depthOf grcList) (depthOf e)
        | None -> depthOf grcList
    let d = if dRaw = 0 then 1 else dRaw

    let g = truncateTo d grcList
    let e = engList |> Option.map (truncateTo d)

    let em = Dictionary<string, EngEntry>()
    e |> Option.iter (List.iter (fun s -> em.[String.concat "." s.Key] <- { Blocks = s.Blocks; Taken = false }))

    let used = HashSet<string>()

    let segsArr =
        g
        |> List.map (fun s ->
            let k = String.concat "." s.Key
            match em.TryGetValue k with
            | true, es ->
                used.Add k |> ignore
                { Ref = k; Key = s.Key; Grc = s.Blocks; Eng = Some es.Blocks }
            | false, _ -> { Ref = k; Key = s.Key; Grc = s.Blocks; Eng = None })
        |> Array.ofList

    match e with
    | Some elist ->
        let eo = elist |> List.map (fun s -> String.concat "." s.Key) |> Array.ofList
        let epos = Dictionary<string, int>()
        eo |> Array.iteri (fun i k -> epos.[k] <- i)

        // Which Greek passage each English passage went to (by key).
        let owner = Dictionary<string, int>()
        segsArr |> Array.iteri (fun i s -> if used.Contains s.Ref then owner.[s.Ref] <- i)

        for i in 0 .. segsArr.Length - 1 do
            let s = segsArr.[i]
            if s.Eng.IsNone then
                let prev =
                    seq { i - 1 .. -1 .. 0 }
                    |> Seq.map (fun j -> segsArr.[j])
                    |> Seq.tryFind (fun x -> x.Eng.IsSome && epos.ContainsKey x.Ref)
                let next =
                    seq { i + 1 .. segsArr.Length - 1 }
                    |> Seq.map (fun j -> segsArr.[j])
                    |> Seq.tryFind (fun x -> x.Eng.IsSome && epos.ContainsKey x.Ref)
                let lo = match prev with Some p -> epos.[p.Ref] | None -> -1
                let hi = match next with Some n -> epos.[n.Ref] | None -> eo.Length
                let candidates =
                    eo
                    |> Array.indexed
                    |> Array.filter (fun (j, k) -> j > lo && j < hi && not (used.Contains k) && not em.[k].Taken)
                    |> Array.map snd
                if candidates.Length = 1 then
                    let k0 = candidates.[0]
                    s.Eng <- Some em.[k0].Blocks
                    em.[k0].Taken <- true
                    owner.[k0] <- i

        // English passages that no Greek passage claimed used to be dropped.
        // They are where the translation is divided more finely than the
        // edition: Jebb's Antigone opens a card at 332, the ode "Wonders are
        // many", while the Greek card runs on from 315 to 342, so the whole
        // ode vanished from the English column. Each such passage now joins the
        // Greek passage holding the English passage just before it (the line
        // refinement below then sets it beside its own lines). Only when most
        // of the translation already matched: with little in common, the two
        // are numbered on different schemes and there is no telling where the
        // leftovers belong.
        let isLeftover (k: string) = not (used.Contains k) && not em.[k].Taken
        let leftovers = eo |> Array.filter isLeftover
        if leftovers.Length > 0 && owner.Count * 2 >= eo.Length then
            let mutable lastOwner: int option = None
            for k in eo do
                match owner.TryGetValue k with
                | true, i -> lastOwner <- Some i
                | false, _ ->
                    if isLeftover k then
                        match lastOwner with
                        | Some i -> segsArr.[i].Eng <- Some(segsArr.[i].Eng.Value @ em.[k].Blocks)
                        | None -> ()
    | None -> ()

    let final =
        segsArr |> Array.map (fun s -> ({ Ref = s.Ref; Key = s.Key; Grc = s.Grc; Eng = s.Eng }: Segment))
    d, final

// ---------------------------------------------------------------------------
// line-level refinement
//
// Verse editions and their translations are usually divided on incompatible
// schemes — the Perseus Iliad is cut into `card` milestones every ~35 lines
// while Murray's translation carries no cards at all, only `line` milestones.
// Matching on those coarse units leaves a tall column of verse beside a tall
// paragraph of prose that drift apart as you read down.
//
// Both sides do still carry line numbers, though: the Greek numbers its `<l>`
// elements, and the translation's line milestones survive as inline `⟦n⟧`
// markers. Where a matched pair exposes the same anchors on both sides, this
// splits it into one row per anchor, so the two texts stay side by side. Pairs
// that don't qualify (prose works, verse with no marked translation) are
// returned untouched, which is why nothing else in the catalogue shifts.
// ---------------------------------------------------------------------------

let private markerRe = Regex(@"⟦\s*([0-9]+)\s*⟧")

let private parseNum (s: string) =
    match System.Int32.TryParse s with
    | true, v -> Some v
    | false, _ -> None

/// One verse line with the line number in force (numbers are only marked on
/// some lines, so the last seen one carries forward) and its speaker.
type private FlatLine =
    { Num: int option
      Speaker: string option
      Line: string option * string }

/// Flattens a segment's verse lines, or `None` if it isn't pure verse.
let private flattenVerse (blocks: Block list) : FlatLine list option =
    let out = ResizeArray<FlatLine>()
    let mutable isVerse = true
    let mutable current: int option = None
    for b in blocks do
        match b with
        | Verse(sp, lines) ->
            for (nOpt, t) in lines do
                match nOpt |> Option.bind parseNum with
                | Some n -> current <- Some n
                | None -> ()
                out.Add { Num = current; Speaker = sp; Line = (nOpt, t) }
        | Heading _ -> ()
        | Prose _ -> isVerse <- false
    if isVerse && out.Count > 0 then Some(List.ofSeq out) else None

/// A piece of the translation tagged with the line anchor in force.
type private EngPiece = { Anchor: int option; Block: Block }

/// Breaks a translation into anchored pieces. Prose translations carry their
/// line milestones as inline `⟦n⟧` markers; verse translations number their
/// lines directly. Both are reduced to the same shape here. `None` when the
/// translation exposes no line anchors at all.
let private engPieces (blocks: Block list) : EngPiece list option =
    let out = ResizeArray<EngPiece>()
    let mutable current: int option = None
    let mutable sawAnchor = false
    for b in blocks do
        match b with
        | Prose(_, text) ->
            let mutable pos = 0
            for m in markerRe.Matches text do
                let before = text.Substring(pos, m.Index - pos)
                if before.Trim() <> "" then out.Add { Anchor = current; Block = Prose(None, before) }
                current <- parseNum m.Groups.[1].Value
                sawAnchor <- true
                pos <- m.Index + m.Length
            let tail = text.Substring pos
            if tail.Trim() <> "" then out.Add { Anchor = current; Block = Prose(None, tail) }
        | Verse(sp, lines) ->
            for (nOpt, t) in lines do
                match nOpt |> Option.bind parseNum with
                | Some n ->
                    current <- Some n
                    sawAnchor <- true
                | None -> ()
                out.Add { Anchor = current; Block = Verse(sp, [ (nOpt, t) ]) }
        | Heading _ -> ()
    if sawAnchor then Some(List.ofSeq out) else None

/// Rejoins pieces that ended up in the same row.
let private mergeBlocks (blocks: Block list) : Block list =
    blocks
    |> List.fold
        (fun acc b ->
            match acc, b with
            | Prose(sp, t1) :: rest, Prose(_, t2) -> Prose(sp, t1.TrimEnd() + " " + t2.TrimStart()) :: rest
            | Verse(sp1, l1) :: rest, Verse(sp2, l2) when sp1 = sp2 -> Verse(sp1, l1 @ l2) :: rest
            | _ -> b :: acc)
        []
    |> List.rev

/// Regroups consecutive lines sharing a speaker back into `Verse` blocks.
let private toVerseBlocks (lines: FlatLine list) : Block list =
    lines
    |> List.fold
        (fun acc fl ->
            match acc with
            | Verse(sp, ls) :: rest when sp = fl.Speaker -> Verse(sp, ls @ [ fl.Line ]) :: rest
            | _ -> Verse(fl.Speaker, [ fl.Line ]) :: acc)
        []
    |> List.rev

/// Minimum lines between row starts. Drama translations number every line, so
/// without this each row would be a single line under its own heading. Four
/// rather than five so that texts anchored every fifth line — numbered from 1,
/// making the first interval 1→5 only four lines wide — keep every anchor and
/// stay on their natural cadence.
let private minAnchorGap = 4

let private groupAnchors (anchors: int list) : int list =
    match anchors with
    | [] -> []
    | first :: rest ->
        rest
        |> List.fold (fun (kept, last) a -> if a - last >= minAnchorGap then (a :: kept, a) else (kept, last)) ([ first ], first)
        |> fst
        |> List.rev

/// Whether the last component of a text's keys is a line position (Homer's
/// card 1.33, a play's card 176) rather than a unit of its own (Pindar's ode
/// 2, an epigram). Units of their own restart their line numbers: ode 2 begins
/// again at line 1. Positions keep counting from one passage to the next under
/// the same parent — even where a card milestone sits a line off (the Hymn to
/// Demeter's card 40 opens at line 39). Decided once per text, by the majority
/// of steps between neighbouring passages, so one stray line cannot flip it;
/// a text with no such steps keeps the old behaviour.
let private lastKeyIsLinePosition (segs: Segment array) : bool =
    let spans =
        segs
        |> Array.choose (fun s ->
            match s.Key, flattenVerse s.Grc |> Option.map (List.choose (fun fl -> fl.Num)) with
            | _ :: _, Some(first :: _ as nums) -> Some(List.take (s.Key.Length - 1) s.Key, first, List.last nums)
            | _ -> None)
    let steps = spans |> Array.pairwise |> Array.filter (fun ((p1, _, _), (p2, _, _)) -> p1 = p2)
    let restarts = steps |> Array.filter (fun ((_, _, last1), (_, first2, _)) -> first2 <= last1) |> Array.length
    restarts * 2 <= steps.Length

let private refineByLines (lastIsLinePos: bool) (seg: Segment) : Segment list =
    match seg.Eng with
    | None -> [ seg ]
    | Some engBlocks ->
        match flattenVerse seg.Grc, engPieces engBlocks with
        | Some flat, Some pieces ->
            let anchors = pieces |> List.choose (fun p -> p.Anchor) |> List.distinct |> List.sort |> groupAnchors
            let numbered = flat |> List.choose (fun fl -> fl.Num)
            // Needs at least two anchors to be worth splitting, and the Greek
            // must actually carry the line numbers the anchors refer to.
            if anchors.Length < 2 || List.isEmpty numbered || List.max numbered < List.head anchors then
                [ seg ]
            else
                let headings = seg.Grc |> List.filter (function Heading _ -> true | _ -> false)
                // When the last key component names a position inside the
                // parent, the anchor *replaces* it: a book/card key like 1.33
                // becomes 1.35, and a bare line key like 39 becomes 45. When
                // it names a unit of its own (ode 2), the anchor is appended
                // (2.5); replacing it gave every ode the same refs 1, 5, 10….
                let keyPrefix =
                    if seg.Key.IsEmpty then []
                    elif lastIsLinePos then seg.Key |> List.take (seg.Key.Length - 1)
                    else seg.Key
                let anchorArr = List.toArray anchors
                let built =
                    anchors
                    |> List.mapi (fun i anchor ->
                        let isFirst = i = 0
                        let nextAnchor = if i + 1 < anchorArr.Length then Some anchorArr.[i + 1] else None
                        let within (n: int) =
                            // anything before the first anchor rides along with it
                            (n >= anchor || isFirst)
                            && (match nextAnchor with
                                | Some nx -> n < nx
                                | None -> true)
                        let lines = flat |> List.filter (fun fl -> match fl.Num with Some n -> within n | None -> isFirst)
                        let eng =
                            pieces
                            |> List.filter (fun p ->
                                match p.Anchor with
                                | Some n -> within n
                                | None -> isFirst)
                            |> List.map (fun p -> p.Block)
                            |> mergeBlocks
                        anchor, lines, eng)
                    |> List.filter (fun (_, lines, eng) -> not (List.isEmpty lines) || not (List.isEmpty eng))
                if built.Length < 2 then
                    [ seg ]
                else
                    built
                    |> List.mapi (fun i (anchor, lines, eng) ->
                        { Ref = String.concat "." (keyPrefix @ [ string anchor ])
                          Key = keyPrefix @ [ string anchor ]
                          Grc = (if i = 0 then headings else []) @ toVerseBlocks lines
                          Eng = if List.isEmpty eng then None else Some eng })
        | _ -> [ seg ]

/// The numbered Greek lines a row holds.
let private greekLineNums (seg: Segment) : int list =
    match flattenVerse seg.Grc with
    | Some flat -> flat |> List.choose (fun fl -> fst fl.Line |> Option.bind parseNum)
    | None -> []

/// Where the translation's card starts a few lines before the Greek one,
/// refining gives a row of English with no Greek, while the Greek of those
/// lines sits in the neighbouring passage: the Libation Bearers showed the
/// English of 195–222 under one "195" and the Greek under another, and the
/// Theogony's English for 235–239 stood on an empty row after Greek 230–239.
/// Each such row's English joins the passage whose Greek holds its line, in
/// line order, and that passage is refined again. Rows whose line the Greek
/// does not have (a gap, a translation running past the end) stay as they are.
let private rehomeOrphans (lastIsLinePos: bool) (rows: Segment array) : Segment array =
    let isOrphan (s: Segment) =
        s.Eng.IsSome && List.isEmpty (greekLineNums s)
        && not (s.Grc |> List.exists (function Prose _ -> true | _ -> false))
    let prefixOf (s: Segment) = if s.Key.IsEmpty then [] else List.take (s.Key.Length - 1) s.Key
    let anchorOf (s: Segment) = s.Key |> List.tryLast |> Option.bind parseNum
    // Rows of the same unit: siblings under one parent (cards; refined rows of
    // one ode), or in append mode the ode's own unrefined passage.
    let sameUnit (t: Segment) (o: Segment) =
        prefixOf t = prefixOf o || (not lastIsLinePos && t.Key = prefixOf o)
    let targetOf (o: Segment) : int option =
        match anchorOf o with
        | None -> None
        | Some anchor ->
            rows
            |> Array.tryFindIndex (fun t -> not (isOrphan t) && sameUnit t o && List.contains anchor (greekLineNums t))
    let moves = rows |> Array.indexed |> Array.choose (fun (i, s) -> if isOrphan s then targetOf s |> Option.map (fun t -> i, t) else None)
    if Array.isEmpty moves then
        rows
    else
        let moved = moves |> Array.map fst |> Set.ofArray
        let byTarget = moves |> Array.groupBy snd |> Map.ofArray
        // Refining took the ⟦n⟧ markers out of prose English; each part gets
        // its row's back so the second pass can place it again.
        let withMarker (s: Segment) =
            match s.Key |> List.tryLast, s.Eng |> Option.defaultValue [] with
            | Some a, Prose(sp, t) :: rest -> Prose(sp, "⟦" + a + "⟧ " + t) :: rest
            | _, blocks -> blocks
        rows
        |> Array.indexed
        |> Array.collect (fun (i, t) ->
            if moved.Contains i then
                [||]
            else
                match byTarget.TryFind i with
                | Some ms ->
                    let orphans = ms |> Array.toList |> List.map (fun (o, _) -> rows.[o])
                    let o0 = List.head orphans
                    // A sibling row keyed by its own anchor carries English of
                    // its own; an ode's untranslated passage does not.
                    let sibling = prefixOf t = prefixOf o0
                    let parts =
                        (if sibling && t.Eng.IsSome then [ t ] else []) @ orphans
                        |> List.sortBy (fun p -> anchorOf p |> Option.defaultValue 0)
                    let eng = parts |> List.collect withMarker
                    // Refine under the unit's key: in append mode that is the
                    // ode, not the row, or the anchor would be appended twice.
                    let key = if lastIsLinePos then t.Key else prefixOf o0
                    refineByLines lastIsLinePos { t with Key = key; Eng = Some eng } |> Array.ofList
                | None -> [| t |])

/// Full pipeline result for a work: aligns Greek with an optional translation, groups
/// the result into citable chunks (mirrors the original's chunk-by-first-key-component
/// heuristic in `start()`), and computes translation coverage.
let buildAlignedText (grcList: RawSegment list) (engList: RawSegment list option) : AlignedText =
    let d, aligned = align grcList engList
    let lastIsLinePos = lastKeyIsLinePosition aligned
    let segments =
        aligned
        |> Array.collect (refineByLines lastIsLinePos >> Array.ofList)
        |> rehomeOrphans lastIsLinePos
    // Chunk on the keys as refined: Pindar aligns at depth 1 (the ode) but
    // its rows are keyed ode.line, and each ode should be its own chunk.
    let keyDepth = segments |> Array.fold (fun acc s -> max acc s.Key.Length) 0

    let firsts =
        let seen = HashSet<string>()
        let ordered = ResizeArray<string>()
        for s in segments do
            match s.Key with
            | f :: _ -> if seen.Add f then ordered.Add f
            | [] -> ()
        List.ofSeq ordered

    let firstOf (s: Segment) = match s.Key with f :: _ -> Some f | [] -> None

    let chunks =
        if max d keyDepth >= 2 && firsts.Length > 1 && firsts.Length <= 120 && float segments.Length / float firsts.Length >= 6.0 then
            firsts
            |> List.map (fun f ->
                { Ref = f; Segments = segments |> Array.filter (fun s -> firstOf s = Some f) })
        else
            [ { Ref = "all"; Segments = segments } ]

    let coverage =
        match engList with
        | Some _ ->
            let withEng = segments |> Array.filter (fun s -> s.Eng.IsSome)
            Some
                { Covered = withEng.Length
                  Total = segments.Length
                  First = withEng |> Array.tryHead |> Option.map (fun s -> s.Ref)
                  Last = withEng |> Array.tryLast |> Option.map (fun s -> s.Ref) }
        | None -> None

    { Depth = d; Chunks = chunks; Segments = segments; Coverage = coverage }
