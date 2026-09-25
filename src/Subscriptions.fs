module Subscriptions

open Fable.Core
open Fable.Core.JsInterop
open Browser.Dom
open Browser.Types
open Elmish
open Types

[<Emit("!!document.querySelector('.rv-card')")>]
let private hasReviewCard () : bool = jsNative

[<Emit("document.getElementById($0)?.classList.contains($1)")>]
let private hasClass (id: string) (cls: string) : bool = jsNative

// Textareas count too: without them, typing "/" or "\" in a note was swallowed
// by the shortcuts and the arrow keys turned the page instead of moving the cursor.
[<Emit("$0 && $0.matches && $0.matches('input,select,textarea,[contenteditable]')")>]
let private isFormTarget (target: obj) : bool = jsNative

[<Emit("$0 && $0.closest && $0.closest($1)")>]
let private closest (target: obj) (selector: string) : obj = jsNative

[<Emit("matchMedia('(max-width: 900px)').matches")>]
let private isPhoneWidth () : bool = jsNative

[<Emit("$0.touches.length")>]
let private touchCount (e: obj) : int = jsNative

[<Emit("$0.touches[0].clientX")>]
let private touchX (e: obj) : float = jsNative

[<Emit("$0.touches[0].clientY")>]
let private touchY (e: obj) : float = jsNative

[<Emit("document.addEventListener($0, $1, { passive: true })")>]
let private addPassiveDocListener (eventType: string) (handler: Event -> unit) : unit = jsNative

/// Passive, so following the scroll never blocks it.
[<Emit("window.addEventListener($0, $1, { passive: true })")>]
let private addPassiveWinListener (eventType: string) (handler: Event -> unit) : unit = jsNative

/// Address-bar navigation (browser back/forward, a manually edited or pasted
/// URL) — never fires for our own `Router.pushState`/`replaceState` calls,
/// which is exactly why `HashChanged` can safely treat every occurrence as
/// real external navigation (see State.fs).
///
/// `popstate` is listened to alongside `hashchange` because the two cover
/// different gaps: stepping between two entries that share a hash fires only
/// popstate, and both firing for one navigation is harmless since `HashChanged`
/// no-ops when the hash already matches.
let private historySub: Sub<Msg> =
    [ [ "history" ],
      fun dispatch ->
          let handler = fun (_: Event) -> dispatch (HashChanged(Router.currentHash ()))
          window.addEventListener ("hashchange", handler)
          window.addEventListener ("popstate", handler)
          { new System.IDisposable with
              member _.Dispose() =
                  window.removeEventListener ("hashchange", handler)
                  window.removeEventListener ("popstate", handler) } ]

/// Escape closes the popover/settings/sidebar unconditionally; the arrow keys,
/// `/` (focus the library search) and `\` (collapse/restore the library) are
/// ignored while typing in an input, select or text box.
let private keydownSub: Sub<Msg> =
    [ [ "keydown" ],
      fun dispatch ->
          let handler =
              fun (e: Event) ->
                  let ke = e :?> KeyboardEvent
                  let isForm = isFormTarget ke.target
                  match ke.key with
                  | "Escape" -> dispatch (KeyPressed("Escape", ke.altKey))
                  | "ArrowLeft" when ke.altKey ->
                      ke.preventDefault ()
                      dispatch (KeyPressed("ArrowLeft", true))
                  | "ArrowLeft" when not isForm -> dispatch (KeyPressed("ArrowLeft", false))
                  | "ArrowRight" when not isForm -> dispatch (KeyPressed("ArrowRight", false))
                  | "/" when not isForm ->
                      ke.preventDefault ()
                      dispatch (KeyPressed("/", false))
                  | "\\" when not isForm ->
                      ke.preventDefault ()
                      dispatch (KeyPressed("\\", false))
                  // flashcards: Space/Enter shows the answer, 1 = again, 2 = got it
                  // (not when a button or link has focus: that would act twice)
                  | " " | "Enter" | "1" | "2" when not isForm && hasReviewCard () && isNull (closest ke.target "button, a") ->
                      ke.preventDefault ()
                      dispatch (KeyPressed(ke.key, false))
                  | _ -> ()
          document.addEventListener ("keydown", handler)
          { new System.IDisposable with
              member _.Dispose() = document.removeEventListener ("keydown", handler) } ]

/// Closes the settings pane / sidebar / popover on an outside click (mirrors
/// the original's three independent `closest` checks); reads the live DOM
/// class rather than the Model so the subscription's shape never depends on
/// state (Elmish would otherwise treat a changing SubId as start/stop churn).
let private outsideClickSub: Sub<Msg> =
    [ [ "outsideclick" ],
      fun dispatch ->
          let handler =
              fun (e: Event) ->
                  let target = box e.target
                  // The source menu's "More text-source settings…" opens the sheet,
                  // so a click inside the menu counts as "inside" the sheet too —
                  // otherwise that same click would close what it just opened.
                  if isNullOrUndefined (closest target "#settings")
                     && isNullOrUndefined (closest target "#btnSettings")
                     && isNullOrUndefined (closest target "#srcMenu")
                     && hasClass "settings" "show" then
                      dispatch (Settings_ ToggleSettingsPane)
                  // The chip is the menu's own toggle, so it must not count as outside.
                  if isNullOrUndefined (closest target "#srcMenu")
                     && isNullOrUndefined (closest target "#srcChip")
                     && hasClass "srcMenu" "show" then
                      dispatch (Source_ ToggleSourceMenu)
                  if isNullOrUndefined (closest target "#side") && isNullOrUndefined (closest target "#navToggle") && hasClass "side" "open" then
                      dispatch (ToggleSide false)
                  if isNullOrUndefined (closest target "#pop") && isNullOrUndefined (closest target ".w") then
                      dispatch ClosePopover
          document.addEventListener ("click", handler)
          { new System.IDisposable with
              member _.Dispose() = document.removeEventListener ("click", handler) } ]

/// Phone-only swipe-open/close gesture for the library drawer (mirrors the
/// original's touch handlers), simplified to a start/end threshold decision
/// rather than a live pixel-tracking transform — see the `DrawerTouch` case
/// in State.fs for why.
let private touchSub: Sub<Msg> =
    [ [ "touch" ],
      fun dispatch ->
          let touchStart =
              fun (e: Event) ->
                  if isPhoneWidth () && touchCount e = 1 then
                      dispatch (DrawerTouch("start", touchX e, touchY e))
          let touchMove =
              fun (e: Event) ->
                  if touchCount e > 0 then
                      dispatch (DrawerTouch("move", touchX e, touchY e))
          let touchEnd = fun (_: Event) -> dispatch (DrawerTouch("end", 0.0, 0.0))
          addPassiveDocListener "touchstart" touchStart
          addPassiveDocListener "touchmove" touchMove
          document.addEventListener ("touchend", touchEnd)
          { new System.IDisposable with
              member _.Dispose() =
                  document.removeEventListener ("touchstart", touchStart)
                  document.removeEventListener ("touchmove", touchMove)
                  document.removeEventListener ("touchend", touchEnd) } ]

/// Which passage sits just under the sticky column heading. `elementFromPoint`
/// keeps this O(1) — walking 250 segments' bounding boxes on every scroll frame
/// would not be. The probe is taken at 30% of the reader's width so it lands in
/// the Greek column and never on the scroll rail pinned to the right edge.
[<Emit("""
(() => {
    const rd = document.querySelector('.reader');
    if (!rd) return null;
    const b = rd.getBoundingClientRect();
    const x = b.left + Math.min(b.width * 0.3, 240);
    const head = document.querySelector('.colhead');
    const hb = head ? head.getBoundingClientRect() : null;
    const y = ((hb && hb.height) ? hb.bottom : 60) + 10;
    const el = document.elementFromPoint(x, y);
    const s = (el && el.closest) ? el.closest('.seg[data-ref]') : null;
    return s ? s.getAttribute('data-ref') : null;
})()
""")>]
let private passageAtViewportTop () : string = jsNative

/// Reports the passage under the viewport as it changes.
///
/// Deliberately *not* one dispatch per scroll event: each one re-renders the
/// reader, and a page holds up to 250 segments. Work is coalesced onto a short
/// timer, and a tick only dispatches if the passage actually changed or the
/// readout has gone quiet — so ordinary scrolling costs one dispatch per passage
/// boundary crossed rather than one per event.
///
/// A timer rather than `requestAnimationFrame`: rAF is throttled heavily and
/// suspended outright in a background tab, which silently stops the readout —
/// and, because the mark repaint hangs off the same idle signal, leaves the
/// scrollbar bands on a stale layout too. 80ms is finer than anyone can read a
/// changing passage label at, and bounds dispatches to about twelve a second.
let private scrollSub: Sub<Msg> =
    [ [ "readerscroll" ],
      fun dispatch ->
          let mutable lastRef: string = null
          let mutable lastAt = 0.0
          let mutable queued = false
          let measure () =
              queued <- false
              let ref = passageAtViewportTop ()
              if not (isNullOrUndefined ref) then
                  let now = JS.Constructors.Date.now ()
                  if ref <> lastRef || now - lastAt > 400.0 then
                      lastRef <- ref
                      lastAt <- now
                      dispatch (Reader_(ScrolledTo(Some ref)))
          let handler =
              fun (_: Event) ->
                  if not queued then
                      queued <- true
                      window.setTimeout ((fun () -> measure ()), 80) |> ignore
          // A resize (rotation, window drag, the phone keyboard opening) changes
          // the document height the mark bands are measured against, so they are
          // rebuilt once it settles rather than left on the old scale.
          let mutable resizeTimer = 0.0
          let onResize =
              fun (_: Event) ->
                  window.clearTimeout resizeTimer
                  resizeTimer <- window.setTimeout ((fun () -> dispatch (Reader_ RepaintMarks)), 180)
          addPassiveWinListener "scroll" handler
          addPassiveWinListener "resize" onResize
          { new System.IDisposable with
              member _.Dispose() =
                  window.removeEventListener ("scroll", handler)
                  window.removeEventListener ("resize", onResize) } ]

/// Coming back to the tab syncs the library, so what you saved on another
/// device appears (ignored when not signed in).
let private focusSub: Sub<Msg> =
    [ [ "focus" ],
      fun dispatch ->
          let onVisible = fun (_: Event) -> if document?visibilityState = "visible" then dispatch (Account_ SyncSoon)
          document.addEventListener ("visibilitychange", onVisible)
          { new System.IDisposable with
              member _.Dispose() = document.removeEventListener ("visibilitychange", onVisible) } ]

/// The subscription set only depends on whether the app has finished booting
/// (a one-time transition), never on ordinary Model content, so Elmish's
/// SubId diffing starts these once and never tears them down/restarts them
/// on later re-renders.
let subscribe (model: Model) : Sub<Msg> =
    match model.Boot with
    | Booted -> Sub.batch [ historySub; keydownSub; outsideClickSub; touchSub; scrollSub; focusSub ]
    | _ -> Sub.none
