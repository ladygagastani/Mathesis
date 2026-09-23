module Lenses.Places

// Finds the places a passage names. The translation is where names are easy to
// spot — capitalised, in a language Wikidata labels well — so candidates come
// from there and are resolved against Wikidata, keeping only items that carry
// a Pleiades id (P1584). That one filter does most of the work: Pleiades lists
// ancient places, so "Hector" and "Zeus" drop out while "Troy" and "Pylos" stay.

open Fable.Core
open Fable.Core.JsInterop
open System.Text.RegularExpressions
open Types

[<Emit("fetch($0, { headers: { 'Accept': 'application/sparql-results+json' } }).then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

/// Capitalised words that are never places — sentence openers, pronouns,
/// titles. Anything else is only a candidate; Wikidata decides.
let private notNames =
    set [
        "The"; "And"; "But"; "For"; "Then"; "Now"; "When"; "Thus"; "This"; "That"; "There"; "These"; "Those"
        "His"; "Her"; "Him"; "She"; "They"; "Them"; "Their"; "Who"; "What"; "Why"; "How"; "Where"; "Which"
        "Yet"; "Nor"; "Not"; "Nay"; "Yea"; "Lord"; "Lady"; "King"; "Queen"; "God"; "Gods"; "Father"; "Mother"
        "Son"; "Sons"; "Book"; "Chapter"; "Come"; "Let"; "Say"; "Tell"; "See"; "Behold"; "Even"; "With"
        "From"; "Into"; "Upon"; "After"; "Before"; "While"; "Since"; "Our"; "Your"; "You"; "Its"; "All"
        "One"; "Some"; "Many"; "Most"; "Other"; "Such"; "Have"; "Had"; "Has"; "Was"; "Were"; "Are"; "Our"
        "Here"; "Once"; "Again"; "Also"; "Only"; "Muse"; "Fate"; "Night"; "Dawn"; "Sun"; "Earth"; "Heaven"
        // Gods and heroes. Several share a spelling with some village or
        // island in Wikidata ("Athene"), and none of them is a place.
        "Zeus"; "Jove"; "Jupiter"; "Hera"; "Juno"; "Athene"; "Athena"; "Minerva"; "Pallas"; "Apollo"; "Phoebus"
        "Poseidon"; "Neptune"; "Aphrodite"; "Venus"; "Ares"; "Mars"; "Hermes"; "Mercury"; "Hephaestus"; "Vulcan"
        "Artemis"; "Diana"; "Thetis"; "Dionysus"; "Bacchus"; "Demeter"; "Persephone"; "Hades"; "Pluto"; "Iris"
        "Heracles"; "Hercules"; "Achilles"; "Agamemnon"; "Hector"; "Priam"; "Paris"; "Helen"; "Odysseus"; "Ulysses"
        "Nestor"; "Ajax"; "Diomedes"; "Patroclus"; "Menelaus"; "Aeneas"; "Chryses"; "Calchas"; "Briseis"
        "Telemachus"; "Penelope"; "Circe"; "Calypso"; "Polyphemus"; "Cyclops"; "Eos"; "Helios"; "Cronos"; "Kronos"
        // The world-river of myth, which Wikidata files under the Atlantic.
        "Oceanus"; "Ocean"
    ]

let private nameRe = Regex(@"\b[A-Z][a-z]{2,}(?:[’'][a-z]+)?\b")

let private plainText (blocks: Block list) =
    blocks
    |> List.map (function
        | Heading _ -> ""
        | Prose(_, t) -> t
        | Verse(_, lines) -> lines |> List.map snd |> String.concat " ")
    |> String.concat " "

/// Candidate names in a run of passages, with the passages each occurs in.
let candidates (segs: Segment array) : (string * string list) list =
    let found = System.Collections.Generic.Dictionary<string, ResizeArray<string>>()
    let order = ResizeArray<string>()
    for s in segs do
        match s.Eng with
        | Some blocks ->
            for m in nameRe.Matches(plainText blocks) do
                let w = Regex.Replace(m.Value, @"[’'].*$", "")
                if not (notNames.Contains w) then
                    match found.TryGetValue w with
                    | true, l -> if l.[l.Count - 1] <> s.Ref then l.Add s.Ref
                    | _ ->
                        found.[w] <- ResizeArray [ s.Ref ]
                        order.Add w
        | None -> ()
    [ for w in order -> w, List.ofSeq found.[w] ]

let private sparql (names: string list) : string =
    let values = names |> List.map (fun n -> "\"" + n.Replace("\"", "") + "\"@en") |> String.concat " "
    "SELECT ?name ?item ?itemLabel ?coord ?pleiades ?links WHERE { VALUES ?name { " + values + " } "
    + "?item rdfs:label|skos:altLabel ?name . ?item wdt:P1584 ?pleiades ; wdt:P625 ?coord ; wikibase:sitelinks ?links . "
    + "SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\". } }"

let private coordRe = Regex(@"Point\(([-\d.eE]+) ([-\d.eE]+)\)")

/// Resolves candidate names to places. A name that matches several ancient
/// places (Thebes, Ida) goes to the best-known one — the one with most
/// Wikipedia articles — which is right far more often than not.
let resolve (cands: (string * string list) list) : JS.Promise<PlaceHit list> =
    promise {
        // Frequent names first, so the cap never drops a place the passage
        // keeps returning to.
        let names = cands |> List.sortByDescending (fun (_, refs) -> refs.Length) |> List.truncate 150 |> List.map fst
        if names.IsEmpty then return []
        else
            let url = "https://query.wikidata.org/sparql?format=json&query=" + enc (sparql names)
            let! json = fetchJson url
            let rows: obj array = json?results?bindings
            let v (row: obj) (k: string) : string = if isNull (row?(k)) then "" else row?(k)?value
            let byName =
                rows
                |> Array.choose (fun r ->
                    let m = coordRe.Match(v r "coord")
                    if not m.Success then None
                    else
                        let qid = (v r "item").Split('/') |> Array.last
                        Some(v r "name", (int (v r "links")), (qid, v r "itemLabel", v r "pleiades", float m.Groups.[2].Value, float m.Groups.[1].Value)))
                |> Array.groupBy (fun (n, _, _) -> n)
                // One item can come back once per coordinate statement.
                |> Array.map (fun (n, xs) -> n, xs |> Array.distinctBy (fun (_, _, (qid, _, _, _, _)) -> qid))
            // A name with one ancient referent anchors the passage; a name with
            // several (Thebes in Boeotia or in Egypt, Ida in Crete or the Troad)
            // goes to the one nearest those anchors. Only with no anchors at all
            // does fame — Wikipedia sitelinks — decide.
            let anchors =
                byName |> Array.filter (fun (_, xs) -> xs.Length = 1) |> Array.map (fun (_, xs) -> let (_, _, (_, _, _, lat, lon)) = xs.[0] in lat, lon)
            let centre =
                if anchors.Length = 0 then None
                else Some(anchors |> Array.averageBy fst, anchors |> Array.averageBy snd)
            let best =
                byName
                |> Array.map (fun (n, xs) ->
                    // An item whose main label *is* the name beats one that
                    // only lists it as an alias.
                    let exact = xs |> Array.filter (fun (_, _, (_, label, _, _, _)) -> label = n)
                    let xs = if exact.Length > 0 then exact else xs
                    let pick =
                        match centre with
                        | Some(clat, clon) when xs.Length > 1 ->
                            xs |> Array.minBy (fun (_, _, (_, _, _, lat, lon)) -> (lat - clat) ** 2.0 + (lon - clon) ** 2.0)
                        | _ -> xs |> Array.maxBy (fun (_, links, _) -> links)
                    n, (let (_, _, p) = pick in p))
                |> Map.ofArray
            return
                cands
                |> List.choose (fun (name, refs) ->
                    best.TryFind name
                    |> Option.map (fun (qid, label, pl, lat, lon) ->
                        { Name = name; Label = label; Lat = lat; Lon = lon; Pleiades = pl; Qid = qid; Refs = refs }))
    }
