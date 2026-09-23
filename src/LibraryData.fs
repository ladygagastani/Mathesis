module LibraryData

open System.Text.RegularExpressions
open Fable.Core
open Thoth.Json
open Types

[<Emit("Math.random().toString(36).slice(2,9) + Date.now().toString(36)")>]
let private newId (): string = jsNative

// ---------------------------------------------------------------------------
// favourites
// ---------------------------------------------------------------------------

let isFav (lib: Library) (workId: string) : bool = List.contains workId lib.Favs

let toggleFav (lib: Library) (workId: string) : Library =
    if isFav lib workId then
        { lib with Favs = lib.Favs |> List.filter (fun x -> x <> workId) }
    else
        { lib with Favs = lib.Favs @ [ workId ] }

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
        { lib with Marks = lib.Marks @ [ m ] }, m

let removeMark (lib: Library) (work: string) (ref_: string) : Library =
    { lib with Marks = lib.Marks |> List.filter (fun m -> not (m.Work = work && m.Ref = ref_)) }

let private updateMark (lib: Library) (work: string) (ref_: string) (f: Mark -> Mark) : Library =
    { lib with
        Marks = lib.Marks |> List.map (fun m -> if m.Work = work && m.Ref = ref_ then f m else m) }

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
    { lib with AuthorNotes = Map.add authorId text lib.AuthorNotes }

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

let private encodeMarkLink (l: MarkLink) =
    Encode.object [
        "work", Encode.string l.Work
        "ref", Encode.string l.Ref
        "label", (match l.Label with Some s -> Encode.string s | None -> Encode.nil)
    ]

let private encodeMark (m: Mark) =
    Encode.object [
        "id", Encode.string m.Id
        "work", Encode.string m.Work
        "ref", Encode.string m.Ref
        "label", Encode.string m.Label
        "snippet", Encode.string m.Snippet
        "note", Encode.string m.Note
        "links", Encode.list (List.map encodeMarkLink m.Links)
        "ts", Encode.float m.Ts
    ]

let exportJson (lib: Library) : string =
    Encode.toString 0 (
        Encode.object [
            "favs", Encode.list (List.map Encode.string lib.Favs)
            "marks", Encode.list (List.map encodeMark lib.Marks)
            "anotes", lib.AuthorNotes |> Map.toList |> List.map (fun (k, v) -> k, Encode.string v) |> Encode.object
        ])

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
        { Favs = get.Optional.Field "favs" (Decode.list Decode.string) |> Option.defaultValue []
          Marks = get.Required.Field "marks" (Decode.list decodeMarkImport)
          AuthorNotes =
            get.Optional.Field "anotes" (Decode.keyValuePairs Decode.string)
            |> Option.map Map.ofList
            |> Option.defaultValue Map.empty })

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
    { Favs = favs; Marks = current.Marks @ newMarks; AuthorNotes = authorNotes }
