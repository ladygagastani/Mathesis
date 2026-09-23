module Lenses.Paleography

// How a passage looked before printing. None of this is a transcription of a
// real witness — it is the edited text re-set in the conventions scribes
// actually used, so a reader can learn to see through them to the words.

open System.Text.RegularExpressions
open Fable.Core
open Fable.Core.JsInterop
open Types

/// Uncial book-hand (papyri, the great codices): capitals only, no accents or
/// breathings, no word division, lunate sigma, iota adscript written in the
/// line, and punctuation reduced to the high point.
let diplomatic (text: string) : string =
    let d = text.Normalize(System.Text.NormalizationForm.FormD)
    let sb = System.Text.StringBuilder()
    for ch in d do
        if ch = 'ͅ' then sb.Append 'Ι' |> ignore                  // iota subscript → adscript
        elif ch >= '̀' && ch <= 'ͯ' then ()                  // accents, breathings
        elif ch = '’' || ch = '\'' || ch = 'ʼ' then ()                 // elision marks
        elif System.Char.IsWhiteSpace ch then ()                       // scriptio continua
        elif ch = ',' || ch = '.' || ch = ';' || ch = '·' || ch = ':' || ch = '·' then
            sb.Append '·' |> ignore
        elif ch = '⟦' || ch = '⟧' then ()
        else
            let up = System.Char.ToUpperInvariant ch
            sb.Append(if up = 'Σ' then 'Ϲ' else up) |> ignore
    Regex.Replace(sb.ToString(), "[^Ͱ-Ͽἀ-῿·]", "")

/// Byzantine minuscule as an early (9th–10th c.) scribe wrote it: lower case,
/// breathings and accents kept, iota *adscript* rather than subscript (the
/// subscript is a later convention), and no modern punctuation.
let minuscule (text: string) : string =
    let d = text.Normalize(System.Text.NormalizationForm.FormD)
    let sb = System.Text.StringBuilder()
    for ch in d do
        if ch = 'ͅ' then sb.Append 'ι' |> ignore
        elif ch = ',' || ch = ';' || ch = '·' || ch = '·' || ch = '.' then sb.Append '·' |> ignore
        elif ch = '⟦' || ch = '⟧' then ()
        else sb.Append(System.Char.ToLowerInvariant ch) |> ignore
    Regex.Replace(sb.ToString().Normalize(System.Text.NormalizationForm.FormC), @"\s+", " ").Trim()

// ---------------------------------------------------------------------------
// known witnesses
// ---------------------------------------------------------------------------

type Witness =
    { Shelfmark : string
      Date      : string
      Note      : string
      Manifest  : string }

/// Digitised manuscripts whose contents were checked against the holding
/// library's own catalogue record, keyed by author id. Deliberately short:
/// a wrong attribution here would teach the reader something false. Any
/// other IIIF manifest can be opened by URL from the lens.
let witnesses : Map<string, Witness list> =
    Map.ofList [
        "tlg7000",
        [ { Shelfmark = "Heidelberg, Cod. Pal. graec. 23"
            Date = "Constantinople, mid-10th century"
            Note = "The Palatine Anthology itself — the codex the Greek Anthology is named after. Its last quires are in Paris (suppl. gr. 384)."
            Manifest = "https://digi.ub.uni-heidelberg.de/diglit/iiif/cpgraec23/manifest.json" } ]
        "tlg0655",
        [ { Shelfmark = "Heidelberg, Cod. Pal. graec. 398"
            Date = "Constantinople, late 9th century"
            Note = "A collection of paradoxographers, geographers and mythographers; the only manuscript to preserve Parthenius' Erotika Pathemata."
            Manifest = "https://digi.ub.uni-heidelberg.de/diglit/iiif/cpgraec398/manifest.json" } ]
    ]

// ---------------------------------------------------------------------------
// IIIF manifests (Presentation API 2 and 3)
// ---------------------------------------------------------------------------

[<Emit("fetch($0).then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

[<Emit("Array.isArray($0)")>]
let private isArray (o: obj) : bool = jsNative

let rec private str (o: obj) : string =
    if isNull o then ""
    elif jsTypeof o = "string" then unbox o
    elif isArray o then
        let a: obj array = unbox o
        if a.Length = 0 then "" else
        let first = a.[0]
        if jsTypeof first = "string" then unbox first
        elif not (isNull first?("@value")) then unbox first?("@value")
        else ""
    elif jsTypeof o = "object" then
        // v3 language map: { "en": ["Folio 1r"] }
        let keys: string array = JS.Constructors.Object.keys o |> Array.ofSeq
        if keys.Length = 0 then "" else str (o?(keys.[0]))
    else string o

let private serviceId (svc: obj) : string =
    if isNull svc then ""
    else
        let s = if isArray svc then (unbox<obj array> svc |> Array.tryHead |> Option.defaultValue null) else svc
        if isNull s then ""
        elif not (isNull s?id) then unbox s?id
        elif not (isNull s?("@id")) then unbox s?("@id")
        else ""

/// Reads a manifest's title and every canvas's image service.
let load (url: string) : JS.Promise<string * IiifCanvas list> =
    promise {
        let! m = fetchJson url
        let title = str (if isNull m?label then null else m?label)
        let canvases: IiifCanvas list =
            if not (isNull m?sequences) then
                // Presentation 2: sequences[0].canvases[].images[0].resource.service
                let seqs: obj array = m?sequences
                let cs: obj array = if seqs.Length > 0 && not (isNull seqs.[0]?canvases) then seqs.[0]?canvases else [||]
                [ for c in cs do
                      let imgs: obj array = if isNull c?images then [||] else c?images
                      if imgs.Length > 0 then
                          let svc = serviceId imgs.[0]?resource?service
                          if svc <> "" then yield { Label = str c?label; Service = svc } ]
            elif not (isNull m?items) then
                // Presentation 3: items[].items[0].items[0].body.service
                let cs: obj array = m?items
                [ for c in cs do
                      let pages: obj array = if isNull c?items then [||] else c?items
                      if pages.Length > 0 && not (isNull pages.[0]?items) then
                          let annos: obj array = pages.[0]?items
                          if annos.Length > 0 then
                              let body = annos.[0]?body
                              let body = if isArray body then (unbox<obj array> body).[0] else body
                              let svc = serviceId body?service
                              if svc <> "" then yield { Label = str c?label; Service = svc } ]
            else []
        if canvases.IsEmpty then failwith "This manifest lists no images with a IIIF image service."
        return (if title = "" then url else title), canvases
    }
