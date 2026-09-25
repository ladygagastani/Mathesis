/// The small Markdown subset the "Start here" pages are written in: headings,
/// paragraphs, rules, block quotes, tables, (nested) lists and
/// `<details><summary>…</summary> … </details>`, with **strong**, *emphasis*,
/// <u>underline</u>, [links](…) and `\*` escapes inline. Pure parsing only; Views.Guide renders it.
module Markdown

open System.Text.RegularExpressions

type Inline =
    | Text of string
    | Strong of Inline list
    | Em of Inline list
    /// `<u>…</u>`: the letters of an example word that make the sound
    /// ("as in *c<u>u</u>p*")
    | Under of Inline list
    | Link of href: string * Inline list

type Block =
    | Heading of level: int * Inline list
    | Para of Inline list
    /// A quote's lines, already regrouped: a line that opens with a quotation
    /// mark or a parenthesis starts a new visual line (translation, reference);
    /// anything else continues the line before it.
    | Quote of Inline list list
    /// A block quote with no Greek, translation or reference in it: a tip or
    /// instruction, whose lines are ordinary Markdown (paragraphs, lists)
    | Note of Block list
    | Bullets of Block list list
    /// `start` is the first item's number, so a list split by a heading keeps counting
    | Numbered of start: int * Block list list
    | Table of header: Inline list list * rows: Inline list list list
    | Details of summary: string * Block list
    | Rule

// ---------------------------------------------------------------------------
// inline
// ---------------------------------------------------------------------------

let rec parseInline (s: string) : Inline list =
    let out = ResizeArray<Inline>()
    let buf = System.Text.StringBuilder()
    let flush () =
        if buf.Length > 0 then
            out.Add(Text(buf.ToString()))
            buf.Clear() |> ignore
    let mutable i = 0
    while i < s.Length do
        let c = s.[i]
        if c = '\\' && i + 1 < s.Length && "*_[]()\\`".Contains(string s.[i + 1]) then
            buf.Append(s.[i + 1]) |> ignore
            i <- i + 2
        elif c = '*' && i + 1 < s.Length && s.[i + 1] = '*' then
            let close = s.IndexOf("**", i + 2)
            if close > i + 2 then
                flush ()
                out.Add(Strong(parseInline (s.Substring(i + 2, close - i - 2))))
                i <- close + 2
            else
                buf.Append("**") |> ignore
                i <- i + 2
        elif c = '*' && i + 1 < s.Length && s.[i + 1] <> ' ' then
            // the closing * is the next single one not preceded by a space
            // and not part of a ** pair
            let mutable j = i + 1
            let mutable found = -1
            while found < 0 && j < s.Length do
                if s.[j] = '\\' then j <- j + 2
                elif s.[j] = '*' && j + 1 < s.Length && s.[j + 1] = '*' then
                    let close = s.IndexOf("**", j + 2)
                    j <- if close < 0 then s.Length else close + 2
                elif s.[j] = '*' && s.[j - 1] <> ' ' then found <- j
                else j <- j + 1
            if found > 0 then
                flush ()
                out.Add(Em(parseInline (s.Substring(i + 1, found - i - 1))))
                i <- found + 1
            else
                buf.Append(c) |> ignore
                i <- i + 1
        elif c = '<' && i + 3 <= s.Length && s.Substring(i, 3) = "<u>" && s.IndexOf("</u>", i + 3) > 0 then
            let close = s.IndexOf("</u>", i + 3)
            flush ()
            out.Add(Under(parseInline (s.Substring(i + 3, close - i - 3))))
            i <- close + 4
        elif c = '[' then
            let m = Regex.Match(s.Substring(i), @"^\[((?:[^\[\]]|\[[^\]]*\])*)\]\(([^)\s]+)\)")
            if m.Success then
                flush ()
                out.Add(Link(m.Groups.[2].Value, parseInline m.Groups.[1].Value))
                i <- i + m.Length
            else
                buf.Append(c) |> ignore
                i <- i + 1
        else
            buf.Append(c) |> ignore
            i <- i + 1
    flush ()
    List.ofSeq out

// ---------------------------------------------------------------------------
// blocks
// ---------------------------------------------------------------------------

let private headingRe = Regex(@"^(#{1,6})\s+(.*)$")
let private bulletRe = Regex(@"^([-*])\s+(.*)$")
let private numberRe = Regex(@"^(\d+)\.\s+(.*)$")
let private detailsRe = Regex(@"^<details>\s*<summary>(.*?)</summary>\s*$")

let private indentOf (line: string) = line.Length - line.TrimStart(' ').Length
let private isBlank (line: string) = line.Trim() = ""

let private tableCells (line: string) : Inline list list =
    let t = line.Trim()
    let t = if t.StartsWith "|" then t.Substring 1 else t
    let t = if t.EndsWith "|" then t.Substring(0, t.Length - 1) else t
    t.Split('|') |> Array.map (fun c -> parseInline (c.Trim())) |> List.ofArray

/// Regroups a quote's source lines into visual lines (see `Quote`).
let private quoteLines (lines: string list) : Inline list list =
    lines
    |> List.fold
        (fun (acc: string list) (l: string) ->
            let l = l.Trim()
            let startsNew = l.StartsWith "\"" || l.StartsWith "(" || l.StartsWith "“"
            match acc with
            | prev :: rest when not startsNew && l <> "" -> (prev + " " + l) :: rest
            | _ when l = "" -> acc
            | _ -> l :: acc)
        []
    |> List.rev
    |> List.map parseInline

let rec parseBlocks (lines: string list) : Block list =
    let arr = Array.ofList lines
    let n = arr.Length
    let blocks = ResizeArray<Block>()
    let mutable i = 0
    // A list item runs until a line that is neither blank nor indented past
    // the marker; blank lines inside it are kept when the item goes on after them.
    let collectList (markerRe: Regex) (markerWidth: int) : Block list list =
        let items = ResizeArray<Block list>()
        let mutable go = true
        while go && i < n && markerRe.IsMatch arr.[i] do
            let m = markerRe.Match arr.[i]
            let body = ResizeArray<string>([ m.Groups.[2].Value ])
            i <- i + 1
            let mutable inItem = true
            while inItem && i < n do
                let l = arr.[i]
                if isBlank l then
                    // keep the blank only if the item continues afterwards
                    let mutable k = i
                    while k < n && isBlank arr.[k] do k <- k + 1
                    if k < n && indentOf arr.[k] >= 2 then
                        body.Add ""
                        i <- i + 1
                    else
                        inItem <- false
                elif indentOf l >= 2 then
                    let strip = min (indentOf l) markerWidth
                    body.Add(l.Substring strip)
                    i <- i + 1
                else
                    inItem <- false
            items.Add(parseBlocks (List.ofSeq body))
            // blank lines between items of the same list are allowed
            let mutable k = i
            while k < n && isBlank arr.[k] do k <- k + 1
            if k < n && markerRe.IsMatch arr.[k] then i <- k else go <- false
        List.ofSeq items
    while i < n do
        let line = arr.[i]
        if isBlank line then
            i <- i + 1
        elif headingRe.IsMatch line then
            let m = headingRe.Match line
            blocks.Add(Heading(m.Groups.[1].Value.Length, parseInline (m.Groups.[2].Value.Trim())))
            i <- i + 1
        elif Regex.IsMatch(line, @"^\s*-{3,}\s*$") then
            blocks.Add Rule
            i <- i + 1
        elif detailsRe.IsMatch(line.Trim()) then
            let summary = (detailsRe.Match(line.Trim())).Groups.[1].Value
            let inner = ResizeArray<string>()
            i <- i + 1
            while i < n && arr.[i].Trim() <> "</details>" do
                inner.Add arr.[i]
                i <- i + 1
            i <- i + 1
            blocks.Add(Details(summary, parseBlocks (List.ofSeq inner)))
        elif line.TrimStart().StartsWith ">" then
            let q = ResizeArray<string>()
            while i < n && arr.[i].TrimStart().StartsWith ">" do
                q.Add(Regex.Replace(arr.[i].TrimStart(), @"^>\s?", ""))
                i <- i + 1
            // a quote opens with Greek, a translation in quotation marks or a
            // reference in brackets; anything else is a note
            let opensQuote =
                q
                |> Seq.tryFind (fun l -> l.Trim() <> "")
                |> Option.map (fun l -> l.TrimStart('*', ' '))
                |> Option.exists (fun l ->
                    l <> ""
                    && (let c = l.[0]
                        c = '"' || c = '“' || c = '(' || (c >= 'Ͱ' && c <= 'Ͽ') || (c >= 'ἀ' && c <= '῿')))
            if opensQuote then blocks.Add(Quote(quoteLines (List.ofSeq q)))
            else blocks.Add(Note(parseBlocks (List.ofSeq q)))
        elif line.TrimStart().StartsWith "|" then
            let rows = ResizeArray<string>()
            while i < n && arr.[i].TrimStart().StartsWith "|" do
                rows.Add arr.[i]
                i <- i + 1
            let isSep (r: string) = Regex.IsMatch(r, @"^\s*\|?[\s:\-|]+\|?\s*$")
            match List.ofSeq rows with
            | head :: sep :: body when isSep sep -> blocks.Add(Table(tableCells head, body |> List.map tableCells))
            | all -> blocks.Add(Table([], all |> List.map tableCells))
        elif bulletRe.IsMatch line then
            blocks.Add(Bullets(collectList bulletRe 2))
        elif numberRe.IsMatch line then
            let start = int (numberRe.Match line).Groups.[1].Value
            blocks.Add(Numbered(start, collectList numberRe 3))
        else
            let para = ResizeArray<string>()
            let startsBlock (l: string) =
                isBlank l || headingRe.IsMatch l || l.TrimStart().StartsWith ">" || l.TrimStart().StartsWith "|"
                || bulletRe.IsMatch l || numberRe.IsMatch l || detailsRe.IsMatch(l.Trim())
                || Regex.IsMatch(l, @"^\s*-{3,}\s*$")
            para.Add(line.Trim())
            i <- i + 1
            while i < n && not (startsBlock arr.[i]) do
                para.Add(arr.[i].Trim())
                i <- i + 1
            blocks.Add(Para(parseInline (String.concat " " para)))
    List.ofSeq blocks

/// The text of some inlines with the markup dropped.
let rec plainText (xs: Inline list) : string =
    xs
    |> List.map (function
        | Text s -> s
        | Strong ys | Em ys | Under ys | Link(_, ys) -> plainText ys)
    |> String.concat ""

let parse (text: string) : Block list =
    text.Replace("\r\n", "\n").Split('\n') |> List.ofArray |> parseBlocks

/// The text of the first level-1 heading, for page titles.
let title (text: string) : string =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.tryFind (fun l -> l.StartsWith "# ")
    |> Option.map (fun l -> l.Substring(2).Trim())
    |> Option.defaultValue ""
