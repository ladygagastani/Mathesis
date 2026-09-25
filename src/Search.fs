/// The header search: what a query finds, as grouped results that each carry
/// the messages that open them. Pure, so the view (which lists the results)
/// and State (which runs the highlighted one on Enter) compute the same list.
///
/// Matching ignores accents, breathings, case and final sigma, so "odysseus",
/// "λογος" and "Λόγος" all find what they should. Every word of the query must
/// appear (in any order). Recognised forms:
///   Iliad 1.33 · Il. 1.33 · Apology 17a · tlg0012.tlg001 1.1   → a passage
///   1.33 (while reading)                                        → a passage here
///   #simile                                                     → tagged bookmarks
///   any Greek word                                              → dictionary links
module Search

open System.Text.RegularExpressions
open Types

type Hit =
    { Key: string
      Group: string
      Title: string
      Grc: string
      Sub: string
      /// What choosing it does inside the app
      Msgs: Msg list
      /// ... or the site it opens in a new tab
      External: string option }

/// Groups in the order they are shown, with the scope chip each belongs to.
let groups: (string * string) list =
    [ "Go to", "texts"
      "Texts", "texts"
      "Authors", "authors"
      "My library", "mine"
      "Guide & wiki", "guide"
      "Look up", "all" ]

let scopes: (string * string) list =
    [ "all", "All"; "texts", "Texts"; "authors", "Authors"; "mine", "My library"; "guide", "Guide & wiki" ]

// ---------------------------------------------------------------------------
// folding
// ---------------------------------------------------------------------------

let private marks = Regex("[̀-ͯ]")

/// Lower case, without accents or breathings, final sigma as σ.
let fold (s: string) : string =
    if isNull s then ""
    else marks.Replace(s.Normalize(System.Text.NormalizationForm.FormD), "").ToLowerInvariant().Replace("ς", "σ")

let private words (s: string) = fold(s).Split([| ' '; '\t'; ','; '·' |], System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

let private hasAll (tokens: string list) (hay: string) = tokens |> List.forall (fun t -> hay.Contains t)

let private isGreek (s: string) = Regex.IsMatch(s, "[Ͱ-Ͽἀ-῿]")

/// The works people reach for first: everything the home page and the
/// Library's "where to start" recommend. They rank above obscure namesakes.
let private wellKnown: Set<string> =
    (Content.browse |> List.collect (fun b -> b.WorkIds))
    @ (Content.paths |> List.collect (fun p -> p.WorkIds))
    @ (Content.passages |> List.map (fun p -> p.Work))
    |> Set.ofList

// ---------------------------------------------------------------------------
// the catalogue, folded once
// ---------------------------------------------------------------------------

type private Entry = { Author: Author; Work: Work; Title: string; Name: string; Hay: string }

let mutable private index: Entry array = [||]
let mutable private indexedCount = -1

let private entries (catalog: Catalog) : Entry array =
    if indexedCount <> catalog.WorkById.Count then
        index <-
            catalog.Authors
            |> List.collect (fun a ->
                a.Works
                |> List.map (fun w ->
                    let grcTitle = Catalog.titleGrc w |> Option.defaultValue ""
                    { Author = a
                      Work = w
                      Title = fold w.Title
                      Name = fold a.Name
                      Hay = fold (String.concat " " [ w.Title; grcTitle; a.Name; a.Grc |> Option.defaultValue ""; w.Id ]) }))
            |> Array.ofList
        indexedCount <- catalog.WorkById.Count
    index

/// Common scholarly abbreviations for the works people cite most.
let private abbreviations: Map<string, string> =
    Map
        [ "il", "tlg0012.tlg001"; "iliad", "tlg0012.tlg001"
          "od", "tlg0012.tlg002"; "odyssey", "tlg0012.tlg002"
          "ap", "tlg0059.tlg002"; "apol", "tlg0059.tlg002"
          "rep", "tlg0059.tlg030"; "resp", "tlg0059.tlg030"
          "hdt", "tlg0016.tlg001"; "th", "tlg0003.tlg001"; "thuc", "tlg0003.tlg001"
          "theog", "tlg0020.tlg001"; "op", "tlg0020.tlg002"
          "anab", "tlg0032.tlg006"; "ag", "tlg0085.tlg005"; "agam", "tlg0085.tlg005" ]

let private readerHash (work: string) (ref_: string option) =
    Router.toHash (ReaderRoute(work, "", "", None, ref_))

let private go (hash: string) : Msg list = [ Navigate(hash, false) ]

// ---------------------------------------------------------------------------
// each group
// ---------------------------------------------------------------------------

let private refRe = Regex(@"^(.*?)[\s,:]+(\d+[a-e]?(?:[.:,]\d+[a-e]?)*)\s*$")
let private bareRefRe = Regex(@"^\d+[a-e]?(?:[.:,]\d+[a-e]?)*$")

let private normRef (r: string) = r.Replace(':', '.').Replace(',', '.')

let private scoreWork (q: string) (e: Entry) : int option =
    if e.Title = q || e.Work.Id = q then Some 0
    elif e.Title.StartsWith q then Some 1
    elif (e.Name + " " + e.Title).StartsWith q then Some 2
    elif e.Title.Contains(" " + q) then Some 3
    elif hasAll (words q) e.Hay then Some 4
    else None

let private passageHits (model: Model) (query: string) : Hit list =
    let q = query.Trim()
    let here =
        match model.Reader with
        | Some rm when bareRefRe.IsMatch q ->
            let r = normRef q
            [ { Key = "here:" + r
                Group = "Go to"
                Title = rm.Work.Title + " " + r
                Grc = ""
                Sub = "In the text you are reading"
                Msgs = [ Reader_(GotoRef r) ]
                External = None } ]
        | _ -> []
    let elsewhere =
        let m = refRe.Match q
        if not m.Success then []
        else
            let workPart = fold (m.Groups.[1].Value.Trim().TrimEnd('.'))
            let r = normRef m.Groups.[2].Value
            if workPart = "" then []
            else
                let fromAbbrev = abbreviations.TryFind workPart |> Option.bind model.Catalog.WorkById.TryFind
                let fromTitle =
                    entries model.Catalog
                    |> Array.choose (fun e -> scoreWork workPart e |> Option.map (fun s -> s, e.Work))
                    |> Array.sortBy fst
                    |> Array.truncate 3
                    |> Array.map snd
                    |> List.ofArray
                (Option.toList fromAbbrev @ fromTitle)
                |> List.distinctBy (fun w -> w.Id)
                |> List.truncate 3
                |> List.map (fun w ->
                    let author = Catalog.authorOf model.Catalog w.Id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
                    { Key = "ref:" + w.Id + ":" + r
                      Group = "Go to"
                      Title = w.Title + " " + r
                      Grc = ""
                      Sub = author
                      Msgs = go (readerHash w.Id (Some r))
                      External = None })
    here @ elsewhere

let private textHits (model: Model) (tokens: string list) (q: string) : Hit list =
    entries model.Catalog
    |> Array.choose (fun e -> scoreWork q e |> Option.map (fun s -> s, e))
    |> Array.sortBy (fun (s, e) ->
        s,
        // "homer" means Homer's own works before the Homeric Hymns
        (if e.Name = q then 0 else 1),
        (if wellKnown.Contains e.Work.Id then 0 else 1),
        (if Catalog.hasTranslation e.Work then 0 else 1),
        e.Title)
    |> Array.map (fun (_, e) ->
        let grc = Catalog.titleGrc e.Work |> Option.filter (fun g -> g <> e.Work.Title && isGreek g) |> Option.defaultValue ""
        { Key = "w:" + e.Work.Id
          Group = "Texts"
          Title = e.Work.Title
          Grc = grc
          Sub = e.Author.Name + (if Catalog.hasTranslation e.Work then "" else " · Greek only")
          Msgs = go (readerHash e.Work.Id None)
          External = None })
    |> List.ofArray

let private fmtYear (v: int) = if v < 0 then string (-v) + " BCE" else string v + " CE"

let private authorHits (model: Model) (tokens: string list) (q: string) : Hit list =
    model.Catalog.Authors
    |> List.choose (fun a ->
        let name = fold a.Name
        let hay = name + " " + fold (a.Grc |> Option.defaultValue "") + " " + a.Id
        let score =
            if name.StartsWith q then Some 0
            elif name.Contains(" " + q) then Some 1
            elif hasAll tokens hay then Some 2
            else None
        score |> Option.map (fun s -> s, a))
    |> List.sortBy (fun (s, a) -> s, (if fold a.Name = q then 0 else 1), -a.Works.Length, a.Name)
    |> List.map (fun (_, a) ->
        let meta = model.Meta.Authors.TryFind a.Id
        let dates =
            match meta with
            | Some m when m.Birth.IsSome || m.Death.IsSome ->
                (m.Birth |> Option.map fmtYear |> Option.defaultValue "?") + " – " + (m.Death |> Option.map fmtYear |> Option.defaultValue "?")
            | Some m when m.Floruit.IsSome -> "fl. " + fmtYear m.Floruit.Value
            | _ -> ""
        let n = a.Works.Length
        { Key = "a:" + a.Id
          Group = "Authors"
          Title = a.Name
          Grc = a.Grc |> Option.defaultValue ""
          Sub = (if dates = "" then "" else dates + " · ") + string n + (if n = 1 then " work" else " works")
          Msgs = go ("#author/" + a.Id)
          External = None })

let private mineHits (model: Model) (tokens: string list) (query: string) : Hit list =
    let lib = model.Library
    let title w = Catalog.workTitle model.Catalog w |> Option.defaultValue w
    let q = query.Trim()
    if q.StartsWith "#" then
        let t = q.Substring(1).ToLowerInvariant()
        LibraryData.allTags lib
        |> List.filter (fun (tag, _) -> t = "" || tag.StartsWith t)
        |> List.map (fun (tag, n) ->
            { Key = "tag:" + tag
              Group = "My library"
              Title = "#" + tag
              Grc = ""
              Sub = string n + (if n = 1 then " bookmark" else " bookmarks")
              Msgs = [ Library_(SetMarkTag(Some tag)); Navigate("#lib", false) ]
              External = None })
    else
        let marks =
            lib.Marks
            |> List.filter (fun m -> hasAll tokens (fold (String.concat " " [ title m.Work; m.Ref; m.Label; m.Snippet; m.Note ])))
            |> List.map (fun m ->
                let note = m.Note.Replace('\n', ' ').Trim()
                { Key = "m:" + m.Id
                  Group = "My library"
                  Title = title m.Work + " " + (if m.Label <> "" then m.Label else m.Ref)
                  Grc = ""
                  Sub = "Bookmark" + (if note = "" then "" else " · " + (if note.Length > 70 then note.Substring(0, 70) + "…" else note))
                  Msgs = go (readerHash m.Work (Some m.Ref))
                  External = None })
        let wordsFound =
            lib.Words
            |> List.filter (fun w -> hasAll tokens (fold (String.concat " " [ w.Word; w.Lemma; w.Gloss ])))
            |> List.map (fun w ->
                { Key = "wd:" + w.Id
                  Group = "My library"
                  Title = (if w.Gloss <> "" then w.Gloss else "Saved word")
                  Grc = w.Word + (if w.Lemma <> "" && w.Lemma <> w.Word then " (" + w.Lemma + ")" else "")
                  Sub = "Word · " + title w.Work + " " + w.Ref
                  Msgs = go "#lib/words"
                  External = None })
        let placesFound =
            lib.Places
            |> List.filter (fun p -> hasAll tokens (fold (p.Label + " " + p.Note)))
            |> List.map (fun p ->
                { Key = "pl:" + p.Qid
                  Group = "My library"
                  Title = p.Label
                  Grc = ""
                  Sub = "Place · named in " + string p.Seen.Length + (if p.Seen.Length = 1 then " passage" else " passages")
                  Msgs = go "#lib/places"
                  External = None })
        marks @ wordsFound @ placesFound

/// The guide's steps and the wiki's sections, with the words that find them.
let private guidePages (model: Model) : (string * string * string * string) list =
    let steps =
        GuideData.steps
        |> List.map (fun (i, p) -> sprintf "Step %d: %s" i p.Title, "Start here", p.Summary, GuideData.hashOf p.Slug)
    let eras =
        model.Meta.Eras
        |> List.map (fun e -> e.Name, "Wiki · Eras of Greek", sprintf "%s to %s" (fmtYear e.From) (fmtYear e.To), "#wiki/eras/" + e.Id)
    [ "Start here", "Guide", "A beginner's guide in eight steps, from the alphabet to word studies", "#start"
      "Authors", "Wiki", "Lives and timelines, by era and by genre", "#wiki/authors"
      "Eras of Greek", "Wiki", "From Homeric epic to Byzantine Greek", "#wiki/eras"
      "Manuscripts & transmission", "Wiki", "How the texts survived, papyri codices", "#wiki/manuscripts"
      "Textual variants", "Wiki", "Interpolations, disputed works, textual problems", "#wiki/variants"
      "Editions & translations", "Wiki", "The printed editions behind the texts", "#wiki/editions"
      "Everyday life", "Wiki", "How the Greeks lived: food, family, gods, music, medicine, games, war", "#wiki/life"
      "About & acknowledgments", "About", "Sources, licences, credits", "#about"
      "Forum", "Town hall", "Discuss passages, debate, ask, report bugs", "#forum"
      "Report a bug", "Forum", "Something broken or confusing", "#forum/bugs"
      "Your account", "Account", "Sign in, sync your library", "#account" ]
    @ steps
    @ eras
    @ (LifeData.pages |> List.map (fun p -> p.Title, "Wiki · Everyday life", p.Summary, LifeData.hashOf p.Slug))

/// The Everyday life articles' whole text, folded once: a search for
/// "black broth" or "Diogenes" finds the article that tells the story.
let private lifeBodies: Lazy<Map<string, string>> =
    lazy (LifeData.pages |> List.map (fun p -> LifeData.hashOf p.Slug, fold p.Markdown) |> Map.ofList)

let private guideHits (model: Model) (tokens: string list) : Hit list =
    guidePages model
    |> List.choose (fun (title, where, summary, hash) ->
        let t = fold title
        if hasAll tokens t then Some(0, title, where, summary, hash)
        elif hasAll tokens (t + " " + fold summary + " " + fold where) then Some(1, title, where, summary, hash)
        else
            match lifeBodies.Value.TryFind hash with
            // an article that only mentions the words ranks by how often it does
            | Some body when hasAll tokens body ->
                let mentions = tokens |> List.sumBy (fun tok -> body.Split([| tok |], System.StringSplitOptions.None).Length - 1)
                Some(1000 - min 997 mentions, title, where, summary, hash)
            | _ -> None)
    |> List.sortBy (fun (s, _, _, _, _) -> s)
    |> List.map (fun (_, title, where, summary, hash) ->
        { Key = "g:" + hash
          Group = "Guide & wiki"
          Title = title
          Grc = ""
          Sub = where + " · " + summary
          Msgs = go hash
          External = None })

[<Fable.Core.Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = Fable.Core.Util.jsNative

let private lookupHits (query: string) : Hit list =
    let w = query.Trim()
    if not (isGreek w) || w.Contains " " then []
    else
        let e = enc (w.Normalize(System.Text.NormalizationForm.FormC))
        [ "Logeion", "LSJ, Middle Liddell and more", "https://logeion.uchicago.edu/" + e
          "Perseus word study", "What form it is", "https://www.perseus.tufts.edu/hopper/morph?l=" + e + "&la=greek"
          "Wiktionary", "Every form, and its history", "https://en.wiktionary.org/wiki/" + e + "#Ancient_Greek" ]
        |> List.map (fun (site, sub, url) ->
            { Key = "x:" + site
              Group = "Look up"
              Title = site
              Grc = w
              Sub = sub
              Msgs = []
              External = Some url })

// ---------------------------------------------------------------------------
// all together
// ---------------------------------------------------------------------------

/// How many of each group show when searching everything.
let private perGroupAll = Map [ "Go to", 4; "Texts", 6; "Authors", 4; "My library", 4; "Guide & wiki", 3; "Look up", 3 ]

/// Every result for the query, grouped, before the scope and the limits.
let allHits (model: Model) (query: string) : Hit list =
    let q = fold (query.Trim())
    if q = "" then []
    else
        let tokens = words query
        let isTag = query.Trim().StartsWith "#"
        if isTag then mineHits model tokens query
        else
            let texts = textHits model tokens q
            let authors = authorHits model tokens q
            // a query that is an author's name ("plato", "homer") wants the
            // author first; one that is a title ("republic") the text
            let namesAuthor =
                model.Catalog.Authors |> List.exists (fun a -> fold a.Name = q || (fold a.Name).StartsWith(q + " "))
                && not (entries model.Catalog |> Array.exists (fun e -> e.Title = q))
            passageHits model query
            @ (if namesAuthor then authors @ texts else texts @ authors)
            @ mineHits model tokens query
            @ guideHits model tokens
            @ lookupHits query

let scopeOf (group: string) = groups |> List.tryFind (fun (g, _) -> g = group) |> Option.map snd |> Option.defaultValue "all"

/// What the dropdown lists: the scope applied, and in "all" a few per group.
let results (model: Model) : Hit list =
    let s = model.Search
    let hits = allHits model s.Query
    if s.Scope = "all" then
        hits
        |> List.groupBy (fun h -> h.Group)
        |> List.collect (fun (g, hs) -> hs |> List.truncate (perGroupAll.TryFind g |> Option.defaultValue 4))
    else
        hits |> List.filter (fun h -> scopeOf h.Group = s.Scope || h.Group = "Go to" && s.Scope = "texts") |> List.truncate 40

/// How many results each scope chip would show.
let scopeCounts (model: Model) : Map<string, int> =
    let hits = allHits model model.Search.Query
    scopes
    |> List.map (fun (id, _) ->
        id, (if id = "all" then hits.Length else hits |> List.filter (fun h -> scopeOf h.Group = id) |> List.length))
    |> Map.ofList

/// With an empty box: what you were reading, and what you searched for.
let suggestions (model: Model) : Hit list =
    let continuing =
        model.Recent
        |> List.truncate 3
        |> List.choose (fun r ->
            model.Catalog.WorkById.TryFind r.Id
            |> Option.map (fun w ->
                let hash = Router.toHash (ReaderRoute(w.Id, "", "", r.Chunk, None))
                { Key = "c:" + w.Id
                  Group = "Continue reading"
                  Title = w.Title + (match r.Chunk with Some c when c <> "all" -> " " + c | _ -> "")
                  Grc = ""
                  Sub = Catalog.authorOf model.Catalog w.Id |> Option.map (fun a -> a.Name) |> Option.defaultValue ""
                  Msgs = go hash
                  External = None }))
    let searched =
        model.Search.Recent
        |> List.map (fun q ->
            { Key = "s:" + q
              Group = "Recent searches"
              Title = q
              Grc = ""
              Sub = ""
              Msgs = [ Search_(SetSearchQuery q) ]
              External = None })
    continuing @ searched
