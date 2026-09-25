module LearnFx

// Everything in the Learn section that the DOM has to do imperatively: the page
// turn, the ink that blurs into focus as a leaf opens, the small feedback
// animations (a card flipping, a wrong pair shaking), tracing a letter on a
// canvas, dragging word tiles, the device's Greek voice and the synthesised
// pitch tones. All of it lives in one object on `window.__lx`, bound once when
// the module loads (not an [<Emit>], which Fable would paste into every call
// site), so the JavaScript is in one place and each F# wrapper is one line.
//
// Timing: Elmish renders through React 18's createRoot, which commits a frame
// later than the update that asked for it, so anything that has to find the
// *new* DOM waits two animation frames (`paint`). Everything that animates is
// skipped under prefers-reduced-motion.
//
// The page turn has two halves. `snapshot` runs in the click handler, before
// dispatch, while the old leaf is still on screen: it lays a copy of it over
// the page, fixed where the reader sees it. `turn` runs from the update's
// command once the new leaf is rendered, and swings the copy away like a page
// of a codex. A snapshot nobody turns (the click led out of Learn) removes
// itself after 1.5 s.

open Fable.Core
open Fable.Core.JsInterop

let private fx: obj =
    emitJsExpr () """(window.__lx || (window.__lx = (() => {
  const RM = () => !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const paint = f => requestAnimationFrame(() => requestAnimationFrame(f));
  const E = 'cubic-bezier(.5,.05,.2,1)';
  const leafEl = () => document.querySelector('main .lx-leaf');
  const inkEl = (root, delay) => {
    if (!root || !root.animate || RM()) return;
    const n = root.hasAttribute('data-ink') ? [root] : [];
    root.querySelectorAll('[data-ink]').forEach(x => n.push(x));
    n.forEach((x, i) => x.animate(
      [{ opacity: 0, filter: 'blur(6px)', transform: 'translateY(3px)' },
       { opacity: .6, filter: 'blur(1.5px)', offset: .55 },
       { opacity: 1, filter: 'blur(0)', transform: 'none' }],
      { duration: 1000, delay: delay + Math.min(i, 14) * 70, easing: 'cubic-bezier(.2,.65,.25,1)', fill: 'backwards' }));
  };
  let pending = null;
  const drop = () => { if (pending) { clearTimeout(pending.timer); pending.wrap.remove(); pending = null; } };
  const headerBottom = () => { const h = document.querySelector('header'); return h ? Math.max(0, h.getBoundingClientRect().bottom) : 0; };
  let audio = null;
  return {
    ink(sel, delay) { paint(() => inkEl(document.querySelector(sel), delay)); },
    snapshot() {
      drop();
      const leaf = leafEl();
      if (!leaf || RM()) return;
      const r = leaf.getBoundingClientRect();
      const top = Math.max(r.top, headerBottom()), bottom = Math.min(r.bottom, window.innerHeight);
      if (bottom - top < 40) return;
      const wrap = document.createElement('div');
      wrap.className = 'lx-turning';
      wrap.setAttribute('aria-hidden', 'true');
      wrap.style.cssText = 'left:' + r.left + 'px;top:' + top + 'px;width:' + r.width + 'px;height:' + (bottom - top) + 'px';
      const copy = leaf.cloneNode(true);
      copy.style.cssText = 'position:absolute;left:0;top:' + (r.top - top) + 'px;width:' + r.width + 'px;margin:0';
      const shade = document.createElement('div');
      shade.className = 'lx-shade';
      wrap.append(copy, shade);
      document.body.appendChild(wrap);
      pending = { wrap, timer: setTimeout(drop, 1500) };
    },
    turn() {
      paint(() => {
        window.scrollTo(0, 0);
        const leaf = leafEl(), t = pending;
        if (t) {
          clearTimeout(t.timer); pending = null;
          const a = t.wrap.animate(
            [{ transform: 'perspective(1700px) rotateY(0deg)' }, { transform: 'perspective(1700px) rotateY(-102deg)' }],
            { duration: 780, easing: E, fill: 'forwards' });
          a.onfinish = a.oncancel = () => t.wrap.remove();
          t.wrap.lastChild.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 780, easing: E, fill: 'forwards' });
          if (leaf && leaf.animate) leaf.animate([{ filter: 'brightness(.88)' }, { filter: 'brightness(1)' }], { duration: 780, easing: E });
        }
        inkEl(leaf, t ? 300 : 180);
      });
    },
    flipCard() {
      paint(() => {
        const c = document.querySelector('[data-card]');
        if (c && c.animate && !RM())
          c.animate([{ transform: 'perspective(900px) rotateY(88deg)' }, { transform: 'perspective(900px) rotateY(0deg)' }],
                    { duration: 560, easing: 'cubic-bezier(.2,.7,.2,1)' });
        inkEl(document.querySelector('[data-reveal="card"]'), 120);
      });
    },
    slideCard() {
      paint(() => {
        const c = document.querySelector('[data-card]');
        if (!c || !c.animate || RM()) return;
        c.animate([{ transform: 'translateX(36px)', opacity: 0 }, { transform: 'none', opacity: 1 }],
                  { duration: 420, easing: 'cubic-bezier(.2,.7,.2,1)' });
        inkEl(c, 80);
      });
    },
    shake(sel) {
      paint(() => {
        const el = document.querySelector(sel);
        if (el && el.animate && !RM())
          el.animate([{ transform: 'translateX(0)' }, { transform: 'translateX(-6px)' }, { transform: 'translateX(5px)' },
                      { transform: 'translateX(-2px)' }, { transform: 'translateX(0)' }], { duration: 360 });
      });
    },
    bars() {
      if (RM()) return;
      document.querySelectorAll('[data-bar]').forEach((b, i) => b.animate(
        [{ transform: 'scaleY(.3)', opacity: .45 },
         { transform: 'scaleY(' + (1 + ((i * 7) % 5) / 4) + ')', opacity: 1, backgroundColor: 'var(--solid)' },
         { transform: 'scaleY(.5)', opacity: .7 },
         { transform: 'scaleY(1)', opacity: .45 }],
        { duration: 1100, delay: i * 22, easing: 'ease-in-out' }));
    },
    contour(i, ms) {
      const p = document.querySelector('[data-contour="' + i + '"]');
      if (p && p.animate && !RM()) p.animate([{ strokeDashoffset: 300 }, { strokeDashoffset: 0 }], { duration: ms, easing: 'linear' });
    },
    watch() {
      const t = document.querySelector('[data-ghost]');
      if (t && t.animate && !RM())
        t.animate([{ strokeDashoffset: 1400, fillOpacity: 0 }, { strokeDashoffset: 0, fillOpacity: 0, offset: .75 },
                   { strokeDashoffset: 0, fillOpacity: .2 }], { duration: 2400, easing: 'ease-in-out' });
    },
    speak(text) {
      try {
        if (!window.speechSynthesis) return;
        const u = new SpeechSynthesisUtterance(text);
        u.lang = 'el-GR'; u.rate = .8;
        speechSynthesis.cancel(); speechSynthesis.speak(u);
      } catch (e) {}
    },
    // syls: [[pitches...], durationMs] per syllable; pitches in semitones above G3.
    tone(syls, gap) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      try {
        // One sound at a time across the app: this also stops the meter lens.
        if (window.__anagAudio) { try { window.__anagAudio.stop(); } catch (e) {} }
        audio = audio || new AC();
        const ac = audio;
        if (ac.state === 'suspended') ac.resume();
        const o = ac.createOscillator(), f = ac.createBiquadFilter(), g = ac.createGain();
        o.type = 'triangle'; f.type = 'lowpass'; f.frequency.value = 1400;
        o.connect(f); f.connect(g); g.connect(ac.destination);
        const hz = st => 196 * Math.pow(2, st / 12);
        let t = ac.currentTime + .06;
        g.gain.setValueAtTime(0, ac.currentTime);
        syls.forEach(([p, dur]) => {
          const d = dur / 1000;
          o.frequency.setValueAtTime(hz(p[0]), t);
          g.gain.setValueAtTime(.001, t);
          g.gain.linearRampToValueAtTime(.2, t + .035);
          p.forEach((q, k) => { if (k > 0) o.frequency.linearRampToValueAtTime(hz(q), t + d * k / (p.length - 1)); });
          g.gain.setValueAtTime(.2, t + d - .05);
          g.gain.linearRampToValueAtTime(.001, t + d);
          t += d + gap / 1000;
        });
        o.start(); o.stop(t + .1);
        window.__anagAudio = { stop: () => { try { o.stop(); } catch (e) {} window.__anagAudio = null; } };
      } catch (e) {}
    },
    penDown(e) {
      const c = e.currentTarget;
      const r = c.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
      const W = Math.round(c.clientWidth * dpr), H = Math.round(c.clientHeight * dpr);
      if (c.width !== W || c.height !== H) { c.width = W; c.height = H; }
      const ctx = c.getContext('2d');
      ctx.setTransform(c.width / r.width, 0, 0, c.height / r.height, 0, 0);
      ctx.lineCap = 'round'; ctx.lineJoin = 'round';
      ctx.strokeStyle = ctx.fillStyle = getComputedStyle(c).color;
      try { c.setPointerCapture(e.pointerId); } catch (_) {}
      c.__pen = { x: e.clientX - r.left, y: e.clientY - r.top, r, ctx, w: 5 };
      ctx.beginPath(); ctx.arc(c.__pen.x, c.__pen.y, 2.4, 0, Math.PI * 2); ctx.fill();
    },
    penMove(e) {
      const c = e.currentTarget, p = c.__pen;
      if (!p) return c.__ink || 0;
      const x = e.clientX - p.r.left, y = e.clientY - p.r.top, dist = Math.hypot(x - p.x, y - p.y);
      if (dist < 1) return c.__ink || 0;
      p.w = p.w * .65 + Math.max(2.2, Math.min(7.5, 7.5 - dist * .3)) * .35;
      const ctx = p.ctx;
      ctx.lineWidth = p.w; ctx.globalAlpha = .92;
      ctx.beginPath(); ctx.moveTo(p.x, p.y); ctx.lineTo(x, y); ctx.stroke();
      p.x = x; p.y = y;
      c.__ink = (c.__ink || 0) + dist;
      return c.__ink;
    },
    penUp(e) { e.currentTarget.__pen = null; },
    clearInk() {
      const c = document.querySelector('[data-canvas]');
      if (!c) return;
      c.getContext('2d').clearRect(0, 0, c.width, c.height);
      c.__ink = 0;
    },
    dragDown(e) {
      const el = e.currentTarget;
      try { el.setPointerCapture(e.pointerId); } catch (_) {}
      el.__drag = { x: e.clientX, y: e.clientY, moved: false };
    },
    dragMove(e) {
      const el = e.currentTarget, d = el.__drag;
      if (!d) return;
      const dx = e.clientX - d.x, dy = e.clientY - d.y;
      if (!d.moved && Math.hypot(dx, dy) < 5) return;
      d.moved = true;
      el.classList.add('dragging');
      el.style.transform = 'translate(' + dx + 'px,' + dy + 'px) rotate(' + (dx / 70) + 'deg)';
    },
    // Where a tile was let go: { moved, toLine, index } — index is the slot on
    // the line it was dropped at, counted over the other tiles there.
    dragUp(e) {
      const el = e.currentTarget, d = el.__drag;
      el.__drag = null;
      el.classList.remove('dragging');
      el.style.transform = '';
      if (!d || !d.moved) return { moved: false, toLine: false, index: 0 };
      const line = document.querySelector('[data-line]');
      const x = e.clientX, y = e.clientY;
      const lr = line && line.getBoundingClientRect();
      if (!lr || x < lr.left - 10 || x > lr.right + 10 || y < lr.top - 10 || y > lr.bottom + 10)
        return { moved: true, toLine: false, index: 0 };
      let index = 0;
      line.querySelectorAll('[data-lt]').forEach(n => {
        if (n === el) return;
        const r = n.getBoundingClientRect(), cy = r.top + r.height / 2;
        if (cy < y - r.height / 2 || (Math.abs(cy - y) <= r.height / 2 && r.left + r.width / 2 < x)) index++;
      });
      return { moved: true, toLine: true, index };
    }
  };
})()))"""

/// Blurs the element matching `selector` and its `[data-ink]` descendants into focus.
let inkIn (selector: string) (delay: int) : unit = fx?ink (selector, delay)

/// Call in the click handler, before dispatching a message that turns the leaf.
let snapshot () : unit = fx?snapshot ()

/// Call from the command of the update that turned the leaf.
let turn () : unit = fx?turn ()

let flipCard () : unit = fx?flipCard ()
let slideCard () : unit = fx?slideCard ()
let shake (selector: string) : unit = fx?shake selector
let pulseBars () : unit = fx?bars ()
let drawContour (index: int) (ms: int) : unit = fx?contour (index, ms)
let watchLetter () : unit = fx?watch ()

/// Speaks with the device's Greek (modern, el-GR) voice, if it has one.
let speak (text: string) : unit = fx?speak text

/// Plays a pitch contour: per syllable, its pitch points (semitones) and length (ms).
let tone (syllables: (float array * int) array) (gapMs: int) : unit = fx?tone (syllables, gapMs)

let penDown (e: Browser.Types.PointerEvent) : unit = fx?penDown e
/// Returns the total length of ink laid on the canvas so far.
let penMove (e: Browser.Types.PointerEvent) : float = fx?penMove e
let penUp (e: Browser.Types.PointerEvent) : unit = fx?penUp e
let clearInk () : unit = fx?clearInk ()

type Drop = { moved: bool; toLine: bool; index: int }

let dragDown (e: Browser.Types.PointerEvent) : unit = fx?dragDown e
let dragMove (e: Browser.Types.PointerEvent) : unit = fx?dragMove e
let dragUp (e: Browser.Types.PointerEvent) : Drop = fx?dragUp e
