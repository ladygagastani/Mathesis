/// The "Start here" beginner's guide (`#wiki/start/<slug>`), rendered from the
/// Markdown in content/start-here/ (see GuideData). Greek runs get the Greek
/// face and `lang="grc"`; "page N" in the running text links to that page;
/// links written `read:<workId>:<ref>` open the passage in the reader.
module Views.Guide

open Feliz
open Types
open Markdown
open System.Text.RegularExpressions

let private navigateTo (dispatch: Msg -> unit) (hash: string) (e: Browser.Types.MouseEvent) =
    e.preventDefault ()
    dispatch (Navigate(hash, false))

/// Greek words, and the spaces and punctuation between them, so a phrase stays
/// one run rather than a span per word.
let private greekRun =
    Regex(
        "[Ͱ-Ͽἀ-῿][Ͱ-Ͽἀ-῿̀-ͯʼ’᾽']*"
        + "(?:[\\s,.;:··—–/()\\-]+[Ͱ-Ͽἀ-῿][Ͱ-Ͽἀ-῿̀-ͯʼ’᾽']*)*"
    )

let private pageRef = Regex(@"\bpage ([1-9])\b")

/// Splits `s` into (isMatch, text) pieces around the matches of `re`.
let private pieces (re: Regex) (s: string) : (bool * string) list =
    let out = ResizeArray<bool * string>()
    let mutable last = 0
    for m in re.Matches s do
        if m.Index > last then out.Add(false, s.Substring(last, m.Index - last))
        out.Add(true, m.Value)
        last <- m.Index + m.Length
    if last < s.Length then out.Add(false, s.Substring last)
    List.ofSeq out

let private greekText (key: string) (s: string) : ReactElement list =
    pieces greekRun s
    |> List.mapi (fun i (isGreek, t) ->
        if isGreek then Html.span [ prop.key (key + "g" + string i); prop.className "grc"; prop.lang "grc"; prop.text t ]
        else Html.text t)

let private textRuns (dispatch: Msg -> unit) (key: string) (s: string) : ReactElement list =
    pieces pageRef s
    |> List.mapi (fun i (isRef, t) ->
        let k = key + "p" + string i
        if isRef then
            match GuideData.hashOfPageNumber (int (pageRef.Match t).Groups.[1].Value) with
            | Some h -> [ Html.a [ prop.key k; prop.href h; prop.text t; prop.onClick (navigateTo dispatch h) ] ]
            | None -> [ Html.text t ]
        else
            greekText k t)
    |> List.concat

/// (href, external?) for a link as written in the Markdown.
let private resolveHref (href: string) : string * bool =
    if href.StartsWith "read:" then
        let rest = href.Substring 5
        let i = rest.IndexOf ':'
        if i > 0 then Router.toHash (ReaderRoute(rest.Substring(0, i), "", "", None, Some(rest.Substring(i + 1)))), false
        else Router.toHash (ReaderRoute(rest, "", "", None, None)), false
    elif href.EndsWith ".md" then (GuideData.hashOfFile href |> Option.defaultValue "#wiki/start"), false
    elif href.StartsWith "#" then href, false
    else href, true

let rec private inlines (dispatch: Msg -> unit) (key: string) (xs: Inline list) : ReactElement list =
    xs
    |> List.mapi (fun i x ->
        let k = key + "." + string i
        match x with
        | Text s -> textRuns dispatch k s
        | Strong ys -> [ Html.strong [ prop.key k; prop.children (inlines dispatch k ys) ] ]
        | Em ys -> [ Html.em [ prop.key k; prop.children (inlines dispatch k ys) ] ]
        | Link(href, ys) ->
            let h, external = resolveHref href
            [ Html.a (
                  [ prop.key k; prop.href h; prop.children (inlines dispatch k ys) ]
                  @ (if external then [ prop.target "_blank"; prop.rel "noopener" ]
                     else [ prop.onClick (navigateTo dispatch h) ])
              ) ])
    |> List.concat

let private firstChar (xs: Inline list) : char option =
    let rec go xs =
        match xs with
        | Text s :: _ when s <> "" -> Some s.[0]
        | (Strong ys | Em ys | Link(_, ys)) :: rest -> (match go ys with Some c -> Some c | None -> go rest)
        | _ :: rest -> go rest
        | [] -> None
    go xs

let private isGreekChar (c: char) = (c >= 'Ͱ' && c <= 'Ͽ') || (c >= 'ἀ' && c <= '῿')

let rec private blocks (dispatch: Msg -> unit) (key: string) (bs: Block list) : ReactElement list =
    bs
    |> List.mapi (fun i b ->
        let k = key + "/" + string i
        match b with
        | Heading(1, xs) -> Html.h1 [ prop.key k; prop.className "ph"; prop.children (inlines dispatch k xs) ]
        | Heading(2, xs) -> Html.h2 [ prop.key k; prop.className "g-h2"; prop.children (inlines dispatch k xs) ]
        | Heading(_, xs) -> Html.h3 [ prop.key k; prop.className "g-h3"; prop.children (inlines dispatch k xs) ]
        | Para xs -> Html.p [ prop.key k; prop.children (inlines dispatch k xs) ]
        | Quote lines ->
            Html.blockquote [
                prop.key k
                prop.className "g-quote"
                prop.children [
                    for j, line in List.indexed lines ->
                        let cls =
                            match firstChar line with
                            | Some '(' -> "q-ref"
                            | Some '"' | Some '“' -> "q-tr"
                            | Some c when isGreekChar c -> "q-grc"
                            | _ -> "q-text"
                        Html.div [ prop.key j; prop.className cls; prop.children (inlines dispatch (k + "q" + string j) line) ]
                ]
            ]
        | Bullets items -> Html.ul [ prop.key k; prop.className "g-list"; prop.children (listItems dispatch k items) ]
        | Numbered(start, items) ->
            // prop.custom: Feliz's `prop.start` overload compiles to a throw
            Html.ol [ prop.key k; prop.className "g-list"; prop.custom ("start", start); prop.children (listItems dispatch k items) ]
        | Table(header, rows) ->
            Html.div [
                prop.key k
                prop.className "g-table"
                prop.children [
                    Html.table [
                        prop.children [
                            if not (List.isEmpty header) then
                                Html.thead [
                                    Html.tr [
                                        prop.children [ for j, c in List.indexed header -> Html.th [ prop.key j; prop.children (inlines dispatch (k + "h" + string j) c) ] ]
                                    ]
                                ]
                            Html.tbody [
                                prop.children [
                                    for r, row in List.indexed rows ->
                                        Html.tr [
                                            prop.key r
                                            prop.children [
                                                for j, c in List.indexed row ->
                                                    Html.td [ prop.key j; prop.children (inlines dispatch (k + "r" + string r + "c" + string j) c) ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        | Details(summary, inner) ->
            Html.details [
                prop.key k
                prop.className "g-ans"
                prop.children ([ Html.summary [ prop.text summary ] ] @ blocks dispatch k inner)
            ]
        | Rule -> Html.hr [ prop.key k; prop.className "g-rule" ])

and private listItems (dispatch: Msg -> unit) (key: string) (items: Block list list) : ReactElement list =
    items
    |> List.mapi (fun i item ->
        let k = key + "i" + string i
        match item with
        // a one-paragraph item renders tight, without a <p>
        | [ Para xs ] -> Html.li [ prop.key k; prop.children (inlines dispatch k xs) ]
        | Para xs :: rest -> Html.li [ prop.key k; prop.children (inlines dispatch k xs @ blocks dispatch k rest) ]
        | _ -> Html.li [ prop.key k; prop.children (blocks dispatch k item) ])

let render (model: Model) (dispatch: Msg -> unit) (slug: string option) : ReactElement =
    let pages = GuideData.pages
    let idx =
        match GuideData.tryFind slug with
        | Some p -> pages |> List.findIndex (fun q -> q.Slug = p.Slug)
        | None -> 0
    let p = pages.[idx]
    let pagerLink (target: GuideData.GuidePage) (label: string) =
        let h = GuideData.hashOf target.Slug
        Html.a [ prop.className "btn"; prop.href h; prop.text label; prop.onClick (navigateTo dispatch h) ]
    Html.div [
        prop.className "page guide"
        prop.children [
            Shared.wikiCrumbs dispatch (if idx = 0 then [ "Start here", None ] else [ "Start here", Some "#wiki/start"; p.Title, None ])
            if idx > 0 then
                Html.p [ prop.className "g-kicker"; prop.text (sprintf "Start here · page %d of %d" idx (pages.Length - 1)) ]
            Html.article [ prop.className "g-body"; prop.children (blocks dispatch p.Slug (Markdown.parse p.Markdown)) ]
            // Pages that quote the texts (every quotation links into the reader)
            // say whose translation it is: ours, not the published one the
            // reader shows beside the Greek.
            if p.Markdown.Contains "](read:" then
                Html.p [
                    prop.className "tr-note"
                    prop.children [
                        Html.text "Translations in this guide are our own. The reader shows each text with the published translation from Perseus or First1KGreek, which may read differently. "
                        Html.span [ prop.className "tr-star"; prop.text "*" ]
                        Html.text " marks a rendering we are unsure of, or one that scholars dispute."
                    ]
                ]
            Html.div [
                prop.className "pager"
                prop.children [
                    (if idx > 0 then pagerLink pages.[idx - 1] ("← " + (if idx = 1 then "Contents" else pages.[idx - 1].Title))
                     else Html.span [])
                    (if idx < pages.Length - 1 then pagerLink pages.[idx + 1] ((if idx = 0 then "Begin: " + pages.[1].Title else pages.[idx + 1].Title) + " →")
                     else Html.span [])
                ]
            ]
        ]
    ]
