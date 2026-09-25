/// The optional server behind accounts, library sync and the forum: a Supabase
/// project, spoken to over its plain HTTP APIs (Auth for sign-in, PostgREST
/// for tables), so no client library is bundled. The site is built with the
/// project's address and public ("anon") key in `VITE_SUPABASE_URL` and
/// `VITE_SUPABASE_ANON_KEY`; without them `configured` is false and everything
/// here reports that instead of failing. The tables, and the row-level
/// security that decides who may read and write what, are in supabase/schema.sql.
///
/// Sign-in is by a one-time code sent by email (no passwords are kept here).
/// The session lives in this module so every request can renew an expired
/// token on the way; `onSessionChange` tells the app when that happens.
module Server

open Fable.Core
open Fable.Core.JsInterop
open Thoth.Json
open Types

[<Emit("(import.meta.env && import.meta.env.VITE_SUPABASE_URL || '').replace(/\\/+$/, '')")>]
let private baseUrl: string = jsNative

[<Emit("import.meta.env && import.meta.env.VITE_SUPABASE_ANON_KEY || ''")>]
let private anonKey: string = jsNative

let configured: bool = baseUrl <> "" && anonKey <> ""

[<Emit("Date.now()")>]
let private now (): float = jsNative

[<Emit("Date.parse($0) || 0")>]
let private parseTime (iso: string) : float = jsNative

[<Emit("encodeURIComponent($0)")>]
let private enc (s: string) : string = jsNative

/// fetch, reading the body as JSON when there is one. Never rejects on an HTTP
/// error status (only on a network failure): callers look at `ok`.
[<Emit("""fetch($0, $1).then(async r => {
  const t = await r.text(); let j = null;
  try { j = t ? JSON.parse(t) : null; } catch (e) { j = null; }
  return { ok: r.ok, status: r.status, json: j, text: t };
})""")>]
let private rawFetch (url: string) (init: obj) : JS.Promise<obj> = jsNative

type private Reply = { Ok: bool; Status: int; Json: obj }

let private request (url: string) (method_: string) (headers: (string * string) list) (body: obj option) : JS.Promise<Reply> =
    let h = createObj [ for k, v in ("apikey", anonKey) :: headers -> k ==> v ]
    let init =
        createObj [
            "method" ==> method_
            "headers" ==> h
            match body with
            | Some b -> "body" ==> JS.JSON.stringify b
            | None -> ()
        ]
    rawFetch url init
    |> Promise.map (fun r -> { Ok = unbox r?ok; Status = unbox r?status; Json = r?json })

/// The most useful words in an error reply from either API.
let private errorText (r: Reply) : string =
    let j = r.Json
    let pick (k: string) =
        if isNull j then None
        else match j?(k) with
             | s when isNullOrUndefined s -> None
             | s -> Some(string s)
    pick "error_description"
    |> Option.orElse (pick "msg")
    |> Option.orElse (pick "message")
    |> Option.orElse (pick "error")
    |> Option.defaultValue (sprintf "The server answered %d" r.Status)

let private networkError (e: exn) : string =
    "Couldn't reach the server (" + e.Message + "). Check your connection."

// ---------------------------------------------------------------------------
// session
// ---------------------------------------------------------------------------

let mutable private current: Session option = None
let mutable onSessionChange: Session option -> unit = ignore

let setSession (s: Session option) = current <- s
let session () = current

let private sessionOf (j: obj) (fallbackEmail: string) : Session option =
    if isNull j || isNull j?access_token then None
    else
        let user = j?user
        let expiresIn: float = if isNull j?expires_in then 3600.0 else unbox j?expires_in
        Some
            { AccessToken = unbox j?access_token
              RefreshToken = unbox j?refresh_token
              ExpiresAt = now () + expiresIn * 1000.0
              UserId = (if isNull user then "" else unbox user?id)
              Email = (if isNull user || isNull user?email then fallbackEmail else unbox user?email) }

let private refresh (s: Session) : JS.Promise<Session option> =
    request (baseUrl + "/auth/v1/token?grant_type=refresh_token") "POST" [ "Content-Type", "application/json" ]
        (Some(createObj [ "refresh_token" ==> s.RefreshToken ]))
    |> Promise.map (fun r -> if r.Ok then sessionOf r.Json s.Email else None)
    |> Promise.catch (fun _ -> Some s) // offline: keep what we have and let the request fail

/// The session with a token good for at least another minute, renewing it if
/// need be. A refresh the server refuses ends the session.
let private freshSession () : JS.Promise<Session option> =
    match current with
    | None -> Promise.lift None
    | Some s when s.ExpiresAt - 60000.0 > now () -> Promise.lift (Some s)
    | Some s ->
        refresh s
        |> Promise.map (fun renewed ->
            current <- renewed
            onSessionChange renewed
            renewed)

/// A request as the signed-in user (or as a visitor, for public reads).
let private rest (path: string) (method_: string) (extra: (string * string) list) (body: obj option) : JS.Promise<Reply> =
    freshSession ()
    |> Promise.bind (fun s ->
        let auth =
            match s with
            | Some s -> [ "Authorization", "Bearer " + s.AccessToken ]
            | None -> [ "Authorization", "Bearer " + anonKey ]
        request (baseUrl + "/rest/v1/" + path) method_ (auth @ [ "Content-Type", "application/json" ] @ extra) body)

let private needSession () : Result<Session, string> =
    match current with
    | Some s -> Ok s
    | None -> Error "Sign in first."

// ---------------------------------------------------------------------------
// sign-in by emailed code
// ---------------------------------------------------------------------------

/// Where the emailed sign-in link returns to: the site's front page.
let private siteUrl () : string = Router.siteRoot ()

/// Sends the one-time code (and a sign-in link) to `email`, creating the
/// account on first use.
let sendCode (email: string) : JS.Promise<Result<unit, string>> =
    request (baseUrl + "/auth/v1/otp?redirect_to=" + enc (siteUrl ())) "POST" [ "Content-Type", "application/json" ]
        (Some(createObj [ "email" ==> email; "create_user" ==> true ]))
    |> Promise.map (fun r ->
        if r.Ok then Ok()
        // Supabase caps how many emails an hour it sends (very few until the
        // project has its own email sender: see supabase/README.md)
        elif r.Status = 429 || (errorText r).ToLower().Contains "rate limit" then
            Error "Too many sign-in emails have been sent in the last hour, so this one wasn't. Please try again later. If an earlier email reached you, its code still works."
        else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

let verifyCode (email: string) (code: string) : JS.Promise<Result<Session, string>> =
    request (baseUrl + "/auth/v1/verify") "POST" [ "Content-Type", "application/json" ]
        (Some(createObj [ "type" ==> "email"; "email" ==> email; "token" ==> code ]))
    |> Promise.map (fun r ->
        if r.Ok then
            match sessionOf r.Json email with
            | Some s -> Ok s
            | None -> Error "The server didn't send a session back."
        else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

/// The sign-in link in the email returns to the site with the session in the
/// URL fragment (`#access_token=…&refresh_token=…&expires_in=…`). Returns the
/// tokens, if that is what the fragment holds.
let tokensInHash (hash: string) : (string * string * float) option =
    let h = if hash.StartsWith "#" then hash.Substring 1 else hash
    if not (h.Contains "access_token=") then None
    else
        let parts =
            h.Split('&')
            |> Array.choose (fun kv ->
                match kv.Split('=') with
                | [| k; v |] -> Some(k, JS.decodeURIComponent v)
                | _ -> None)
            |> Map.ofArray
        match parts.TryFind "access_token", parts.TryFind "refresh_token" with
        | Some a, Some r ->
            let exp = parts.TryFind "expires_in" |> Option.map float |> Option.defaultValue 3600.0
            Some(a, r, exp)
        | _ -> None

/// Completes a sign-in link: asks who the access token belongs to.
let sessionFromTokens (access: string) (refreshToken: string) (expiresIn: float) : JS.Promise<Result<Session, string>> =
    request (baseUrl + "/auth/v1/user") "GET" [ "Authorization", "Bearer " + access ] None
    |> Promise.map (fun r ->
        if r.Ok && not (isNull r.Json) then
            Ok
                { AccessToken = access
                  RefreshToken = refreshToken
                  ExpiresAt = now () + expiresIn * 1000.0
                  UserId = unbox r.Json?id
                  Email = (if isNull r.Json?email then "" else unbox r.Json?email) }
        else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

let signOut () : JS.Promise<unit> =
    match current with
    | Some s ->
        current <- None
        request (baseUrl + "/auth/v1/logout") "POST" [ "Authorization", "Bearer " + s.AccessToken ] None
        |> Promise.map ignore
        |> Promise.catch ignore
    | None -> Promise.lift ()

// ---------------------------------------------------------------------------
// profile: the name shown on forum posts
// ---------------------------------------------------------------------------

let loadProfile () : JS.Promise<Result<string * bool, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        rest ("profiles?select=display_name,is_admin&id=eq." + enc s.UserId) "GET" [] None
        |> Promise.map (fun r ->
            if not r.Ok then Error(errorText r)
            else
                let rows: obj array = unbox r.Json
                if rows.Length = 0 then Ok("", false)
                else
                    let row = rows.[0]
                    Ok((if isNull row?display_name then "" else unbox row?display_name), (row?is_admin = box true)))
        |> Promise.catch (fun e -> Error(networkError e))

let saveProfile (name: string) : JS.Promise<Result<unit, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        rest "profiles?on_conflict=id" "POST" [ "Prefer", "resolution=merge-duplicates,return=minimal" ]
            (Some(createObj [ "id" ==> s.UserId; "display_name" ==> name ]))
        |> Promise.map (fun r -> if r.Ok then Ok() else Error(errorText r))
        |> Promise.catch (fun e -> Error(networkError e))

// ---------------------------------------------------------------------------
// library sync: one JSON document per account
// ---------------------------------------------------------------------------

/// The library saved on the server, or the empty library if there is none yet.
let pullLibrary () : JS.Promise<Result<Library, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        rest ("libraries?select=data&user_id=eq." + enc s.UserId) "GET" [] None
        |> Promise.map (fun r ->
            if not r.Ok then Error(errorText r)
            else
                let rows: obj array = unbox r.Json
                if rows.Length = 0 then Ok Storage.defaultLibrary
                else
                    match Decode.fromValue "$" Storage.decodeLibrary rows.[0]?data with
                    | Ok lib -> Ok lib
                    | Error e -> Error("The saved library couldn't be read: " + e))
        |> Promise.catch (fun e -> Error(networkError e))

let pushLibrary (lib: Library) : JS.Promise<Result<float, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        let data = JS.JSON.parse (Storage.libraryToJson lib)
        rest "libraries?on_conflict=user_id" "POST" [ "Prefer", "resolution=merge-duplicates,return=minimal" ]
            (Some(createObj [ "user_id" ==> s.UserId; "data" ==> data ]))
        |> Promise.map (fun r -> if r.Ok then Ok(now ()) else Error(errorText r))
        |> Promise.catch (fun e -> Error(networkError e))

// ---------------------------------------------------------------------------
// forum
// ---------------------------------------------------------------------------

let private str (o: obj) (k: string) : string =
    match o?(k) with
    | v when isNullOrUndefined v -> ""
    | v -> string v

let private threadOf (o: obj) : ForumThread =
    { Id = str o "id"
      Category = str o "category"
      Title = str o "title"
      Body = str o "body"
      AuthorId = str o "author_id"
      AuthorName = (match str o "author_name" with "" -> "A reader" | n -> n)
      Work = str o "work"
      Ref = str o "ref"
      Status = str o "status"
      Created = parseTime (str o "created_at")
      LastPost = parseTime (str o "last_post_at")
      Replies = (match o?reply_count with v when isNullOrUndefined v -> 0 | v -> unbox v) }

let private postOf (o: obj) : ForumPost =
    { Id = str o "id"
      ThreadId = str o "thread_id"
      Body = str o "body"
      AuthorId = str o "author_id"
      AuthorName = (match str o "author_name" with "" -> "A reader" | n -> n)
      Created = parseTime (str o "created_at") }

let private rows (r: Reply) : obj array = if isNull r.Json then [||] else unbox r.Json

/// The threads of one board, liveliest first; "" lists the latest across all
/// boards except bug reports, which keep to their own.
let listThreads (category: string) : JS.Promise<Result<ForumThread list, string>> =
    let filter = if category = "" then "&category=neq.bugs" else "&category=eq." + enc category
    rest ("forum_threads?select=*" + filter + "&order=last_post_at.desc&limit=60") "GET" [] None
    |> Promise.map (fun r -> if r.Ok then Ok(rows r |> Array.map threadOf |> List.ofArray) else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

/// Threads about one passage (or, with ref "", about one work).
let threadsAbout (work: string) (ref_: string) : JS.Promise<Result<ForumThread list, string>> =
    let refFilter = if ref_ = "" then "" else "&ref=eq." + enc ref_
    rest ("forum_threads?select=*&work=eq." + enc work + refFilter + "&order=last_post_at.desc&limit=20") "GET" [] None
    |> Promise.map (fun r -> if r.Ok then Ok(rows r |> Array.map threadOf |> List.ofArray) else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

let loadThread (id: string) : JS.Promise<Result<ForumThread * ForumPost list, string>> =
    Promise.all [
        rest ("forum_threads?select=*&id=eq." + enc id) "GET" [] None
        rest ("forum_posts?select=*&thread_id=eq." + enc id + "&order=created_at.asc&limit=500") "GET" [] None
    ]
    |> Promise.map (fun rs ->
        let t, p = rs.[0], rs.[1]
        if not t.Ok then Error(errorText t)
        elif not p.Ok then Error(errorText p)
        else
            match rows t with
            | [||] -> Error "This thread has been removed."
            | ts -> Ok(threadOf ts.[0], rows p |> Array.map postOf |> List.ofArray))
    |> Promise.catch (fun e -> Error(networkError e))

/// Returns the new thread's id. The author's name is filled in by the server
/// from their profile, so it can't be set to someone else's here.
let createThread (d: ForumDraft) (body: string) : JS.Promise<Result<string, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        rest "forum_threads" "POST" [ "Prefer", "return=representation" ]
            (Some(
                createObj [
                    "category" ==> d.Category
                    "title" ==> d.Title.Trim()
                    "body" ==> body
                    "work" ==> d.Work
                    "ref" ==> d.Ref.Trim()
                    "status" ==> (if d.Category = "bugs" then "open" else "")
                    "author_id" ==> s.UserId
                ]
            ))
        |> Promise.map (fun r ->
            if r.Ok then
                match rows r with
                | [||] -> Error "The server didn't return the new thread."
                | xs -> Ok(str xs.[0] "id")
            else Error(errorText r))
        |> Promise.catch (fun e -> Error(networkError e))

let createPost (threadId: string) (body: string) : JS.Promise<Result<unit, string>> =
    match needSession () with
    | Error e -> Promise.lift (Error e)
    | Ok s ->
        rest "forum_posts" "POST" [ "Prefer", "return=minimal" ]
            (Some(createObj [ "thread_id" ==> threadId; "body" ==> body; "author_id" ==> s.UserId ]))
        |> Promise.map (fun r -> if r.Ok then Ok() else Error(errorText r))
        |> Promise.catch (fun e -> Error(networkError e))

/// Only moderators may change a bug report's status (the server enforces it).
let setThreadStatus (threadId: string) (status: string) : JS.Promise<Result<unit, string>> =
    rest ("forum_threads?id=eq." + enc threadId) "PATCH" [ "Prefer", "return=minimal" ] (Some(createObj [ "status" ==> status ]))
    |> Promise.map (fun r -> if r.Ok then Ok() else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

let deletePost (postId: string) : JS.Promise<Result<unit, string>> =
    rest ("forum_posts?id=eq." + enc postId) "DELETE" [ "Prefer", "return=minimal" ] None
    |> Promise.map (fun r -> if r.Ok then Ok() else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))

let deleteThread (threadId: string) : JS.Promise<Result<unit, string>> =
    rest ("forum_threads?id=eq." + enc threadId) "DELETE" [ "Prefer", "return=minimal" ] None
    |> Promise.map (fun r -> if r.Ok then Ok() else Error(errorText r))
    |> Promise.catch (fun e -> Error(networkError e))
