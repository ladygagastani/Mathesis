module Types

// ---------------------------------------------------------------------------
// 3.1 Catalog & metadata
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// 3.2 TEI pipeline
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// 3.3 Settings, source, library, routes
// ---------------------------------------------------------------------------

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

/// Where a text was actually read from. Local mode falls back to GitHub for any
/// text the connected copy happens not to hold, so "the app is set to Local" and
/// "this text came from your disk" are different claims — the reader is told
/// which one is true rather than left to assume.
type TextOrigin =
    | OriginLocalZip of name: string
    | OriginLocalFolder of name: string
    | OriginLocalFiles
    | OriginGitHub
    | OriginUrl of baseUrl: string

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
    /// The "Start here" guide: None = its contents page, Some slug = one page
    | WikiGuide of slug: string option

/// The Learn section's pages (`#learn/…`). Leaves *within* a lesson are not
/// pages: they live on `LearnModel`, so the browser's back button leaves the
/// lesson instead of un-turning one leaf at a time.
type LearnPage =
    | LearnWelcome          // #learn/welcome   first visit only
    | LearnPreface          // #learn/preface   choose a pace
    | LearnContents         // #learn
    | LearnLetters          // #learn/letters   the alphabet at a glance, with sound
    | LearnAlphabet         // #learn/alphabet  Book I, Lesson 1 (5 leaves)
    | LearnLesson           // #learn/declension  Book II, Lesson 3 (6 leaves)
    | LearnDone             // #learn/declension/done
    | LearnSounds           // #learn/sounds    pitch accent
    | LearnIliad            // #learn/iliad     Iliad 1.1–5 with glosses
    | LearnMyth             // #learn/myth      Odysseus and the Cyclops (3 leaves)

type Route =
    | Landing
    | Browse
    | LibraryRoute
    | AboutRoute
    | AuthorRoute of id: string * section: string option
    | WikiRoute   of WikiRoute
    | ReaderRoute of workId: string * grcSuffix: string * engSuffix: string
                     * chunk: string option * seg: string option
    | LearnRoute  of LearnPage

// ---------------------------------------------------------------------------
// 3.4 Reader state & async wrappers
// ---------------------------------------------------------------------------

type LoadError =
    | NetworkBlocked
    | HttpStatus of int
    | XmlParseFailed of string
    | ErrorMessage of string

type ReaderPhase =
    | Loading of status: string
    | Ready
    | LoadFailed of LoadError

// ---------------------------------------------------------------------------
// Study lenses — the rail that opens beside the reader
// ---------------------------------------------------------------------------

type LensKind =
    | LensEchoes        // repeated phrases, in this work and in other texts opened
    | LensWords         // where a word (or stem) occurs, by part and by era
    | LensManuscript    // diplomatic views of the passage + a IIIF image viewer
    | LensMeter         // scansion of a verse passage, with rhythm playback
    | LensMap           // places named in the translation, on a map

/// A place the translation names, resolved through Wikidata to a Pleiades id.
type PlaceHit =
    { Name     : string             // the word as it appears in the translation
      Label    : string             // Wikidata's English label
      Lat      : float
      Lon      : float
      Pleiades : string
      Qid      : string
      Refs     : string list }      // passages naming it, in reading order

type PlacesState =
    | PlacesIdle
    | PlacesLoading of chunk: string
    | PlacesReady   of chunk: string * PlaceHit list
    | PlacesFailed  of chunk: string * string

/// One IIIF image the manuscript viewer can show: a label and an image service.
type IiifCanvas = { Label: string; Service: string }

type ManifestState =
    | ManifestNone
    | ManifestLoading of url: string
    | ManifestReady   of url: string * title: string * IiifCanvas list * index: int
    | ManifestFailed  of url: string * string

type MsView = Diplomatic | Minuscule | Normalized

type LensState =
    { Kind  : LensKind
      Seg   : string option         // the passage the lens is about
      Word  : string option         // the headword being traced (Words lens)
      Stem  : string                // the editable search stem (Words lens)
      MsView: MsView }

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
      OpenEditor : (string * string) option     // (workId, segRef) editor expanded
      GrcOrigin  : TextOrigin option            // where each text was actually read from
      EngOrigin  : TextOrigin option
      // Scroll position readout. ScrollRef is the passage under the top of the
      // viewport; ScrollSeq guards the fade timer the way Token guards loads, so
      // a timer from an earlier scroll can't hide a readout a later one raised.
      ScrollRef  : string option
      ScrollSeq  : int
      ScrollLive : bool
      // Study lenses. Places and the manuscript survive switching lenses, so
      // flicking between Map and Meter doesn't re-query Wikidata.
      Lens      : LensState option
      MeterOn   : bool                  // scansion marks over every verse line
      Playhead  : (string * int * int) option   // (segRef, line, syllable) sounding now
      PlayToken : int
      Places    : PlacesState
      Manifest  : ManifestState }

// ---------------------------------------------------------------------------
// Learn — the beginner's lessons (#learn), ported from the "Arche" design
// ---------------------------------------------------------------------------

/// What survives a reload (`anag:learn`). Everything else on `LearnModel` is
/// the state of an exercise in progress and starts fresh each visit.
type LearnProgress =
    { Onboarded : bool
      Pace      : int              // 0 a line, 1 a page, 2 a book (per day)
      Step      : int              // Book II lesson leaf, 0..5; 6 = finished
      AlphaDone : bool }           // Book I, Lesson 1 read to the end

type LearnModel =
    { Progress : LearnProgress
      // Book II, Lesson 3 — the first declension
      Queue    : int list          // flashcards still to show; "Again" re-queues one
      QueuePos : int
      Flipped  : bool
      MatchG   : int option        // selected Greek word in the match grid
      MatchE   : int option        // ... and English meaning
      Matched  : Set<int>          // Greek indices paired off
      MatchWrong : (int * int) option
      WrongSeq : int               // guards the timer that clears MatchWrong
      Mc       : int option        // multiple-choice pick
      Line     : string list       // tile ids on the composing line, in order
      Built    : bool option       // result of "Check the line"
      Cells    : Map<string, string>   // paradigm slot ("gs", "dp"…) → chosen ending
      Active   : string option     // the slot the next ending goes into
      TableChecked : bool
      // Letters, Iliad, myth
      Letter   : int
      Gloss    : (int * int) option    // (line, word) in the Iliad passage
      ShowTrans: bool
      MythLeaf : int
      MythPick : int option
      // Book I, Lesson 1 — the alphabet
      AStep    : int
      WriteIdx : int
      Written  : bool
      FfIdx    : int
      FfPick   : int option
      DcWord   : int
      DcShown  : int list
      NameShown: bool
      // Sounds
      Playing  : (int * int) option    // (accent example, syllable) sounding now
      PlayToken: int
      ExWord   : int
      ExPick   : int option
      ExPlayed : bool }

type LearnMsg =
    | SetPace of int
    | FinishOnboarding of hash: string
    | StartLesson
    | LeafTo of int
    | Flip
    | NextCard of again: bool
    | TapMatch of greek: bool * index: int
    | ClearMatchWrong of seq_: int
    | PickMc of int
    | TapTile of id: string * fromBank: bool
    | MoveTile of id: string * toLine: bool * index: int
    | CheckLine
    | TapCell of slot: string
    | TapChip of ending: string
    | CheckTable
    | FinishLesson
    | PickLetter of int
    | Listen
    | PickGloss of line: int * word: int
    | ToggleTrans
    | MythTo of int
    | PickMyth of int
    | AlphaTo of int
    | PickWriteLetter of int
    | Inked
    | ClearInk
    | WatchLetter
    | PickFf of int
    | NextFf
    | ShowDcLetter of int
    | NextDcWord
    | ShowName
    | ToBookTwo
    | PlayAccent of int
    | Playhead of token: int * at: (int * int) option
    | PlayExercise
    | PickExercise of int
    | AnotherSound
    | SetExercise of int

// ---------------------------------------------------------------------------
// 3.5 Boot + shell + Model
// ---------------------------------------------------------------------------

type BootState =
    | Booting of message: string
    | BootFailed of string
    | Booted

type PopoverKind = WordPopover of word: string * anchorRect: {| left: float; top: float; bottom: float |}

/// Something the reader can drag around. `X`/`Y` are viewport coordinates of
/// its top-left corner; `None` means it hasn't been moved and still sits where
/// the stylesheet puts it. The pointer offset isn't kept here — the view holds
/// it on the element, since that is where the geometry is legible.
type Draggable =
    { X: float option
      Y: float option
      Dragging: bool }

/// The notes panel and the floating button that opens it, each placed
/// independently. `ButtonMoved` distinguishes a drag from a click: the button
/// only toggles the panel when the pointer was released without having moved.
type NotesPanel =
    { Open: bool
      Panel: Draggable
      Button: Draggable
      ButtonMoved: bool }

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
      Learn    : LearnModel

      // shell / chrome
      // The desktop sidebar remembers two states, not one: reading is a focus
      // mode, so the reader collapses it by default, while the browsing pages
      // keep it open. Toggling on a reader route updates only NavHiddenReader.
      // Use `Router.navHiddenOn` to pick the one that applies to a route.
      NavHidden       : bool              // desktop sidebar collapsed, browsing pages
      NavHiddenReader : bool              // ... and on a reader route
      SideOpen     : bool                 // phone drawer open
      SettingsOpen : bool
      SourceMenuOpen : bool               // the header chip's own text-source menu
      Notes        : NotesPanel           // draggable notes panel, shown while reading
      EditingNote  : string option        // mark id whose note is open for editing in the panel / My library
      Popover      : PopoverKind option
      Toast        : (int * string) option    // (id, message) — id lets Cmd cancel
      JumpInput    : string

      // sidebar
      NavQuery     : string
      OpenAuthors  : Set<string>          // expanded when no query
      ClosedAuthors: Set<string>          // collapsed while a query is active ("!"+id in JS)
      PartsOpen    : bool
      /// Genre chip (a `WikiData` genre id, or "other"), shared by the sidebar
      /// catalogue and My library. Session-only: deliberately not persisted.
      Genre        : string option

      // search boxes
      HomeQuery    : string
      BrowseQuery  : string
      WikiQuery    : string

      // misc
      Recent    : RecentEntry list        // max 6
      Collapsed : Map<string, bool>       // data-col section state on phones
      TextCache : Map<string, RawSegment list>   // keyed by TextMeta.Urn
      OriginCache : Map<string, TextOrigin>      // ... and where each came from
      History   : string list             // in-app back stack of hashes
      CurrentHash : string
      DrawerDrag  : {| StartX: float; Dx: float; Mode: string; Width: float |} option
      NextToken   : int }

// ---------------------------------------------------------------------------
// 3.6 Msg
// ---------------------------------------------------------------------------

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
    | ToggleSourceMenu
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
    /// Open (Some mark id) or close (None) the small note editor used in the
    /// notes panel and on My library. The reader has its own, fuller editor.
    | EditNote of markId: string option
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
    | GrcLoaded  of token: int * RawSegment list * TextOrigin
    | EngLoaded  of token: int * RawSegment list option * TextOrigin option
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
    | ScrolledTo of segRef: string option
    | ScrollIdle of seq_: int
    | RepaintMarks
    | ClearFlash
    // -- study lenses --
    | OpenLens of LensKind * seg: string option
    /// The ◇ on a passage: opens (or closes) the rail on it in the last-used lens.
    | StudyPassage of seg: string
    | CloseLens
    | TraceWord of word: string
    | SetLensStem of string
    | SetMsView of MsView
    | ToggleMeter
    | PlayLine of segRef: string * line: int
    | StopPlayback
    | PlayheadAt of token: int * segRef: string * line: int * syl: int
    | PlaybackDone of token: int
    | PlacesLoaded of token: int * chunk: string * Result<PlaceHit list, string>
    | PlacePicked of segRef: string
    | LoadManifest of url: string
    | ManifestLoaded of url: string * Result<string * IiifCanvas list, string>
    | ManifestPage of int

type Msg =
    | Boot of Result<Catalog * Meta, string>
    | HashChanged of string
    | Navigate of hash: string * replace: bool
    | BackClicked
    | Settings_ of SettingsMsg
    | Source_ of SourceMsg
    | Library_ of LibraryMsg
    | Reader_ of ReaderMsg
    | Learn_ of LearnMsg
    | SetFilter of WorksFilter
    | SetNavQuery of string
    | SetGenre of string option
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
    /// A fleeting confirmation — "Copied", "Saved". Gone in a couple of seconds.
    | ShowToast of string
    /// A message worth actually reading, such as which source a text came from.
    /// It stays up for `lingerMs` and can be dismissed early.
    | ShowToastFor of message: string * lingerMs: int
    | HideToast of int
    | WikipediaSummary of authorId: string * extract: string
    | DrawerTouch of phase: string * x: float * y: float
    | ToggleNotesPanel
    /// Dragging the notes panel ("panel") or its floating button ("button").
    /// "move" carries the new top-left the view worked out from the pointer;
    /// "grab" and "drop" only mark the drag's start and end.
    | NotesDrag of part: string * phase: string * x: float * y: float
    | KeyPressed of key: string * alt: bool
    | NoOp
