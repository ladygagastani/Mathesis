module Tei.Tokenizer

open Fable.Core.JsInterop
open Browser.Types
open Types

let private skipTags =
    set [ "note"; "teiHeader"; "figure"; "fw"; "pb"; "interp"; "interpGrp"; "link"; "linkGrp"; "index" ]

/// Div subtypes that describe a play's or poem's structure (or its metre)
/// rather than a citation level. A numbered one that slipped through became a
/// path level of its own: the Agamemnon's refrain `<div subtype="ephymn." n="1">`
/// was keyed "1" like the prologue's first card, so lines 1455–1488 were
/// merged into the opening passage. Subtypes are compared trimmed and without
/// a trailing full stop, since editions abbreviate ("ephymn.", "str.").
let private nonCiteSubtypes =
    set [ "strophe"; "antistrophe"; "epode"; "mesode"; "proode"; "lyric"; "choral"
          "episode"; "anapests"; "anapaests"; "prologue"; "parodos"; "exodos"
          "stasimon"; "kommos"; "speech"; "dialogue"; "scene"; "act"
          "ephymn"; "ephymnion"; "close" ]

let private divRe = System.Text.RegularExpressions.Regex(@"^div\d?$")
let private isDivTag (tag: string) = divRe.IsMatch tag

let private wsRe = System.Text.RegularExpressions.Regex(@"\s+")
let private collapseWs (s: string) = wsRe.Replace(s, " ")

let private attr (el: Element) (name: string) : string option = Option.ofObj (el.getAttribute name)

let private elemOpt (e: Element) : Element option =
    if isNullOrUndefined e then None else Some e

/// NodeList has no .NET enumerator in this binding, so index it manually.
let private childList (n: Node) : Node array =
    Array.init n.childNodes.length (fun i -> n.childNodes.[i])

/// Tokenizes TEI XML into a flat event stream (mirrors the original app's `tokenize` 1:1,
/// including its quirks — e.g. lines directly inside a numbered div become real `EvLine`
/// events, but lines nested in a `<quote>`/`<lg>` collapse into inline "text /" markers).
let tokenize (xmlText: string) : TeiEvent list =
    // NFC before anything else: Google Fonts' subsets leave out the combining
    // breathings, circumflex and iota subscript (U+0313/0314/0342/0345), so a
    // decomposed accent would be drawn from some other font, usually misplaced.
    // Every polytonic combination has a precomposed form, so nothing is lost;
    // markup is ASCII and passes through unchanged.
    let doc = Interop.parseXml (xmlText.Normalize(System.Text.NormalizationForm.FormC))
    if not (isNullOrUndefined (doc.querySelector "parsererror")) then
        failwith "The XML file could not be parsed."

    let bodyElems = doc.getElementsByTagName "body"
    let body : Element = if bodyElems.length > 0 then bodyElems.[0] else doc.documentElement

    let out = ResizeArray<TeiEvent>()
    let path = ResizeArray<string>()
    let mutable inQuote = false

    let textOf (el: Element) : string =
        let sb = System.Text.StringBuilder()
        let rec go (e: Node) =
            for c in childList e do
                if c.nodeType = 3.0 then sb.Append(c.nodeValue: string) |> ignore
                elif c.nodeType = 1.0 then
                    let ce = c :?> Element
                    if not (skipTags.Contains ce.localName) then go ce
        go el
        (collapseWs (sb.ToString())).Trim()

    let choiceChild (el: Element) : Element option =
        match elemOpt (el.querySelector ":scope>corr,:scope>reg,:scope>expan") with
        | Some c -> Some c
        | None -> if el.children.length > 0 then Some el.children.[0] else None

    let appChild (el: Element) : Element option =
        match elemOpt (el.querySelector ":scope>lem") with
        | Some c -> Some c
        | None -> elemOpt (el.querySelector ":scope>rdg")

    let rec walk (el: Element) =
        let tag = el.localName
        if skipTags.Contains tag then
            ()
        elif isDivTag tag then
            let n = attr el "n"
            let typ = attr el "type"
            let sub = (attr el "subtype" |> Option.defaultValue "").Trim().TrimEnd('.').ToLowerInvariant()
            let nTruthy = match n with Some v when v <> "" -> true | _ -> false
            if nTruthy
               && n <> Some "front"
               && typ <> Some "edition" && typ <> Some "translation" && typ <> Some "commentary"
               && not (nonCiteSubtypes.Contains sub) then
                path.Add n.Value
                out.Add (EvDiv (List.ofSeq path))
                walkChildren el
                path.RemoveAt (path.Count - 1)
            elif n = Some "front" then
                ()
            else
                walkChildren el
        elif tag = "milestone" then
            out.Add (EvMilestone (attr el "unit", attr el "n"))
        elif tag = "speaker" then
            out.Add (EvSpeaker (textOf el))
        elif tag = "label" then
            let t = textOf el
            if t.Length < 32 then out.Add (EvSpeaker t) else out.Add (EvText (" " + t + " "))
        elif tag = "head" then
            out.Add (EvHead (textOf el))
        elif tag = "gap" then
            out.Add (EvText " […] ")
        elif tag = "lb" then
            out.Add (EvText " ")
        elif tag = "choice" then
            choiceChild el |> Option.iter walk
        elif tag = "app" then
            appChild el |> Option.iter walk
        elif tag = "l" && not inQuote then
            let buf = System.Text.StringBuilder()
            let rec inner (e: Element) =
                if skipTags.Contains e.localName then
                    ()
                elif e.localName = "milestone" then
                    out.Add (EvMilestone (attr e "unit", attr e "n"))
                elif e.localName = "gap" then
                    buf.Append(" […] ") |> ignore
                else
                    for c in childList e do
                        if c.nodeType = 3.0 then buf.Append(c.nodeValue: string) |> ignore
                        elif c.nodeType = 1.0 then inner (c :?> Element)
            inner el
            out.Add (EvLine (attr el "n", (collapseWs (buf.ToString())).Trim()))
        elif tag = "sp" then
            walkChildren el
            out.Add EvSpeakerEnd
        elif tag = "p" || tag = "lg" || tag = "quote" || tag = "q" || tag = "said" || tag = "item" || tag = "ab" then
            if tag = "p" || tag = "item" || tag = "ab" then out.Add EvParaBreak
            if tag = "quote" || tag = "q" then out.Add (EvText " “")
            let was = inQuote
            if tag = "quote" || tag = "lg" then inQuote <- true
            for c in childList el do
                if c.nodeType = 3.0 then
                    out.Add (EvText (c.nodeValue: string))
                elif c.nodeType = 1.0 then
                    let ce = c :?> Element
                    if ce.localName = "l" && inQuote then out.Add (EvText (" " + textOf ce + " /"))
                    else walk ce
            inQuote <- was
            if tag = "quote" || tag = "q" then out.Add (EvText "” ")
            if tag = "p" || tag = "item" || tag = "ab" then out.Add EvParaBreak
        else
            walkChildren el

    and walkChildren (el: Node) =
        for c in childList el do
            if c.nodeType = 3.0 then out.Add (EvText (c.nodeValue: string))
            elif c.nodeType = 1.0 then walk (c :?> Element)

    walk body
    List.ofSeq out

// ---------------------------------------------------------------------------
// known typos in the source files
// ---------------------------------------------------------------------------

/// A line number misprinted in a TEI file we fetch but do not control. Checked
/// against the text around it; `Path` (the numbered divs in force) pins the
/// fix to one place when the wrong number is right elsewhere in the file.
type private SourceFix = { Urn: string; Path: string list option; Wrong: string; Right: string }

let private sourceFixes: SourceFix list =
    [ // `<l n="4097">` right after card 407 and before line 410: the King's
      // "Surely there is need of deep and salutary counsel" is line 407.
      { Urn = "urn:cts:greekLit:tlg0085.tlg001.perseus-eng2"; Path = None; Wrong = "4097"; Right = "407" }
      // Book 16, card 266: a line marker "580" between 275 and 285, at "will
      // they hearken to thee", which is 16.280. "580" is correct in five other
      // books of the same file, hence the path.
      { Urn = "urn:cts:greekLit:tlg0012.tlg002.perseus-eng3"; Path = Some [ "16"; "266" ]; Wrong = "580"; Right = "280" } ]

/// Applies `sourceFixes` for the text `urn` to its tokenized events.
let fixKnownTypos (urn: string) (events: TeiEvent list) : TeiEvent list =
    match sourceFixes |> List.filter (fun f -> f.Urn = urn) with
    | [] -> events
    | fixes ->
        let mutable path: string list = []
        let fix (n: string option) =
            match n with
            | Some v ->
                match fixes |> List.tryFind (fun f -> f.Wrong = v && (f.Path.IsNone || f.Path = Some path)) with
                | Some f -> Some f.Right
                | None -> n
            | None -> n
        events
        |> List.map (fun ev ->
            match ev with
            | EvDiv p ->
                path <- p
                ev
            | EvMilestone(u, n) -> EvMilestone(u, fix n)
            | EvLine(n, t) -> EvLine(fix n, t)
            | _ -> ev)
