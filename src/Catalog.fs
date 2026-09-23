module Catalog

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Types

// ---------------------------------------------------------------------------
// derived properties / filtering
// ---------------------------------------------------------------------------

let hasTranslation (w: Work) : bool =
    w.Texts |> List.exists (fun t -> t.Kind = Translation && t.Lang <> "grc")

let passesFilter (filter: WorksFilter) (w: Work) : bool =
    match filter with
    | FilterAll -> true
    | FilterTranslated -> hasTranslation w
    | FilterGreekOnly -> not (hasTranslation w)

/// The Greek-script title, when the work has a Greek text (mirrors `titleGrc` in the original).
let titleGrc (w: Work) : string option =
    w.Texts |> List.tryFind (fun t -> t.Lang = "grc") |> Option.map (fun t -> t.Label)

let workTitle (catalog: Catalog) (workId: string) : string option =
    catalog.WorkById.TryFind workId |> Option.map (fun w -> w.Title)

let authorOf (catalog: Catalog) (workId: string) : Author option =
    catalog.AuthorOfWork.TryFind workId

// ---------------------------------------------------------------------------
// search — mirrors the original `bindWorkSearch` ranking exactly:
// author-name match first, then title match, then a loose fallback across
// text labels/ids; capped at 40 results.
// ---------------------------------------------------------------------------

let searchWorks (catalog: Catalog) (filter: WorksFilter) (query: string) : (Author * Work) list =
    let f = query.Trim().ToLowerInvariant()
    if f = "" then
        []
    else
        let score (a: Author) (w: Work) : int option =
            let an = a.Name.ToLowerInvariant()
            let tn = w.Title.ToLowerInvariant()
            let grc = a.Grc |> Option.defaultValue "" |> fun s -> s.ToLowerInvariant()
            if an.StartsWith f then Some 0
            elif an.Contains f || grc.Contains f then Some 1
            elif tn.StartsWith f then Some 2
            elif tn.Contains f then Some 3
            elif (w.Texts |> List.exists (fun t -> t.Label.ToLowerInvariant().Contains f)) || w.Id.ToLowerInvariant().Contains f then Some 4
            else None

        catalog.Authors
        |> List.collect (fun a -> a.Works |> List.filter (passesFilter filter) |> List.map (fun w -> a, w))
        |> List.choose (fun (a, w) -> score a w |> Option.map (fun s -> a, w, s))
        |> List.sortBy (fun (_, _, s) -> s)
        |> List.truncate 40
        |> List.map (fun (a, w, _) -> a, w)

// ---------------------------------------------------------------------------
// boot: decode the embedded #catalog (gzip+base64) and #meta (JSON) blobs
// ---------------------------------------------------------------------------

let decodeEmbedded () : JS.Promise<Catalog * Meta> =
    promise {
        let catalogEl = document.getElementById "catalog"
        let metaEl = document.getElementById "meta"
        if isNullOrUndefined catalogEl || isNullOrUndefined metaEl then
            return failwith "Missing catalog/meta data in the page."
        elif not Interop.hasDecompressionStream then
            return failwith "This browser doesn't support DecompressionStream (needs current Chrome, Safari, Firefox, or Edge)."
        else
            let bytes = Interop.base64ToBytes (catalogEl.textContent.Trim())
            let! catalogJson = Interop.decompressToText "gzip" bytes
            match Json.parseCatalog catalogJson with
            | Error e -> return failwith ("Could not read the text catalogue: " + e)
            | Ok catalog ->
                match Json.parseMeta metaEl.textContent with
                | Error e -> return failwith ("Could not read author metadata: " + e)
                | Ok meta -> return catalog, meta
    }
