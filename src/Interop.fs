module Interop

open Fable.Core
open Fable.Core.JsInterop

// --- base64 / bytes -------------------------------------------------------
[<Emit("Uint8Array.from(atob($0.trim()), c => c.charCodeAt(0))")>]
let base64ToBytes (s: string) : JS.Uint8Array = jsNative

// --- streaming decompression (gzip for catalog, deflate-raw for ZIP) -----
[<Emit("'DecompressionStream' in window")>]
let hasDecompressionStream : bool = jsNative

[<Emit("new Response(new Blob([$1]).stream().pipeThrough(new DecompressionStream($0))).text()")>]
let decompressToText (format: string) (bytes: obj) : JS.Promise<string> = jsNative

[<Emit("new Response($1.stream().pipeThrough(new DecompressionStream($0))).text()")>]
let decompressBlobToText (format: string) (blob: Browser.Types.Blob) : JS.Promise<string> = jsNative

// --- XML ------------------------------------------------------------------
[<Emit("new DOMParser().parseFromString($0, 'application/xml')")>]
let parseXml (text: string) : Browser.Types.Document = jsNative

// --- regexes that .NET Regex cannot express identically -------------------
/// /[Ͱ-Ͽἀ-῿][Ͱ-Ͽἀ-῿̀-ͯ'ʼ’]*/g
[<Emit("$0.match(/[\\u0370-\\u03FF\\u1F00-\\u1FFF][\\u0370-\\u03FF\\u1F00-\\u1FFF\\u0300-\\u036F'\\u02BC\\u2019]*/gu) || []")>]
let greekWords (s: string) : string array = jsNative

/// exec-style tokenizer used by Views.Reader.tokens (returns index+length pairs)
[<Emit("(() => { const re=/[\\u0370-\\u03FF\\u1F00-\\u1FFF][\\u0370-\\u03FF\\u1F00-\\u1FFF\\u0300-\\u036F'\\u02BC\\u2019]*|⟦[^⟧]+⟧/gu; const out=[]; let m; while((m=re.exec($0))) out.push([m.index, m[0]]); return out; })()")>]
let scanTokens (s: string) : (int * string) array = jsNative
