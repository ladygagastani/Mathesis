module Lenses.Prosody

// Scansion of Greek verse from the printed text alone — no dictionary of vowel
// quantities, so α, ι and υ that carry no circumflex or iota subscript are left
// open and the meter decides them. That is how a reader scans too, and it is
// enough for dactylic hexameter, the elegiac pentameter and the iambic trimeter
// of drama. Lyric metres are not attempted: without a colometry they have no
// fixed pattern to fit, and a wrong scansion is worse than none.

open System.Collections.Generic

/// What the letters alone say about a syllable's weight.
type Weight =
    | Heavy          // long by nature or by position — cannot be short
    | Light          // short vowel, at most one consonant after it
    | Open           // α ι υ with nothing to fix their length
    | Correptable    // long vowel or diphthong before another vowel: usually long, may shorten

/// Pitch accent of a syllable, for the rhythm player. Dionysius of
/// Halicarnassus (De comp. verb. 11) puts the acute a fifth above the level
/// pitch; the circumflex rises and falls within one syllable.
type Pitch =
    | Level
    | Rise
    | RiseFall

type Syl =
    { Word  : int            // index into the line's Greek words
      Text  : string         // the letters of this syllable, as printed (NFC)
      Weight: Weight
      Pitch : Pitch
      WordEnd: bool          // last syllable of its word
      Merged: bool           // two vowels read as one (synizesis)
      Span  : int }          // how many plain syllables it stands for (2 when merged)

type Scansion =
    { Meter   : string
      Long    : bool array             // per syllable: scanned long?
      FootEnd : bool array             // a foot (or metron) boundary falls after this syllable
      Caesura : (int * string) option  // after which syllable, and its name
      Bucolic : int option             // bucolic diaeresis after this syllable
      Cost    : int }                  // 0 = no licence needed; each correption/resolution adds

type LineScan =
    { Syls   : Syl array
      /// Per Greek word of the line: its pieces, each with the syllable it
      /// belongs to (None for a word with no vowel, e.g. elided δ’).
      Pieces : (string * int option) list array
      Scan   : Scansion option }

// ---------------------------------------------------------------------------
// letters
// ---------------------------------------------------------------------------

type private Letter = { Orig: string; Base: char; Marks: string }

let private ACUTE = '́'
let private GRAVE = '̀'
let private CIRC = '͂'
let private DIAER = '̈'
let private SMOOTH = '̓'
let private ROUGH = '̔'
let private IOTASUB = 'ͅ'

let private vowels = set [ 'α'; 'ε'; 'η'; 'ι'; 'ο'; 'υ'; 'ω' ]
let private isVowel (c: char) = vowels.Contains c
let private isConsonant (c: char) = c >= 'α' && c <= 'ω' && not (isVowel c) || c = 'ς'
let private doubles = set [ 'ζ'; 'ξ'; 'ψ' ]
let private mutes = set [ 'β'; 'γ'; 'δ'; 'θ'; 'κ'; 'π'; 'τ'; 'φ'; 'χ' ]
let private liquids = set [ 'λ'; 'ρ'; 'μ'; 'ν' ]
let private diphthongs = set [ "αι"; "ει"; "οι"; "υι"; "αυ"; "ευ"; "ηυ"; "ου"; "ωυ" ]

let private letters (word: string) : Letter list =
    let d = word.Normalize(System.Text.NormalizationForm.FormD)
    let out = ResizeArray<Letter>()
    for ch in d do
        if ch >= '̀' && ch <= 'ͯ' then
            if out.Count > 0 then
                let l = out.[out.Count - 1]
                out.[out.Count - 1] <- { l with Orig = l.Orig + string ch; Marks = l.Marks + string ch }
        elif ch = '’' || ch = '\'' || ch = 'ʼ' then
            // Elision: the vowel is already gone; the mark itself stays with the
            // word for display but is not a letter.
            if out.Count > 0 then
                let l = out.[out.Count - 1]
                out.[out.Count - 1] <- { l with Orig = l.Orig + string ch }
        else
            let lower = System.Char.ToLowerInvariant ch
            out.Add { Orig = string ch; Base = (if lower = 'ς' then 'σ' else lower); Marks = "" }
    List.ofSeq out

let private has (m: char) (l: Letter) = l.Marks.IndexOf m >= 0

/// Nuclei of one word, as (first letter, last letter) index pairs.
let private nuclei (ls: Letter array) : (int * int) list =
    let out = ResizeArray<int * int>()
    let mutable i = 0
    while i < ls.Length do
        let l = ls.[i]
        if isVowel l.Base then
            let joins =
                i + 1 < ls.Length
                && isVowel ls.[i + 1].Base
                && diphthongs.Contains(System.String [| l.Base; ls.[i + 1].Base |])
                && not (has DIAER ls.[i + 1])
                // Breathing and accent sit on the *second* vowel of a diphthong;
                // on the first they mean two syllables (ὀΐω, ἀΰτμή).
                && not (has ACUTE l || has CIRC l || has GRAVE l || has SMOOTH l || has ROUGH l)
                && not (has IOTASUB l)
            if joins then
                out.Add(i, i + 1)
                i <- i + 2
            else
                out.Add(i, i)
                i <- i + 1
        else
            i <- i + 1
    List.ofSeq out

let private consonantValue (l: Letter) = if doubles.Contains l.Base then 2 else 1

// ---------------------------------------------------------------------------
// syllables of a line
// ---------------------------------------------------------------------------

type private Nuc =
    { W: int; A: int; B: int }     // word index, first/last letter of the nucleus

/// Splits a line (given as its Greek words) into syllables with weights, and
/// each word into display pieces aligned to those syllables.
let syllables (words: string array) : Syl array * (string * int option) list array =
    let ws = words |> Array.map (letters >> Array.ofList)
    let nucs =
        ws
        |> Array.mapi (fun wi ls -> nuclei ls |> List.map (fun (a, b) -> { W = wi; A = a; B = b }))
        |> List.concat
        |> Array.ofList
    // Consonants between nucleus k and the next one, across word ends; also
    // whether they sit inside one word (muta cum liquida is only "common" there).
    let following (k: int) : Letter list * bool =
        let n = nucs.[k]
        let stopW, stopA =
            if k + 1 < nucs.Length then nucs.[k + 1].W, nucs.[k + 1].A else ws.Length - 1, (Array.length ws.[ws.Length - 1])
        let acc = ResizeArray<Letter>()
        let mutable w = n.W
        let mutable i = n.B + 1
        while w < stopW || (w = stopW && i < stopA) do
            if i >= ws.[w].Length then
                w <- w + 1
                i <- 0
            else
                if isConsonant ws.[w].[i].Base then acc.Add ws.[w].[i]
                i <- i + 1
        List.ofSeq acc, (k + 1 < nucs.Length && nucs.[k + 1].W = n.W)
    let syls =
        nucs
        |> Array.mapi (fun k n ->
            let ls = ws.[n.W]
            let first = ls.[n.A]
            let last = ls.[n.B]
            let isDiph = n.B > n.A
            let natural =
                if isDiph then Heavy
                else
                    match first.Base with
                    | 'η' | 'ω' -> Heavy
                    | 'ε' | 'ο' -> Light
                    | _ -> if has CIRC first || has IOTASUB first then Heavy else Open
            // Mute + liquid leaves the syllable before it free — inside a word
            // (πατρός) and, in Homer, across a word end too (γε πρίν).
            let cons, _sameWord = following k
            let count = cons |> List.sumBy consonantValue
            let common =
                count = 2
                && (match cons with
                    | [ a; b ] -> mutes.Contains a.Base && liquids.Contains b.Base
                    | _ -> false)
            let beforeVowel = count = 0 && k + 1 < nucs.Length
            let weight =
                if count >= 2 && not common then Heavy
                elif common then (if natural = Heavy then Heavy else Open)
                else
                    match natural with
                    | Heavy when beforeVowel -> Correptable
                    | w -> w
            let pitchOf (l: Letter) = if has CIRC l then RiseFall elif has ACUTE l then Rise else Level
            let pitch =
                match pitchOf last with
                | Level -> pitchOf first
                | p -> p
            let isLastInWord = k + 1 >= nucs.Length || nucs.[k + 1].W <> n.W
            { Word = n.W; Text = ""; Weight = weight; Pitch = pitch; WordEnd = isLastInWord; Merged = false; Span = 1 })
    // Display pieces: onset consonants go with the syllable they begin, one
    // consonant of a cluster stays behind (two, for mute + liquid, both go on).
    let pieces =
        ws
        |> Array.mapi (fun wi ls ->
            let wn = nucs |> Array.mapi (fun k n -> k, n) |> Array.filter (fun (_, n) -> n.W = wi)
            if wn.Length = 0 then
                [ ((ls |> Array.map (fun l -> l.Orig) |> String.concat ""), None) ]
            else
                let starts =
                    wn
                    |> Array.mapi (fun j (_, n) ->
                        if j = 0 then 0
                        else
                            let prevEnd = (snd wn.[j - 1]).B
                            let between = n.A - prevEnd - 1
                            if between <= 1 then prevEnd + 1
                            elif between = 2 && mutes.Contains ls.[prevEnd + 1].Base && liquids.Contains ls.[prevEnd + 2].Base then prevEnd + 1
                            else prevEnd + 2)
                wn
                |> Array.mapi (fun j (k, _) ->
                    let a = starts.[j]
                    let b = if j + 1 < starts.Length then starts.[j + 1] - 1 else ls.Length - 1
                    let text = ls.[a..b] |> Array.map (fun l -> l.Orig) |> String.concat ""
                    text.Normalize(System.Text.NormalizationForm.FormC), Some k)
                |> List.ofArray)
    let syls =
        syls
        |> Array.mapi (fun k s ->
            let text =
                pieces.[s.Word] |> List.tryPick (fun (t, ko) -> if ko = Some k then Some t else None) |> Option.defaultValue ""
            { s with Text = text })
    syls, pieces

// ---------------------------------------------------------------------------
// fitting a metre
// ---------------------------------------------------------------------------

type private Elem =
    | EL           // longum
    | ES           // breve
    | EX           // anceps
    | EB           // biceps: ∪∪ or —
    | EF           // final syllable, indifferent

type private Meter =
    { Name: string
      Elems: Elem array
      Resolve: bool                // a longum/anceps may be two shorts (drama)
      FootAfter: Set<int>          // element indices a foot ends on
      SpondeeCost: Map<int, int> } // extra cost for a spondee at this biceps

let private hexameter =
    { Name = "dactylic hexameter"
      Elems = [| EL; EB; EL; EB; EL; EB; EL; EB; EL; EB; EL; EF |]
      Resolve = false
      FootAfter = set [ 1; 3; 5; 7; 9 ]
      // A spondee in the fifth foot happens (versus spondeiacus) but rarely.
      SpondeeCost = Map.ofList [ (9, 2) ] }

let private pentameter =
    { Name = "elegiac pentameter"
      Elems = [| EL; EB; EL; EB; EL; EL; ES; ES; EL; ES; ES; EF |]
      Resolve = false
      FootAfter = set [ 1; 3; 4; 7; 10 ]
      SpondeeCost = Map.empty }

let private trimeter =
    { Name = "iambic trimeter"
      Elems = [| EX; EL; ES; EL; EX; EL; ES; EL; EX; EL; ES; EF |]
      Resolve = true
      FootAfter = set [ 3; 7 ]
      SpondeeCost = Map.empty }

let private meters = [ hexameter; pentameter; trimeter ]

let private INF = 1000

/// A light syllable read long costs this much when lengthening is allowed at
/// all — enough that any reading without it wins.
let private LENGTHEN = 3

let private costLongWith (lengthen: bool) (w: Weight) = if w = Light then (if lengthen then LENGTHEN else INF) else 0
let private costShort (w: Weight) =
    match w with
    | Heavy -> INF
    | Correptable -> 1
    | _ -> 0

/// Least-licence fit of the syllables to a metre, or None. Returns per
/// syllable its quantity and the element index it realised.
let private fit (lengthen: bool) (m: Meter) (syls: Syl array) : (int * bool array * int array) option =
    let costLong = costLongWith lengthen
    let n = syls.Length
    let ne = m.Elems.Length
    let memo = Dictionary<int, (int * (bool list * int list))>()
    let rec go (si: int) (ei: int) : int * (bool list * int list) =
        if ei = ne then
            (if si = n then 0 else INF), ([], [])
        elif si >= n then
            INF, ([], [])
        else
            let key = si * 64 + ei
            match memo.TryGetValue key with
            | true, v -> v
            | _ ->
                let w i = syls.[i].Weight
                let options = ResizeArray<int * int * bool list>()   // cost, syllables used, quantities
                let one c q = options.Add(c, 1, [ q ])
                let two c = if si + 1 < n then options.Add(c + costShort (w si) + costShort (w (si + 1)), 2, [ false; false ])
                match m.Elems.[ei] with
                | EL ->
                    one (costLong (w si)) true
                    if m.Resolve then two 2
                | ES -> one (costShort (w si)) false
                | EX ->
                    let cl = costLong (w si)
                    let cs = costShort (w si)
                    if cl <= cs then one cl true else one cs false
                    if m.Resolve then two 2
                | EB ->
                    two 0
                    one (costLong (w si) + (m.SpondeeCost.TryFind ei |> Option.defaultValue 0)) true
                | EF -> one 0 (w si <> Light)
                let mutable best = INF, ([], [])
                for (c, used, qs) in options do
                    if c < INF then
                        let rc, (rq, re) = go (si + used) (ei + 1)
                        if c + rc < fst best then
                            best <- c + rc, (qs @ rq, List.replicate used ei @ re)
                memo.[key] <- best
                best
    let cost, (qs, es) = go 0 0
    if cost >= INF then None else Some(cost, Array.ofList qs, Array.ofList es)

let private scanWithLicence (lengthen: bool) (m: Meter) (syls: Syl array) : Scansion option =
    fit lengthen m syls
    |> Option.map (fun (cost, qs, es) ->
        let n = syls.Length
        let lastOfElem e = es |> Array.tryFindIndexBack (fun x -> x = e)
        let footEnd = Array.init n (fun i -> i < n - 1 && m.FootAfter.Contains es.[i] && es.[i + 1] <> es.[i])
        let wordEndAt i = i >= 0 && i < n - 1 && syls.[i].WordEnd
        let caesura, bucolic =
            if m.Name = hexameter.Name then
                // Third foot: elements 4 (longum) and 5 (biceps).
                let firstOf5 = es |> Array.tryFindIndex (fun x -> x = 5)
                let isDactyl5 = (es |> Array.filter (fun x -> x = 5)).Length = 2
                let c =
                    match lastOfElem 4, firstOf5 with
                    | Some l4, _ when wordEndAt l4 -> Some(l4, "penthemimeral caesura")
                    | _, Some f5 when isDactyl5 && wordEndAt f5 -> Some(f5, "trochaic caesura")
                    | _ ->
                        match lastOfElem 6 with
                        | Some l6 when wordEndAt l6 -> Some(l6, "hephthemimeral caesura")
                        | _ -> None
                let b =
                    match lastOfElem 7 with
                    | Some l7 when (es |> Array.filter (fun x -> x = 7)).Length = 2 && wordEndAt l7 -> Some l7
                    | _ -> None
                c, b
            elif m.Name = pentameter.Name then
                (lastOfElem 4 |> Option.map (fun i -> i, "diaeresis")), None
            else
                // Trimeter: after the fifth element (penthemimeral) or seventh.
                let c =
                    match lastOfElem 4, lastOfElem 6 with
                    | Some a, _ when wordEndAt a -> Some(a, "penthemimeral caesura")
                    | _, Some b when wordEndAt b -> Some(b, "hephthemimeral caesura")
                    | _ -> None
                c, None
        { Meter = m.Name; Long = qs; FootEnd = footEnd; Caesura = caesura; Bucolic = bucolic; Cost = cost })

let private scanWith (m: Meter) (syls: Syl array) = scanWithLicence false m syls

/// Reads two adjacent vowels as one (synizesis) — only where nothing stands
/// between them inside a word and the first is ε, which is where Homer and
/// the tragedians actually do it (θεός, πόλεως, Πηληϊάδεω).
let private merge (syls: Syl array) (k: int) : Syl array =
    let a = syls.[k]
    let b = syls.[k + 1]
    let merged = { a with Text = a.Text + b.Text; Weight = (if b.Weight = Correptable then Correptable else Heavy); WordEnd = b.WordEnd; Merged = true; Span = a.Span + b.Span; Pitch = (if a.Pitch = Level then b.Pitch else a.Pitch) }
    Array.concat [ syls.[.. k - 1]; [| merged |]; syls.[k + 2 ..] ]

let private mergeable (syls: Syl array) : int list =
    [ for k in 0 .. syls.Length - 2 do
          let a = syls.[k]
          if a.Word = syls.[k + 1].Word && not a.WordEnd && a.Text.Length > 0 then
              let tail = Lenses.Greek.fold a.Text
              if tail.EndsWith "ε" && Lenses.Greek.fold(syls.[k + 1].Text).Length > 0 && "αεηιουω".IndexOf((Lenses.Greek.fold syls.[k + 1].Text).[0]) >= 0 then
                  yield k ]

/// Which metres a passage is written in, judged from a sample of its lines.
/// Elegiacs come back as hexameter + pentameter; anything that fits none
/// (lyric, or prose laid out as lines) comes back empty.
let detect (lines: string list) : string list =
    let sample = lines |> List.filter (fun l -> l.Trim() <> "") |> List.truncate 40
    if sample.IsEmpty then []
    else
        let fits (m: Meter) =
            sample |> List.filter (fun l -> (syllables (Interop.greekWords l) |> fst |> scanWith m).IsSome) |> List.length
        let scores = meters |> List.map (fun m -> m, fits m)
        let total = sample.Length
        let hex = scores |> List.find (fun (m, _) -> m.Name = hexameter.Name) |> snd
        let pen = scores |> List.find (fun (m, _) -> m.Name = pentameter.Name) |> snd
        let tri = scores |> List.find (fun (m, _) -> m.Name = trimeter.Name) |> snd
        if hex * 10 >= total * 7 then [ hexameter.Name ]
        elif hex * 10 >= total * 3 && pen * 10 >= total * 3 then [ hexameter.Name; pentameter.Name ]
        elif tri * 10 >= total * 5 then [ trimeter.Name ]
        elif hex * 10 >= total * 4 then [ hexameter.Name ]
        else []

/// Scans one line against the metres the passage was judged to be in,
/// allowing up to two synizeses where the plain reading will not fit.
let private scanLineUncached (meterNames: string list) (words: string array) : LineScan =
    let syls, pieces = syllables words
    let candidates = meters |> List.filter (fun m -> List.contains m.Name meterNames)
    let tryAll (lengthen: bool) (ss: Syl array) =
        candidates |> List.choose (fun m -> scanWithLicence lengthen m ss) |> List.sortBy (fun s -> s.Cost) |> List.tryHead
    // Plainest reading first: as written, then with one or two synizeses, and
    // only then allowing a short syllable to be lengthened — the licence Homer
    // takes where a digamma has dropped out of the spelling (ὃς ᾔδη, ἔδεισεν).
    let attempt (lengthen: bool) =
        match tryAll lengthen syls with
        | Some s -> Some(syls, s)
        | None ->
            let once = mergeable syls |> List.tryPick (fun k -> let ss = merge syls k in tryAll lengthen ss |> Option.map (fun s -> ss, s))
            match once with
            | Some r -> Some r
            | None when lengthen -> None
            | None ->
                mergeable syls
                |> List.tryPick (fun k ->
                    let ss = merge syls k
                    mergeable ss |> List.tryPick (fun k2 -> let ss2 = merge ss k2 in tryAll lengthen ss2 |> Option.map (fun s -> ss2, s)))
    let result = attempt false |> Option.orElseWith (fun () -> attempt true)
    match result with
    | Some(ss, scan) ->
        // Re-point the display pieces at the merged syllable numbering.
        if ss.Length = syls.Length then
            { Syls = ss; Pieces = pieces; Scan = Some scan }
        else
            let mapping = ss |> Array.mapi (fun j s -> Array.replicate s.Span j) |> Array.concat
            // Two pieces now pointing at one merged syllable become one piece.
            let collapse (ps: (string * int option) list) =
                ps
                |> List.fold
                    (fun acc (t, ko) ->
                        match acc, ko with
                        | (t0, Some k0) :: rest, Some k when k = k0 -> (t0 + t, Some k0) :: rest
                        | _ -> (t, ko) :: acc)
                    []
                |> List.rev
            let pieces2 = pieces |> Array.map (List.map (fun (t, ko) -> t, ko |> Option.map (fun k -> mapping.[k])) >> collapse)
            { Syls = ss; Pieces = pieces2; Scan = Some scan }
    | None -> { Syls = syls; Pieces = pieces; Scan = None }

let private scanCache = Dictionary<string, LineScan>()

/// `scanLineUncached`, remembered. The reader re-renders on every scroll
/// report and a page holds hundreds of lines; a line's scansion never
/// changes, so it is worked out once.
let scanLine (meterNames: string list) (words: string array) : LineScan =
    let key = String.concat "," meterNames + "|" + String.concat " " words
    match scanCache.TryGetValue key with
    | true, v -> v
    | _ ->
        let v = scanLineUncached meterNames words
        if scanCache.Count > 40000 then scanCache.Clear()
        scanCache.[key] <- v
        v

/// Notation for one scanned syllable.
let mark (long: bool) = if long then "—" else "∪"

/// Why a syllable came out long or short, in a phrase.
let reason (s: Syl) (long: bool) : string =
    match s.Weight, long with
    | Heavy, _ -> if s.Merged then "two vowels read as one (synizesis)" else "long by nature or by position"
    | Light, true -> "short by spelling, lengthened by the metre — in Homer usually a trace of a lost digamma (ϝ)"
    | Light, false -> "short vowel, not followed by two consonants"
    | Open, true -> "α/ι/υ — long here, as the metre requires"
    | Open, false -> "α/ι/υ — short here, as the metre requires"
    | Correptable, true -> "long vowel before a vowel, kept long"
    | Correptable, false -> "long vowel shortened before a vowel (correption)"
