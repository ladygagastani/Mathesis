/// The header search box and its dropdown (see Search.fs for what it finds).
/// One component for every width: in the header's centre on wide screens;
/// on phones the box is hidden until the Search tab opens it as a full-screen
/// sheet. It is an ARIA combobox: the input keeps focus while ↑ ↓ move the
/// highlight (aria-activedescendant), Enter opens, Esc closes.
module Views.SearchBox

open Feliz
open Types

let private optionId (i: int) = "hs-opt-" + string i

let private placeholder (model: Model) =
    match model.Reader with
    | Some _ -> "Search, or type a line here: 1.33"
    | None -> "Search texts, authors, your library"

/// One result. `mouseDown` is cancelled so the input keeps focus (and the
/// dropdown stays open) until the click lands.
let private item (dispatch: Msg -> unit) (active: int) (i: int) (h: Search.Hit) : ReactElement =
    let body =
        [ Html.span [
              prop.className "hs-main"
              prop.children [
                  Html.span [ prop.className "hs-title"; prop.children [ Shared.titleText h.Title ] ]
                  if h.Grc <> "" then Html.span [ prop.className "hs-grc grc"; prop.lang "grc"; prop.text h.Grc ]
              ]
          ]
          if h.Sub <> "" then Html.span [ prop.className "hs-sub"; prop.text h.Sub ]
          if h.External.IsSome then Html.span [ prop.className "hs-ext"; prop.ariaHidden true; prop.text "↗" ] ]
    let common: IReactProperty list =
        [ prop.id (optionId i)
          prop.role "option"
          prop.ariaSelected ((i = active))
          prop.className ("hs-item" + (if i = active then " on" else ""))
          prop.onMouseDown (fun e -> e.preventDefault ())
          prop.onMouseEnter (fun _ -> if i <> active then dispatch (Search_(MoveSearch(i - active)))) ]
    Html.li [
        prop.key h.Key
        prop.role "presentation"
        prop.children [
            match h.External with
            | Some url ->
                Html.a (
                    common
                    @ [ prop.href (Router.href url)
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.tabIndex -1
                        prop.onClick (fun _ -> dispatch (Search_ Chose))
                        prop.children body ]
                )
            | None ->
                let href =
                    h.Msgs
                    |> List.tryPick (function
                        | Navigate(hash, _) -> Some hash
                        | _ -> None)
                Html.a (
                    common
                    @ [ prop.href (Router.href (href |> Option.defaultValue "#"))
                        prop.tabIndex -1
                        prop.onClick (fun e ->
                            e.preventDefault ()
                            dispatch (Search_ Chose)
                            h.Msgs |> List.iter dispatch)
                        prop.children body ]
                )
        ]
    ]

/// Results under their group headings, numbered in one run for the keys.
let private listOf (dispatch: Msg -> unit) (active: int) (hits: Search.Hit list) : ReactElement list =
    hits
    |> List.mapi (fun i h -> i, h)
    |> List.groupBy (fun (_, h) -> h.Group)
    |> List.collect (fun (g, xs) ->
        Html.li [ prop.key ("g-" + g); prop.role "presentation"; prop.className "hs-group"; prop.text g ]
        :: [ for i, h in xs -> item dispatch active i h ])

let private tips (dispatch: Msg -> unit) : ReactElement =
    let tryIt (q: string) (label: string) =
        Html.button [
            prop.key q
            prop.className "hs-try"
            prop.onMouseDown (fun e -> e.preventDefault ())
            prop.onClick (fun _ -> dispatch (Search_(SetSearchQuery q)))
            prop.children [ Html.b [ prop.text q ]; Html.span [ prop.text label ] ]
        ]
    Html.div [
        prop.className "hs-tips"
        prop.children [
            Html.p [ prop.className "hs-tips-h"; prop.text "Try" ]
            tryIt "Iliad 1.1" "a passage"
            tryIt "Plato" "an author"
            tryIt "λόγος" "a Greek word"
            tryIt "#" "your tags"
            tryIt "eras" "the guide and wiki"
        ]
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let s = model.Search
    let hasQuery = s.Query.Trim() <> ""
    let hits = if hasQuery then Search.results model else Search.suggestions model
    let active = if hits.IsEmpty then -1 else max 0 (min s.Active (hits.Length - 1))
    let counts = if hasQuery then Search.scopeCounts model else Map.empty
    Html.div [
        prop.className ("hsearch" + (if s.Open then " open" else ""))
        prop.role "search"
        prop.children [
            Html.div [
                prop.className "hs-bar"
                prop.children [
                    Html.input [
                        prop.id "hsq"
                        prop.type' "search"
                        prop.autoComplete "off"
                        prop.custom ("spellCheck", false)
                        prop.role "combobox"
                        prop.ariaLabel "Search"
                        prop.custom ("aria-expanded", s.Open)
                        prop.custom ("aria-controls", "hsList")
                        prop.custom ("aria-autocomplete", "list")
                        if s.Open && active >= 0 then prop.custom ("aria-activedescendant", optionId active)
                        prop.placeholder (placeholder model)
                        prop.value s.Query
                        prop.onFocus (fun _ -> if not s.Open then dispatch (Search_ OpenSearch))
                        prop.onChange (fun (v: string) -> dispatch (Search_(SetSearchQuery v)))
                        prop.onKeyDown (fun e ->
                            match e.key with
                            | "ArrowDown" ->
                                e.preventDefault ()
                                dispatch (Search_(MoveSearch 1))
                            | "ArrowUp" ->
                                e.preventDefault ()
                                dispatch (Search_(MoveSearch -1))
                            | "Enter" ->
                                e.preventDefault ()
                                dispatch (Search_ ChooseActive)
                            | "Escape" ->
                                e.preventDefault ()
                                e.stopPropagation ()
                                dispatch (Search_ CloseSearch)
                            | _ -> ())
                    ]
                    if not s.Open then Html.kbd [ prop.className "hs-key"; prop.title "Press / to search"; prop.text "/" ]
                    Html.button [
                        prop.className "hs-cancel"
                        prop.type' "button"
                        prop.text "Cancel"
                        prop.onClick (fun _ -> dispatch (Search_ CloseSearch))
                    ]
                ]
            ]
            if s.Open then
                Html.div [
                    prop.className "hs-drop"
                    prop.onMouseDown (fun e ->
                        // keep focus in the box, except on the scope chips' own focus ring
                        let t = e.target :?> Browser.Types.Element
                        if t.tagName <> "INPUT" then e.preventDefault ())
                    prop.children [
                        if hasQuery then
                            Html.div [
                                prop.className "hs-scopes"
                                prop.role "group"
                                prop.ariaLabel "Search in"
                                prop.children [
                                    for id, label in Search.scopes ->
                                        let n = counts.TryFind id |> Option.defaultValue 0
                                        Html.button [
                                            prop.key id
                                            prop.className ("chip" + (if s.Scope = id then " on" else ""))
                                            prop.custom ("aria-pressed", (s.Scope = id))
                                            prop.disabled (n = 0 && s.Scope <> id)
                                            prop.onClick (fun _ -> dispatch (Search_(SetSearchScope id)))
                                            prop.children [ Html.text (label + " "); Html.span [ prop.text (string n) ] ]
                                        ]
                                ]
                            ]
                        if hits.IsEmpty then
                            if hasQuery then
                                Html.p [
                                    prop.className "hs-none"
                                    prop.text ("Nothing found for “" + s.Query.Trim() + "”. Check the spelling, or try part of a name or title.")
                                ]
                        else
                            Html.ul [ prop.id "hsList"; prop.role "listbox"; prop.className "hs-list"; prop.children (listOf dispatch active hits) ]
                        if not hasQuery then tips dispatch
                        if not hasQuery && not s.Recent.IsEmpty then
                            Html.button [
                                prop.className "linkbtn hs-forget"
                                prop.text "Clear recent searches"
                                prop.onClick (fun _ -> dispatch (Search_ ForgetSearches))
                            ]
                        Html.p [
                            prop.className "hs-keys"
                            prop.ariaHidden true
                            prop.children [
                                Html.kbd [ prop.text "↑" ]
                                Html.kbd [ prop.text "↓" ]
                                Html.text " move · "
                                Html.kbd [ prop.text "Enter" ]
                                Html.text " open · "
                                Html.kbd [ prop.text "Esc" ]
                                Html.text " close"
                            ]
                        ]
                    ]
                ]
        ]
    ]
