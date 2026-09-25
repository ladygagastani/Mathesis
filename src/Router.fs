module Router

open Fable.Core
open Types

[<Emit("decodeURIComponent($0)")>]
let private decodeUri (s: string) : string = jsNative

[<Emit("encodeURIComponent($0)")>]
let private encodeUri (s: string) : string = jsNative

let private stripHash (h: string) : string = if h.StartsWith "#" then h.Substring 1 else h

/// Parses a location-hash fragment (with or without the leading '#') into a
/// Route. Anything not matching a recognized prefix (lib/about/browse/author/
/// wiki) is treated as a tentative reader route — the caller (which has the
/// catalog) is responsible for validating the work id and falling back to
/// `Landing` if it doesn't exist, exactly like the original app's outer
/// `route()` does after `FEAT.route()` returns false.
let parseHash (hash: string) : Route =
    let h = stripHash hash
    if h = "" then
        Landing
    else
        let p = h.Split('/') |> Array.map decodeUri |> List.ofArray
        match p with
        // a sign-in link from the email returns here with the session (or an
        // error) in the fragment: that belongs to the account page
        | first :: _ when first.StartsWith "access_token=" || first.StartsWith "error=" -> AccountRoute
        | "lib" :: "words" :: _ -> LibraryRoute LibWords
        | "lib" :: "places" :: _ -> LibraryRoute LibPlaces
        | "lib" :: "favourites" :: _ -> LibraryRoute LibFavs
        | "lib" :: "notes" :: _ -> LibraryRoute LibNotes
        | "lib" :: _ -> LibraryRoute LibMarks
        | "about" :: _ -> AboutRoute
        | "privacy" :: _ -> PrivacyRoute
        | "account" :: _ -> AccountRoute
        | "forum" :: "t" :: id :: _ when id <> "" -> ForumRoute(ForumThread id)
        | "forum" :: "new" :: cat :: _ when cat <> "" -> ForumRoute(ForumNew cat)
        | "forum" :: "new" :: _ -> ForumRoute(ForumNew "square")
        | "forum" :: "rules" :: _ -> ForumRoute ForumRules
        | "forum" :: cat :: _ when cat <> "" -> ForumRoute(ForumBoard cat)
        | "forum" :: _ -> ForumRoute ForumHome
        | "library" :: _
        | "browse" :: _ -> Browse
        | ("study" | "learn") :: rest ->
            LearnRoute(
                match rest with
                | "welcome" :: _ -> LearnWelcome
                | "preface" :: _ -> LearnPreface
                | "letters" :: _ -> LearnLetters
                | "alphabet" :: _ -> LearnAlphabet
                | "declension" :: "done" :: _ -> LearnDone
                | "declension" :: _ -> LearnLesson
                | "sounds" :: _ -> LearnSounds
                | "iliad" :: _ -> LearnIliad
                | "myth" :: _ -> LearnMyth
                | _ -> LearnContents
            )
        | "author" :: id :: rest -> AuthorRoute(id, List.tryHead rest)
        | "start" :: slug :: _ when slug <> "" -> GuideRoute(Some slug)
        | "start" :: _ -> GuideRoute None
        // the guide used to live in the wiki; old links still work
        | "wiki" :: "start" :: slug :: _ when slug <> "" -> GuideRoute(Some slug)
        | "wiki" :: "start" :: _ -> GuideRoute None
        | "wiki" :: rest ->
            let wikiRoute =
                match rest with
                | [] -> WikiHome
                | "authors" :: "era" :: v :: _ -> WikiAuthors(Some(ByEra v))
                | "authors" :: "genre" :: v :: _ -> WikiAuthors(Some(ByGenre v))
                | "authors" :: _ -> WikiAuthors None
                | "eras" :: id :: _ -> WikiEras(Some id)
                | "eras" :: [] -> WikiEras None
                | "manuscripts" :: _ -> WikiArticles Manuscripts
                | "variants" :: _ -> WikiArticles Variants
                | "editions" :: _ -> WikiEditions
                | "life" :: slug :: _ when slug <> "" -> WikiLife(Some slug)
                | "life" :: _ -> WikiLife None
                | "undated" :: _ -> WikiAuthors(Some(ByEra "undated")) // old links
                | _ -> WikiHome
            match rest with
            | [] | ("authors" | "eras" | "manuscripts" | "variants" | "editions" | "life" | "undated") :: _ -> WikiRoute wikiRoute
            | _ -> NotFoundRoute("#" + h)
        | id :: rest ->
            let grc = rest |> List.tryItem 0 |> Option.defaultValue ""
            let eng = rest |> List.tryItem 1 |> Option.defaultValue ""
            // An empty chunk slot is a placeholder holding the position open for
            // the passage after it, not a chunk named "".
            let chunk = rest |> List.tryItem 2 |> Option.filter (fun c -> c <> "")
            let seg = rest |> List.tryItem 3 |> Option.filter (fun s -> s <> "")
            ReaderRoute(id, grc, eng, chunk, seg)
        | [] -> Landing

/// The hash of a Study page (the inverse of the "study" branch of `parseHash`;
/// the section was first called Learn, and `#learn/…` links still parse).
let learnHash (page: LearnPage) : string =
    match page with
    | LearnContents -> "#study"
    | LearnWelcome -> "#study/welcome"
    | LearnPreface -> "#study/preface"
    | LearnLetters -> "#study/letters"
    | LearnAlphabet -> "#study/alphabet"
    | LearnLesson -> "#study/declension"
    | LearnDone -> "#study/declension/done"
    | LearnSounds -> "#study/sounds"
    | LearnIliad -> "#study/iliad"
    | LearnMyth -> "#study/myth"

/// Inverse of `parseHash` — builds the hash fragment (including the leading
/// '#') the app itself would navigate to for a given route.
let toHash (route: Route) : string =
    let join (segs: string list) = "#" + (segs |> List.map encodeUri |> String.concat "/")
    match route with
    | Landing -> "#"
    | LearnRoute page -> learnHash page
    | Browse -> join [ "library" ]
    | LibraryRoute LibMarks -> join [ "lib" ]
    | LibraryRoute LibWords -> join [ "lib"; "words" ]
    | LibraryRoute LibPlaces -> join [ "lib"; "places" ]
    | LibraryRoute LibFavs -> join [ "lib"; "favourites" ]
    | LibraryRoute LibNotes -> join [ "lib"; "notes" ]
    | AccountRoute -> join [ "account" ]
    | ForumRoute ForumHome -> join [ "forum" ]
    | ForumRoute(ForumBoard c) -> join [ "forum"; c ]
    | ForumRoute(ForumThread id) -> join [ "forum"; "t"; id ]
    | ForumRoute(ForumNew c) -> join [ "forum"; "new"; c ]
    | ForumRoute ForumRules -> join [ "forum"; "rules" ]
    | AboutRoute -> join [ "about" ]
    | PrivacyRoute -> join [ "privacy" ]
    // the address the reader asked for stays in the address bar
    | NotFoundRoute h -> h
    | AuthorRoute(id, section) -> join ([ "author"; id ] @ (section |> Option.toList))
    | WikiRoute WikiHome -> join [ "wiki" ]
    | WikiRoute(WikiAuthors None) -> join [ "wiki"; "authors" ]
    | WikiRoute(WikiAuthors(Some(ByEra v))) -> join [ "wiki"; "authors"; "era"; v ]
    | WikiRoute(WikiAuthors(Some(ByGenre v))) -> join [ "wiki"; "authors"; "genre"; v ]
    | WikiRoute(WikiEras None) -> join [ "wiki"; "eras" ]
    | WikiRoute(WikiEras(Some id)) -> join [ "wiki"; "eras"; id ]
    | WikiRoute(WikiArticles Manuscripts) -> join [ "wiki"; "manuscripts" ]
    | WikiRoute(WikiArticles Variants) -> join [ "wiki"; "variants" ]
    | WikiRoute WikiEditions -> join [ "wiki"; "editions" ]
    | WikiRoute(WikiLife None) -> join [ "wiki"; "life" ]
    | WikiRoute(WikiLife(Some slug)) -> join [ "wiki"; "life"; slug ]
    | GuideRoute None -> join [ "start" ]
    | GuideRoute(Some slug) -> join [ "start"; slug ]
    | ReaderRoute(id, grc, eng, chunk, seg) ->
        // An empty edition slot means "no preference — pick the usual one", and
        // is distinct from the literal "none", which means "show Greek only".
        // Collapsing the first into the second stripped the translation from any
        // link that didn't name editions, and because the Greek is segmented
        // against the translation, the passage being linked to then no longer
        // existed under that reference.
        let engPart = eng
        match seg with
        | None -> join ([ id; grc; engPart ] @ (chunk |> Option.toList))
        // A reader hash is positional, so a passage reference can only be read
        // back correctly if the chunk slot before it is actually there. Dropping
        // an absent chunk shifted the passage into the chunk's place, and the
        // route came back with no passage at all — which is why following a
        // cross-reference in a note landed at the top of the text.
        | Some s -> join [ id; grc; engPart; (chunk |> Option.defaultValue ""); s ]

/// Which top-nav tab (and library button) should read "active" for this route —
/// mirrors the original `setNav`'s grouping exactly (author/about pages
/// highlight the Wiki tab, not Texts).
let navKey (route: Route) : string =
    match route with
    | WikiRoute _
    | AuthorRoute _
    | AboutRoute
    | PrivacyRoute -> "wiki"
    | NotFoundRoute _ -> ""
    | LibraryRoute _
    | AccountRoute -> "lib"
    | ForumRoute _ -> "forum"
    | LearnRoute _ -> "learn"
    | Browse
    | ReaderRoute _ -> "library"
    | Landing -> "home"
    | GuideRoute _ -> "learn"

let isReader (route: Route) : bool =
    match route with
    | ReaderRoute _ -> true
    | _ -> false

// ---------------------------------------------------------------------------
// addresses: the app works in route hashes ("#wiki/life/food"), which are also
// its history keys (Model.CurrentHash, History). Online the browser shows real
// paths (/Mathesis/wiki/life/food/) so that search engines see one page per
// address and every link can be opened in a new tab; opened from disk
// (file:) or built with VITE_ROUTING=hash, the old #… addresses are used.
// Old #… links still work: on arrival they are read and replaced by the path.
// ---------------------------------------------------------------------------

/// Real paths online; hash addresses from disk or when the build asks for them.
[<Emit("(window.location.protocol !== 'file:' && import.meta.env.VITE_ROUTING !== 'hash')")>]
let usePaths: bool = jsNative

/// The site's root path ("/Mathesis/" on GitHub Pages, "/" elsewhere): the
/// parent of the folder this module was loaded from (assets/ once built).
[<Emit("new URL('../', import.meta.url).pathname")>]
let basePath: string = jsNative

[<Emit("window.location.origin")>]
let private origin (): string = jsNative

[<Emit("window.location.pathname")>]
let private pathname (): string = jsNative

[<Emit("window.location.search")>]
let private search (): string = jsNative

[<Emit("window.location.hash")>]
let private locationHash (): string = jsNative

/// The address 404.html sent on to the front page ("/Mathesis/forum/t/1/"),
/// until the app first writes the address bar.
[<Emit("(window.__anagPath || '')")>]
let private restoredAddress (): string = jsNative

[<Emit("window.__anagPath = undefined")>]
let private forgetRestored (): unit = jsNative

let private routeWords =
    set [ "lib"; "about"; "privacy"; "account"; "forum"; "library"; "browse"; "study"; "learn"; "author"; "start"; "wiki" ]

/// tlg0012.tlg001, tlg0007.tlg082b, tlg0093.ogl001, stoa0033a.tlg001 …
let private workIdRe = System.Text.RegularExpressions.Regex(@"^[a-z]+\d+[a-z0-9]*\.[a-z]+\d+[a-z0-9]*$")

let private isWorkId (s: string) = workIdRe.IsMatch s

/// True for a hash that names a page of the app, as opposed to an anchor in
/// the page ("#ap-tlg0012.tlg001", "#s-1.33") or a sign-in fragment.
let isRouteHash (hash: string) : bool =
    let s = stripHash hash
    if s = "" then true
    else
        let first = decodeUri (s.Split('/').[0])
        routeWords.Contains first || isWorkId first

/// "#wiki/life/food" → "wiki/life/food/"; a reader hash
/// "#<work>/<grc>/<eng>/<part>/<passage>" → "<work>/?grc=…&eng=…&part=…&at=…"
/// (only the slots that are set).
let private pathOfHash (hash: string) : string =
    let s = stripHash hash
    if s = "" then ""
    else
        let parts = s.TrimEnd('/').Split('/') |> List.ofArray
        let withQuery (path: string) (query: (string * string) list) =
            let q = query |> List.filter (fun (_, v) -> v <> "") |> List.map (fun (k, v) -> k + "=" + v)
            path + "/" + (if List.isEmpty q then "" else "?" + String.concat "&" q)
        // Every address below has a page file (scripts/site-pages.mjs), so any
        // static host serves it: parts that vary without limit (a passage, a
        // forum thread, an author's section) go in the query instead.
        match parts with
        | work :: rest when isWorkId (decodeUri work) ->
            let slot i = rest |> List.tryItem i |> Option.defaultValue ""
            withQuery work [ "grc", slot 0; "eng", slot 1; "part", slot 2; "at", slot 3 ]
        | "author" :: id :: section :: _ -> withQuery ("author/" + id) [ "at", section ]
        | "forum" :: "t" :: id :: _ -> withQuery "forum/t" [ "id", id ]
        | "forum" :: "new" :: board :: _ -> withQuery "forum/new" [ "board", board ]
        // old names: one page, one address
        | "browse" :: _ -> "library/"
        | "learn" :: rest -> String.concat "/" ("study" :: rest) + "/"
        | "wiki" :: "start" :: rest -> String.concat "/" ("start" :: rest) + "/"
        | _ -> String.concat "/" parts + "/"

/// The inverse of `pathOfHash`, for the address the browser is showing.
let private hashOfPath (rest: string) (query: string) : string =
    let rest = rest.Trim('/')
    if rest = "" then "#"
    else
        let q =
            query.TrimStart('?').Split('&')
            |> Array.choose (fun kv ->
                match kv.IndexOf '=' with
                | i when i > 0 -> Some(kv.Substring(0, i), kv.Substring(i + 1))
                | _ -> None)
            |> Map.ofArray
        let get k = q.TryFind k |> Option.defaultValue ""
        match rest.Split('/') |> List.ofArray with
        | [ work ] when isWorkId (decodeUri work) ->
            let slots =
                match get "part", get "at" with
                | "", "" -> [ work; get "grc"; get "eng" ]
                | part, "" -> [ work; get "grc"; get "eng"; part ]
                | part, at -> [ work; get "grc"; get "eng"; part; at ]
            "#" + String.concat "/" slots
        | [ "author"; id ] when get "at" <> "" -> "#author/" + id + "/" + get "at"
        | [ "forum"; "t" ] when get "id" <> "" -> "#forum/t/" + get "id"
        | [ "forum"; "new" ] when get "board" <> "" -> "#forum/new/" + get "board"
        | _ -> "#" + rest

/// The address to show (and link to) for a route hash.
let url (hash: string) : string =
    if usePaths then basePath + pathOfHash hash else (if hash = "" then "#" else hash)

/// For `prop.href`: route hashes become real addresses online; anything else
/// (external links, anchors in the page) is returned as it is.
let href (target: string) : string =
    if usePaths && target.StartsWith "#" && isRouteHash target then url target else target

/// The site's own address, for links that must come back to it (sign-in).
let siteRoot () : string =
    if usePaths then origin () + basePath else origin () + pathname ()

// These go through `window.` rather than the bare globals on purpose: Fable
// emits F# locals under their own names, so a binding called `history` or
// `location` in a calling scope would otherwise shadow the global here and the
// call would fail silently inside an Elmish effect.
[<Emit("window.history.pushState(null, '', $0)")>]
let private pushRaw (address: string) : unit = jsNative

[<Emit("window.history.replaceState(null, '', $0)")>]
let private replaceRaw (address: string) : unit = jsNative

let pushState (hash: string) : unit =
    forgetRestored ()
    pushRaw (url hash)

let replaceState (hash: string) : unit =
    forgetRestored ()
    replaceRaw (url hash)

/// Steps the *browser's* history back one entry, which is what the back button,
/// Alt+Left and the trackpad's two-finger swipe all do. The resulting
/// popstate/hashchange is what re-routes the app, so in-app back and native
/// back stay on exactly one history timeline.
[<Emit("window.history.back()")>]
let historyBack () : unit = jsNative

/// The route hash for the address the browser is showing. An old-style
/// "#wiki/…" link, or a sign-in fragment, wins over the path it arrived on.
let currentHash () : string =
    let restored = restoredAddress ()
    let p, q, h =
        if restored = "" then pathname (), search (), locationHash ()
        else
            let hashAt = restored.IndexOf '#'
            let beforeHash, h = if hashAt >= 0 then restored.Substring(0, hashAt), restored.Substring hashAt else restored, ""
            let queryAt = beforeHash.IndexOf '?'
            if queryAt >= 0 then beforeHash.Substring(0, queryAt), beforeHash.Substring queryAt, h
            else beforeHash, "", h
    if not usePaths then h
    elif h <> "" && h <> "#" && (isRouteHash h || h.Contains "access_token=" || h.Contains "error=") then h
    else
        let rest = if p.StartsWith basePath then p.Substring basePath.Length else p
        hashOfPath rest q

/// True when the address bar should be rewritten to the page's path: the
/// page was opened at an old "#…" address, or sent on by 404.html (not a
/// sign-in fragment, which the account page reads).
let arrivedByHash () : bool =
    let h = locationHash ()
    usePaths && (restoredAddress () <> "" || (h <> "" && h <> "#" && isRouteHash h))
