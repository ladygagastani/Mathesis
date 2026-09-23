module Lenses.Widgets

// The three lenses that need something the DOM can't do declaratively: sound
// (Web Audio), a slippy map (Leaflet) and a deep-zoom image (OpenSeadragon).
// The two libraries are fetched from cdnjs the first time their lens opens,
// never on page load, so a reader who never opens them never pays for them.
//
// The map and image viewer draw into a div React renders empty and keyed, and
// keep their instance on that element — React owns the div, the library owns
// what's inside it.

open Fable.Core

/// Plays a rhythm: one note per syllable, `units` long (1 = short, 2 = long),
/// gliding from f0 to f1 Hz. Calls `onStep i` as note i sounds and `onDone`
/// after the last. Starting a new rhythm stops the one before.
[<Emit("""(() => {
  const __notes = $0, __unit = $1 / 1000, __onStep = $2, __onDone = $3;
  if (window.__anagAudio) { try { window.__anagAudio.stop(); } catch (e) {} }
  const Ctx = window.AudioContext || window.webkitAudioContext;
  if (!Ctx) { __onDone(); return; }
  const ctx = new Ctx();
  const timers = [];
  const master = ctx.createGain(); master.gain.value = 0.16; master.connect(ctx.destination);
  let t = ctx.currentTime + 0.06;
  const start = t;
  __notes.forEach((n, i) => {
    const d = n[0] * __unit, f0 = n[1], f1 = n[2];
    const o = ctx.createOscillator(); o.type = 'triangle';
    const g = ctx.createGain();
    o.frequency.setValueAtTime(f0, t);
    if (f1 !== f0) { o.frequency.linearRampToValueAtTime(Math.max(f0, f1) , t + d * 0.45); o.frequency.linearRampToValueAtTime(f1, t + d * 0.9); }
    g.gain.setValueAtTime(0, t);
    g.gain.linearRampToValueAtTime(1, t + 0.012);
    g.gain.setValueAtTime(1, t + d * 0.78);
    g.gain.linearRampToValueAtTime(0, t + d * 0.94);
    o.connect(g); g.connect(master); o.start(t); o.stop(t + d);
    timers.push(setTimeout(() => __onStep(i), (t - start) * 1000 + 60));
    t += d;
  });
  const stop = () => { timers.forEach(clearTimeout); try { ctx.close(); } catch (e) {} window.__anagAudio = null; };
  timers.push(setTimeout(() => { stop(); __onDone(); }, (t - start) * 1000 + 120));
  window.__anagAudio = { stop };
})()""")>]
let playRhythm (notes: (float * float * float) array) (unitMs: float) (onStep: int -> unit) (onDone: unit -> unit) : unit = jsNative

[<Emit("(window.__anagAudio && window.__anagAudio.stop())")>]
let stopRhythm () : unit = jsNative

// ---------------------------------------------------------------------------
// script loading
// ---------------------------------------------------------------------------

[<Emit("""(window.__anagLoad = window.__anagLoad || {}, window.__anagLoad[$0] || (window.__anagLoad[$0] = new Promise((res, rej) => {
  if ($1) { const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = $1; document.head.appendChild(l); }
  const s = document.createElement('script'); s.src = $0; s.async = true;
  s.onload = () => res(); s.onerror = () => { delete window.__anagLoad[$0]; rej(new Error('Could not load ' + $0)); };
  document.head.appendChild(s);
})))""")>]
let private loadScript (src: string) (css: string) : JS.Promise<unit> = jsNative

let private LEAFLET = "https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.js"
let private LEAFLET_CSS = "https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.css"
let private OSD = "https://cdnjs.cloudflare.com/ajax/libs/openseadragon/4.1.0/openseadragon.min.js"
let OSD_IMAGES = "https://cdnjs.cloudflare.com/ajax/libs/openseadragon/4.1.0/images/"

// ---------------------------------------------------------------------------
// map
// ---------------------------------------------------------------------------

/// A place as the map script wants it: plain fields, no F# types.
type MapPlace =
    { lat: float; lon: float; label: string; name: string; refs: string array; pleiades: string; here: bool }

let mapPlace lat lon label name refs pleiades here : MapPlace =
    { lat = lat; lon = lon; label = label; name = name; refs = refs; pleiades = pleiades; here = here }

[<Emit("""(() => {
  const __id = $0, __places = $1, __onPick = $2;
  const el = document.getElementById(__id);
  if (!el || !window.L) return;
  const L = window.L;
  let st = el.__anag;
  if (!st) {
    const map = L.map(el, { zoomControl: true, attributionControl: true, worldCopyJump: true });
    // Digital Atlas of the Roman Empire: the ancient Mediterranean, with
    // ancient names, roads and coastlines rather than modern borders.
    L.tileLayer('https://dh.gu.se/tiles/imperium/{z}/{x}/{y}.png', {
      maxZoom: 11, minZoom: 3,
      attribution: 'Tiles: <a href="https://imperium.ahlfeldt.se/" target="_blank" rel="noopener">DARE</a>, CC BY 4.0 · Places: <a href="https://pleiades.stoa.org/" target="_blank" rel="noopener">Pleiades</a> via Wikidata'
    }).addTo(map);
    st = { map, layer: L.layerGroup().addTo(map), key: '', markers: {} };
    el.__anag = st;
    setTimeout(() => map.invalidateSize(), 0);
  }
  const key = __places.map(p => p.pleiades).join(',');
  const css = getComputedStyle(document.documentElement);
  const accent = css.getPropertyValue('--accent').trim() || '#1F5F5B';
  const hot = css.getPropertyValue('--accent-2').trim() || '#9A3B2E';
  if (st.key !== key) {
    st.layer.clearLayers(); st.markers = {};
    if (__places.length > 1) {
      L.polyline(__places.map(p => [p.lat, p.lon]), { color: accent, weight: 1.5, opacity: .55, dashArray: '4 5' }).addTo(st.layer);
    }
    __places.forEach((p, i) => {
      const m = L.circleMarker([p.lat, p.lon], { radius: 6, color: accent, weight: 2, fillColor: accent, fillOpacity: .35 });
      const tip = document.createElement('div');
      const b = document.createElement('b'); b.textContent = (i + 1) + '. ' + p.label; tip.appendChild(b);
      if (p.label !== p.name) { tip.appendChild(document.createTextNode(' (“' + p.name + '”)')); }
      tip.appendChild(document.createElement('br'));
      tip.appendChild(document.createTextNode(p.refs.length === 1 ? 'named at ' + p.refs[0] : 'named at ' + p.refs.slice(0, 6).join(', ') + (p.refs.length > 6 ? '…' : '')));
      tip.appendChild(document.createElement('br'));
      const a = document.createElement('a'); a.href = 'https://pleiades.stoa.org/places/' + p.pleiades; a.target = '_blank'; a.rel = 'noopener'; a.textContent = 'Pleiades ' + p.pleiades; tip.appendChild(a);
      m.bindTooltip(p.label, { direction: 'top', offset: [0, -6] });
      m.bindPopup(tip);
      m.on('click', () => __onPick(p.refs[0]));
      m.addTo(st.layer);
      st.markers[p.pleiades] = m;
    });
    if (__places.length) {
      // Frame the cluster the passage is about: a single far-flung name
      // (Ethiopia at the edge of the world, say) is still marked, but it
      // doesn't zoom the whole Aegean down to a dot.
      const med = a => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };
      const mlat = med(__places.map(p => p.lat)), mlon = med(__places.map(p => p.lon));
      const near = __places.filter(p => Math.hypot(p.lat - mlat, p.lon - mlon) < 18);
      const b = L.latLngBounds((near.length ? near : __places).map(p => [p.lat, p.lon]));
      st.map.fitBounds(b.pad(0.25), { maxZoom: 8 });
    } else {
      st.map.setView([38.5, 24], 5);
    }
    st.key = key;
  }
  __places.forEach(p => {
    const m = st.markers[p.pleiades]; if (!m) return;
    m.setStyle(p.here ? { color: hot, fillColor: hot, fillOpacity: .8, radius: 9 } : { color: accent, fillColor: accent, fillOpacity: .35, radius: 6 });
    if (p.here) m.bringToFront();
  });
})()""")>]
let private drawMap (elId: string) (places: MapPlace array) (onPick: string -> unit) : unit = jsNative

let renderMap (elId: string) (places: MapPlace array) (onPick: string -> unit) : JS.Promise<unit> =
    loadScript LEAFLET LEAFLET_CSS |> Promise.map (fun () -> drawMap elId places onPick)

// ---------------------------------------------------------------------------
// deep-zoom image
// ---------------------------------------------------------------------------

[<Emit("""(() => {
  const __id = $0, __svc = $1, __prefix = $2;
  const el = document.getElementById(__id);
  if (!el || !window.OpenSeadragon) return;
  const src = __svc.replace(/\/$/, '') + '/info.json';
  if (el.__osd) { if (el.__osdSrc !== src) { el.__osd.open(src); el.__osdSrc = src; } return; }
  el.__osd = window.OpenSeadragon({ element: el, prefixUrl: __prefix, tileSources: src, showNavigator: true, navigatorPosition: 'BOTTOM_RIGHT', gestureSettingsMouse: { clickToZoom: false } });
  el.__osdSrc = src;
})()""")>]
let private drawImage (elId: string) (service: string) (prefix: string) : unit = jsNative

let renderImage (elId: string) (service: string) : JS.Promise<unit> =
    loadScript OSD "" |> Promise.map (fun () -> drawImage elId service OSD_IMAGES)
