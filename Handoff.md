# Handoff: Μάθησις (Ancient Greek Reader), complete feature specification

Purpose: a technical inventory of every feature in the existing app, for an agent building a new website/app. It describes behaviour, data, algorithms, storage and constraints. Where a decision is a deliberate product rule it is marked **Rule**.

## 0. System overview

- **Type.** Client-only SPA, static hosting (GitHub Pages at `/Mathesis/`, also Vercel at `/`). No app server of its own. Optional backend is Supabase (Auth + PostgREST) called over plain `fetch`, no client library.
- **Stack.** F# compiled by Fable 5 to JS; Elmish 4 (MVU) + Feliz 2 over React 18; Thoth.Json for the catalogue/meta blobs; Vite 5 build with three custom plugins (`guideMarkdown`, `sitePages`, `stampStylesheetVersion`). ~18,000 lines of F#. CSS is one hand-written file, `public/style.css` (class names are the contract; design system "Stoichedon").
- **Architecture.** Single `Model`, `Msg` union, `update : Msg -> Model -> Model * Cmd<Msg>`. Update is pure; DOM/clipboard/history/network effects are `Cmd`s. Feature update logic for accounts/forum lives in `Features.fs`, Study lessons in `LearnState.fs`, everything else in `State.fs`. Compile order in `src/App.fsproj` is significant.
- **Data sources at runtime.** (1) Catalogue: ~1,800 works from Perseus `canonical-greekLit` and First1KGreek, shipped as a gzip+base64 blob (`dist/data/catalog.txt`; inline on the front page so `file:` works). (2) Meta blob: eras, per-author Wikidata metadata, hand-written core articles (`meta.json`). (3) TEI XML fetched on demand from GitHub raw, a local folder, a ZIP, or a custom base URL. (4) Markdown content compiled into JS chunks: `content/start-here/*.md` (9 files), `content/life/*.md` (22 articles).
- **Build outputs.** `dist/` plus ~2,240 generated per-page `index.html` files (see 12), `sitemap.xml`, `robots.txt`, `404.html`.

## 1. Catalogue and data model

- Types: `Catalog { Authors; WorkById; AuthorOfWork }`, `Author { Id "tlg0012"; Name; Grc; Works }`, `Work { Id "tlg0012.tlg001"; Title; AuthorId; Texts: TextMeta list }`, `TextMeta { Urn; Label; Desc; Lang; Kind (Edition|Translation|Commentary|Other); File; Repo; Kb; Refs }`.
- `Meta { Eras; Authors: Map<id, AuthorMeta> }`; `AuthorMeta` has birth/death/floruit/sort year, era id, Wikidata description and Q-id, place, occupations, work count, optional `CoreArticle` (summary, timeline, manuscripts, variants, editions).
- English titles for works the catalogue names only in Latin: `Json.englishTitles`; the catalogue's own titles kept in `Json.formerTitles` so search still finds them. Titles held only in Greek render in the Greek face (`Shared.titleText`).
- Genre is derived, not stored: `WikiData.genreOf` reads the Wikidata description, then occupations, then the name, taking the earliest keyword match; ids poets, philosophers, historians, orators, scientists, fiction, church, scholars, other; cached per author.

## 2. Text loading (Sources.fs, Zip.fs, Storage.fs)

- **Source modes:** GitHub (online), Local (this computer), Web address (custom base URL). A base of `""`, `"."` or `"./"` is shown as "read from this site".
- **Local folder:** File System Access API (`showDirectoryPicker`), fallback `<input webkitdirectory>` (session only). Directory handles remembered in IndexedDB (db `anag`, store `kv`, key `dirs`); a "reconnect" step re-grants permission.
- **ZIP:** custom central-directory parser + `DecompressionStream('deflate-raw')`; the ZIP blob is remembered in IndexedDB (`zips`). Repos are keyed `Perseus | First1K`.
- **Errors:** `LoadError = NetworkBlocked | HttpStatus | XmlParseFailed | ErrorMessage`; reader shows status pane with Retry and "Open text source".
- **Token guard:** `ReaderModel.Token`/`NextToken` discards stale async results. Parsed segments are cached in `Model.TextCache` keyed by URN.

## 3. TEI pipeline (Tei/Tokenizer.fs, Segmenter.fs, Aligner.fs)

1. **Tokenizer:** XML to event list (`EvDiv path`, `EvMilestone`, `EvSpeaker`, `EvHead`, `EvParaBreak`, `EvText`, `EvLine`). NFC-normalises every file (Google Fonts subsets lack combining marks). `nonCiteSubtypes` lists structural div subtypes (strophe, ephymnion, close…) that must not become citation levels. `sourceFixes`/`fixKnownTypos urn` corrects known numbering typos in third-party files (Suppliants eng2, Odyssey eng3 bk 16).
2. **Segmenter:** events to `RawSegment { Key; Blocks }` keyed by numbered div path plus section/card milestones. Blocks: `Heading | Prose(speaker, text) | Verse(speaker, lines)`.
3. **Aligner:** truncates, aligns Greek and translation segments, `refineByLines` (replaces the last key component with the line anchor when it is a line position; appends when numbering restarts per unit, decided by majority in `lastKeyIsLinePosition`), `rehomeOrphans` (English rows whose card starts before the Greek are re-attached by line), chunking and paging from refined key depth. Output `AlignedText { Depth; Chunks; Segments; Coverage }`.
- Guard: before changing any of it, diff passage refs before/after over the verse works (35-work check documented in CLAUDE.md §14).

## 4. Reader (Views/Reader.fs, Popover.fs)

- Two aligned columns (`.col.grc`, `.col.eng`); modes Both / Greek only / English only; centred at ≥1001px; prose passages hang the number in a left gutter, verse keeps the row.
- Per passage: CTS URN (click copies), line numbers every 5, speaker labels, inline `⟦page/line⟧` markers, bookmark button, study-lens button, empty marker-only passages collapsed (`.seg-empty`).
- **Verse fit:** `verseEm` estimates per-text long-line width in ems (chars × 0.43; 99.8th percentile, or 1.12 × the 95th where long lines are another metre); exposed as `--verse-em`; `.line` font shrinks via a container query (`cqi`), floors `.84rem` phones / `.84em` above 760px.
- **Greek words** are clickable spans (no `dangerouslySetInnerHTML`; tokenised with a Unicode regex over `Ͱ-Ͽἀ-῿` + combining marks). Popover offers Logeion, Perseus morph, Wiktionary, and "save word".
- **Keyboard:** each Greek column is one Tab stop; ←/→/Home/End move between words; Enter/Space looks up.
- **Navigation:** edition pickers (Greek and translation, "none" allowed), part stepper (`Reader.readerNav`), page stepper for long parts, prev/next unit, jump to reference, translation coverage note, work header linking to work article, favourite toggle.
- **Ref jump:** `gotoRefResolve` prefers the passage containing the line over one whose range merely spans it.
- Passages render through a `React.memo` (`sameSeg`); new per-passage data must be added to `SegProps`.

## 5. Study lenses (Lenses/*, Views/Lens.fs)

State on `ReaderModel` (`Lens`, `MeterOn`, `Playhead`, `PlayToken`, `Places`, `Manifest`); `lensFollowUp` runs after messages that change what the open lens needs.
- **Echoes:** trigram index over the aligned text plus every other Greek text in `TextCache`; plus user `[[links]]` and backlinks.
- **Words:** stem concordance per chunk and per era (`Meta.Eras`) over `TextCache`; accent folding and stopwords in `Lenses/Greek.fs`.
- **Meter:** `Prosody` syllabifies and fits dactylic hexameter, elegiac pentameter, iambic trimeter (synizesis, correption, digamma lengthening as last resort); Web Audio playback with a playhead; setting `anag:meter`.
- **Map:** capitalised names in the translation to Wikidata SPARQL (items with Pleiades id P1584) at query.wikidata.org; DARE tiles; Leaflet loaded from cdnjs only when the lens opens; places can be saved to My library.
- **Manuscript:** uncial and minuscule re-settings of the passage; IIIF manifests (`Paleography.witnesses`, each verified against the holding library's record); OpenSeadragon lazy-loaded; setting `anag:iiif`.
- **Rule:** Leaflet/OpenSeadragon are never bundled.

## 6. Search (Search.fs pure, Views/SearchBox.fs)

- ARIA combobox in the header. Normalisation: accent-, breathing-, case- and final-sigma-insensitive; every query word must match.
- Groups: Go to (passage refs like "Iliad 1.33", "Il. 1.33", "Apology 17a", "tlg0012.tlg001 1.1", bare "1.33" while reading), Texts, Authors, My library (bookmarks, words, places, `#tag`), Guide and wiki (titles, summaries, lower-ranked whole-article text via `Search.lifeBodies`), Look up (Greek word to external dictionaries), plus scope chips.
- Ranking: an author's name puts Authors first; texts rank by match, the author's own works, well-known works (`Content.browse/paths/passages`), then having a translation.
- Empty state: continue reading, recent searches (`anag:searches`), examples. Keys: `/` opens, ↑↓, Enter, Esc. A hit carries the `Msg`s that open it; `updateSearch` runs the highlighted one.
- Phones: hidden until the middle tab opens it as a full-screen sheet with Cancel. Home's "Find something to read" is a button opening the same search.
- "Your account" hit only when accounts are configured.

## 7. Pages and routes

Internal routes are hash strings (`Router.toHash`); `Router` converts to real paths at the edge (see 12). Route union: Landing, Browse (`#library`), LibraryRoute of tab (`#lib`, `/words`, `/places`, `/favourites`, `/notes`), AboutRoute, PrivacyRoute, AuthorRoute, WikiRoute (Home, Authors scope, Eras, Articles Manuscripts|Variants, Editions, Life), GuideRoute (`#start`, `#start/<slug>`), LearnRoute (`#study/...`), ForumRoute (Home, Board, Thread, Rules, New), AccountRoute (incl. `#access_token=` sign-in return), ReaderRoute (workId, grc suffix, eng suffix, chunk, seg), NotFoundRoute. Unknown work/author/era/life slug/guide slug/forum board becomes NotFoundRoute in `State.loadForRoute` after catalogue load (address left as typed). Old `#learn`, `#wiki/start`, `#browse` still parse.

- **Home** order: "New to Greek? Start in Study" band (`newcomerSection`) → continue reading (from `anag:recent`, max 6) → hero with stoichedon motto (hover/focus maps capitals ⟷ accented words) and Passage of the day (index `floor(now/864e5) % available`, ochre drop cap) → find → suggested picks → reading paths → eras band → how it works → corpus → wiki card → offline setup (connect ZIP/folder) → sources footer. On phones, sections fold; `Shared.collapsedByDefault` = picks, wiki, paths, eras, corpus; How-it-works never folds.
- **Library page** (`Views/Browse.fs`): sort Author A–Z / Title A–Z / By era; letter bar (accent-stripped); era select; translation filter (All / Translated / Greek-only); genre chips (session-only); "Where to start" list.
- **About:** credits, typefaces, CC BY-SA 4.0 licence section (`#licence`), privacy summary. **Privacy:** what the browser stores, what an account stores, deletion, third parties; adapts when accounts are off. **Not found** page.
- **Header:** Library · Study · Wiki · Forum, three-column grid (left links, centred search, right controls: text source chip, Notes, account, My library, Αα settings, theme). Labels collapse to icons below 1180px. Phones: bottom tab bar Library · Study · Search · Wiki · My library; Forum becomes a header icon.

## 8. Study section (Views/Study.fs, Guide.fs, Learn.fs, LearnState.fs/Data/Fx, Markdown.fs)

- **Front page order:** μάθησις hero (root family) → Start here (guide path, "Begin with the alphabet") → alphabet at a glance (`Content.alphabet`, 24 cells) → "Reading Greek: five things to know" (`Content.tips`) → Practise → "On learning" essay. Neither reference carries a link to the guide (its steps sit directly above).
- **Start here guide:** 9 Markdown files → 8 steps (alphabet and sounds; vowels and diphthongs; breathings, accents, punctuation; dictionary forms; looking up a word; reading a word study; two word-study pages). Custom parser `Markdown.fs` for a subset: headings, paragraphs, rules, quotes, tables, nested lists, `<details>`, bold/italic/links, `<u>` for sound-making letters. Link forms: `read:<workId>:<ref>` (opens the reader; a verse line number resolves to the holding passage), page links `NN-slug.md`. Quote lines classed by first character (Greek, `"`, `(`); a non-Greek `>` quote is a note box. "step N" in running text auto-links. The `## For review (not for publication)` heading and everything below is stripped at build time by the Vite plugin. **Rule:** never ship review notes.
- **Practise (Learn):** routes `#study/welcome, preface, letters, alphabet` (Book I lesson 1, 5 leaves), `declension` (Book II lesson 3, 6 leaves) `+done`, `sounds`, `iliad`, `myth`. Leaves are model state, not routes. Exercises: cards, pairs, tiles, paradigm cells, answer buttons `.lx-opt` (sel/ok/bad/dim/done); pitch tones via Web Audio (`window.__anagAudio` shared with the meter lens), speech via device `el-GR` voice. Persistence `anag:learn` = `{onboarded, pace, step, alpha}`; exercise state is session-only. **Rule:** no page-turn animation; a new leaf just appears and scrolls to top. Reveal/ink-in effects skipped under `prefers-reduced-motion`. `LearnFx` JS is one object bound with `emitJsExpr` (an `[<Emit>]` per call site bloated the bundle). Own `learn` chunk.

## 9. Wiki (Views/WikiPages.fs, Life.fs, WikiData.fs)

- Front page: lead, ruled contents list with Greek labels, right rail ("Start with": featured Iliad article, Study).
- Authors index: era chips and genre chips, `#wikiQ` search, author rows.
- Author page: header, meta chips, works, editions, timeline, notes, core article, work articles (`WikiData.workArticles`, Iliad only; anchor `#author/<id>/<workId>`), optional live Wikipedia summary (`WikipediaSummary` msg; view never fetches). Layout: article left, rail (works, timeline, notes) right; on phones works → article → timeline → notes via CSS order.
- Eras page: `Shared.erasBand`, drawn to scale (column width = share of years via inline `flex-basis`, bar height = share of works %), undated authors as a dashed end column, phones rotated; prev/next pager.
- Manuscripts & transmission and Textual variants indexes (excerpt via `WikiData.excerpt`, whole sentences only); Editions & translations grouped by publisher series (regexes in `WikiData.series`, `<details>` with 400-row cap).
- **Everyday life:** 22 Markdown articles (`content/life/NN-slug.md`, grouped with Greek labels in `LifeData.fs`); first paragraph is summary; quotations are Greek line + English + `read:` link (supports `read:<workId>@<edition>:<ref>` for split works); every quotation was verified against the TEI and every link opened in the reader (re-check after any Aligner change). Own `life` chunk.
- **Rule (copy style):** double quotes for glosses; British -ise spellings; "encyclopaedia"; play titles as the catalogue names them; date ranges via `WikiData.yearRange/eraSpan`; a work article must not repeat its author article.

## 10. My library and notes (LibraryData.fs, Views/LibraryPage.fs, NotesPanel.fs, Storage.fs)

- Model `Library { Favs; Marks; AuthorNotes; Words; Places; Stamps }`. Tabs: Bookmarks (search; order recent / by work in reading order / oldest; `#tags` parsed from notes by `LibraryData.tagsOf`), Words, Places, Favourites, Notes/back-up.
- **Mark** = `{ Id; Work; Ref; Label; Snippet; Note; Links: MarkLink list; Ts }`. Notes support `[[work:ref|label]]` cross-references rendered as `a.xref` with backlinks (`Shared.noteBody`). Bookmark editor: textarea, linked passages chips, saved-passage select, "discuss" (starts a forum thread).
- **Words:** saved from the popover with the passage and Greek context; Leitner review, `boxDays` 1, 3, 7, 16, 35; a miss returns in 10 minutes and, if then known, restarts at box 1; keys Space/Enter, 1, 2 via `Subscriptions.keydownSub`.
- **Places:** saved from the Map lens, drawn by `Widgets.renderPlaces`.
- **Enter saves** in every multi-line box (`Shared.onEnterSave`; Shift+Enter newline; IME composition ignored); bug-report form uses `onEnterNext`.
- **One codec:** `Storage.encodeLibrary/decodeLibrary` used for localStorage, JSON export/import (confirm step), clear-all, and sync; new fields optional on read.
- **Merge/sync:** every change stamps the item key in `Library.Stamps` (deletions too); `LibraryData.merge` is item-by-item, later stamp wins. All changes must go through `LibraryData` functions and `saveLib`/`saveLibrary` (which schedules `SyncSoon`).
- Notes panel (`Header.notesButton`, `.notes-ib`; floating FAB hidden but code retained).

## 11. Accounts and forum (Server.fs, Features.fs, Views/Account.fs, Forum.fs, supabase/schema.sql)

- **Config:** `VITE_SUPABASE_URL` / `VITE_SUPABASE_ANON_KEY` at build time; unset means `Server.configured = false` and all account UI disappears (no Sign in button, no search hit, forum/account pages explain, privacy text adapts). **Live site currently has accounts OFF**: `.github/workflows/pages.yml` passes the variables only while repo variable `ACCOUNTS` is `on`.
- **Auth:** emailed one-time code, or the link in the same email returning `#access_token=…` (parsed to AccountRoute). Session held in `Server`, auto-renewing, reports via `onSessionChange`. 429 from Supabase's own sender is converted to a plain message.
- **Sync:** pull → merge with current model → push if different; on sign-in, boot, tab refocus, and after changes.
- **Profiles:** display names. **Delete account:** type "delete" → RPC `delete_my_account` (security definer, deletes the auth user; everything else cascades); local library kept and unlinked.
- **Forum:** boards from `Content.forumBoards` (ids must match the schema check constraint): general boards, Bug reports (own form: what / steps / expected + page + browser; statuses; GitHub-issue fallback when the server is off), Suggestions (Γνῶμαι), Passages threads. Threads, replies, latest list, welcome, community rules (`#forum/rules`).
- **Safety:** Report (signed in, not own) with reasons spam/abuse/offtopic/other → `forum_reports` (duplicate = 409 treated as sent); moderators get a queue at the top of the forum front (`status = 'done'` to resolve). Hide person is **client-only** (`anag:blocked`): posts fold to "Show it", threads dropped from lists with a count; managed on the account page.
- **Rule:** all authorisation (owner-only libraries, author names from profiles, moderator-only status, rate limits) is enforced by RLS and triggers in `schema.sql`, never by the client. Existing projects must re-run the schema for new tables/functions.
- **Testing:** `node scripts/mock-supabase.mjs` on :54321 (code 123456; `mod@example.com` moderates; `ratelimit@example.com` returns 429).

## 12. Routing, addresses, SEO (Router.fs, scripts/site-pages.mjs)

- Online the browser shows real paths (`/Mathesis/wiki/life/food/`, `/Mathesis/author/tlg0012/`, reader `/<workId>/?grc=&eng=&part=&at=`). `Router.basePath` derived from the module URL, so one build serves `/Mathesis/` and `/`. Hash mode for `file:` and `VITE_ROUTING=hash` builds; old `#` links rewrite to paths (`arrivedByHash`). `prop.href` always goes through `Router.href`.
- Build step writes ~2,240 static `index.html` (home, library, study, guide steps, wiki pages, life articles, every author and work) each with title, description, canonical, Open Graph, Twitter tags; `sitemap.xml`, `robots.txt`; `404.html` redirects unknown addresses (forum threads, passages) to the front page as `?/<address>`. Site URL from `VITE_SITE_URL`. Share image `public/og-card.png` (1200×630, from `scripts/og-card.html`). Reader titles "Work — Author — Μάθησις".

## 13. Settings, persistence, theming

- Settings sheet: column mode, typeface (Serif/Sans), text size (0.85–1.80 rem step 0.06, default 1.18), line height (1.2–2.2 step 0.1, default 1.65), theme (auto/light/dark), text source. Applied by `applySettingsEffect` setting `--fs`, `--lh`, `--body`, `--text-scale`, `data-theme`.
- localStorage keys (`anag:` prefix): mode, face, fs, lh, theme, src, srcBase, filter, recent, colState, lib, learn, meter, iiif, searches, blocked, (navHidden legacy). IndexedDB `anag/kv`: `dirs`, `zips`. Library stored via the single codec.
- **Design system (Stoichedon):** light limestone/lamp-black, dark black gloss/clay; `--accent` Egyptian blue for structure and links, `--accent-2` miltos red only for the reader's own additions, `--ochre` for ornament only; primary and selected states use `--solid/--on-solid`; radii 2/4/8px; one shadow token; fonts Gentium Book Plus (Greek/display), Source Serif 4 (translation), Inter (UI), Noto Sans (Sans option); never Georgia in a stack that renders Greek. Icons only via `Content.icons` + `Shared.icon` (papyrus roll, temple, oil lamp, olive sprig, etc.). Ornament limited to motto, ochre initial, eras band. Sticky header on phones needs `overflow-x:clip`.

## 14. Build, test and deploy

- `dotnet fable src && vite build`; deploy = squash-merge to `main` triggers `.github/workflows/pages.yml`. `vercel.json` + `scripts/vercel-build.sh` for Vercel.
- Verified with Playwright against: `vite preview` (dist), a mock-Supabase build, and a GitHub-Pages imitation server (hosts-mapped to `mathesis.github.io:4180/Mathesis/`). Sweeps at 1300/960/390px, light and dark, ~30 pages.
- Repo docs: `CLAUDE.md` (master blueprint §1–§24, the authoritative detail), `supabase/README.md` (project setup, email sender, updating), `README.md`.

## 15. Gotchas worth carrying into a new build

- Feliz lists: `for … ->` disables implicit yields (use `for … do`); put each `prop.classes [ if … ]` condition on its own line; `prop.start` throws (use `prop.custom("start", n)`).
- Anything positioned inside a verse line needs `text-indent:0`.
- Keep the review notes cut from shipped Markdown; keep `Privacy` truthful when storage keys, tables or outside services change.
- `[<Emit>]` inlines its body at every call site; bind large JS once.
- Google Fonts subsets omit combining Greek marks: NFC-normalise text.
