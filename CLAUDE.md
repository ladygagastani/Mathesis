# CLAUDE.md — Μάθησις (Ancient Greek Reader) → Fable 5.1 / Elmish / Feliz Port

This file is the **master blueprint** for porting the existing single-file HTML/CSS/JS
application ("Μάθησις — Ancient Greek Reader") to **F# / Fable 5.1** using the
**Elmish MVU** architecture and **Feliz** views.

Read this file completely before writing any code. Implement in the order given in
**§9 Implementation Checklist**. Run `dotnet build` after *every* file and fix all
compiler errors before continuing (see **§10 Terminal & Compiler Rules**).

---

## 1. What the original app does (functional inventory)

A client-only SPA for reading Ancient Greek texts with a translation aligned beside them.

| Area | Behaviour |
|---|---|
| **Catalog** | A gzip+base64 blob (`<script id="catalog">`) is decoded in-browser into a list of authors → works → texts (TEI file metadata). A second plain JSON blob (`<script id="meta">`) holds eras + per-author Wikidata metadata + hand-written "core" articles. |
| **Text loading** | TEI XML is fetched on demand from GitHub raw (`PerseusDL/canonical-greekLit`, `OpenGreekAndLatin/First1KGreek`), or read from a **local folder** (File System Access API / `<input webkitdirectory>`), or from a **downloaded ZIP** (custom central-directory parser + `DecompressionStream('deflate-raw')`), or from a custom **base URL**. Handles are remembered in IndexedDB. |
| **TEI pipeline** | `tokenize` (XML → event list) → `segment` (events → segments keyed by numbered `div` path + `section`/`card` milestone) → `truncateTo` + `align` (Greek segments ⟷ translation segments) → chunking + paging. |
| **Reader** | Two-column aligned view, per-segment CTS URN (click to copy), line numbers every 5, speaker labels, inline `⟦page/line⟧` markers, clickable Greek words → lookup popover (Logeion / Perseus morph / Wiktionary), edition pickers, chunk/part navigation, page navigation, "go to ref" jump box, translation coverage note. |
| **My library** | Favourite works, per-passage bookmarks with notes, wiki-style cross-references `[[tlg0012.tlg001:1.1|label]]` with backlinks, per-author notes, JSON export/import, clear-all. Stored in `localStorage`. |
| **Wiki** | Wiki home, authors index (filter by era / by derived genre, search), eras pages, "Manuscripts & transmission" and "Textual variants" article indexes, "Editions & translations" grouped by publisher series, author pages (timeline, works, articles, editions, notes, optional live Wikipedia summary fetch), About & acknowledgments. |
| **Home** | Hero, stats, lead, "Continue reading", filter + work search, suggested picks, wiki card, passage of the day, reading paths, eras strip, alphabet table, "five things to know", how-it-works, offline setup block, sources footer. Sections collapsible on phones. |
| **Chrome** | Sticky header, breadcrumbs, in-app back stack (`history.pushState`), sidebar library with search/filter/accordion, settings sheet (columns, typeface, text size, line height, theme, text source), toast, mobile drawer with touch-drag, bottom tab bar, backdrop. |
| **Persistence** | `localStorage` keys: `anag:mode`, `anag:face`, `anag:fs`, `anag:lh`, `anag:theme`, `anag:src`, `anag:srcBase`, `anag:filter`, `anag:navHidden`, `anag:recent`, `anag:colState`, `anag:lib`. IndexedDB: db `anag`, store `kv`, keys `dirs`, `zips`. |

> Note: the pasted second document (`FEAT` module) is **the same code** as the inline
> `window.FEAT` block in `index.html`. Port it **once** into `Library.fs` + `Wiki.fs` +
> `Views/*`.

---

## 2. Project Architecture & Tech Stack

### 2.1 Stack

- **.NET SDK 8.0+**
- **Fable 5.1** (local dotnet tool)
- **Elmish 4.x** (`Fable.Elmish`, `Fable.Elmish.React`, `Fable.Elmish.HMR`)
- **Feliz 2.x** for views (`Feliz`, `Feliz.UseListener` optional)
- **Thoth.Json 10.x** for decoding the catalog/meta JSON blobs
- **Fable.Promise** + `Fable.Browser.*` bindings for browser APIs
- **Vite 5** as dev server / bundler
- **No component framework, no Tailwind** — the original CSS is reused verbatim.

### 2.2 Files / folders

```
/
├── CLAUDE.md
├── .config/dotnet-tools.json          # fable tool manifest
├── package.json                       # vite + npm scripts
├── vite.config.js
├── index.html                         # shell only (see §4.1)
├── public/style.css                   # ALL original CSS, unchanged
├── src/
│   ├── App.fsproj
│   ├── Interop.fs                     # raw JS interop (§5)
│   ├── Types.fs                       # domain + Model + Msg (§3)
│   ├── Storage.fs                     # localStorage + IndexedDB
│   ├── Json.fs                        # Thoth decoders for catalog & meta
│   ├── Catalog.fs                     # base64+gzip decode, indexes, helpers
│   ├── Tei/Tokenizer.fs
│   ├── Tei/Segmenter.fs
│   ├── Tei/Aligner.fs
│   ├── Sources.fs                     # GitHub / local dir / ZIP / URL loaders
│   ├── Zip.fs                         # ZIP central-directory reader
│   ├── Router.fs                      # hash <-> Route, in-app history stack
│   ├── LibraryData.fs                 # favs/marks/notes/links pure functions
│   ├── WikiData.fs                    # eras, genres, era notes, static article prose
│   ├── Content.fs                     # PASSAGES, PATHS, BROWSE, ALPHABET, TIPS, ICONS
│   ├── Lenses/Greek.fs                # accent-folding, word splitting, stopwords (§11)
│   ├── Lenses/Prosody.fs              # syllabification + metre fitting (§11)
│   ├── Lenses/Corpus.fs               # trigram echo index, stem concordance (§11)
│   ├── Lenses/Places.fs               # translation names → Wikidata/Pleiades (§11)
│   ├── Lenses/Paleography.fs          # uncial/minuscule views, IIIF manifests (§11)
│   ├── Lenses/Widgets.fs              # Web Audio, lazy Leaflet + OpenSeadragon (§11)
│   ├── Views/Shared.fs                # esc-free helpers, chips, buttons, icons, toast
│   ├── Views/Header.fs
│   ├── Views/Nav.fs
│   ├── Views/SettingsPane.fs
│   ├── Views/Popover.fs
│   ├── Views/Home.fs
│   ├── Views/Lens.fs                  # the study rail (§11)
│   ├── Views/Reader.fs
│   ├── Views/LibraryPage.fs
│   ├── Views/WikiPages.fs
│   ├── Views/About.fs
│   ├── Views/Browse.fs
│   ├── State.fs                       # init + update + Cmd helpers
│   ├── Subscriptions.fs               # hashchange, keydown, clicks, touch, theme
│   └── App.fs                         # Program.mkProgram wiring
```

**F# compilation order is significant.** `App.fsproj` `<Compile Include>` items must be
listed exactly in the order above. Never reference a module defined later in the list.

### 2.3 `App.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors>FS0025</WarningsAsErrors> <!-- incomplete matches are bugs -->
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Interop.fs" />
    <Compile Include="Types.fs" />
    <!-- ... exact order from §2.2 ... -->
    <Compile Include="App.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Fable.Core" Version="4.*" />
    <PackageReference Include="Fable.Browser.Dom" Version="2.*" />
    <PackageReference Include="Fable.Browser.Blob" Version="1.*" />
    <PackageReference Include="Fable.Browser.Event" Version="1.*" />
    <PackageReference Include="Fable.Browser.WebStorage" Version="1.*" />
    <PackageReference Include="Fable.Promise" Version="3.*" />
    <PackageReference Include="Fable.Elmish" Version="4.*" />
    <PackageReference Include="Fable.Elmish.React" Version="4.*" />
    <PackageReference Include="Fable.Elmish.HMR" Version="7.*" />
    <PackageReference Include="Feliz" Version="2.*" />
    <PackageReference Include="Thoth.Json" Version="10.*" />
  </ItemGroup>
</Project>
```

If any package version fails to restore, run `dotnet add package <name>` and let NuGet
pick the latest compatible version — **do not** hand-edit versions blindly.

---

## 3. Domain & State Model (Types.fs)

All types below are **normative**. Use exactly these names unless a compiler error forces
a change (then update this file's intent, not the semantics).

### 3.1 Catalog & metadata

```fsharp
module Types

type TextKind = Edition | Translation | Commentary | OtherKind of string

/// One TEI file (edition or translation) belonging to a work.
type TextMeta =
    { Urn   : string            // urn:cts:greekLit:tlg0012.tlg001.perseus-grc2
      Label : string            // Greek title / short label from TEI
      Desc  : string option     // printed-edition description
      Lang  : string            // "grc" | "eng" | "lat" | ...
      Kind  : TextKind
      File  : string            // tlg0012.tlg001.perseus-grc2.xml
      Repo  : string            // "PerseusDL/canonical-greekLit" | "OpenGreekAndLatin/First1KGreek"
      Kb    : int
      Refs  : string list }     // citation scheme, e.g. ["book";"line"]

type Work =
    { Id       : string         // "tlg0012.tlg001"
      Title    : string
      AuthorId : string
      Texts    : TextMeta list }

type Author =
    { Id    : string            // "tlg0012"
      Name  : string
      Grc   : string option
      Works : Work list }

type Catalog =
    { Authors     : Author list
      WorkById    : Map<string, Work>
      AuthorOfWork: Map<string, Author> }

type Era = { Id: string; Name: string; From: int; To: int }

type CoreArticle =
    { Summary     : string
      Timeline    : (int * string) list
      Manuscripts : string
      Variants    : string
      Editions    : string list }

type AuthorMeta =
    { Name    : string
      Grc     : string option
      Birth   : int option
      Death   : int option
      Floruit : int option
      Year    : int option          // sort key
      Era     : string option
      Desc    : string option
      Wiki    : string option
      Q       : string option
      Place   : string option
      Occ     : string list
      NWorks  : int
      Core    : CoreArticle option }

type Meta = { Eras: Era list; Authors: Map<string, AuthorMeta> }
```

### 3.2 TEI pipeline

```fsharp
/// Output of Tei.Tokenizer (mirrors the JS event tuples 1:1).
type TeiEvent =
    | EvDiv       of path: string list
    | EvMilestone of unit_: string option * n: string option
    | EvSpeaker   of string
    | EvSpeakerEnd
    | EvHead      of string
    | EvParaBreak
    | EvText      of string
    | EvLine      of n: string option * text: string

/// A rendered paragraph-level unit inside a segment.
type Block =
    | Heading of string
    | Prose   of speaker: string option * text: string
    | Verse   of speaker: string option * lines: (string option * string) list

type RawSegment = { Key: string list; Blocks: Block list }

/// Greek + (optional) aligned translation for one citable passage.
type Segment =
    { Ref : string              // "1.33"  (Key joined with '.')
      Key : string list
      Grc : Block list
      Eng : Block list option }

type Chunk = { Ref: string; Segments: Segment array }   // Ref = "all" when unchunked

type Coverage = { Covered: int; Total: int; First: string option; Last: string option }

type AlignedText =
    { Depth    : int
      Chunks   : Chunk list
      Segments : Segment array
      Coverage : Coverage option }
```

### 3.3 Settings, source, library, routes

```fsharp
type ColumnMode = Both | GrcOnly | EngOnly
type Typeface   = Serif | Sans
type ThemePref  = ThemeAuto | ThemeLight | ThemeDark

type Settings =
    { Mode  : ColumnMode
      Face  : Typeface
      FontSize   : float      // rem, 0.85 .. 1.80 step 0.06, default 1.18
      LineHeight : float      // 1.2 .. 2.2 step 0.1, default 1.65
      Theme : ThemePref }

type WorksFilter = FilterAll | FilterTranslated | FilterGreekOnly

type SourceMode = SourceGitHub | SourceLocal | SourceUrl

type RepoKey = Perseus | First1K        // REPO_KEY(repo) in JS

type LocalKind =
    | ZipArchive of name: string        // files served from a parsed ZIP index
    | DirHandle  of name: string        // File System Access directory
    | FileList                          // <input webkitdirectory> (session only)

type SourceState =
    { Mode     : SourceMode
      BaseUrl  : string
      Local    : Map<RepoKey, LocalKind>   // what is connected, for UI text
      NeedsReconnect : string list }       // remembered handle names awaiting a click

type MarkLink = { Work: string; Ref: string; Label: string option }

type Mark =
    { Id: string; Work: string; Ref: string
      Label: string; Snippet: string; Note: string
      Links: MarkLink list; Ts: float }

type Library =
    { Favs        : string list
      Marks       : Mark list
      AuthorNotes : Map<string, string> }

type RecentEntry = { Id: string; Chunk: string option; Ts: float }

type AuthorScope = ByEra of string | ByGenre of string      // "undated"/"other" allowed
type ArticleKind = Manuscripts | Variants

type WikiRoute =
    | WikiHome
    | WikiAuthors of AuthorScope option
    | WikiEras    of string option
    | WikiArticles of ArticleKind
    | WikiEditions

type Route =
    | Landing
    | Browse
    | LibraryRoute
    | AboutRoute
    | AuthorRoute of id: string * section: string option
    | WikiRoute   of WikiRoute
    | ReaderRoute of workId: string * grcSuffix: string * engSuffix: string
                     * chunk: string option * seg: string option
```

### 3.4 Reader state & async wrappers

```fsharp
type LoadError =
    | NetworkBlocked
    | HttpStatus of int
    | XmlParseFailed of string
    | ErrorMessage of string

type ReaderPhase =
    | Loading of status: string
    | Ready
    | LoadFailed of LoadError

type ReaderModel =
    { Token   : int                     // guards stale async results (loadToken in JS)
      Work    : Work
      Grc     : TextMeta
      Eng     : TextMeta option
      Phase   : ReaderPhase
      Data    : AlignedText option
      Chunk   : string option
      Page    : int
      PendingSeg : string option
      FlashSeg   : string option
      OpenEditor : (string * string) option }   // (workId, segRef) editor expanded
```

### 3.5 Boot + shell + Model

```fsharp
type BootState =
    | Booting of message: string
    | BootFailed of string
    | Booted

type PopoverKind = WordPopover of word: string * anchorRect: {| left: float; top: float; bottom: float |}

type Model =
    { Boot     : BootState
      Catalog  : Catalog
      Meta     : Meta
      Route    : Route
      Settings : Settings
      Filter   : WorksFilter
      Library  : Library
      Source   : SourceState
      Reader   : ReaderModel option

      // shell / chrome
      NavHidden    : bool                 // desktop sidebar collapsed
      SideOpen     : bool                 // phone drawer open
      SettingsOpen : bool
      Popover      : PopoverKind option
      Toast        : (int * string) option    // (id, message) — id lets Cmd cancel
      JumpInput    : string

      // sidebar
      NavQuery     : string
      OpenAuthors  : Set<string>          // expanded when no query
      ClosedAuthors: Set<string>          // collapsed while a query is active ("!"+id in JS)
      PartsOpen    : bool

      // search boxes
      HomeQuery    : string
      BrowseQuery  : string
      WikiQuery    : string

      // misc
      Recent    : RecentEntry list        // max 6
      Collapsed : Map<string, bool>       // data-col section state on phones
      TextCache : Map<string, RawSegment list>   // keyed by TextMeta.Urn
      History   : string list             // in-app back stack of hashes
      CurrentHash : string
      DrawerDrag  : {| StartX: float; Dx: float; Mode: string; Width: float |} option
      NextToken   : int }
```

### 3.6 Msg

```fsharp
type SettingsMsg =
    | SetMode of ColumnMode
    | SetFace of Typeface
    | SetTheme of ThemePref
    | BumpFontSize of float
    | BumpLineHeight of float
    | ToggleSettingsPane
    | ToggleThemeQuick

type SourceMsg =
    | SetSourceMode of SourceMode
    | SetBaseUrl of string
    | PickZipClicked
    | PickDirClicked
    | ZipConnected of RepoKey * name: string
    | DirConnected of RepoKey list * name: string
    | FileListConnected of count: int
    | ReconnectRemembered
    | RememberedFound of string list
    | SourceFailed of string

type LibraryMsg =
    | ToggleFav of workId: string
    | OpenMarkEditor of workId: string * segRef: string * label: string * snippet: string
    | CloseMarkEditor
    | SetMarkNote of workId: string * segRef: string * note: string
    | AddMarkLink of workId: string * segRef: string * MarkLink
    | RemoveMarkLink of workId: string * segRef: string * work: string * ref_: string
    | RemoveMark of workId: string * segRef: string
    | SetAuthorNote of authorId: string * text: string
    | SaveAuthorNote of authorId: string
    | ExportRequested
    | ImportText of string
    | ImportConfirmed
    | ClearLibraryConfirmed

type ReaderMsg =
    | OpenWork of workId: string * grcUrn: string option * engUrn: string option
                  * chunk: string option * seg: string option
    | LoadStatus of token: int * string
    | GrcLoaded  of token: int * RawSegment list
    | EngLoaded  of token: int * RawSegment list option
    | LoadFailedMsg of token: int * LoadError
    | RetryLoad
    | PickGrcEdition of urn: string
    | PickEngEdition of urn: string          // "none" allowed
    | ShowChunk of chunkRef: string * seg: string option * page: int option
    | PrevUnit
    | NextUnit
    | GotoRefSubmitted
    | SetJumpInput of string
    | CopyUrn of string
    | WordClicked of word: string * rect: obj

type Msg =
    | Boot of Result<Catalog * Meta, string>
    | HashChanged of string
    | Navigate of hash: string * replace: bool
    | BackClicked
    | Settings_ of SettingsMsg
    | Source_ of SourceMsg
    | Library_ of LibraryMsg
    | Reader_ of ReaderMsg
    | SetFilter of WorksFilter
    | SetNavQuery of string
    | ToggleAuthor of authorId: string
    | ToggleParts
    | ToggleNavHidden
    | ToggleSide of bool
    | SetHomeQuery of string
    | SetBrowseQuery of string
    | SetWikiQuery of string
    | ToggleCollapsed of key: string
    | ForgetRecent of string option          // None = clear all
    | ClosePopover
    | ShowToast of string
    | HideToast of int
    | WikipediaSummary of authorId: string * extract: string
    | DrawerTouch of phase: string * x: float * y: float
    | KeyPressed of key: string * alt: bool
    | NoOp
```

### 3.7 Required signatures

```fsharp
val init   : unit -> Model * Cmd<Msg>
val update : Msg -> Model -> Model * Cmd<Msg>
val view   : Model -> (Msg -> unit) -> Fable.React.ReactElement
val subscribe : Model -> Sub<Msg>
```

`init` must:
1. Build a `Model` with `Boot = Booting "Opening the catalogue…"`, empty `Catalog`, settings/library/filter/recent/collapsed/source read synchronously from `localStorage`.
2. Return `Cmd.OfPromise.either Catalog.decodeEmbedded () (Ok >> Boot) (fun e -> Boot(Error e.Message))`.
3. Not touch the DOM. All DOM side effects (CSS variables, `data-theme`, body classes, `history.pushState`, clipboard, file pickers) live in `Cmd.ofEffect`/`Cmd.OfFunc` commands, never inside `update`'s pure part.

---

## 4. HTML-to-Fable View Mapping

### 4.1 `index.html` after the port

`index.html` keeps **only** the shell; every other node is rendered by Feliz:

```html
<!DOCTYPE html><html lang="en"><head>
  <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Μάθησις — Ancient Greek Reader</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Cardo:ital,wght@0,400;0,700;1,400&family=Noto+Sans:ital,wght@0,400;0,500;0,600;1,400&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="/style.css">
</head><body>
  <div id="root"></div>
  <script id="catalog" type="application/gzip;base64">...unchanged blob...</script>
  <script id="meta" type="application/json">...unchanged blob...</script>
  <script type="module" src="/src/App.fs.js"></script>
</body></html>
```

- Copy **all** CSS from the original `<style>` blocks into `public/style.css` verbatim
  (the original has a stray unclosed `<style>` tag — merge the blocks and close properly).
- **Class names must not change.** The CSS is the contract.

### 4.2 Component map

| Original HTML / JS function | Fable module → function | Notes |
|---|---|---|
| `#loading` overlay | `Views.Shared.bootOverlay : BootState -> ReactElement` | Rendered only when `Boot <> Booted`; shows `.g`/`#loadmsg`/`.err`. |
| `<header>` | `Views.Header.render : Model -> dispatch -> ReactElement` | Contains the six children below. |
| `#navToggle` | `Views.Header.navToggleButton` | `< 900px` → `ToggleSide (not SideOpen)`, else `ToggleNavHidden`. |
| `#btnBack` | `Views.Header.backButton` | `prop.hidden (List.isEmpty model.History)`; dispatches `BackClicked`. |
| `.brand#home` | `Views.Header.brand` | click → `Navigate("#", false)`. |
| `.topnav` | `Views.Header.topNav` | Two anchors `Texts` / `Wiki`, `active` class from `Router.navKey model.Route`. |
| `.crumbs` | `Views.Header.crumbs` | Derived from `Route` + `Reader` (author › work › chunk). |
| `.jump#jump` | `Views.Header.jumpBox` | `hidden` unless a reader route; input + Go button. |
| `.tools` (`#libBtn`, `#btnType`, `#btnTheme`) | `Views.Header.tools` | |
| `nav.side#side` | `Views.Nav.render` | `.side-head`, `#q` search, `.filter` group, `#authors`. |
| `renderNav()` accordion | `Views.Nav.authorList` | Pure: filter authors/works by `NavQuery` + `Filter`; `.author`/`.works`/`.work`/`.parts-toggle`/`.chunks`. Use `prop.key` = author/work id. |
| `main#main` | `Views.render` (`State.view`) | `prop.className ("mode-" + modeClass)`; body of switch on `model.Route`. |
| `.corner-amphora` | `Views.Shared.cornerAmphora` | static decorative div. |
| `#settings` dialog | `Views.SettingsPane.render` | `.grp` rows, `.segbtn` groups, `.stepper` for fs/lh, `#srcBtns`, `#srcLocal`, `#srcUrl`, hidden file inputs (`#dirInput`, `#zipInput`). |
| `#pop` word popover | `Views.Popover.render` | Positioned via `prop.style` from `Popover` rect; three external links built with `encodeURIComponent`. |
| `#toast` | `Views.Shared.toast` | `show` class when `model.Toast.IsSome`. |
| `.backdrop` (created in JS) | `Views.Shared.backdrop` | rendered declaratively; class `show` when drawer open on phones. |
| `landing()` / `FEAT.renderHome` | `Views.Home.render` | See sub-map below. |
| `.hero-wrap` | `Views.Home.hero` | |
| `.stats` | `Views.Home.stats` | counts: authors, works, works with translation. |
| `.lead` | `Views.Home.lead` | |
| `continueReading()` | `Views.Home.continueReading` | `.cont-item`, `.cont-open`, `.cont-x`, `#contClear`. |
| `.home-tools` + `#homeQ` + `#homeResults` | `Views.Home.tools` / `Views.Home.results` | `bindWorkSearch` becomes pure ranked filtering (`Catalog.searchWorks`). |
| `.picks-card` | `Views.Home.picksCard` | uses `Views.Shared.workCard`. |
| `wikiCard()` | `Views.Home.wikiCard` | |
| `passageOfDay()` | `Views.Home.passageOfDay` | index `floor(now/864e5) % available`. |
| `readingPaths()` | `Views.Home.readingPaths` | from `Content.paths`. |
| `erasStrip()` | `Views.Home.erasStrip` | |
| `alphabetBlock()` | `Views.Home.alphabet` | from `Content.alphabet`. |
| `tipsBlock()` | `Views.Home.tips` | |
| `howTo()` | `Views.Home.howTo` | |
| `.offline` block | `Views.Home.offlineBlock` | `#landZip`, `#landPick`, `#landLocal`, download links. |
| `.sources` | `Views.Home.sources` | |
| `bindCollapsible()` | `Views.Shared.collapsibleSection key title children` | Adds `mcol`/`mhead`/`collapsed` classes from `model.Collapsed` + `Content.colDefaults`. |
| `workHead()` | `Views.Reader.workHead` | `h1`, fav button, `.meta`, `.pickers` with two `select`s (`onChange` → `PickGrcEdition`/`PickEngEdition`). |
| `.status` (loading/error) | `Views.Reader.statusPane` | `.g`, `#st`, `.err`, `#retry`, `#openSrc`. |
| `.pages` | `Views.Reader.pageBar` | |
| `.cover-note` | `Views.Reader.coverNote` | |
| `.colhead` | `Views.Reader.columnHead` | |
| `showChunk()` segment loop | `Views.Reader.segment` | `<section class="seg" id=("s-"+ref) data-ref=ref tabIndex=-1>`; `.ref` (`b` label, `.urn`, `.mk-btn`), `.col.grc`, `.col.eng` (or `.col.eng.none`), `.mk-box`, `.mk-edit`. |
| `renderBlocks()` | `Views.Reader.blocks : bool -> Block list -> ReactElement list` | `Heading` → `.head-block`; `Prose` → `<p>` (+ `.speaker` span); `Verse` → `.line` spans with `.n`. |
| `words()` | `Views.Reader.tokens : bool -> string -> ReactElement list` | **Do not use `dangerouslySetInnerHTML`.** Split with `Interop.greekWordRegex` and `⟦…⟧` marker regex, emit `span.w` / `span.mk` / plain text. Greek words carry `onClick` → `Reader_(WordClicked …)`. |
| `.pager` | `Views.Reader.pager` | |
| `libraryPage()` | `Views.LibraryPage.render` | favourites `.picks`, `.lib-work`/`.lib-mark`/`.lm-head`/`.lm-snip`, `.mk-note`, `.mk-back`, `.lib-io` export/import/clear, `#libJson`. |
| `openEditor()` | `Views.Reader.markEditor` | textarea + `.mk-linked`/`.mk-chips` + `.mk-saved` select + `<details class="mk-more">` + `.mk-actions`. |
| `renderNote()` | `Views.Shared.noteBody : Catalog -> string -> ReactElement list` | Parses `[[work(:ref)(|label)]]` into `a.xref` elements and `\n` → `br`. |
| `wikiHome()` | `Views.WikiPages.home` | `.wiki-cats`, `.wcat`, `.ic` icons. |
| `authorsIndex()` | `Views.WikiPages.authorsIndex` | `.subcats` chips (era + genre), `#wikiQ`, `.author-list`/`.author-row`. |
| `erasPage()` | `Views.WikiPages.eras` / `Views.WikiPages.era` | includes prev/next `.pager`. |
| `articleIndex()` | `Views.WikiPages.articleIndex` | `.art-list`/`.art-item`, 220-char excerpt + `…`. |
| `editionsPage()` | `Views.WikiPages.editions` | series grouping via `WikiData.series` regexes; `<details>` with 400-row cap. |
| `authorPage()` | `Views.WikiPages.authorPage` | `.ap-head`, `.ap-meta` chips, `.ap-nav`, `.ap-cols`, `.timeline`, `.ap-works`, `.ap-eds`, `#anote`. Wikipedia summary comes from `WikipediaSummary` msg, not from the view. |
| `aboutPage()` | `Views.About.render` | static prose, `.cred` blocks — copy text verbatim. |
| `browsePage()` | `Views.Browse.render` | `Content.browse` sections + `#browseQ` search. |
| `favButton()` / `bindFav` | `Views.Shared.favButton : Library -> workId -> dispatch -> ReactElement` | |
| `linkChip()` | `Views.Shared.linkChip` | |
| `crumbs()` (wiki) | `Views.Shared.wikiCrumbs` | |
| `toast()` (JS fn) | `ShowToast` msg + `Cmd` that dispatches `HideToast id` after 2000 ms | |
| `applySettings()` | `State.applySettingsEffect : Settings -> Cmd<Msg>` | Sets `--fs`, `--lh`, `--body` on `documentElement.style`, `data-theme` attribute. The `mode-*` class is a view concern. |

### 4.3 Feliz conventions

- `prop.className`, `prop.classes [ ... ]` for conditional classes.
- `prop.custom("data-ref", ref)`, `prop.custom("data-col", key)`, `prop.custom("aria-pressed", v)`.
- `prop.key` on **every** element produced inside a `List.map`/`Array.map`.
- Text with mixed markup (e.g. `<i>` inside prose) → nest `Html.i`, never raw HTML.
- No `esc()` anywhere: React escapes by construction. Delete every `esc(...)` call.
- Inputs are controlled: `prop.value model.NavQuery` + `prop.onChange (SetNavQuery >> dispatch)`.
- The debounced searches in JS (`setTimeout 120/200`) are not needed; filter synchronously
  in the view. If profiling shows lag on the 1,800-work list, add a `Cmd`-based debounce
  msg pair — do not add `useEffect`.

---

## 5. Interop specification (Interop.fs)

Implement exactly these; everything else must be pure F#.

```fsharp
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
/// /[\u0370-\u03FF\u1F00-\u1FFF][\u0370-\u03FF\u1F00-\u1FFF\u0300-\u036F'ʼ’]*/g
[<Emit("$0.match(/[\\u0370-\\u03FF\\u1F00-\\u1FFF][\\u0370-\\u03FF\\u1F00-\\u1FFF\\u0300-\\u036F'\\u02BC\\u2019]*/gu) || []")>]
let greekWords (s: string) : string array = jsNative
/// exec-style tokenizer used by Views.Reader.tokens (returns index+length pairs)
[<Emit("(() => { const re=/[\\u0370-\\u03FF\\u1F00-\\u1FFF][\\u0370-\\u03FF\\u1F00-\\u1FFF\\u0300-\\u036F'\\u02BC\\u2019]*|⟦[^⟧]+⟧/gu; const out=[]; let m; while((m=re.exec($0))) out.push([m.index, m[0]]); return out; })()")>]
let scanTokens (s: string) : (int * string) array =

---

## 11. Study lenses (added 2026-09-23)

A rail beside the reader (`Views/Lens.fs`) holding five lenses, opened from the
`.lens-bar` row above the columns or a passage's ◇ (`.ln-btn`). All state lives on
`ReaderModel` (`Lens`, `MeterOn`, `Playhead`, `PlayToken`, `Places`, `Manifest`);
`State.update` wraps `updateCore` and runs `lensFollowUp` after any message that can
change what the open lens needs (places for the chunk, map redraw, manifest load).

| Lens | Data | Network |
|---|---|---|
| Echoes | trigram index over the aligned text + every other Greek text in `TextCache`; user `[[links]]`/backlinks | none |
| Words | stem concordance, per chunk and per era (`Meta.Eras`) across `TextCache` | none |
| Meter | `Lenses.Prosody`: hexameter, elegiac pentameter, iambic trimeter; synizesis, correption, digamma lengthening as last resort; Web Audio playback | none |
| Map | capitalised names in the translation → Wikidata SPARQL (items with Pleiades id P1584) | query.wikidata.org, DARE tiles, Leaflet from cdnjs |
| Manuscript | uncial / minuscule re-settings of the passage; IIIF manifests (`Paleography.witnesses`, verified against the holding library's record — keep it that way) | the library's IIIF server, OpenSeadragon from cdnjs |

Rules: Leaflet and OpenSeadragon are loaded only when their lens opens
(`Widgets.loadScript`), never bundled. Passages render through `Views.Reader.segment`
(a `React.memo` with `sameSeg`) — anything new a passage displays must be added to
`SegProps` or it will not re-render. New `localStorage` keys: `anag:meter`, `anag:iiif`.


---

## 12. Genre chips, work articles, centred reader (added 2026-09-23)

- **Genre filter.** `Model.Genre : string option` + `Msg.SetGenre`. One value shared by
  the Library page (`Views.Browse`) and My library (`Views.LibraryPage`), rendered by
  `Views.Shared.genreChips`. Session-only by design: no `localStorage` key.
- **Genres.** `WikiData.genreOf` decides from the Wikidata description first, then
  occupations, then the name, taking the *earliest* keyword match (list order is not a
  priority). `genreOfAuthor` caches per author id. Genre ids: poets, philosophers,
  historians, orators, scientists, fiction, church, scholars, plus "other".
- **Work articles.** `WikiData.workArticles : Map<workId, WorkArticle>` (Iliad only so
  far). Rendered at the top of the author page's article column with id `ap-<workId>`,
  so `#author/<authorId>/<workId>` scrolls to it; the reader's work header links there.
- **Wiki prose** (`core.summary` / `manuscripts` / `variants` in the meta blob) may hold
  several paragraphs separated by a blank line (`\n\n`).
- **Reader layout** (CSS, end of `public/style.css`): the column pair is centred at
  ≥1001px; prose passages hang their number in the Greek column's left gutter (verse
  keeps the row because line numbers use that gutter); `.col.grc` is 1.04× the text size
  with +.08 leading; long parts page with one `.pages` control (prev · select · next).


---

## 13. Stoichedon design system (added 2026-09-23)

The visual design is "Stoichedon".
Token and class names did not change; values did, plus a final layer at the end of
`public/style.css` headed "Stoichedon".

- **Palette.** Light = limestone ground, lamp-black ink; dark = black gloss with
  clay-white text and clay headings (`--display`). `--accent` (Egyptian blue) is the
  edition's structure and links; `--accent-2` (miltos red) is *only* what the reader
  added. `--ochre` is ornament only (initials, `.sh` marker). Control borders use
  `--rule-strong` (≥3:1); text on accent fills uses `--on-accent`, never `#fff`.
  Selected segmented buttons and chips are ink-filled, not accent-filled.
- **Type.** Gentium Book Plus (Greek + display), Source Serif 4 (translation), Inter
  (UI), Noto Sans (the "Sans" option; "Noto Sans Greek" is not a Google Fonts family).
  Never put Georgia in a stack that can render Greek. `Tei.Tokenizer` NFC-normalises
  every file, because Google Fonts subsets omit the combining breathings/accents.
- **Shape.** Radii `--r1/--r2/--r3` = 2/4/8px; one elevation (`--shadow`, a hairline
  ring in dark) for floating things only. No meanders, amphorae, hover lifts or
  accent rails — ornament appears in three places only:
  1. the stoichedon motto in the home hero (`Home.StoichedonMotto`, hover/focus maps
     capitals ⟷ accented words);
  2. the ochre initial (`Shared.initialOf`): Passage of the day (float drop cap) and
     the first passage of each part in the reader (`SegProps.Initial`, raised cap);
  3. the eras band (`Shared.erasBand`): width = years, height = works; rows on phones
     and in the wiki-home aside.
- **Layout.** Home: continue → hero (+ Passage of the day) → find → paths → eras →
  how it works → corpus → wiki → offline → alphabet → tips → Start here → sources. Wiki home is a ruled
  contents list (`.wcat` rows with a Greek label). Author page: `.ap-main` article
  left, `.ap-rail` (works, timeline, notes) right; rail first in the DOM so phones
  see works first. My library: `.lib-main` bookmarks, `.lib-rail` favourites, author
  notes, back-up. Phone tab bar (≤900px): Library · Wiki · Search · Forum ·
  My library (`.tab-phone` items; Search in the middle); the header's `.libbtn`
  hides there. (Superseded in part by §16–17.)
- **Design language** (the "Design language" layer at the end of `style.css`):
  primary actions and *every* selected state use `--solid` / `--on-solid` (ink by
  day, clay by night), never the link blue; blue is only links, references and
  focus; red only the reader's own things; ochre only markers (card-heading icons,
  `.howto .num`, timeline diamonds, the initial). Everything is a rectangle at
  `--r2`: buttons (`.btn` outline, `.btn.big`/`.primary` solid), chips, segmented
  controls, fields, cards. (The old round `.notes-fab` is hidden; see §15.) Cards are
  flat tablets: `--paper`, one `--rule` hairline, no shadow; hover darkens the edge
  and turns the title to link colour.
- **Icons.** One Greek set in `Content.icons`, rendered only via `Shared.icon` /
  `Shared.iconShapes` (`svg.icon`, 1.6 stroke; `IFill` shapes are solid): texts =
  papyrus roll, contents = interpunct list, library = scroll pigeonholes, wiki =
  temple front, settings = Αα, theme = oil lamp, favourite = olive sprig (leaves fill
  when on), plus herm, hourglass, codex, stemma, owl, wax tablet. Back, search and
  the text-source indicators stay plain glyphs on purpose. Never inline raw SVG
  paths in a view; add the shape to `Content.icons`.
- **Keyboard.** Each Greek column (`.col.grc`) is one Tab stop; ← → / Home / End move
  between `.w` words (`tabIndex -1`, text in `data-w`), Enter/Space looks one up.
  Handled keys stop propagation so the page-level ← → does not also fire.


---

## 14. "Start here" beginner's guide (added 2026-09-23)

- **Source of truth:** Markdown in `content/start-here/00–08-*.md`, edited by the
  user as scholar-editor. Each file ends with `## For review (not for publication)`.
  Vite's `guide-markdown` loader (`vite.config.js`) serves `*.md?guide` imports as
  strings and **cuts everything from that heading down at build time**. Never render
  or ship the review notes.
- **Files:** `Markdown.fs` (pure parser for the subset used: headings, paragraphs,
  rules, quotes, tables, nested lists, `<details><summary>`, `**`/`*`/links/`\*`)
  and `GuideData.fs` (one `importDefault` per page; slug = file name minus number)
  sit after `Content.fs`. `Views/Guide.fs` sits before `Views/Home.fs` (Home renders
  `Guide.path`).
- **Route:** `GuideRoute of string option`, hashes `#start` and `#start/<slug>`
  (old `#wiki/start…` links still parse; `GuideData.tryFind` maps merged slugs).
  The guide hangs off the home page (crumbs Home › Start here), not the wiki.
  Entry point: the "Start here" block at the foot of the home page
  (`Guide.path`: eight steps in three parts), plus the links under the home
  alphabet and tips. Each step's description in the path is its page's first
  paragraph, so keep that to one or two sentences.
- **Steps:** 1 alphabet and sounds, 2 vowels and diphthongs, 3 breathings, accents,
  punctuation (and sound change), 4 dictionary forms, 5 looking up a word,
  6 reading a word study (stages of Greek), 7–8 word studies (8 ends with a
  reading path). "step N" in running text auto-links.
- **Link syntax in the Markdown:** `[label](read:<workId>:<ref>)` opens the reader
  (a verse line number resolves to the passage holding it); `[…](03-….md)` links
  a guide page; `<u>…</u>` underlines the sound-making letters of an example word
  (`*c<u>u</u>p*`). A `>` quote whose first line is not Greek, `"` or `(` is a
  note box (`Markdown.Note`, `.g-note`) and may hold lists. Greek runs render as
  `span.grc[lang=grc]` in the Greek face. Quote lines are classed by their first
  character: Greek → `.q-grc`, `"` → `.q-tr`, `(` → `.q-ref`.
- Feliz's `prop.start` compiles to a throw; use `prop.custom("start", n)`.

**Passage references (fixed 2026-09-23).**
- `Tei.Tokenizer.nonCiteSubtypes` must list every *structural* div subtype
  (strophe, ephymnion, close…); subtypes are compared trimmed, lower-case, with
  no trailing "." A numbered one that slips through becomes a citation level.
  The Agamemnon's `ephymn.` refrain was keyed "1" and merged lines 1455–1488
  into the prologue.
- `Tei.Aligner.refineByLines` *replaces* the last key component with the line
  anchor only when that component is a line position (cards: numbering carries
  on across passages). When it is a unit of its own (Pindar's odes, Bacchylides,
  epigrams: numbering restarts), it *appends*, giving `ode.line`. This is decided
  once per text by majority (`lastKeyIsLinePosition`). Chunking uses the refined
  key depth, so each ode is its own chunk.
- `gotoRefResolve` prefers the passage that contains the line over one whose
  first-to-last range merely spans it.
- `Tei.Aligner.rehomeOrphans`: when a translation card starts before the Greek
  card, refinement leaves English rows with no Greek. Each one's English joins
  the passage whose Greek holds its line (in line order, with its ⟦n⟧ marker
  restored), and that passage is refined again. This fixed Libation Bearers'
  duplicate "195" and English-only rows in the Theogony, Works and Days, Shield,
  Iliad and Trachiniae. English rows for lines the Greek doesn't have stay:
  Agamemnon 1675 (translation runs past the Greek), Bacchylides 1.15 (a lacuna).
- **Typos in source files** we fetch but don't control are corrected in
  `Tei.Tokenizer.sourceFixes`, applied by `fixKnownTypos urn` right after
  `tokenize` (State.fs). Each entry names the text's URN, the wrong and right
  number, and optionally the div path, when the wrong number is valid
  elsewhere in the file. Current entries: Suppliants eng2 `4097`→`407`;
  Odyssey eng3 book 16 card 266 `580`→`280`. Check each against the
  surrounding text before adding one.
- Before changing any of these, diff passage refs before and after across the
  verse works. The 35-work check on this date left the Iliad, Odyssey, Sophocles,
  Euripides, Aristophanes, Hesiod and Hymns byte-identical.

---

## 15. Fixes of 24 September 2026

- **Home sections on phones.** One list of fold-by-default keys,
  `Views.Shared.collapsedByDefault` (State.fs calls it; the two copies had
  drifted, so the first tap on "How it works" did nothing). Folded on first
  open: picks, wiki, paths, eras, alphabet, tips, corpus. Passage of the day
  stays open. How it works uses `Shared.fixedSection` and never folds.
  `SetFilter` opens "picks" so the filter always shows its effect.
- **Sticky header on phones** needs `overflow-x:clip` (not `hidden`) on
  html/body, or body becomes its own scroll box and the header scrolls away.
- **Prose gutter** (`.ref` hanging left of the Greek at ≥1001px) has
  `z-index:2`; without it `.col.grc` covered the bookmark and study buttons.
- **Marker-only passages** (Plato's bare Stephanus page "17" before 17a) get
  `.seg-empty` (zero height, still in the DOM for links); the ochre initial
  goes on the first passage with words.
- No "more →" / "open →" links in home card headers (standing decision).
- **Notes button** is `Header.notesButton` (`.notes-ib`) at every width: labelled
  "Notes" above 1100px, icon only below. The floating `.notes-fab` covered the
  ends of lines and is hidden with CSS (its code is still in NotesPanel.fs).


---

## 16. Library, My library tabs, accounts and forum (added 2026-09-26)

- **Names.** Top bar: *Library* (the catalogue, `Browse` route, `#library`;
  `#browse` still parses), *Wiki*, *Forum*. (The sidebar was removed in §17.)
- **Library page** (`Views/Browse.fs`): sort Author A–Z / Title A–Z / By era
  (`Model.Shelf`, `ShelfMsg`), letter bar (letters filed with accents
  stripped), era select, translation filter (`Model.Filter`) and genre chips
  (`Model.Genre`), both shared with the sidebar. "Where to start" (`Content.browse`)
  folds away above the list and hides while filtering.
- **My library** is tabbed: `LibraryRoute of LibTab`, `#lib`, `#lib/words`,
  `#lib/places`, `#lib/favourites`, `#lib/notes`. Bookmarks: search, order
  (recent / by work in reading order / oldest), `#tags` written in notes
  (`LibraryData.tagsOf`). Words: saved from the word popover with the passage
  and its Greek context; Leitner review (`boxDays` 1, 3, 7, 16, 35 days; a
  miss comes back in 10 minutes and, if then known, restarts at box 1);
  keys Space/Enter, 1, 2 go through `Subscriptions.keydownSub`. Places: saved
  from the Map lens (`.ln-save`), drawn with `Widgets.renderPlaces` (no line).
- **One library codec**: `Storage.encodeLibrary`/`decodeLibrary`, used for
  localStorage, export/import and sync. New fields are optional when read.
- **Sync** merges item by item (`LibraryData.merge`): every change stamps the
  item's key in `Library.Stamps` (deletions too); the later stamp wins. Every
  library change must go through `LibraryData`'s functions (which stamp) and
  `State.saveLib`/`Features.saveLibrary` (which schedules `Account_ SyncSoon`).
  Sync = pull, merge with the *current* model, push if different; also on
  sign-in, boot and returning to the tab.
- **Server** (`Server.fs`): Supabase over plain HTTP (Auth + PostgREST), no
  client library. Configured by `VITE_SUPABASE_URL`/`VITE_SUPABASE_ANON_KEY` at
  build time; unset, `Server.configured` is false and Account/Forum explain
  themselves. Sign-in is an emailed code (or the link in the same email, which
  returns `#access_token=…`, parsed to `AccountRoute`). The session lives in
  `Server` and renews itself; `onSessionChange` reports back. All rules
  (owner-only libraries, author names from profiles, moderator-only status,
  rate limit) are in `supabase/schema.sql`: never rely on the client for them.
- **Forum** (`Views/Forum.fs`, `ForumRoute`): boards `Content.forumBoards`
  (ids must match the schema's check constraint); bug reports have their own
  board, form (what / steps / expected + page and browser) and statuses, and
  fall back to a prefilled GitHub issue when the server is off. "Discuss" in
  the bookmark editor and the study rail starts a Passages thread.
- **Update logic** for all of this is in `Features.fs` (before the views);
  State.fs only routes messages there.
- **Testing without a real project:** `node scripts/mock-supabase.mjs` serves
  the endpoints the app uses (code 123456; mod@example.com moderates); build
  with the two `VITE_` variables pointing at `http://localhost:54321`.


---

## 17. Header search; the sidebar retired (added 2026-09-27)

- **No sidebar.** `Views/Nav.fs`, the Contents toggle, the phone drawer and its
  touch handling, and their Model fields/Msgs are gone. Finding a text is the
  header search; browsing is the Library page; moving between parts of a work
  is the reader's own part picker (`Reader.readerNav`: part stepper and, for
  long parts, the page stepper — label · ‹ · menu · ›).
- **Header** is a three-column grid: `.h-left` (back, brand, sections) ·
  `.hsearch` · `.h-right` (text source, notes, account, My library, Αα, theme).
  Side columns are `minmax(max-content,1fr)`, so the search sits in the true
  centre when there is room and never covers the links. Every `.h-right`
  control is the same bordered `--r2` box; labels drop to icons below 1180px.
  The old header crumbs and "Passage… Go" box are gone: the search does both.
- **Search** (`Search.fs`, pure; `Views/SearchBox.fs`, the ARIA combobox).
  Accent-, breathing-, case- and final-sigma-insensitive; every query word must
  match. Groups: Go to (passage refs: "Iliad 1.33", "Il. 1.33", "Apology 17a",
  "tlg0012.tlg001 1.1", a bare "1.33" while reading) · Texts · Authors · My
  library (bookmarks, words, places; "#tag") · Guide & wiki · Look up (a Greek
  word → Logeion/Perseus/Wiktionary). Scope chips narrow it. An author's name
  puts Authors first; texts rank by match, the author's own works, well-known
  works (`Content.browse`/`paths`/`passages`), then having a translation.
  Empty box: continue reading, recent searches (`anag:searches`), examples.
  Keys: `/` opens, ↑ ↓ move, Enter opens, Esc closes. A hit carries the Msgs
  that open it; `State.updateSearch` runs the highlighted one on Enter.
- **Phones:** the box is hidden until the middle tab opens it as a full-screen
  sheet (`.hsearch.open`), with Cancel. The home page's "Find something to
  read" field is a button that opens the same search.
- **One page width:** `.page` and `.landing` share `max-width:82rem`, so the
  left edge doesn't move between pages; narrow content (guide, account) stays
  left-aligned inside it.
- The header text-source chip uses the Settings names: Online, This computer,
  Web address.


---

## 18. Verse fit, audit fixes and wiki copy-edit (added 2026-09-27)

- **Verse fits its column.** `Views.Reader.verseEm` estimates, once per text,
  how wide the long lines are in ems (characters × 0.43; the 99.8th
  percentile, or 1.12 × the 95th where long lines are a different metre, so
  tragedy's lyric and comedy's tetrameters wrap rather than shrinking the
  whole play). The reader sets it as `--verse-em`; `.col.grc` is a size
  container and `.line` takes `min(1em, 100cqi / (verse-em + hang + extra))`,
  scaled by `--text-scale` (the Text size setting as a ratio, set in
  `applySettingsEffect`). Floors: `.84rem` on phones, `.84em` above 760px.
  Phones also get a narrower number gutter and a shorter hanging indent.
  Scansion adds `--extra` for the ▸ play button at the line's *end*; never
  change `--hang` for it (the line numbers sit in the hang).
- **Anything positioned inside a verse line** needs `text-indent:0`: it
  inherits the line's negative hanging indent (scansion marks did).
- **Titles.** `Json.englishTitles` gives English titles where the catalogue
  has only Latin ones readers don't use (Meditations, Elements…).
  `Shared.titleText` sets titles the catalogue has only in Greek in the Greek
  face (`.t-grc`, `lang="grc"`).
- **Wiki.** Home: title and lead → contents → the essay "On learning". Author
  page on phones: works → article → timeline → notes (CSS order; the rail
  stays first in the DOM). Section blurbs live once in `WikiData.blurb*` and
  feed both the wiki contents and the home Wiki card. Dates:
  `WikiData.yearRange`/`eraSpan` ("480–323 BCE", "c. 800–480 BCE");
  Wikidata descriptions go through `WikiData.cleanDesc`. Index cards use
  `WikiData.excerpt` (whole sentences, never mid-word).
- **Copy style for all wiki prose** (meta blob and WikiData): double quotes for
  glosses, terms and nicknames ("the Graces"); British -ise spellings;
  "encyclopaedia"; play titles as the catalogue names them (Oedipus
  Tyrannus). A work article sits directly above its author's article, so it
  must not repeat what the author article says (dialect, dating, Venetus A).
- A passage's number copies its CTS citation (phones have no hover for the URN).
---

## 16b. Learn — beginner's lessons (added 2026-09-25; now the Study section, see §19)

Ported from the "Arche" design (Claude Design handoff, `Arche Prototype v2`),
restyled in the Stoichedon design language: no papyrus texture, torn edges or
hand-drawn corners. The design's page turn and ink-in reveal are kept.

- **Routes:** `Route.LearnRoute of LearnPage`; hashes `#learn` (contents),
  `/welcome`, `/preface`, `/letters`, `/alphabet` (Book I, Lesson 1, 5 leaves),
  `/declension` (Book II, Lesson 3, 6 leaves), `/declension/done`, `/sounds`,
  `/iliad`, `/myth` (3 leaves). `Router.learnHash` is the inverse. Leaves inside
  a lesson are model state, not routes, so Back leaves the lesson. A first visit
  to `#learn` is redirected (replaceState) to `#learn/welcome` until onboarded.
  Header tab "Learn" (`navKey` "learn", icon `learn`); phone tab bar order is
  Texts · Contents · Learn · Wiki · My library.
- **Files:** `LearnData.fs` (all lesson content: edit wording there),
  `LearnFx.fs` (DOM/audio effects), `LearnState.fs` (`init`, `enterPage`,
  `update`; State routes `Learn_` messages and calls `enterPage` from
  `loadForRoute`), `Views/Learn.fs`. Types: `LearnPage`, `LearnProgress`,
  `LearnModel`, `LearnMsg`; `Model.Learn`.
- **Persistence:** `anag:learn` = `{onboarded, pace, step, alpha}`. Exercise
  state (cards, pairs, tiles, paradigm cells…) is session-only by design.
- **Page turn:** the view calls `LearnFx.snapshot()` in the click handler, only
  for messages that turn a leaf (`turning` / `pageLink` in the view); the update
  returns `LearnFx.turn`. A snapshot that nobody turns removes itself after
  1.5 s, so never snapshot for a message that doesn't turn. `[data-ink]` marks
  what blurs in when a leaf opens; `[data-reveal=name]` blocks do the same when
  an answer appears. Both are skipped under prefers-reduced-motion.
- **LearnFx** keeps its JavaScript in one object bound once with `emitJsExpr`
  (an `[<Emit>]` would be pasted into every call site: it made the file 183 kB).
  Pitch tones share `window.__anagAudio` with the meter lens, so one sound
  plays at a time. Speech uses the device's `el-GR` voice (modern Greek).
- **CSS:** "Learn" layer at the end of `style.css`, classes `lx-*`. One new token,
  `--ok` (olive), for right answers; wrong is `--danger`. Answer buttons share
  `.lx-opt` + `sel/ok/bad/dim/done`.
- **Views:** in a Feliz list, don't mix `for … ->` with other items. An explicit
  yield turns off implicit yields and the other items are silently dropped
  (FS0020). Use `for … do`.
- **Bundle:** `vite.config.js` puts the Learn modules in their own `learn` chunk.
- **Left out of the design on purpose:** the myth's placeholder plate (no image
  yet), the placeholder "Day twelve · 214 words" stats, and the design's own dark
  toggle (the header's theme button covers it).


---

## 19. Study section, Enter to save, forum suggestions, eras to scale (added 2026-09-27)

- **Study** (`#study`, top-bar tab "Study", `navKey` "learn") is the Learn
  section of §16b renamed: `Router.learnHash` writes `#study/…`, and `#learn/…`
  still parses. Its front page (`LearnRoute LearnContents`) is `Views/Study.fs`:
  the μάθησις hero (text in `WikiData.wikiIntro.Lead`, rewritten about
  studying) with the root family, the "Start here" guide path, **Practise**
  (the lessons' contents leaf, `Learn.contents`, with a "just for fun, not an
  assessment" note), and the essay "On learning" (philosophers, quotations).
  A first visit is no longer redirected to the welcome leaf. The guide
  (`#start…`) belongs to Study: crumbs Study › Start here, Study tab active.
  The home page's foot now points to Study instead of listing the steps.
- **Header:** Library · Study · Wiki · Forum. Phones: Library · Study · Search
  · Wiki · My library; Forum becomes an icon in the phone header
  (`.forum-ib`; the tab is `.tab-desk`).
- **Wiki front page:** its own lead (`WhyWiki`), contents, and a right rail
  ("Start with": featured Iliad article, Study). No μάθησις hero, no eras chart.
- **Enter finishes an edit** in every multi-line box (`Shared.onEnterSave`):
  bookmark notes (closes the editor), the small note editor, author notes
  (saves), place notes, word fields, forum reply and post body (posts), the
  import box (loads). Shift+Enter is a new line; IME composition is ignored.
  In the bug-report form Enter moves to the next box (`Shared.onEnterNext`)
  and posts from the last. Put `Shared.enterHint` under a box that says so.
- **Forum:** Bug reports and **Suggestions** (board id `suggestions`, Greek
  Γνῶμαι) are two panels side by side under the welcome, above Boards and
  Latest. The schema's category check includes `suggestions`; an existing
  Supabase project must re-run `supabase/schema.sql` (it widens the check).
- **Greek through the centuries** (`Shared.erasBand`) is drawn to scale:
  column width = the era's share of the years (inline `flex-basis`), bar
  height = its share of the works (inline %), so CSS must not add gaps or
  padding to columns. Works by undated authors (or eras not drawn) are a
  dashed column set apart at the end. Every count is computed. Phones turn it
  on its side (row height = years, bar length = works). It lives on the Eras
  page (`#wiki/eras`) and the home page, no longer on the wiki front.
- **Page grid:** one main column plus, where a page has one, a right rail of
  `--rail` (16–22rem) with `--gutter` between, sections spaced by `--sect`.
  Used by Study, the wiki front, My library (back-up in the rail), Account
  (why sign in, in the rail) and the author page.
- **Feliz lists, again:** `prop.classes [ if a then "x"; if b then "y" ]` on one
  line parses as a nested sequence (the second `if` only runs when the first
  is true): put each `if` on its own line. The header's active tab never
  showed on desktop because of this.
