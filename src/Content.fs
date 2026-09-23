module Content

// ---------------------------------------------------------------------------
// icons — structured (not raw SVG markup) so the view layer can build real
// Feliz elements instead of dangerouslySetInnerHTML.
// ---------------------------------------------------------------------------

type IconShape =
    | IPath of d: string
    | ICircle of cx: float * cy: float * r: float
    /// A solid shape (a flame, a beak, an interpunct): filled, never stroked.
    | IFill of d: string

type Icon = { ViewBox: string; Shapes: IconShape list }

let private icon (shapes: IconShape list) : Icon = { ViewBox = "0 0 24 24"; Shapes = shapes }

/// One icon set for the whole app, drawn from Greek objects rather than
/// generic UI glyphs: 24-unit grid, 1.6 stroke, round caps (the stylesheet sets
/// the stroke). Where a Greek object would be ambiguous (back, search, the
/// source indicators) the plain glyph stays, because being understood comes first.
let icons: Map<string, Icon> =
    Map.ofList [
        // An open papyrus roll between its two rollers: the texts themselves.
        "texts",
        icon [
            IPath "M5.5 3v18M18.5 3v18"
            IPath "M4.3 3h2.4M4.3 21h2.4M17.3 3h2.4M17.3 21h2.4"
            IPath "M5.5 6h13M5.5 18h13"
            IPath "M8.5 9.5h7M8.5 12h7M8.5 14.5h4.5"
        ]
        // A list with inscriptional interpuncts (the ⁝ that divided words on stone).
        "contents",
        icon [
            IPath "M10 6h10M10 12h10M10 18h7"
            IFill "M5.5 4.6l1.4 1.4-1.4 1.4-1.4-1.4zM5.5 10.6l1.4 1.4-1.4 1.4-1.4-1.4zM5.5 16.6l1.4 1.4-1.4 1.4-1.4-1.4z"
        ]
        // A library's pigeonholes of rolled books; one roll is out, its label tag
        // (the sillybos) left hanging: your own shelf.
        "library",
        icon [
            IPath "M3.5 3.5h17v17h-17zM3.5 12h17"
            ICircle(7.6, 7.9, 2.0); ICircle(12.0, 7.9, 2.0); ICircle(16.4, 7.9, 2.0)
            ICircle(7.6, 16.3, 2.0); ICircle(12.0, 16.3, 2.0)
            IPath "M15 18.3h3.3"
        ]
        // A temple front: the wiki is the museum beside the library.
        "wiki",
        icon [
            IPath "M3 9L12 4l9 5z"
            IPath "M6.5 11.2v6.3M10.2 11.2v6.3M13.8 11.2v6.3M17.5 11.2v6.3"
            IPath "M4.5 18.8h15M3 21h18"
        ]
        // Αα: the text's own appearance (columns, typeface, size, theme).
        "textset",
        icon [
            IPath "M3 19L7.5 5l4.5 14M4.6 14h5.8"
            IPath "M20.8 12.2c-.5 3.9-1.8 6.8-3.9 6.8-1.6 0-2.7-1.4-2.7-3.3s1.1-3.3 2.7-3.3c1.9 0 3.1 2.6 4 6.6"
        ]
        // A clay oil lamp (lychnos): light and dark.
        "lamp",
        icon [
            IPath "M2.8 15.6c1.5 2 4.6 3 8.7 3 4 0 6.8-1.6 8.3-4.6-1.9.3-3.5 0-4.8-.9-1.3-.9-3-1.3-4.8-1.3-3.3 0-6.3 1.4-7.4 3.8z"
            IPath "M10.2 13.9h2.4M8.2 18.5l-.7 2h8.9l-.7-2"
            IFill "M19.9 11.9c-1.5-1-1.7-2.7-.3-4.5.6 1.6 1.9 2.7.3 4.5z"
        ]
        // An olive sprig, the victor's crown: favourites. The stem comes first so
        // the stylesheet can fill only the leaves when a work is a favourite.
        "olive",
        icon [
            IPath "M5 20.5C8 15.5 12.5 10 19.5 3.5"
            IPath "M8.5 15.9c-2.8.1-4.6-1.1-5.3-3.5 2.6-.2 4.5 1.1 5.3 3.5zM11.4 12.4c.3-2.7 1.8-4.3 4.3-4.8-.1 2.6-1.6 4.3-4.3 4.8zM13.6 9.6c-2.5-.5-3.8-2.1-3.9-4.6 2.4.4 3.8 2 3.9 4.6zM16.9 7.9c.2-2.3 1.4-3.7 3.6-4.2-.1 2.2-1.3 3.7-3.6 4.2z"
        ]
        "bookmark", icon [ IPath "M6.5 3.5h11v17l-5.5-4-5.5 4z" ]
        "study", icon [ IPath "M12 3l8 9-8 9-8-9z" ]
        "back", icon [ IPath "M15 5l-7 7 7 7" ]
        // A herm: a head on a pillar, as authors were shown in libraries.
        "authors",
        icon [
            ICircle(12.0, 7.0, 3.3)
            IPath "M6.5 20v-3c0-2.7 2.5-4.4 5.5-4.4s5.5 1.7 5.5 4.4v3"
            IPath "M5 20.5h14"
        ]
        "eras",
        icon [
            IPath "M6.5 3.5h11M6.5 20.5h11"
            IPath "M8 3.5c0 4.5 4 5.5 4 8.5s-4 4-4 8.5M16 3.5c0 4.5-4 5.5-4 8.5s4 4 4 8.5"
            IFill "M9.4 19.6c.7-1.9 1.5-2.8 2.6-2.8s1.9.9 2.6 2.8z"
        ]
        // An open codex: how the texts came down to us.
        "manuscripts",
        icon [
            IPath "M3 6c3-1.2 6-1.2 9 .8 3-2 6-2 9-.8v13c-3-1.2-6-1.2-9 .8-3-2-6-2-9-.8z"
            IPath "M12 6.8v13"
            IPath "M5.3 9.6c1.6-.4 3-.3 4.4.3M5.3 12.6c1.6-.4 3-.3 4.4.3M14.3 9.9c1.4-.6 2.8-.7 4.4-.3M14.3 12.9c1.4-.6 2.8-.7 4.4-.3"
        ]
        // A stemma codicum, the family tree editors draw of the manuscripts.
        "variants",
        icon [
            IPath "M11 6.1l-3.6 4.4M13 6.1l3.6 4.4M5.8 13.7l-1.1 4.2M7.2 13.7l1.1 4.2M17.5 13.8v4.1"
            ICircle(12.0, 4.5, 1.8); ICircle(6.5, 12.0, 1.8); ICircle(17.5, 12.0, 1.8)
            ICircle(4.3, 19.6, 1.6); ICircle(8.7, 19.6, 1.6); ICircle(17.5, 19.6, 1.6)
        ]
        "editions",
        icon [
            IPath "M4 17h16v3.5H4zM5.5 13.5h13V17h-13zM4.8 10h12.4v3.5H4.8z"
            IPath "M7.5 18.8h2M8.5 15.2h2M7.5 11.8h2"
            IPath "M8 10V7.2a1.4 1.4 0 0 1 1.4-1.4h7.2"
        ]
        // Athena's owl: about the project and the people behind it.
        "about",
        icon [
            IPath "M5.5 9.5c0-3.2 2.9-5.5 6.5-5.5s6.5 2.3 6.5 5.5v5c0 3.6-2.9 6.5-6.5 6.5s-6.5-2.9-6.5-6.5z"
            IPath "M6.4 6.6L5 3.4l3.5 1.5M17.6 6.6L19 3.4l-3.5 1.5"
            ICircle(9.0, 10.3, 2.1); ICircle(15.0, 10.3, 2.1)
            IFill "M11.1 13.2h1.8L12 15z"
            IPath "M9.6 17.6l1.2.8 1.2-.8 1.2.8 1.2-.8"
        ]
        // A wax tablet (deltos) and stylus: notes.
        "notes",
        icon [
            IPath "M3.5 5h12v15h-12z"
            IPath "M6 7.5h7v10H6z"
            IPath "M7.8 11h3.4M7.8 13.8h2.4"
            IPath "M21 3.2l-5.8 11.6-.9.6.1-1.1L20.2 2.7z"
        ]
    ]

// ---------------------------------------------------------------------------
// alphabet table
// ---------------------------------------------------------------------------

type AlphabetRow = { Upper: string; Lower: string; Name: string; Translit: string }

let alphabet: AlphabetRow list =
    [ { Upper = "Α"; Lower = "α"; Name = "alpha"; Translit = "a" }
      { Upper = "Β"; Lower = "β"; Name = "beta"; Translit = "b" }
      { Upper = "Γ"; Lower = "γ"; Name = "gamma"; Translit = "g" }
      { Upper = "Δ"; Lower = "δ"; Name = "delta"; Translit = "d" }
      { Upper = "Ε"; Lower = "ε"; Name = "epsilon"; Translit = "e (short)" }
      { Upper = "Ζ"; Lower = "ζ"; Name = "zeta"; Translit = "z" }
      { Upper = "Η"; Lower = "η"; Name = "eta"; Translit = "ē (long)" }
      { Upper = "Θ"; Lower = "θ"; Name = "theta"; Translit = "th" }
      { Upper = "Ι"; Lower = "ι"; Name = "iota"; Translit = "i" }
      { Upper = "Κ"; Lower = "κ"; Name = "kappa"; Translit = "k" }
      { Upper = "Λ"; Lower = "λ"; Name = "lambda"; Translit = "l" }
      { Upper = "Μ"; Lower = "μ"; Name = "mu"; Translit = "m" }
      { Upper = "Ν"; Lower = "ν"; Name = "nu"; Translit = "n" }
      { Upper = "Ξ"; Lower = "ξ"; Name = "xi"; Translit = "x (ks)" }
      { Upper = "Ο"; Lower = "ο"; Name = "omicron"; Translit = "o (short)" }
      { Upper = "Π"; Lower = "π"; Name = "pi"; Translit = "p" }
      { Upper = "Ρ"; Lower = "ρ"; Name = "rho"; Translit = "r" }
      { Upper = "Σ"; Lower = "σ ς"; Name = "sigma"; Translit = "s" }
      { Upper = "Τ"; Lower = "τ"; Name = "tau"; Translit = "t" }
      { Upper = "Υ"; Lower = "υ"; Name = "upsilon"; Translit = "y (u in diphthongs)" }
      { Upper = "Φ"; Lower = "φ"; Name = "phi"; Translit = "ph" }
      { Upper = "Χ"; Lower = "χ"; Name = "chi"; Translit = "kh" }
      { Upper = "Ψ"; Lower = "ψ"; Name = "psi"; Translit = "ps" }
      { Upper = "Ω"; Lower = "ω"; Name = "omega"; Translit = "ō (long)" } ]

// ---------------------------------------------------------------------------
// "reading Greek: five things to know"
// ---------------------------------------------------------------------------

type Tip = { Grc: string; Label: string; Text: string }

let tips: Tip list =
    [ { Grc = "ἁ ἀ"
        Label = "Breathings"
        Text = "Every word that begins with a vowel carries a breathing mark. The rough breathing ( ῾ ) adds an h sound, so ἁ is \"ha\"; the smooth breathing ( ᾿ ) adds nothing. A word-initial ρ always takes the rough one, which is why ῥήτωρ comes into English as \"rhetor\"." }
      { Grc = "ά ᾶ ὰ"
        Label = "Accents"
        Text = "The acute, circumflex and grave marked the rise and fall of the voice in ancient speech. Scholars at Alexandria began writing them down around 200 BCE. Today they are usually read as stress, and now and then they tell apart two words spelled alike." }
      { Grc = "ᾳ ῃ ῳ"
        Label = "Iota subscript"
        Text = "A small iota written under a long vowel is all that is left of a diphthong whose iota stopped being pronounced. Watch for it at the end of nouns, where it usually marks the dative case." }
      { Grc = "σ ς"
        Label = "Two sigmas"
        Text = "σ inside a word, ς at the end of one: the same letter in two shapes." }
      { Grc = "; ·"
        Label = "Punctuation"
        Text = "The Greek question mark looks like an English semicolon ( ; ), and a raised dot ( · ) does the work of a colon or semicolon." } ]

// ---------------------------------------------------------------------------
// passage of the day
// ---------------------------------------------------------------------------

/// `En` is our own translation, shown as such on the home page; the reader shows
/// the published translation from the collection. A trailing * marks a
/// rendering we are unsure of.
type Passage = { Work: string; Ref: string; Grc: string; En: string; Who: string }

let passages: Passage list =
    [ { Work = "tlg0012.tlg001"; Ref = "1.1"; Grc = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος"; En = "Sing, goddess, the wrath of Peleus' son Achilles."; Who = "Homer, Iliad 1.1" }
      { Work = "tlg0012.tlg002"; Ref = "1.1"; Grc = "ἄνδρα μοι ἔννεπε, Μοῦσα, πολύτροπον"; En = "Tell me, Muse, of the man of many turns."; Who = "Homer, Odyssey 1.1" }
      { Work = "tlg0059.tlg002"; Ref = "17a"; Grc = "ὅτι μὲν ὑμεῖς, ὦ ἄνδρες Ἀθηναῖοι, πεπόνθατε ὑπὸ τῶν ἐμῶν κατηγόρων, οὐκ οἶδα"; En = "How you, men of Athens, have been affected by my accusers, I do not know."; Who = "Plato, Apology 17a" }
      { Work = "tlg0086.tlg010"; Ref = "1.1"; Grc = "πᾶσα τέχνη καὶ πᾶσα μέθοδος … ἀγαθοῦ τινὸς ἐφίεσθαι δοκεῖ"; En = "Every art and every inquiry … seems to aim at some good."; Who = "Aristotle, Nicomachean Ethics 1.1" }
      { Work = "tlg0016.tlg001"; Ref = "1.1.1"; Grc = "Περσέων μέν νυν οἱ λόγιοι Φοίνικας αἰτίους φασὶ γενέσθαι τῆς διαφορῆς"; En = "The learned Persians say the Phoenicians were the cause of the quarrel."; Who = "Herodotus, Histories 1.1" }
      { Work = "tlg0003.tlg001"; Ref = "1.1.1"; Grc = "Θουκυδίδης Ἀθηναῖος ξυνέγραψε τὸν πόλεμον τῶν Πελοποννησίων καὶ Ἀθηναίων"; En = "Thucydides the Athenian wrote the history of the war between the Peloponnesians and the Athenians."; Who = "Thucydides 1.1" }
      { Work = "tlg0011.tlg002"; Ref = "332"; Grc = "πολλὰ τὰ δεινὰ κοὐδὲν ἀνθρώπου δεινότερον πέλει"; En = "Many things are formidable, and none more formidable than man."; Who = "Sophocles, Antigone 332" }
      { Work = "tlg0562.tlg001"; Ref = "2.1"; Grc = "ἕωθεν προλέγειν ἑαυτῷ· συντεύξομαι περιέργῳ, ἀχαρίστῳ, ὑβριστῇ"; En = "Say to yourself at dawn: I shall meet the meddling, the ungrateful, the insolent."; Who = "Marcus Aurelius, Meditations 2.1" }
      { Work = "tlg0020.tlg001"; Ref = "1"; Grc = "Μουσάων Ἑλικωνιάδων ἀρχώμεθ᾽ ἀείδειν"; En = "From the Muses of Helicon let us begin to sing."; Who = "Hesiod, Theogony 1" }
      { Work = "tlg0032.tlg006"; Ref = "1.1.1"; Grc = "Δαρείου καὶ Παρυσάτιδος γίγνονται παῖδες δύο"; En = "Darius and Parysatis had two sons."; Who = "Xenophon, Anabasis 1.1" }
      { Work = "tlg0557.tlg002"; Ref = "1"; Grc = "τῶν ὄντων τὰ μέν ἐστιν ἐφ᾽ ἡμῖν, τὰ δὲ οὐκ ἐφ᾽ ἡμῖν"; En = "Some things are within our power, and others are not."; Who = "Epictetus, Handbook 1" }
      { Work = "tlg1799.tlg001"; Ref = "1.def.1"; Grc = "σημεῖόν ἐστιν, οὗ μέρος οὐθέν"; En = "A point is that which has no part."; Who = "Euclid, Elements 1, def. 1" }
      { Work = "tlg0006.tlg003"; Ref = "1"; Grc = "εἴθ᾽ ὤφελ᾽ Ἀργοῦς μὴ διαπτάσθαι σκάφος"; En = "Would that the hull of the Argo had never flown through…"; Who = "Euripides, Medea 1" }
      { Work = "tlg0059.tlg030"; Ref = "1.327a"; Grc = "κατέβην χθὲς εἰς Πειραιᾶ μετὰ Γλαύκωνος τοῦ Ἀρίστωνος"; En = "I went down yesterday to the Piraeus with Glaucon, son of Ariston."; Who = "Plato, Republic 327a" }
      { Work = "tlg0085.tlg005"; Ref = "1"; Grc = "θεοὺς μὲν αἰτῶ τῶνδ᾽ ἀπαλλαγὴν πόνων"; En = "I ask the gods for release from this toil."; Who = "Aeschylus, Agamemnon 1" } ]

// ---------------------------------------------------------------------------
// reading paths
// ---------------------------------------------------------------------------

type ReadingPath = { Title: string; Desc: string; WorkIds: string list }

let paths: ReadingPath list =
    [ { Title = "First steps in Greek prose"; Desc = "Clear, plain prose first, then richer and harder styles."; WorkIds = [ "tlg0032.tlg006"; "tlg0540.tlg001"; "tlg0059.tlg002"; "tlg0059.tlg003"; "tlg0016.tlg001" ] }
      { Title = "Epic from the beginning"; Desc = "The hexameter tradition in order."; WorkIds = [ "tlg0012.tlg001"; "tlg0012.tlg002"; "tlg0020.tlg001"; "tlg0020.tlg002"; "tlg0001.tlg001" ] }
      { Title = "The Athenian stage"; Desc = "One play from each of the great dramatists."; WorkIds = [ "tlg0085.tlg005"; "tlg0011.tlg002"; "tlg0011.tlg004"; "tlg0006.tlg003"; "tlg0019.tlg003" ] }
      { Title = "Philosophy in sequence"; Desc = "Socrates to the Stoics."; WorkIds = [ "tlg0059.tlg002"; "tlg0059.tlg030"; "tlg0086.tlg010"; "tlg0557.tlg002"; "tlg0562.tlg001" ] }
      { Title = "Koine Greek"; Desc = "The shared Greek of the Hellenistic and Roman world."; WorkIds = [ "tlg0031.tlg004"; "tlg0031.tlg005"; "tlg0031.tlg006"; "tlg0527.tlg001"; "tlg0018.tlg001" ] }
      { Title = "History in Greek"; Desc = "Five historians, five centuries."; WorkIds = [ "tlg0016.tlg001"; "tlg0003.tlg001"; "tlg0032.tlg001"; "tlg0543.tlg001"; "tlg0007.tlg012" ] } ]

// ---------------------------------------------------------------------------
// browse page — curated genre sections
// ---------------------------------------------------------------------------

type BrowseSection = { Title: string; WorkIds: string list }

let browse: BrowseSection list =
    [ { Title = "Epic poetry"; WorkIds = [ "tlg0012.tlg001"; "tlg0012.tlg002"; "tlg0020.tlg001"; "tlg0020.tlg002"; "tlg0001.tlg001"; "tlg0013.tlg002" ] }
      { Title = "Tragedy and comedy"; WorkIds = [ "tlg0085.tlg005"; "tlg0085.tlg003"; "tlg0011.tlg002"; "tlg0011.tlg004"; "tlg0011.tlg007"; "tlg0006.tlg003"; "tlg0006.tlg017"; "tlg0006.tlg005"; "tlg0019.tlg003"; "tlg0019.tlg004" ] }
      { Title = "History"; WorkIds = [ "tlg0016.tlg001"; "tlg0003.tlg001"; "tlg0032.tlg006"; "tlg0032.tlg001"; "tlg0543.tlg001"; "tlg0007.tlg012"; "tlg0007.tlg010"; "tlg0526.tlg001"; "tlg0060.tlg001"; "tlg0086.tlg003" ] }
      { Title = "Philosophy"; WorkIds = [ "tlg0059.tlg002"; "tlg0059.tlg003"; "tlg0059.tlg011"; "tlg0059.tlg030"; "tlg0086.tlg010"; "tlg0086.tlg035"; "tlg0086.tlg025"; "tlg0557.tlg002"; "tlg0557.tlg001"; "tlg0562.tlg001"; "tlg0004.tlg001" ] }
      { Title = "Oratory and rhetoric"; WorkIds = [ "tlg0014.tlg018"; "tlg0014.tlg004"; "tlg0010.tlg011"; "tlg0540.tlg001"; "tlg0026.tlg003"; "tlg0028.tlg001" ] }
      { Title = "Koine and early Christian writing"; WorkIds = [ "tlg0031.tlg001"; "tlg0031.tlg004"; "tlg0031.tlg005"; "tlg0031.tlg006"; "tlg0527.tlg001"; "tlg0018.tlg001"; "tlg1271.tlg001"; "tlg1443.tlg001" ] }
      { Title = "Science, mathematics and medicine"; WorkIds = [ "tlg1799.tlg001"; "tlg0057.tlg002"; "tlg0093.tlg001"; "tlg0627.tlg003"; "tlg0552.tlg001"; "tlg0086.tlg014" ] }
      { Title = "Novel, satire and later prose"; WorkIds = [ "tlg0062.tlg002"; "tlg0062.tlg001"; "tlg0561.tlg001"; "tlg0641.tlg001"; "tlg0008.tlg001"; "tlg0525.tlg001" ] } ]
