module Views.Popover

open Feliz
open Fable.Core
open Types

[<Emit("encodeURIComponent($0)")>]
let private encodeUriComponent (s: string) : string = jsNative

/// Strips a leading/trailing elision mark and normalizes to NFC (mirrors the
/// exact cleanup `showPop` does before displaying/looking up the word).
let private cleanWord (raw: string) : string =
    raw
    |> fun s -> System.Text.RegularExpressions.Regex.Replace(s, "[’'ʼ]$", "")
    |> fun s -> System.Text.RegularExpressions.Regex.Replace(s, "^[’'ʼ]", "")
    |> fun s -> s.Normalize(System.Text.NormalizationForm.FormC)

// The box is a fixed 17rem wide with a stable stack of contents (headword,
// three links, hint), so its footprint is known without measuring it.
let private POP_W = 272.0
let private POP_H = 250.0
let private MARGIN = 8.0
let private GAP = 6.0

/// The word-lookup popover. Kept always-mounted (toggling the `.show` class
/// and content) rather than conditionally rendered, matching the original's
/// persistent `#pop` element.
///
/// `dispatch` is taken only for the close button; everything else here is a
/// plain link.
///
/// The anchor rect comes from `getBoundingClientRect`, i.e. viewport
/// coordinates, so `.pop` is positioned `fixed` and the rect can be used as
/// given. Both axes are kept on-screen: clamped horizontally, and flipped to
/// sit above the word when there isn't room beneath it — without which a click
/// near the right or bottom edge opened the popover partly out of view.
/// Below 900px CSS overrides this entirely and renders it as a bottom sheet.
let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
    let popover = model.Popover
    let isOpen = popover.IsSome

    let word, seg =
        match popover with
        | Some(WordPopover(rawWord, _, seg)) -> cleanWord rawWord, seg
        | None -> "", None
    let saved = if isOpen then LibraryData.wordFor model.Library word else None

    let enc = encodeUriComponent word

    let positionStyle =
        match popover with
        | Some(WordPopover(_, rect, _)) ->
            let vw = Browser.Dom.window.innerWidth
            let vh = Browser.Dom.window.innerHeight
            let left = rect.left |> min (vw - POP_W - MARGIN) |> max MARGIN
            let below = rect.bottom + GAP
            let top =
                if below + POP_H > vh - MARGIN && rect.top - POP_H - GAP >= MARGIN then rect.top - POP_H - GAP
                else min below (max MARGIN (vh - POP_H - MARGIN))
            [ style.left (length.px left); style.top (length.px top) ]
        | None -> []

    let href (baseUrl: string) = if isOpen then baseUrl + enc else "#"

    Html.div [
        prop.className ("pop" + (if isOpen then " show" else ""))
        prop.id "pop"
        prop.role "dialog"
        prop.ariaLabel "Word lookup"
        prop.style positionStyle
        prop.children [
            Html.button [
                prop.className "panel-close"
                prop.ariaLabel "Close word lookup"
                prop.title "Close"
                prop.text "×"
                prop.onClick (fun e ->
                    e.stopPropagation ()
                    dispatch ClosePopover)
            ]
            Html.div [ prop.className "hw"; prop.id "popHw"; prop.lang "grc"; prop.text word ]
            Html.div [
                prop.className "pop-acts"
                prop.children [
                    Html.button [
                        prop.className "btn small pop-trace"
                        prop.text "Trace this word"
                        prop.title "Where this word and its stem occur, part by part and era by era"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            dispatch (Reader_(TraceWord word)))
                    ]
                    match saved with
                    | Some _ ->
                        Html.a [
                            prop.className "btn small pop-save on"
                            prop.href "#lib/words"
                            prop.title "This word is in My library › Words"
                            prop.onClick (fun e ->
                                e.preventDefault ()
                                dispatch ClosePopover
                                dispatch (Navigate("#lib/words", false)))
                            prop.children [ Shared.icon "words"; Html.text "In my words" ]
                        ]
                    | None ->
                        Html.button [
                            prop.className "btn small pop-save"
                            prop.title "Keep this word, with the passage it came from, to learn with flashcards"
                            prop.onClick (fun e ->
                                e.stopPropagation ()
                                dispatch (Library_(SaveWord(word, seg))))
                            prop.children [ Shared.icon "words"; Html.text "Save word" ]
                        ]
                ]
            ]
            Html.div [
                prop.className "row"
                prop.children [
                    Html.a [
                        prop.id "lnkLogeion"
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.href (href "https://logeion.uchicago.edu/")
                        prop.children [ Html.text "Logeion "; Html.small [ prop.text "LSJ, Middle Liddell, Slater" ] ]
                    ]
                    Html.a [
                        prop.id "lnkPerseus"
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.href (if isOpen then "https://www.perseus.tufts.edu/hopper/morph?l=" + enc + "&la=greek" else "#")
                        prop.children [ Html.text "Perseus word study "; Html.small [ prop.text "morphology" ] ]
                    ]
                    Html.a [
                        prop.id "lnkWikt"
                        prop.target "_blank"
                        prop.rel "noopener"
                        prop.href (if isOpen then "https://en.wiktionary.org/wiki/" + enc + "#Ancient_Greek" else "#")
                        prop.children [ Html.text "Wiktionary "; Html.small [ prop.text "inflection tables" ] ]
                    ]
                ]
            ]
            Html.div [ prop.className "hint"; prop.text "Opens in a new tab. Press Esc to close." ]
        ]
    ]
