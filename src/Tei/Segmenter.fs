module Tei.Segmenter

open System.Collections.Generic
open Types

let private wsRe = System.Text.RegularExpressions.Regex(@"\s+")
let private collapseWs (s: string) = wsRe.Replace(s, " ")
let private openQuoteSpaceRe = System.Text.RegularExpressions.Regex("“\\s+")
let private closeQuoteSpaceRe = System.Text.RegularExpressions.Regex("\\s+”")
let private numericRe = System.Text.RegularExpressions.Regex(@"^\d+$")
let private leadingDigitRe = System.Text.RegularExpressions.Regex(@"^\d")
let private hasLetterOrDigitRe = System.Text.RegularExpressions.Regex(@"[\p{L}\p{N}]")

/// Mirrors JS's `nb.sp = st.sp` being skipped entirely when `st.sp` is falsy —
/// a block created while unspoken never records a speaker, even an empty one.
let private truthySp (sp: string option) : string option =
    match sp with
    | Some v when v <> "" -> Some v
    | _ -> None

/// Mirrors JS's `last.sp === st.sp`: since an unspoken block's `.sp` is JS
/// `undefined` (property never set) while the *current* unspoken state is
/// JS `null`, `undefined === null` is false — two "no speaker" blocks never
/// merge with each other. Only two blocks tagged with the same real speaker do.
let private spMatches (blockSp: string option) (currentSp: string option) : bool =
    match blockSp, currentSp with
    | Some a, Some b -> a = b
    | _ -> false

type private MBlock =
    | MHead of string
    | MProse of speaker: string option * text: string ref * closed: bool ref
    | MVerse of speaker: string option * lines: ResizeArray<string option * string>

type private MSeg = { Key: string list; Blocks: ResizeArray<MBlock> }

/// Groups a tokenized event stream into citable segments (mirrors the original
/// app's `segment` 1:1). `inlineMs` names milestone units (e.g. "page", and for
/// translations "line") that become inline `⟦n⟧` markers rather than new segments.
let segment (events: TeiEvent list) (inlineMs: Set<string>) : RawSegment list =
    let segs = Dictionary<string, MSeg>()
    let order = ResizeArray<string>()

    let mutable path : string list = []
    let mutable sec : string option = None
    let mutable card : string option = None
    let mutable sp : string option = None
    let pending = System.Text.StringBuilder()

    let keyOf () : string list =
        let k = ResizeArray<string>(path)
        match (match sec with Some _ -> sec | None -> card) with
        | Some mv when
            k.Count > 0
            && numericRe.IsMatch k.[k.Count - 1]
            && mv.StartsWith(k.[k.Count - 1])
            && mv <> k.[k.Count - 1]
            && not (leadingDigitRe.IsMatch (mv.Substring(k.[k.Count - 1].Length))) ->
            k.[k.Count - 1] <- mv
        | Some mv -> k.Add mv
        | None -> ()
        List.ofSeq k

    let getSeg () : MSeg =
        let k = keyOf ()
        let ks = String.concat "." k
        match segs.TryGetValue ks with
        | true, s -> s
        | false, _ ->
            let s = { Key = k; Blocks = ResizeArray() }
            segs.[ks] <- s
            order.Add ks
            s

    let flush () =
        let raw = pending.ToString()
        pending.Clear() |> ignore
        let t =
            let s0 = (collapseWs raw).Trim()
            let s1 = openQuoteSpaceRe.Replace(s0, "“")
            closeQuoteSpaceRe.Replace(s1, "”")
        if t <> "" then
            let s = getSeg ()
            let b = s.Blocks
            let merged =
                b.Count > 0
                && match b.[b.Count - 1] with
                   | MProse (blockSp, text, closed) when spMatches blockSp sp && not closed.Value ->
                       text.Value <- (text.Value + " " + t).Trim()
                       true
                   | _ -> false
            if not merged then
                b.Add (MProse (truthySp sp, ref t, ref false))

    for ev in events do
        match ev with
        | EvDiv p ->
            flush ()
            path <- p
            sec <- None
            card <- None
        | EvMilestone (unit_, n) ->
            match n with
            | None | Some "" -> ()
            | Some nv ->
                match unit_ with
                | Some u when inlineMs.Contains u -> pending.Append(" ⟦" + nv + "⟧ ") |> ignore
                | Some "section" -> flush (); sec <- Some nv
                | Some "card" -> flush (); card <- Some nv
                | _ -> ()
        | EvSpeaker name ->
            flush ()
            sp <- Some name
        | EvSpeakerEnd ->
            flush ()
            sp <- None
        | EvHead h ->
            flush ()
            (getSeg ()).Blocks.Add (MHead h)
        | EvParaBreak ->
            flush ()
            match segs.TryGetValue(String.concat "." (keyOf ())) with
            | true, s when s.Blocks.Count > 0 ->
                match s.Blocks.[s.Blocks.Count - 1] with
                | MProse (_, _, closed) -> closed.Value <- true
                | _ -> ()
            | _ -> ()
        | EvText t ->
            pending.Append(t: string) |> ignore
        | EvLine (n, text) ->
            flush ()
            let b = (getSeg ()).Blocks
            let merged =
                b.Count > 0
                && match b.[b.Count - 1] with
                   | MVerse (blockSp, lines) when spMatches blockSp sp ->
                       lines.Add (n, text)
                       true
                   | _ -> false
            if not merged then
                let lines = ResizeArray()
                lines.Add (n, text)
                b.Add (MVerse (truthySp sp, lines))

    flush ()

    let hasContent =
        function
        | MHead h -> h <> ""
        | MProse (_, text, _) -> text.Value <> ""
        | MVerse (_, lines) -> lines.Count > 0

    let toBlock =
        function
        | MHead h -> Heading h
        | MProse (spk, text, _) -> Prose (spk, text.Value)
        | MVerse (spk, lines) -> Verse (spk, List.ofSeq lines)

    let mutable list =
        order
        |> Seq.map (fun k -> segs.[k])
        |> Seq.filter (fun s -> s.Blocks |> Seq.exists hasContent)
        |> List.ofSeq

    if list.Length > 1 then
        list <- list |> List.filter (fun s -> not (List.isEmpty s.Key))

    list
    |> List.map (fun s ->
        let hasVerse = s.Blocks |> Seq.exists (function MVerse _ -> true | _ -> false)
        let blocks =
            s.Blocks
            |> Seq.filter (fun mb ->
                match mb with
                | MProse (_, text, _) when hasVerse -> hasLetterOrDigitRe.IsMatch text.Value
                | _ -> true)
            |> Seq.map toBlock
            |> List.ofSeq
        { Key = s.Key; Blocks = blocks })
