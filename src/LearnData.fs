module LearnData

// The content of the Learn section (#learn): Book I, Lesson 1 (the alphabet),
// Book II, Lesson 3 (the first declension), the letters, the pitch accent,
// Iliad 1.1–5 with glosses, and Odysseus in the Cyclops' cave. Ported from the
// "Arche" design prototype; the wording is the design's and should be edited
// here, not in the view.

// ---------------------------------------------------------------------------
// Book II, Lesson 3 — the first declension
// ---------------------------------------------------------------------------

type Flashcard = { G: string; Gen: string; En: string; Note: string }

let flashcards: Flashcard array =
    [| { G = "ἡ τιμή"; Gen = "τιμή, τιμῆς, ἡ"; En = "honour, worth"; Note = "Hence the name Timothy, “honouring god”." }
       { G = "ἡ ψυχή"; Gen = "ψυχή, ψυχῆς, ἡ"; En = "soul, life, breath"; Note = "Hence psychology." }
       { G = "ἡ φωνή"; Gen = "φωνή, φωνῆς, ἡ"; En = "voice, sound"; Note = "Hence telephone, “far-sound”." }
       { G = "ἡ νίκη"; Gen = "νίκη, νίκης, ἡ"; En = "victory"; Note = "Νίκη was also the winged goddess of victory." }
       { G = "ἡ βουλή"; Gen = "βουλή, βουλῆς, ἡ"; En = "will, plan; council"; Note = "Iliad 1.5: Διὸς δ᾽ ἐτελείετο βουλή, the will of Zeus was fulfilled." } |]

/// Match pairs: the Greek column, and the English column with the index of the
/// Greek word each meaning belongs to.
let matchGreek = [| "ψυχή"; "φωνή"; "νίκη"; "βουλή" |]
let matchEnglish = [| "victory", 2; "plan, will", 3; "soul", 0; "voice", 1 |]

let mcOptions = [| "τιμή"; "τιμῆς"; "τιμήν"; "τιμαί" |]
let mcAnswer = 1

/// Compose "Victory brings honour": tile id → word. One tile (x) doesn't belong.
let tiles: Map<string, string> =
    Map.ofList [ "h", "ἡ"; "n", "νίκη"; "t", "τιμὴν"; "f", "φέρει"; "x", "τιμῆς" ]
let bankOrder = [ "t"; "f"; "x"; "h"; "n" ]
let lineAnswer = [ "h"; "n"; "t"; "f" ]

/// The paradigm of τιμή. `given` slots are printed; the reader fills the rest,
/// in `slotOrder`, from `chips` (which include one distractor, -ου).
let answers: Map<string, string> =
    Map.ofList [ "gs", "ῆς"; "ds", "ῇ"; "as", "ήν"; "gp", "ῶν"; "dp", "αῖς"; "ap", "άς" ]
let given: Map<string, string> = Map.ofList [ "ns", "ή"; "vs", "ή"; "np", "αί"; "vp", "αί" ]
let slotOrder = [ "gs"; "ds"; "as"; "gp"; "dp"; "ap" ]
let paradigmRows =
    [ "Nominative", "ns", "np"
      "Genitive", "gs", "gp"
      "Dative", "ds", "dp"
      "Accusative", "as", "ap"
      "Vocative", "vs", "vp" ]
let chips = [ "ῶν"; "ῇ"; "άς"; "ου"; "ῆς"; "αῖς"; "ήν" ]

let lessonLeaves = 6

// ---------------------------------------------------------------------------
// The letters
// ---------------------------------------------------------------------------

type Letter =
    { Upper: string; Lower: string; Name: string; Translit: string
      Ipa: string; Hint: string; Word: string; Gloss: string }

let private L u l n t i h w g = { Upper = u; Lower = l; Name = n; Translit = t; Ipa = i; Hint = h; Word = w; Gloss = g }

let letters: Letter array =
    [| L "Α" "α" "ἄλφα" "alpha" "[a]" "as in father, short or long" "ἀρχή" "beginning"
       L "Β" "β" "βῆτα" "bēta" "[b]" "as in bed" "βίος" "life"
       L "Γ" "γ" "γάμμα" "gamma" "[g]" "as in go, never soft" "γῆ" "earth"
       L "Δ" "δ" "δέλτα" "delta" "[d]" "as in dog" "δῆμος" "the people"
       L "Ε" "ε" "ἒ ψιλόν" "epsilon" "[e]" "short, as in pet" "ἔπος" "word, epic"
       L "Ζ" "ζ" "ζῆτα" "zēta" "[zd]" "as in wisdom" "ζωή" "life"
       L "Η" "η" "ἦτα" "ēta" "[ɛː]" "long, as in air" "ἥλιος" "sun"
       L "Θ" "θ" "θῆτα" "thēta" "[tʰ]" "a breathy t, as in top" "θεός" "god"
       L "Ι" "ι" "ἰῶτα" "iota" "[i]" "as in machine" "ἵππος" "horse"
       L "Κ" "κ" "κάππα" "kappa" "[k]" "as in skin" "κόσμος" "order, world"
       L "Λ" "λ" "λάμβδα" "lambda" "[l]" "as in let" "λόγος" "word, reason"
       L "Μ" "μ" "μῦ" "mu" "[m]" "as in man" "μῦθος" "story"
       L "Ν" "ν" "νῦ" "nu" "[n]" "as in net" "νύξ" "night"
       L "Ξ" "ξ" "ξεῖ" "xi" "[ks]" "as in axe" "ξένος" "stranger, guest"
       L "Ο" "ο" "ὂ μικρόν" "omicron" "[o]" "short, as in not" "ὄνομα" "name"
       L "Π" "π" "πεῖ" "pi" "[p]" "as in spin" "πόλις" "city"
       L "Ρ" "ρ" "ῥῶ" "rho" "[r]" "rolled" "ῥόδον" "rose"
       L "Σ" "σ" "σῖγμα" "sigma" "[s]" "as in sing; ς at word-end" "σοφία" "wisdom"
       L "Τ" "τ" "ταῦ" "tau" "[t]" "as in stop" "τέχνη" "craft, art"
       L "Υ" "υ" "ὖ ψιλόν" "upsilon" "[y]" "French u, lips rounded" "ὕδωρ" "water"
       L "Φ" "φ" "φεῖ" "phi" "[pʰ]" "a breathy p, as in pot" "φῶς" "light"
       L "Χ" "χ" "χεῖ" "chi" "[kʰ]" "a breathy k, as in cat" "χρόνος" "time"
       L "Ψ" "ψ" "ψεῖ" "psi" "[ps]" "as in lapse" "ψυχή" "soul"
       L "Ω" "ω" "ὦ μέγα" "omega" "[ɔː]" "long, as in saw" "ὠκεανός" "ocean" |]

/// Resting heights (0–1) of the bars beside the Listen button.
let bars =
    [| 0.3; 0.5; 0.4; 0.7; 0.9; 0.6; 0.8; 1.0; 0.7; 0.5; 0.6; 0.9; 0.75; 0.55
       0.4; 0.65; 0.85; 0.6; 0.45; 0.7; 0.5; 0.35; 0.55; 0.4; 0.3; 0.45; 0.3; 0.2 |]

// ---------------------------------------------------------------------------
// Book I, Lesson 1 — Friends and False Friends
// ---------------------------------------------------------------------------

type WriteLetter = { Ch: string; Name: string; Tr: string }

let writeLetters: WriteLetter array =
    [| { Ch = "α"; Name = "ἄλφα"; Tr = "alpha · [a]" }
       { Ch = "λ"; Name = "λάμβδα"; Tr = "lambda · [l]" }
       { Ch = "ω"; Name = "ὦ μέγα"; Tr = "omega · [ɔː], the great o" } |]

/// Ink laid down (in CSS px of stroke) before a tracing counts as written.
let inkThreshold = 380.0

type FalseFriend = { Glyph: string; Options: string array; Answer: int; Note: string }

let falseFriends: FalseFriend array =
    [| { Glyph = "Ρρ"; Options = [| "p"; "r"; "b" |]; Answer = 1; Note = "Rho is r. Latin R grew out of it by adding a leg." }
       { Glyph = "Ηη"; Options = [| "h"; "ē, a long e"; "n" |]; Answer = 1; Note = "Eta is a long e. Early Greeks used it for h, and the Romans kept that value." }
       { Glyph = "ν"; Options = [| "v"; "u"; "n" |]; Answer = 2; Note = "Lower-case nu is n. Its capital, Ν, gives it away." }
       { Glyph = "Χχ"; Options = [| "x"; "kh"; "ch, as in church" |]; Answer = 1; Note = "Chi is a breathy k, as in chaos and chorus." } |]

type DecodeWord = { Glyphs: string array; Sounds: string array; Word: string; En: string; Note: string }

let decodeWords: DecodeWord array =
    [| { Glyphs = [| "Κ"; "Ο"; "Σ"; "Μ"; "Ο"; "Σ" |]; Sounds = [| "k"; "o"; "s"; "m"; "o"; "s" |]
         Word = "κόσμος"; En = "order, world, ornament"; Note = "Hence cosmos, and cosmetics." }
       { Glyphs = [| "Ψ"; "Υ"; "Χ"; "Η" |]; Sounds = [| "ps"; "y"; "kh"; "ē" |]
         Word = "ψυχή"; En = "soul, life, breath"; Note = "You will meet it again in Book II, and in the third line of the Iliad." } |]

let alphaLeaves = 5

// ---------------------------------------------------------------------------
// Sounds — the pitch accent
// ---------------------------------------------------------------------------

/// One syllable of a pitch example: its text, the pitch points it moves through
/// (semitones above the base note; one point = level) and its length in ms.
type Syllable = { T: string; Pitch: float array; Dur: int }

type AccentExample = { Word: string; Label: string; Syllables: Syllable array }

let accents: AccentExample array =
    [| { Word = "τιμή"; Label = "Acute on the last syllable: the voice rises."
         Syllables = [| { T = "τι"; Pitch = [| 0.0 |]; Dur = 260 }; { T = "μή"; Pitch = [| 1.0; 7.0 |]; Dur = 380 } |] }
       { Word = "τιμῆς"; Label = "Circumflex: a rise and fall on one long vowel."
         Syllables = [| { T = "τι"; Pitch = [| 0.0 |]; Dur = 260 }; { T = "μῆς"; Pitch = [| 1.0; 7.0; 0.0 |]; Dur = 540 } |] }
       { Word = "ἄνθρωπος"; Label = "Acute on the first syllable: high, then falling away."
         Syllables = [| { T = "ἄν"; Pitch = [| 7.0 |]; Dur = 260 }; { T = "θρω"; Pitch = [| 3.0 |]; Dur = 320 }; { T = "πος"; Pitch = [| 0.0 |]; Dur = 240 } |] } |]

/// Silence between syllables, in ms, for both playback and the drawn contour.
let syllableGap = 40

/// Total length of an example as played, in ms.
let accentLength (a: AccentExample) : int =
    (a.Syllables |> Array.sumBy (fun y -> y.Dur)) + syllableGap * (a.Syllables.Length - 1)

/// The pitch contour as an SVG path in a 110×50 box (baseline at y = 44).
let contourPath (a: AccentExample) : string =
    let total = float (accentLength a)
    let scale = 102.0 / total
    let y (p: float) = sprintf "%.1f" (44.0 - p * 5.0)
    let mutable x = 4.0
    let sb = System.Text.StringBuilder()
    for syl in a.Syllables do
        let w = float syl.Dur * scale
        if syl.Pitch.Length = 1 then
            sb.Append(sprintf "M%.1f %sL%.1f %s" x (y syl.Pitch.[0]) (x + w) (y syl.Pitch.[0])) |> ignore
        else
            syl.Pitch
            |> Array.iteri (fun k p ->
                let px = x + w * float k / float (syl.Pitch.Length - 1)
                sb.Append(sprintf "%s%.1f %s" (if k = 0 then "M" else "L") px (y p)) |> ignore)
        x <- x + w + float syllableGap * scale
    sb.ToString()

// ---------------------------------------------------------------------------
// Iliad 1.1–5, with glosses
// ---------------------------------------------------------------------------

/// A word of the passage with its gloss. `FirstDecl` marks the first-declension
/// nouns, which the reader underlines with a dotted rule.
type GlossWord = { W: string; Lemma: string; Sense: string; Parse: string; FirstDecl: bool }

let private G w l s p = { W = w; Lemma = l; Sense = s; Parse = p; FirstDecl = false }
let private G1 w l s p = { W = w; Lemma = l; Sense = s; Parse = p; FirstDecl = true }

let iliadLines: GlossWord array array =
    [| [| G "Μῆνιν" "μῆνις, -ιος, ἡ" "wrath, esp. of the gods" "acc. sg."
          G "ἄειδε" "ἀείδω" "sing" "imperative, 2 sg."
          G1 "θεὰ" "θεά, -ᾶς, ἡ" "goddess" "voc. sg."
          G "Πηληϊάδεω" "Πηληϊάδης" "son of Peleus" "gen. sg., Homeric"
          G "Ἀχιλῆος" "Ἀχιλλεύς" "Achilles" "gen. sg." |]
       [| G "οὐλομένην" "οὐλόμενος" "accursed, ruinous" "acc. sg. fem."
          G "ἣ" "ὅς, ἥ, ὅ" "which" "nom. sg. fem."
          G "μυρί᾽" "μυρίος" "countless" "acc. pl. neut."
          G "Ἀχαιοῖς" "Ἀχαιοί" "the Achaeans" "dat. pl."
          G "ἄλγε᾽" "ἄλγος, -εος, τό" "pain, grief" "acc. pl."
          G "ἔθηκε" "τίθημι" "put, cause" "aorist, 3 sg." |]
       [| G "πολλὰς" "πολύς" "many" "acc. pl. fem."
          G "δ᾽" "δέ" "and" "particle"
          G "ἰφθίμους" "ἴφθιμος" "mighty, strong" "acc. pl."
          G1 "ψυχὰς" "ψυχή, -ῆς, ἡ" "soul, life" "acc. pl."
          G "Ἄϊδι" "Ἅιδης" "Hades" "dat. sg."
          G "προΐαψεν" "προϊάπτω" "hurl forth" "aorist, 3 sg." |]
       [| G "ἡρώων" "ἥρως, -ωος, ὁ" "hero" "gen. pl."
          G "αὐτοὺς" "αὐτός" "them, their bodies" "acc. pl. masc."
          G "δὲ" "δέ" "and, but" "particle"
          G "ἑλώρια" "ἑλώριον, τό" "spoil, prey" "acc. pl."
          G "τεῦχε" "τεύχω" "make" "imperfect, 3 sg."
          G "κύνεσσιν" "κύων, κυνός, ὁ" "dog" "dat. pl., Homeric" |]
       [| G "οἰωνοῖσί" "οἰωνός, ὁ" "bird of prey" "dat. pl."
          G "τε" "τε" "and" "particle"
          G "πᾶσι" "πᾶς" "all, every" "dat. pl."
          G "Διὸς" "Ζεύς, Διός, ὁ" "Zeus" "gen. sg."
          G "δ᾽" "δέ" "and" "particle"
          G "ἐτελείετο" "τελείω" "be fulfilled" "imperfect, 3 sg."
          G1 "βουλή" "βουλή, -ῆς, ἡ" "will, plan" "nom. sg." |] |]

let iliadTranslation =
    [ "Sing, goddess, of the wrath of Achilles, Peleus’ son,"
      "the ruinous wrath that laid countless griefs on the Achaeans"
      "and hurled down to Hades many mighty souls"
      "of heroes, and made their bodies spoil for dogs"
      "and all the birds; and the will of Zeus was being fulfilled." ]

/// The Iliad in the reader, for "Read the whole book" (Perseus, tlg0012.tlg001).
let iliadWorkHash = "#tlg0012.tlg001"

// ---------------------------------------------------------------------------
// Myth — Odysseus in the Cyclops' cave
// ---------------------------------------------------------------------------

let mythOptions =
    [| "They feared Poseidon’s anger."
       "They heard that nobody was harming him."
       "Odysseus had already escaped." |]
let mythAnswer = 1
let mythLeaves = 3

// ---------------------------------------------------------------------------
// Onboarding
// ---------------------------------------------------------------------------

let paces = [| "I", "A line", "five minutes a day"; "II", "A page", "fifteen minutes a day"; "III", "A book", "thirty minutes a day" |]
