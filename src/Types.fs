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

/// A word saved from the reader for review (My library › Words). Declared
/// before `Mark` so that Mark keeps the field names `Id`/`Work`/`Ref` for
/// type inference; construct these with a type annotation.
type WordCard =
    { Id      : string
      Word    : string              // the form as met in the text (NFC)
      Lemma   : string              // dictionary form, filled in by the reader ("" until then)
      Gloss   : string              // meaning, filled in by the reader
      Work    : string
      Ref     : string
      Context : string              // the Greek around it, for the front of the card
      Ts      : float               // when it was saved
      Box     : int                 // Leitner box: 0 new, 1..5 learned more and more
      Due     : float }             // next review (ms since epoch)

/// A place saved from the Map lens (My library › Places).
type SavedPlace =
    { Qid      : string
      Label    : string
      Pleiades : string
      Lat      : float
      Lon      : float
      Seen     : (string * string) list    // (workId, passage ref) where you met it
      Note     : string
      Ts       : float }

type MarkLink = { Work: string; Ref: string; Label: string option }

type Mark =
    { Id: string; Work: string; Ref: string
      Label: string; Snippet: string; Note: string
      Links: MarkLink list; Ts: float }

type Library =
    { Favs        : string list
      Marks       : Mark list
      AuthorNotes : Map<string, string>
      Words       : WordCard list
      Places      : SavedPlace list
      /// When each item last changed, deletions included, keyed "fav:<work>",
      /// "mark:<work>|<ref>", "anote:<author>", "word:<id>", "place:<qid>".
      /// Syncing merges two libraries key by key: the later change wins.
      Stamps      : Map<string, float> }

type RecentEntry = { Id: string; Chunk: string option; Ts: float }

type AuthorScope = ByEra of string | ByGenre of string      // "undated"/"other" allowed
type ArticleKind = Manuscripts | Variants

type WikiRoute =
    | WikiHome
    | WikiAuthors of AuthorScope option
    | WikiEras    of string option
    | WikiArticles of ArticleKind
    | WikiEditions
    /// Everyday life: the index (None) or one article, by slug
    | WikiLife of string option

/// Tabs of My library (`#lib/<tab>`).
type LibTab = LibMarks | LibWords | LibPlaces | LibFavs | LibNotes

type ForumRoute =
    | ForumHome
    | ForumBoard  of category: string
    | ForumThread of id: string
    | ForumNew    of category: string
    /// The community rules (`#forum/rules`)
    | ForumRules

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
    /// The catalogue ("Library" in the top bar), `#library`
    | Browse
    | LibraryRoute of LibTab
    | ForumRoute   of ForumRoute
    | AccountRoute
    | AboutRoute
    | AuthorRoute of id: string * section: string option
    | WikiRoute   of WikiRoute
    /// The "Start here" guide, which hangs off the home page: None = its
    /// contents page, Some slug = one page
    | GuideRoute  of slug: string option
    | ReaderRoute of workId: string * grcSuffix: string * engSuffix: string
                     * chunk: string option * seg: string option
    | LearnRoute  of LearnPage
    /// What is stored, where, and who can see it (`#privacy`)
    | PrivacyRoute
    /// An address the site has no page for (the hash, for the message)
    | NotFoundRoute of hash: string

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
// The catalogue page, word review, accounts and the forum
// ---------------------------------------------------------------------------

type ShelfSort = ByAuthor | ByTitle | ByDate

/// The Library (catalogue) page's own controls. The search box is
/// `BrowseQuery`, the genre chip `Genre`, and the translation filter `Filter`,
/// all shared with the sidebar.
type ShelfState =
    { Sort   : ShelfSort
      Letter : string option        // "A".."Z", or None for every letter
      Era    : string option }      // a Meta era id, or "undated"

/// A review of the saved words that are due, one card at a time.
type ReviewState =
    { Queue    : string list        // word ids still to see, the current one first
      Revealed : bool
      Seen     : int
      Right    : int              // known at the first try
      Missed   : string list }    // missed this session: a later "got it" starts again at box 1

/// A signed-in session with the sync/forum server (see Account.fs).
type Session =
    { AccessToken  : string
      RefreshToken : string
      ExpiresAt    : float          // ms since epoch
      UserId       : string
      Email        : string }

type SignInStage =
    | EnterEmail
    | CodeSent of email: string
    | Verifying

type SyncStatus =
    | SyncOff
    | Syncing
    | Synced of at: float
    | SyncError of string

type AccountState =
    /// False when the site was built without a server address: accounts and
    /// the forum then explain themselves instead of failing.
    { Configured  : bool
      Session     : Session option
      DisplayName : string          // as saved on the server
      IsAdmin     : bool
      Stage       : SignInStage
      EmailInput  : string
      CodeInput   : string
      NameInput   : string
      Busy        : bool
      Error       : string option
      Sync        : SyncStatus
      SyncToken   : int             // debounces pushes: only the newest timer syncs
      /// The "delete my account" step is open, and what has been typed in it
      DeleteAsk   : bool
      DeleteText  : string }

type Remote<'T> =
    | NotAsked
    | InFlight
    | Loaded of 'T
    | Failed of string

type ForumThread =
    { Id         : string
      Category   : string
      Title      : string
      Body       : string
      AuthorId   : string
      AuthorName : string
      Work       : string           // "" unless it is about a passage
      Ref        : string
      Status     : string           // bug reports: "open" | "confirmed" | "fixed" | "wontfix"; "" otherwise
      Created    : float
      LastPost   : float
      Replies    : int }

type ForumPost =
    { Id         : string
      ThreadId   : string
      Body       : string
      AuthorId   : string
      AuthorName : string
      Created    : float }

/// The new-thread form. Bug reports use the three `Bug*` fields in place of a body.
type ForumDraft =
    { Category    : string
      Title       : string
      Body        : string
      Work        : string
      Ref         : string
      BugWhat     : string
      BugSteps    : string
      BugExpected : string
      FromHash    : string }        // the page the reader came from, sent with a bug report

/// A report as moderators see it. `ThreadTitle` and `Excerpt` are what the
/// reader saw when reporting; the link leads to the thread as it is now.
type ForumReport =
    { Id          : string
      ThreadId    : string
      PostId      : string          // "" when the thread itself was reported
      Reason      : string          // "spam" | "abuse" | "offtopic" | "other"
      Note        : string
      ThreadTitle : string
      Excerpt     : string
      Created     : float }

/// The report form, open under one thread or reply.
type ReportDraft =
    { ThreadId : string
      PostId   : string             // "" for the thread itself
      Excerpt  : string
      Reason   : string
      Note     : string }

type ForumState =
    { Board   : Remote<ForumThread list>
      BoardOf : string              // the category the list is for ("" = latest across all)
      Thread  : Remote<ForumThread * ForumPost list>
      Draft   : ForumDraft
      Reply   : string
      Posting : bool
      /// People this reader has chosen not to see (id, name). Kept in this
      /// browser only (`anag:blocked`); their posts fold away, nothing more.
      Blocked : (string * string) list
      /// Blocked posts the reader has chosen to show anyway, this visit
      Unhidden : Set<string>
      Report  : ReportDraft option
      /// Moderators: open reports
      Reports : Remote<ForumReport list> }

/// The header search. `Scope` narrows the results: "all", "texts",
/// "authors", "mine" (My library) or "guide" (the guide and the wiki).
type SearchState =
    { Query  : string
      Open   : bool
      Active : int                  // the highlighted result, for the arrow keys
      Scope  : string
      Recent : string list }        // searches you chose a result for, newest first
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

/// `seg` is the passage the word was clicked in, so it can be saved with its context.
type PopoverKind = WordPopover of word: string * anchorRect: {| left: float; top: float; bottom: float |} * seg: string option

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
      SettingsOpen : bool
      SourceMenuOpen : bool               // the header chip's own text-source menu
      Notes        : NotesPanel           // draggable notes panel, shown while reading
      EditingNote  : string option        // mark id whose note is open for editing in the panel / My library
      Popover      : PopoverKind option
      Toast        : (int * string) option    // (id, message) — id lets Cmd cancel

      // search (the header box)
      Search       : SearchState
      /// Genre chip (a `WikiData` genre id, or "other"), shared by the Library
      /// page and My library. Session-only: deliberately not persisted.
      Genre        : string option

      // search boxes
      BrowseQuery  : string
      WikiQuery    : string
      Shelf        : ShelfState
      /// My library › Bookmarks: sort order ("recent" | "work" | "oldest"),
      /// a #tag filter, and a search box over labels, snippets and notes
      MarkSort     : string
      MarkTag      : string option
      MarkQuery    : string
      Review       : ReviewState option
      Account      : AccountState
      Forum        : ForumState

      // misc
      Recent    : RecentEntry list        // max 6
      Collapsed : Map<string, bool>       // data-col section state on phones
      TextCache : Map<string, RawSegment list>   // keyed by TextMeta.Urn
      OriginCache : Map<string, TextOrigin>      // ... and where each came from
      History   : string list             // in-app back stack of hashes
      CurrentHash : string
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
    | SetMarkSort of string
    | SetMarkTag of string option
    | SetMarkQuery of string
    // -- words --
    | SaveWord of word: string * seg: string option
    | EditWord of id: string * lemma: string * gloss: string
    | RemoveWord of id: string
    | StartReview
    | RevealCard
    | GradeCard of knew: bool
    | EndReview
    // -- places --
    | SavePlace of PlaceHit
    | RemovePlace of qid: string
    | SetPlaceNote of qid: string * note: string
    | ExportRequested
    | ImportText of string
    | ImportConfirmed
    | ClearLibraryConfirmed

type ShelfMsg =
    | SetShelfSort of ShelfSort
    | SetShelfLetter of string option
    | SetShelfEra of string option

type AccountMsg =
    | SetEmailInput of string
    | SetCodeInput of string
    | SetNameInput of string
    | SendCode
    | CodeSentOk of email: string
    | VerifyCode
    | SignedIn of Session
    /// A sign-in link from the email lands on the site with the session in the URL
    | SessionFromUrl of Session
    | AuthFailed of string
    | ProfileLoaded of name: string * isAdmin: bool
    | SaveName
    | NameSaved of string
    /// `forget`: also remove the library from this browser (a shared computer)
    | SignOut of forget: bool
    | UseDifferentEmail
    | SyncSoon
    | SyncNow of token: int
    | SyncPulled of Result<Library, string>
    | SyncPushed of Result<float, string>
    | SessionRefreshed of Session option
    /// Deleting the account: first press asks, the second (with the word typed) does it
    | AskDeleteAccount of bool
    | SetDeleteConfirm of string
    | DeleteAccount
    | AccountDeleted of Result<unit, string>

type ForumMsg =
    | LoadBoard of category: string
    | BoardLoaded of category: string * Result<ForumThread list, string>
    | LoadThread of id: string
    | ThreadLoaded of id: string * Result<ForumThread * ForumPost list, string>
    | StartThread of category: string * work: string * ref_: string
    | SetDraft of ForumDraft
    | SubmitThread
    | ThreadPosted of Result<string, string>
    | SetReply of string
    | SubmitReply
    | ReplyPosted of Result<unit, string>
    | SetBugStatus of threadId: string * status: string
    | DeletePost of threadId: string * postId: string
    | DeleteThread of threadId: string
    | ForumDone of Result<string, string>
    | OpenReport of threadId: string * postId: string * excerpt: string
    | SetReport of ReportDraft
    | CancelReport
    | SubmitReport
    | ReportSent of Result<unit, string>
    | Block of userId: string * name: string
    | Unblock of userId: string
    | ShowHidden of postId: string
    | LoadReports
    | ReportsLoaded of Result<ForumReport list, string>
    | ResolveReport of reportId: string

type SearchMsg =
    | SetSearchQuery of string
    | OpenSearch
    | CloseSearch
    | MoveSearch of delta: int
    | SetSearchScope of string
    /// Enter: run the highlighted result
    | ChooseActive
    /// A result was chosen (by click or Enter): remember the query, close
    | Chose
    | ForgetSearches

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
    /// Go to a passage reference in the open text (from the search box)
    | GotoRef of string
    | CopyUrn of string
    | WordClicked of word: string * rect: obj * seg: string option
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
    | Shelf_ of ShelfMsg
    | Account_ of AccountMsg
    | Forum_ of ForumMsg
    | Search_ of SearchMsg
    | Learn_ of LearnMsg
    | SetFilter of WorksFilter
    | SetGenre of string option
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
    | ToggleNotesPanel
    /// Dragging the notes panel ("panel") or its floating button ("button").
    /// "move" carries the new top-left the view worked out from the pointer;
    /// "grab" and "drop" only mark the drag's start and end.
    | NotesDrag of part: string * phase: string * x: float * y: float
    | KeyPressed of key: string * alt: bool
    | NoOp
