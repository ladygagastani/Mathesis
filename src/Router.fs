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
        | "account" :: _ -> AccountRoute
        | "forum" :: "t" :: id :: _ when id <> "" -> ForumRoute(ForumThread id)
        | "forum" :: "new" :: cat :: _ when cat <> "" -> ForumRoute(ForumNew cat)
        | "forum" :: "new" :: _ -> ForumRoute(ForumNew "square")
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
                | "undated" :: _ -> WikiAuthors(Some(ByEra "undated")) // old links
                | _ -> WikiHome
            WikiRoute wikiRoute
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
    | AboutRoute -> join [ "about" ]
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
    | AboutRoute -> "wiki"
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
// minimal DOM history interop — the in-app back/forward stack itself lives in
// the Elmish Model (History/CurrentHash), driven from State.fs
// ---------------------------------------------------------------------------

// These go through `window.` rather than the bare globals on purpose: Fable
// emits F# locals under their own names, so a binding called `history` or
// `location` in a calling scope would otherwise shadow the global here and the
// call would fail silently inside an Elmish effect.
[<Emit("window.history.pushState(null, '', $0)")>]
let pushState (hash: string) : unit = jsNative

/// Steps the *browser's* history back one entry, which is what the back button,
/// Alt+Left and the trackpad's two-finger swipe all do. The resulting
/// popstate/hashchange is what re-routes the app, so in-app back and native
/// back stay on exactly one history timeline.
[<Emit("window.history.back()")>]
let historyBack () : unit = jsNative

[<Emit("window.history.replaceState(null, '', $0)")>]
let replaceState (hash: string) : unit = jsNative

[<Emit("window.location.hash = $0")>]
let setLocationHash (hash: string) : unit = jsNative

[<Emit("window.location.hash")>]
let currentHash (): string = jsNative
