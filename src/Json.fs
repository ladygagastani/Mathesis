module Json

open Thoth.Json
open Types

// ---------------------------------------------------------------------------
// catalog (decompressed from the gzip+base64 "catalog" blob)
//
// shape: [{id,name,grc,works:[{id,title,lang,texts:[{urn,file,repo,src,kind,
//          lang,label,desc,kb,refs}]}]}]
// ---------------------------------------------------------------------------

let private decodeTextKind : Decoder<TextKind> =
    Decode.string
    |> Decode.map (function
        | "edition" -> Edition
        | "translation" -> Translation
        | "commentary" -> Commentary
        | other -> OtherKind other)

let private decodeTextMeta : Decoder<TextMeta> =
    Decode.object (fun get ->
        { Urn = get.Required.Field "urn" Decode.string
          Label = get.Required.Field "label" Decode.string
          Desc = get.Optional.Field "desc" Decode.string
          Lang = get.Required.Field "lang" Decode.string
          Kind = get.Required.Field "kind" decodeTextKind
          File = get.Required.Field "file" Decode.string
          Repo = get.Required.Field "repo" Decode.string
          Kb = get.Required.Field "kb" Decode.int
          Refs = get.Required.Field "refs" (Decode.list Decode.string) })

/// The catalogue gives some works only their Latin title. Where English
/// readers know the work by an English one (and the rest of the site already
/// uses it, as the Passage of the day does for the Meditations), use that.
/// Titles scholars also use in English (Deipnosophistae) stay as they are.
let private englishTitles =
    Map [ "tlg0562.tlg001", "Meditations"
          "tlg0004.tlg001", "Lives of the Eminent Philosophers"
          "tlg0018.tlg001", "On the Creation of the World"
          "tlg0057.tlg002", "On the Best Teaching"
          "tlg0086.tlg014", "History of Animals"
          "tlg0552.tlg001", "On the Sphere and Cylinder"
          "tlg0627.tlg003", "Prognostic"
          "tlg0641.tlg001", "Ephesian Tale"
          "tlg1799.tlg001", "Elements" ]

let private decodeWork (authorId: string) : Decoder<Work> =
    Decode.object (fun get ->
        let id = get.Required.Field "id" Decode.string
        { Id = id
          Title = englishTitles |> Map.tryFind id |> Option.defaultWith (fun () -> get.Required.Field "title" Decode.string)
          AuthorId = authorId
          Texts = get.Required.Field "texts" (Decode.list decodeTextMeta) })

let private decodeAuthor : Decoder<Author> =
    Decode.object (fun get ->
        let id = get.Required.Field "id" Decode.string
        { Id = id
          Name = get.Required.Field "name" Decode.string
          Grc = get.Optional.Field "grc" Decode.string
          Works = get.Required.Field "works" (Decode.list (decodeWork id)) })

let private buildCatalog (authors: Author list) : Catalog =
    let workById =
        authors
        |> List.collect (fun a -> a.Works)
        |> List.map (fun w -> w.Id, w)
        |> Map.ofList
    let authorOfWork =
        authors
        |> List.collect (fun a -> a.Works |> List.map (fun w -> w.Id, a))
        |> Map.ofList
    { Authors = authors; WorkById = workById; AuthorOfWork = authorOfWork }

let decodeCatalog : Decoder<Catalog> =
    Decode.list decodeAuthor |> Decode.map buildCatalog

let parseCatalog (json: string) : Result<Catalog, string> =
    Decode.fromString decodeCatalog json

// ---------------------------------------------------------------------------
// meta: {"eras":[{id,name,from,to}], "authors":{<id>: {name,grc,birth,death,
//        floruit,year,era,desc,wiki,q,place,occ,nworks,core?}}}
// ---------------------------------------------------------------------------

let private decodeEra : Decoder<Era> =
    Decode.object (fun get ->
        { Id = get.Required.Field "id" Decode.string
          Name = get.Required.Field "name" Decode.string
          From = get.Required.Field "from" Decode.int
          To = get.Required.Field "to" Decode.int })

let private decodeCoreArticle : Decoder<CoreArticle> =
    Decode.object (fun get ->
        { Summary = get.Required.Field "summary" Decode.string
          Timeline = get.Required.Field "timeline" (Decode.list (Decode.tuple2 Decode.int Decode.string))
          Manuscripts = get.Required.Field "manuscripts" Decode.string
          Variants = get.Required.Field "variants" Decode.string
          Editions = get.Required.Field "editions" (Decode.list Decode.string) })

let private decodeAuthorMeta : Decoder<AuthorMeta> =
    Decode.object (fun get ->
        { Name = get.Required.Field "name" Decode.string
          Grc = get.Optional.Field "grc" Decode.string
          Birth = get.Optional.Field "birth" Decode.int
          Death = get.Optional.Field "death" Decode.int
          Floruit = get.Optional.Field "floruit" Decode.int
          Year = get.Optional.Field "year" Decode.int
          Era = get.Optional.Field "era" Decode.string
          Desc = get.Optional.Field "desc" Decode.string
          Wiki = get.Optional.Field "wiki" Decode.string
          Q = get.Optional.Field "q" Decode.string
          Place = get.Optional.Field "place" Decode.string
          Occ = get.Optional.Field "occ" (Decode.list Decode.string) |> Option.defaultValue []
          NWorks = get.Required.Field "nworks" Decode.int
          Core = get.Optional.Field "core" decodeCoreArticle })

let decodeMeta : Decoder<Meta> =
    Decode.object (fun get ->
        { Eras = get.Required.Field "eras" (Decode.list decodeEra)
          Authors = get.Required.Field "authors" (Decode.keyValuePairs decodeAuthorMeta) |> Map.ofList })

let parseMeta (json: string) : Result<Meta, string> =
    Decode.fromString decodeMeta json
