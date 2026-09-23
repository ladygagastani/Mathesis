module Zip

open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types
open Types

// ---------------------------------------------------------------------------
// low-level binary interop — kept local to this file since nothing else needs
// raw DataView/Uint8Array access
// ---------------------------------------------------------------------------

[<Emit("new DataView($0.buffer, $0.byteOffset)")>]
let private dataViewOf (bytes: JS.Uint8Array) : obj = jsNative

[<Emit("$0.getUint16($1, true)")>]
let private getU16 (dv: obj) (offset: int) : int = jsNative

[<Emit("$0.getUint32($1, true)")>]
let private getU32 (dv: obj) (offset: int) : float = jsNative

[<Emit("Number($0.getBigUint64($1, true))")>]
let private getU64AsFloat (dv: obj) (offset: int) : float = jsNative

[<Emit("$0[$1]")>]
let private byteAt (bytes: JS.Uint8Array) (i: int) : int = jsNative

[<Emit("$0.length")>]
let private byteLength (bytes: JS.Uint8Array) : int = jsNative

[<Emit("new TextDecoder().decode($0.subarray($1, $2))")>]
let private decodeAscii (bytes: JS.Uint8Array) (start: int) (end_: int) : string = jsNative

[<Emit("new Uint8Array($0)")>]
let private toUint8Array (buf: obj) : JS.Uint8Array = jsNative

let private sentinelU32 = 4294967295.0 // 0xffffffff
let private cdSignature = float 0x02014b50
let private lfhSignature = float 0x04034b50

let private baseName (path: string) : string =
    let parts = path.Split('/')
    parts.[parts.Length - 1]

type ZipEntry = { Offset: int; CSize: int; Method: int }

type ZipIndex = { File: File; Entries: Map<string, ZipEntry>; Repo: RepoKey; Name: string }

/// Reads a ZIP's central directory into an in-memory index (mirrors the original
/// `zipIndex` 1:1, including the ZIP64 fallback path). Only `.xml` files under a
/// `/data/` path are indexed by name — unless ZIP64 extra fields were needed to
/// resolve their offset/size, in which case they're indexed regardless of path,
/// matching the original's exact (slightly inconsistent) behavior.
let buildIndex (file: File) : JS.Promise<ZipIndex> =
    promise {
        let tail = min file.size 70000
        let! tailBuf = file.slice(file.size - tail, file.size).arrayBuffer ()
        let buf = toUint8Array tailBuf

        let mutable eocd = -1
        let mutable i = (byteLength buf) - 22
        while eocd < 0 && i >= 0 do
            if byteAt buf i = 0x50 && byteAt buf (i + 1) = 0x4b && byteAt buf (i + 2) = 0x05 && byteAt buf (i + 3) = 0x06 then
                eocd <- i
            i <- i - 1
        if eocd < 0 then failwith "This file is not a ZIP archive."

        let dv = dataViewOf buf
        let mutable count = getU16 dv (eocd + 10)
        let mutable cdSize = getU32 dv (eocd + 12)
        let mutable cdOff = getU32 dv (eocd + 16)

        if count = 0xffff || cdOff = sentinelU32 then
            let mutable loc = -1
            let mutable j = eocd - 20
            while loc < 0 && j >= 0 do
                if byteAt buf j = 0x50 && byteAt buf (j + 1) = 0x4b && byteAt buf (j + 2) = 0x06 && byteAt buf (j + 3) = 0x07 then
                    loc <- j
                j <- j - 1
            if loc < 0 then failwith "Unsupported ZIP64 archive."
            let z64off = getU64AsFloat dv (loc + 8)
            let! z64Buf = file.slice(int z64off, int z64off + 56).arrayBuffer ()
            let zdv = dataViewOf (toUint8Array z64Buf)
            count <- int (getU64AsFloat zdv 32)
            cdSize <- getU64AsFloat zdv 40
            cdOff <- getU64AsFloat zdv 48

        let! cdBuf = file.slice(int cdOff, int cdOff + int cdSize).arrayBuffer ()
        let cd = toUint8Array cdBuf
        let cdv = dataViewOf cd
        let cdLen = byteLength cd

        let idx = Dictionary<string, ZipEntry>()
        let mutable repo: RepoKey option = None
        let mutable p = 0
        let mutable n = 0
        let mutable stop = false
        while not stop && n < count && p + 46 <= cdLen do
            if getU32 cdv p <> cdSignature then
                stop <- true
            else
                let method_ = getU16 cdv (p + 10)
                let csize = getU32 cdv (p + 20)
                let usize = getU32 cdv (p + 24)
                let nlen = getU16 cdv (p + 28)
                let elen = getU16 cdv (p + 30)
                let clen = getU16 cdv (p + 32)
                let off = getU32 cdv (p + 42)
                let name = decodeAscii cd (p + 46) (p + 46 + nlen)

                if csize = sentinelU32 || off = sentinelU32 then
                    let mutable q = p + 46 + nlen
                    let endQ = q + elen
                    let mutable cs = csize
                    let mutable ofv = off
                    while q + 4 <= endQ do
                        let id = getU16 cdv q
                        let sz = getU16 cdv (q + 2)
                        if id = 1 then
                            let mutable r = q + 4
                            if usize = sentinelU32 then r <- r + 8
                            if csize = sentinelU32 then
                                cs <- getU64AsFloat cdv r
                                r <- r + 8
                            if off = sentinelU32 then
                                ofv <- getU64AsFloat cdv r
                        q <- q + 4 + sz
                    idx.[baseName name] <- { Offset = int ofv; CSize = int cs; Method = method_ }
                elif name.EndsWith(".xml") && name.Contains("/data/") then
                    idx.[baseName name] <- { Offset = int off; CSize = int csize; Method = method_ }

                if repo.IsNone && System.Text.RegularExpressions.Regex.IsMatch(name, "^[^/]*canonical-greekLit", System.Text.RegularExpressions.RegexOptions.IgnoreCase) then
                    repo <- Some Perseus
                if repo.IsNone && System.Text.RegularExpressions.Regex.IsMatch(name, "^[^/]*First1KGreek", System.Text.RegularExpressions.RegexOptions.IgnoreCase) then
                    repo <- Some First1K

                p <- p + 46 + nlen + elen + clen
                n <- n + 1

        let repoFinal =
            match repo with
            | Some r -> Some r
            | None ->
                idx.Keys
                |> Seq.tryPick (fun k ->
                    if k.Contains "1st1K" then Some First1K
                    elif k.Contains "perseus-" then Some Perseus
                    else None)

        match repoFinal with
        | Some r when idx.Count > 0 ->
            return { File = file; Entries = [ for kv in idx -> kv.Key, kv.Value ] |> Map.ofList; Repo = r; Name = file.name }
        | _ -> return failwith "This ZIP does not look like canonical-greekLit or First1KGreek."
    }

/// Reads and decompresses one entry by its base file name (mirrors `zipRead`);
/// `None` when the entry isn't in the index.
let readEntry (idx: ZipIndex) (fileName: string) : JS.Promise<string option> =
    promise {
        match idx.Entries.TryFind fileName with
        | None -> return None
        | Some e ->
            let! lhBuf = idx.File.slice(e.Offset, e.Offset + 30).arrayBuffer ()
            let lhDv = dataViewOf (toUint8Array lhBuf)
            if getU32 lhDv 0 <> lfhSignature then
                return failwith "Corrupt ZIP entry"
            else
                let nlen = getU16 lhDv 26
                let elen = getU16 lhDv 28
                let start = e.Offset + 30 + nlen + elen
                let blob = idx.File.slice (start, start + e.CSize)
                if e.Method = 0 then
                    let! text = blob.text ()
                    return Some text
                elif e.Method <> 8 then
                    return failwith "Unsupported compression in ZIP"
                else
                    let! text = Interop.decompressBlobToText "deflate-raw" blob
                    return Some text
    }
