module Lenses.Corpus

// Word-level indexes over Greek texts the reader has loaded. Everything here is
// computed in the browser from the TEI already in memory; nothing is fetched.
// An index is built the first time a lens asks for one and kept per edition
// URN for the session, since the text behind a URN never changes.

open System.Collections.Generic
open Types

type IndexedSeg =
    { Ref    : string
      Words  : string array       // as printed
      Folded : string array }     // Greek.fold of each

type TextIndex =
    { Urn      : string
      Segs     : IndexedSeg array
      ByRef    : Dictionary<string, int>
      Trigrams : Dictionary<string, ResizeArray<int>>   // phrase → segments holding it
      WordCount: int }

let private cache = Dictionary<string, TextIndex>()

let private trigramsOf (folded: string array) : (int * string) list =
    [ for i in 0 .. folded.Length - 3 do
          let a, b, c = folded.[i], folded.[i + 1], folded.[i + 2]
          // At least two content words: "καὶ τὸν μὲν" is everywhere and
          // "ὣς ἔφατ’, οὐδ’" is a formula only because ἔφατ’ carries it.
          let content = [ a; b; c ] |> List.filter Greek.isContent |> List.length
          if content >= 2 then yield i, a + " " + b + " " + c ]

/// Indexes passages given as (ref, Greek blocks). `key` names the cache
/// entry: the text being read is indexed as the reader shows it (aligned
/// passages, which depend on the translation chosen), other texts as their
/// TEI divides them.
let private build (key: string) (urn: string) (passages: (string * Block list) seq) : TextIndex =
    match cache.TryGetValue key with
    | true, ix -> ix
    | _ ->
        let arr =
            passages
            |> Seq.map (fun (r, blocks) ->
                let ws = Greek.words (Greek.blockText blocks)
                { Ref = r; Words = ws; Folded = ws |> Array.map Greek.fold })
            |> Array.ofSeq
        let byRef = Dictionary<string, int>()
        let tri = Dictionary<string, ResizeArray<int>>()
        arr
        |> Array.iteri (fun si s ->
            byRef.[s.Ref] <- si
            for (_, t) in trigramsOf s.Folded do
                match tri.TryGetValue t with
                | true, l -> if l.[l.Count - 1] <> si then l.Add si
                | _ -> tri.[t] <- ResizeArray [ si ])
        let ix = { Urn = urn; Segs = arr; ByRef = byRef; Trigrams = tri; WordCount = arr |> Array.sumBy (fun s -> s.Words.Length) }
        cache.[key] <- ix
        ix

/// A text as its TEI divides it.
let index (urn: string) (segs: RawSegment list) : TextIndex =
    build urn urn (segs |> Seq.map (fun s -> Greek.refOf s, s.Blocks))

/// The text being read, passage by passage as the reader shows it.
let indexAligned (urn: string) (engUrn: string) (segs: Segment array) : TextIndex =
    build (urn + "|" + engUrn) urn (segs |> Seq.map (fun s -> s.Ref, s.Grc))

// ---------------------------------------------------------------------------
// echoes
// ---------------------------------------------------------------------------

type Echo =
    { Urn    : string
      Ref    : string
      Shared : string list        // the phrases in common, as printed in the target
      Score  : float
      Words  : string array       // a window of the target passage
      Hit    : bool array }       // which of those words belong to a shared phrase

/// Phrases held by more passages than this are the text's commonest formulae
/// (ὣς ἔφατ’…, τὸν δ’ ἀπαμειβόμενος…). They are still shown when nothing
/// rarer is shared, but they don't outrank a genuine verbal echo.
let private commonDf = 60

let private window (s: IndexedSeg) (hits: Set<int>) : string array * bool array =
    let first = if hits.IsEmpty then 0 else Set.minElement hits
    let a = max 0 (first - 6)
    let b = min (s.Words.Length - 1) (first + 18)
    if b < a then [||], [||]
    else s.Words.[a..b], [| for i in a..b -> hits.Contains i |]

let private matchesIn (target: TextIndex) (query: Map<string, int>) (exclude: int option) : Echo list =
    let score = Dictionary<int, float * Set<string>>()
    for KeyValue(t, _) in query do
        match target.Trigrams.TryGetValue t with
        | true, postings ->
            let df = postings.Count
            let w = if df > commonDf then 0.05 else 1.0 / sqrt (float df)
            for si in postings do
                if Some si <> exclude then
                    let s0, ts = match score.TryGetValue si with | true, v -> v | _ -> 0.0, Set.empty
                    score.[si] <- s0 + w, Set.add t ts
        | _ -> ()
    [ for KeyValue(si, (sc, ts)) in score do
          let s = target.Segs.[si]
          let hits =
              trigramsOf s.Folded
              |> List.filter (fun (_, t) -> ts.Contains t)
              |> List.collect (fun (i, _) -> [ i; i + 1; i + 2 ])
              |> Set.ofList
          let shared =
              trigramsOf s.Folded
              |> List.filter (fun (_, t) -> ts.Contains t)
              |> List.map (fun (i, _) -> s.Words.[i] + " " + s.Words.[i + 1] + " " + s.Words.[i + 2])
              |> List.distinct
          let ws, hit = window s hits
          yield { Urn = target.Urn; Ref = s.Ref; Shared = shared; Score = sc; Words = ws; Hit = hit } ]
    |> List.sortByDescending (fun e -> e.Score)

/// Passages sharing a three-word phrase with `segRef`: first within the same
/// text, then across the other Greek texts given.
let echoes (ix: TextIndex) (segRef: string) (others: TextIndex list) : Echo list * Echo list =
    match ix.ByRef.TryGetValue segRef with
    | true, si ->
        let query = trigramsOf ix.Segs.[si].Folded |> List.map (fun (i, t) -> t, i) |> Map.ofList
        let here = matchesIn ix query (Some si) |> List.truncate 12
        let there = others |> List.collect (fun o -> matchesIn o query None) |> List.sortByDescending (fun e -> e.Score) |> List.truncate 12
        here, there
    | _ -> [], []

// ---------------------------------------------------------------------------
// word tracing
// ---------------------------------------------------------------------------

/// A starting stem for tracing a word: its folded form less the likely
/// ending. Shown to the reader as an editable field, because no stemmer is
/// right about Greek often enough to be trusted silently.
let defaultStem (word: string) : string =
    let f = Greek.fold word
    let cut = if f.Length >= 7 then 3 elif f.Length >= 5 then 2 elif f.Length >= 4 then 1 else 0
    f.Substring(0, f.Length - cut)

type Hit = { Ref: string; Pos: int; Form: string }

let occurrences (ix: TextIndex) (stem: string) : Hit list =
    let st = Greek.fold stem
    if st.Length < 2 then []
    else
        [ for s in ix.Segs do
              for i in 0 .. s.Folded.Length - 1 do
                  if s.Folded.[i].StartsWith st then yield { Ref = s.Ref; Pos = i; Form = s.Words.[i] } ]

/// A few words either side of a hit.
let context (ix: TextIndex) (h: Hit) : string array * int =
    match ix.ByRef.TryGetValue h.Ref with
    | true, si ->
        let ws = ix.Segs.[si].Words
        let a = max 0 (h.Pos - 5)
        let b = min (ws.Length - 1) (h.Pos + 5)
        ws.[a..b], h.Pos - a
    | _ -> [||], 0
