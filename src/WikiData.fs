module WikiData

open System.Text.RegularExpressions
open Types

// ---------------------------------------------------------------------------
// eras — the era *list* itself comes from the decoded Meta blob (Meta.Eras);
// this holds only the static display glyph and descriptive note per era id.
// ---------------------------------------------------------------------------

let eraLetter: Map<string, string> =
    Map.ofList [
        "archaic", "Α"
        "classical", "Κ"
        "hellenistic", "Ἑ"
        "imperial", "Ῥ"
        "lateantique", "Ὄ"
        "byzantine", "Β"
        "postbyz", "Μ"
    ]

let eraNote: Map<string, string> =
    Map.ofList [
        "archaic",
        "Greek literature begins with poetry. The alphabet, adapted from the Phoenician script in the eighth century BCE, made it possible to write that poetry down. Homer's epics are composed in an artificial poetic language that nobody ever spoke, built up over generations of oral performance, and Hesiod works in the same hexameter verse. The lyric poets sing in their own dialects: Sappho and Alcaeus in the Aeolic of Lesbos, Alcman in Spartan Doric, Archilochus and the writers of elegy and iambus in Ionic. The first Greek prose, written in Ionic, appears late in the period among the philosophers and geographers of Asia Minor."
        "classical",
        "The century and a half from the Persian Wars to the death of Alexander, when Athens set the standard. Attic Greek became the language of tragedy and comedy (though tragic choral songs keep a conventional Doric colouring), of Thucydides, Plato and the orators, and its prose remained the model of \"correct\" Greek for the next two thousand years. Not everyone wrote Attic: Herodotus and the Hippocratic doctors used Ionic, and Pindar's choral odes are in a literary Doric."
        "hellenistic",
        "Alexander's conquests carried Greek from Egypt to Afghanistan, and a shared everyday language, the Koine (\"common\" Greek), gradually replaced the old local dialects. Based mainly on Attic, with a good deal of Ionic mixed in, it was the Greek of government, trade and ordinary life in the kingdoms of Alexander's successors. Alexandria became the capital of learning: its scholars edited Homer, Jewish translators there began the Septuagint, the Greek version of the Hebrew Bible, and poets such as Callimachus and Theocritus wrote for a highly educated audience. Polybius wrote his history in the educated Koine of the day."
        "imperial",
        "Under Roman rule Greek remained the language of the eastern Mediterranean and of educated people across the empire. Koine is the language of the New Testament and of Epictetus' lectures. At the same time, the writers of the \"Second Sophistic\", such as Lucian and Aelius Aristides, deliberately wrote the Attic of five centuries earlier, and Plutarch leaned the same way more moderately. The gap between the Greek people spoke and the Greek they wrote opens here, and it lasted into modern times: only in 1976 did the spoken language (Demotic) become the official language of the Greek state."
        "lateantique",
        "From the founding of Constantinople in 330 to the early seventh century. Christianity reshaped Greek literature: Church Fathers such as Basil, Gregory of Nazianzus and John Chrysostom wrote sermons, letters and theology in a polished classical style, while the last pagan philosophers, above all the Neoplatonist Proclus, still taught in Athens and Alexandria. Procopius wrote a classicising history of Justinian's wars. In speech, the old distinction between long and short vowels had disappeared and the pitch accent had become a stress accent, so pronunciation was moving towards medieval Greek. Poets such as Nonnus still wrote hexameters by the old vowel lengths, but shaped their line endings to suit the new stress."
        "byzantine",
        "Medieval Greek, from the seventh century to the fall of Constantinople in 1453. Educated writers still imitated ancient Attic while the spoken language moved further away from it. Byzantium is the reason classical literature survives at all. From the ninth century scholars recopied the ancient texts in the new minuscule script and compiled reference works such as Photius' Library, a digest of some 280 books he had read, and the tenth-century encyclopaedia known as the Suda. They gathered ancient commentary into the marginal notes known as scholia and added their own. The scholar-copyists of the last Byzantine centuries (the Palaeologan period), among them Maximus Planudes and Demetrius Triclinius, produced many of the manuscripts that modern editions rely on."
        "postbyz",
        "After the fall of Constantinople in 1453, Greek scholars in Italy, some of whom had arrived decades earlier (Manuel Chrysoloras began teaching in Florence in 1397), taught Greek to the Renaissance humanists and helped print the first editions of the classics in Venice, Florence and Milan. Cardinal Bessarion left his great library of Greek manuscripts to Venice. Writing in Greek continued under Ottoman rule, both in the learned language and, increasingly, in the spoken vernacular, and in Venetian-ruled Crete, where Vitsentzos Kornaros wrote the verse romance Erotokritos."
    ]

// ---------------------------------------------------------------------------
// genres — derived from an author's description, occupations and name
// ---------------------------------------------------------------------------

/// (id, full name, short chip label, keywords). The keyword lists are
/// deliberately broad; which genre wins is decided by position (see `genreOf`),
/// not by the order of this list.
let private genreDefs: (string * string * string * Regex) list =
    [ "poets", "Poets & dramatists", "Poetry & drama",
      Regex(@"poet|poem|tragedian|playwright|dramatist|comedian|comic|lyric|epic|hymn|epigram|anthology|orphic|carmina|canticum", RegexOptions.IgnoreCase)
      "philosophers", "Philosophers", "Philosophy",
      Regex(@"philosoph|sophist|stoic|platonist|epicurean|cynic|pythagorean|skeptic|aristotel", RegexOptions.IgnoreCase)
      "historians", "Historians & geographers", "History",
      Regex(@"histor|chronic|chronograph|geograph|biograph", RegexOptions.IgnoreCase)
      "orators", "Orators & rhetoricians", "Oratory",
      Regex(@"orator|rhetor|speech|logographer", RegexOptions.IgnoreCase)
      "scientists", "Physicians, mathematicians & scientists", "Medicine & science",
      Regex(@"physician|medic|surgeon|pharmac|botan|mathemat|geomet|astronom|astrolog|scien|engineer|architect|mechanic|alchem|musicolog|music theor", RegexOptions.IgnoreCase)
      "fiction", "Novelists & satirists", "Novels & satire",
      Regex(@"novel|satir|romance", RegexOptions.IgnoreCase)
      "church", "The Bible & Christian writers", "Bible & Church",
      Regex(@"biblical|bible|testament|septuagint|gospel|apostol|theolog|bishop|priest|monk|saint|patriarch|cleric|apolog|deacon|abbot|hagiograph|church father|martyr|didache|enoch|apocalyp|\bacta\b", RegexOptions.IgnoreCase)
      "scholars", "Grammarians, scholars & lexicographers", "Scholarship",
      Regex(@"grammar|philolog|lexicograph|scholar|librarian|commentat|commentar|scholiast|encyclop|exegesis|critic", RegexOptions.IgnoreCase) ]

type Genre = { Id: string; Name: string; Short: string }

let genres: Genre list = genreDefs |> List.map (fun (id, name, short, _) -> { Id = id; Name = name; Short = short })

/// The genre whose keyword appears *earliest* in the text, if any.
let private earliestGenre (text: string) : string option =
    genreDefs
    |> List.choose (fun (id, _, _, re) ->
        let m = re.Match text
        if m.Success then Some(m.Index, id) else None)
    |> List.sortBy fst
    |> List.tryHead
    |> Option.map snd

/// Which genre an author belongs to, or "other".
///
/// The one-line description is the curated statement of what an author is
/// known for, so it decides first; occupations and then the name are
/// fallbacks. Within a text the earliest keyword wins. Taking the first genre
/// in list order instead filed Plato under poetry (Wikidata also lists him as
/// an epigrammatist) and Galen under philosophy.
let genreOf (m: AuthorMeta) : string =
    [ m.Desc |> Option.defaultValue ""; String.concat " " m.Occ; m.Name ]
    |> List.tryPick earliestGenre
    |> Option.defaultValue "other"

// Meta is fixed once the app has booted, and the sidebar asks for every
// author's genre on each render (which, while reading, means every scroll
// report), so the answers are kept rather than re-run through the regexes.
let private genreCache = System.Collections.Generic.Dictionary<string, string>()

/// `genreOf` for an author id; authors without metadata are "other".
let genreOfAuthor (meta: Meta) (authorId: string) : string =
    match genreCache.TryGetValue authorId with
    | true, g -> g
    | _ ->
        let g =
            match meta.Authors.TryFind authorId with
            | Some m -> genreOf m
            | None -> "other"
        // Before boot Meta is empty; don't pin every author to "other".
        if not meta.Authors.IsEmpty then genreCache.[authorId] <- g
        g

/// The short chip label for a genre id ("Other" for anything unlisted).
let genreShort (id: string) : string =
    genres |> List.tryFind (fun g -> g.Id = id) |> Option.map (fun g -> g.Short) |> Option.defaultValue "Other"

// ---------------------------------------------------------------------------
// editions — publisher-series grouping for the Editions & translations page
// ---------------------------------------------------------------------------

let private seriesDefs: (string * Regex) list =
    [ "Oxford Classical Texts", Regex(@"oxford|clarendon", RegexOptions.IgnoreCase)
      "Teubner", Regex(@"teubner", RegexOptions.IgnoreCase)
      "Loeb Classical Library", Regex(@"loeb|heinemann|harvard university press", RegexOptions.IgnoreCase)
      "Budé (Les Belles Lettres)", Regex(@"belles lettres|bud[eé]", RegexOptions.IgnoreCase)
      "Kühn (Galen)", Regex(@"k[uü]hn", RegexOptions.IgnoreCase)
      "Weidmann", Regex(@"weidmann", RegexOptions.IgnoreCase)
      "Corpus Medicorum Graecorum", Regex(@"corpus medicorum", RegexOptions.IgnoreCase)
      "Migne, Patrologia Graeca", Regex(@"migne|patrologia", RegexOptions.IgnoreCase)
      "Cambridge University Press", Regex(@"cambridge", RegexOptions.IgnoreCase) ]

let series: string list = seriesDefs |> List.map fst

/// The publisher series a text's description belongs to, or "Other".
let seriesOf (desc: string) : string =
    seriesDefs
    |> List.tryFind (fun (_, re) -> re.IsMatch desc)
    |> Option.map fst
    |> Option.defaultValue "Other"

// ---------------------------------------------------------------------------
// static article prose (mirrors GENERAL)
// ---------------------------------------------------------------------------

/// A year range written the way the rest of the site writes it: "480–323 BCE",
/// "31 BCE – 330 CE", with "?" for an end nobody knows.
let yearRange (from: int option) (to_: int option) : string =
    let y (v: int) = if v < 0 then string (-v) + " BCE" else string v + " CE"
    match from, to_ with
    | Some a, Some b when a < 0 && b < 0 -> sprintf "%d–%d BCE" (-a) (-b)
    | Some a, Some b when a > 0 && b > 0 -> sprintf "%d–%d CE" a b
    | Some a, Some b -> y a + " – " + y b
    | Some a, None -> y a + " – ?"
    | None, Some b -> "? – " + y b
    | None, None -> ""

/// An era's span. The first era has no agreed start; like the eras band, it
/// is dated from where the collection starts, Homer, c. 800 BCE.
let eraSpan (e: Era) : string =
    if e.From = -9999 then "c. 800–" + (if e.To < 0 then string (-e.To) + " BCE" else string e.To + " CE")
    elif e.To = 9999 then "from " + (if e.From < 0 then string (-e.From) + " BCE" else string e.From + " CE")
    else yearRange (Some e.From) (Some e.To)

/// Wikidata's one-line descriptions, tidied to the site's style: a capital
/// letter, BCE/CE rather than BC/AD, and no dates in brackets (the dates are
/// shown beside them already).
let cleanDesc (d: string) : string =
    let d = Regex.Replace(d, @"\s*\([^()]*\d[^()]*\)\s*$", "")
    let d = Regex.Replace(d, @"\bBC\b", "BCE")
    let d = Regex.Replace(d, @"\bAD\b", "CE").Trim().TrimEnd('.')
    if d = "" then d else string (System.Char.ToUpper d.[0]) + d.Substring 1

/// The opening of an article for an index card: whole sentences up to about
/// `limit` characters, or, when the first sentence alone is longer, cut at a
/// word with "…". Never ends mid-word or with ".…".
let excerpt (limit: int) (text: string) : string =
    let para = (text.Split([| "\n\n" |], System.StringSplitOptions.RemoveEmptyEntries) |> Array.tryHead |> Option.defaultValue "").Trim()
    let sentences = Regex.Split(para, @"(?<=[.!?])\s+(?=[A-Z\u0370-\u03FF\u1F00-\u1FFF""(])")
    let rec take (acc: string) (rest: string list) =
        match rest with
        | s :: tail when acc.Length < limit / 2 || acc.Length + 1 + s.Length <= limit -> take (if acc = "" then s else acc + " " + s) tail
        | _ -> acc
    let whole = take "" (List.ofArray sentences)
    if whole.Length <= int (float limit * 1.3) then whole
    else
        let cut = whole.Substring(0, limit)
        let at = cut.LastIndexOf ' '
        (if at > limit / 2 then cut.Substring(0, at) else cut).TrimEnd(',', ';', ':', ' ') + "…"

/// One line for each part of the wiki, shared by the wiki's contents and the
/// home page's Wiki card so the two never describe a section differently.
let blurbAuthors (n: int) : string = sprintf "Lives and timelines of %d authors, by era and by genre." n
let blurbEras: string = "From Homeric epic to Byzantine Greek: the periods and their language."
let blurbLife: string = "How people lived, the good and the grim: households, work and money, food and wine, gods and oracles, music and theatre, medicine, games, plague and war."

/// Articles linked under the Everyday life row of the wiki's contents.
let lifeShortcuts: string list = [ "food"; "wine-and-the-symposium"; "medicine"; "mysteries-and-oracles"; "music"; "strange-but-true" ]
let blurbManuscripts: string = "How the texts reached us: papyri, codices and the key witnesses."
let blurbVariants: string = "Lines added later, lines ancient editors doubted, disputed works and other puzzles."
let blurbEditions: string = "The printed edition behind each text here, and the editions scholars cite."
let blurbAbout: string = "The projects, scholars and licences this reader is built on."

let articleIntro (kind: ArticleKind) : string =
    match kind with
    | Manuscripts ->
        "No ancient Greek author's own copy of their work survives. Every text we read reached us through a long chain of copies, in three broad stages. First came papyrus rolls, copied throughout antiquity. The oldest surviving pieces date from the fourth century BCE, and most were found in the rubbish heaps of Roman Egypt or in mummy cases made from recycled papyrus. Next came the parchment books (codices) of late antiquity. Last and most important came the medieval Byzantine manuscripts, mostly from the ninth to the fifteenth century, on which almost every text ultimately depends. In the ninth century scribes switched from capital letters (uncial) to a faster lower-case hand (minuscule), and works that nobody recopied at that point were usually lost for good. The articles below explain, author by author, which manuscripts matter and why."
    | Variants ->
        "Every hand-made copy introduces mistakes, and people also changed texts on purpose. Readers and scribes added lines to explain or expand a passage (interpolation). Ancient critics marked lines they doubted without deleting them (athetesis, signalled by the Alexandrian scholars' marginal sign, the obelos). Scribes \"corrected\" dialect forms to what they expected to see, and whole works were attached to the wrong author. Modern editors rebuild the text by comparing the surviving copies (recension) and, where all of them fail, by informed conjecture (emendation). The articles below set out the main textual questions for each author."

let editionsIntro: string =
    "Every text in this collection comes from a printed critical edition, usually an older one now out of copyright, that the Perseus and First1KGreek projects turned into digital text. The major modern series are the Oxford Classical Texts (OCT); the Teubner series, first published in Leipzig and later in Stuttgart and Berlin; the Loeb Classical Library, with Greek and English on facing pages; the Budé series (Les Belles Lettres), with French; and, for the medical writers, the Corpus Medicorum Graecorum. Below, the sources of this collection are grouped by series, and each author article names the edition scholars currently treat as standard."

// ---------------------------------------------------------------------------
// articles on single works — shown on the author page, linked from the reader
// ---------------------------------------------------------------------------

type WorkArticle =
    { Title    : string
      Grc      : string
      /// Opening words in Greek, with a translation.
      Epigraph : string * string
      /// (optional heading, paragraphs)
      Sections : (string option * string list) list }

let workArticles: Map<string, WorkArticle> =
    Map.ofList [
        "tlg0012.tlg001",
        { Title = "Iliad"
          Grc = "Ἰλιάς"
          Epigraph = "μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος", "Sing, goddess, the wrath of Achilles, son of Peleus."
          Sections =
            [ None,
              [ "The Iliad opens with a single word, μῆνιν (mēnin, \"wrath\"), and everything that follows grows out of it. The Greeks have been besieging Troy (Ilion, which gives the poem its name) for nine years, and the story covers only about fifty days of the tenth, with the fighting packed into four of them. Agamemnon, leader of the Greek army, is forced to give back his captive Chryseis to appease Apollo, and takes the captive woman Briseis from Achilles, his best warrior, in her place. Achilles withdraws from the fighting in anger, and the Trojans, led by Hector, drive the Greeks back to their ships. The poem follows what that anger costs: first the Greeks, then Achilles' closest companion Patroclus, killed by Hector, and finally Hector himself, killed by Achilles in revenge."
                "The Iliad does not tell the whole war. The Judgement of Paris is mentioned only once, near the very end (24.29–30). The wooden horse and the fall of Troy lie outside the poem, though Hector's death foreshadows the city's. Those stories belonged to a wider tradition that audiences already knew, later gathered in the poems of the Epic Cycle. The Iliad ends quietly instead. In the last book the old king Priam, guided by the god Hermes, crosses the Greek camp at night to Achilles' hut to ransom his son's body, and the two enemies weep together. The poem closes with Hector's funeral." ]
              Some "The shape of the poem",
              [ "The 24 books fall into a few large movements, and a first-time reader can find their way by them. Book 1 is the quarrel. Book 2 musters the armies, ending with the famous Catalogue of Ships. Book 3 brings Helen onto the walls of Troy to name the Greek heroes for Priam, and Book 6, where Hector says goodbye to his wife Andromache and their baby son, is often read on its own. In Book 9 the Greeks send an embassy begging Achilles to return, and he refuses. Book 16 sends Patroclus into battle in Achilles' armour. Book 18 describes the new shield the god Hephaestus makes for Achilles, a whole world in miniature. Book 22 is the death of Hector, Book 23 the funeral games for Patroclus, and Book 24 the meeting of Priam and Achilles." ]
              Some "How it was made",
              [ "The poem runs to about 15,700 lines of dactylic hexameter. It is the product of generations of singers who composed as they performed. In the 1920s and 30s Milman Parry, and after him his student Albert Lord, showed that the repeated phrases, such as πόδας ὠκὺς Ἀχιλλεύς (podas ōkys Achilleus, \"swift-footed Achilles\"), are the working tools of this oral craft, each shaped to fill a fixed stretch of the line, rather than lapses of style. How and why an oral poem came to be written down is still debated. Its division into 24 books, each named by a letter of the Ionic alphabet, is usually credited to scholars at Alexandria, though some think it older." ]
              Some "Its afterlife",
              [ "According to Athenian tradition, the poems were recited in full at the Panathenaia festival. By Plato's day Homer's admirers could claim that he had \"educated Greece\" (τὴν Ἑλλάδα πεπαίδευκεν, Republic 606e), a claim Socrates goes on to challenge. Plutarch (Alexander 8) tells us that Alexander took on campaign a copy corrected by Aristotle and kept it under his pillow. At the Library of Alexandria, Zenodotus, Aristophanes of Byzantium and then Aristarchus produced the first critical editions; how their work reached us is told under How the text survived, below. The Greek you read here is Monro and Allen's Oxford edition (3rd ed., 1920)." ]
              Some "Reading it in Greek",
              [ "Homer is hard at first because the word forms are unfamiliar, but the poem rewards persistence faster than almost any other Greek text, because it repeats itself. The formulas that puzzle you in Book 1 will be old friends by Book 3. A few habits account for much of the strangeness: past tenses often drop the augment (βῆ, bē, for ἔβη, \"he went\"), genitives may end in -οιο (-oio) or -αο (-ao), and ὁ, ἡ, τό is usually a pronoun (\"he, she, it\") rather than \"the\". Click or tap any word for a dictionary entry, and try the Meter lens: the hexameter is easier to feel when you hear it." ] ] }
    ]

// ---------------------------------------------------------------------------
// the Study page's introduction (the word μάθησις and what the philosophers
// made of learning) and the wiki's (why an open reference matters)
// ---------------------------------------------------------------------------

/// A run of the introduction's prose. Greek runs get the Greek face and
/// `lang="grc"`; a passage run links into the reader (`Chunk` only for works
/// read part by part, where the reference alone does not say which part).
type IntroRun =
    | Plain of string
    | Greek of string
    | Title of string
    | Passage of label: string * workId: string * chunk: string option * ref: string

type IntroQuote =
    { Grc: string; En: string; Who: string; WorkId: string; Chunk: string option; Ref: string }

type IntroWord = { Grc: string; Translit: string; Gloss: string }

type WikiIntro =
    { Lead: IntroRun list
      Philosophy: IntroRun list
      WhyWiki: IntroRun list
      Living: IntroRun list
      Family: IntroWord list
      Quotes: IntroQuote list }

/// Every Greek quotation and reference here was checked against the Perseus
/// TEI the reader loads (Phaedo 72e, Meno 81d, Metaphysics 980a21, Poetics 4 =
/// 1448b13–14, Agamemnon 177–178), and each link was click-tested to land on it.
/// The English here is our own translation, labelled as such on the page; the
/// reader shows each text with the published translation from the collection.
/// A trailing * marks a rendering scholars dispute (κυρίως ἔχειν at Ag. 178).
/// The Poetics lines are placed from Perseus' five-line markers (l.15 begins at
/// κοινωνοῦσιν).
let wikiIntro: WikiIntro =
    { Lead =
        [ Greek "Μάθησις"
          Plain " (máthēsis) is the Greek word for learning: not the lesson on the page but the act of taking it in, the slow work of making something your own. It comes from the verb "
          Greek "μανθάνω"
          Plain " (manthánō), \"I learn\", and the ending "
          Greek "-σις"
          Plain "\u00a0(\u2011sis) turns that action into a noun, as "
          Greek "ποίησις"
          Plain " (poíēsis) is \"making\". This page is for the act itself: a guide that starts from the letters, and short exercises to practise what it teaches." ]
      Philosophy =
        [ Plain "The philosophers asked what learning is. In Plato's "
          Title "Meno"
          Passage("81d", "tlg0059.tlg024", None, "81d")
          Plain ", Socrates suggests that the soul has already seen everything, so that what people call learning is really recollection, "
          Greek "ἀνάμνησις"
          Plain " (anámnēsis). In the "
          Title "Phaedo"
          Passage("72e", "tlg0059.tlg004", None, "72e")
          Plain " his friend Cebes repeats it as an argument Socrates often made. Aristotle opens the "
          Title "Metaphysics"
          Passage("980a21", "tlg0086.tlg025", Some "1", "1.1")
          Plain " with the claim that all people by nature desire to know, and in the "
          Title "Poetics"
          Passage("1448b13–14", "tlg0086.tlg034", Some "4", "4.4")
          Plain " he says that learning is the most pleasant of things, for philosophers and everyone else alike. Earlier still, the chorus of Aeschylus' "
          Title "Agamemnon"
          Passage("177", "tlg0085.tlg005", None, "177")
          Plain " sings of Zeus, who made it law that we learn by suffering: "
          Greek "πάθει μάθος"
          Plain " (páthei máthos). Reading Greek asks for less suffering than Aeschylus had in mind: only a little, every day." ]
      WhyWiki =
        [ Plain "Greek literature did not survive by itself. It lasted because readers kept explaining it and copying it. Scholars at Alexandria edited Homer and wrote commentaries on him; the margins of Byzantine manuscripts carry notes, the "
          Greek "σχόλια"
          Plain " (skhólia), distilled from commentaries like theirs; and in the tenth century the "
          Title "Suda"
          Plain " gathered what was known into an encyclopaedia of about 31,000 entries. A text that no one can explain is soon a text that no one copies. An open reference that anyone can use is the same work in its modern form, and it is what this wiki is for: who wrote each text and when, how it reached us, which editions to trust, and how the people in it lived." ]
      Living =
        [ Plain "Greek has been written for well over three thousand years, from the Linear B tablets of the Bronze Age palaces to the Greek spoken today, and English still takes from it its words for learning itself: "
          Title "mathematics"
          Plain ", from "
          Greek "μάθημα"
          Plain ", and "
          Title "philosophy"
          Plain ", "
          Title "history"
          Plain " and "
          Title "music"
          Plain ". Every reader who learns even a little of it keeps that line unbroken." ]
      Family =
        [ { Grc = "μανθάνω"; Translit = "manthánō"; Gloss = "I learn, I come to understand" }
          { Grc = "μάθησις"; Translit = "máthēsis"; Gloss = "learning, the act of learning" }
          { Grc = "μάθημα"; Translit = "máthēma"; Gloss = "what is learned, a lesson; in the plural, the branches of study, whence mathematics" }
          { Grc = "μαθητής"; Translit = "mathētḗs"; Gloss = "a pupil, a disciple" }
          { Grc = "μάθος"; Translit = "máthos"; Gloss = "learning, a poetic word, as in πάθει μάθος" } ]
      Quotes =
        [ { Grc = "ἡμῖν ἡ μάθησις οὐκ ἄλλο τι ἢ ἀνάμνησις τυγχάνει οὖσα"
            En = "For us, learning is in fact nothing other than recollection."
            Who = "Plato, Phaedo 72e"
            WorkId = "tlg0059.tlg004"; Chunk = None; Ref = "72e" }
          { Grc = "μανθάνειν οὐ μόνον τοῖς φιλοσόφοις ἥδιστον ἀλλὰ καὶ τοῖς ἄλλοις ὁμοίως"
            En = "Learning is most pleasant, not only to philosophers but to everyone else as well."
            Who = "Aristotle, Poetics 1448b13–14"
            WorkId = "tlg0086.tlg034"; Chunk = Some "4"; Ref = "4.4" }
          { Grc = "τὸν πάθει μάθος θέντα κυρίως ἔχειν"
            En = "Zeus, who laid down \"learning through suffering\" to hold with full authority.*"
            Who = "Aeschylus, Agamemnon 177–178"
            WorkId = "tlg0085.tlg005"; Chunk = None; Ref = "177" } ] }