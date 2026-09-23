module Lenses.Greek

open System.Text.RegularExpressions
open Types

/// Accent-, breathing- and case-insensitive form of a Greek word, with final
/// sigma folded into medial: ἀρετῆς → αρετησ. Every lens that compares words
/// compares them in this form, so "the same word" means the same thing in each.
let fold (w: string) : string =
    let d = w.Normalize(System.Text.NormalizationForm.FormD)
    Regex.Replace(d, "[̀-ͯͅ’'ʼ]", "").ToLowerInvariant().Replace("ς", "σ")

/// The Greek words of a string, in order, as printed.
let words (s: string) : string array = Interop.greekWords s

/// The plain text of a block list, speakers and headings left out — they are
/// apparatus, not the author's words.
let blockText (blocks: Block list) : string =
    blocks
    |> List.map (function
        | Heading _ -> ""
        | Prose(_, t) -> t
        | Verse(_, lines) -> lines |> List.map snd |> String.concat " ")
    |> String.concat " "

/// Segment ref from a raw TEI segment's key.
let refOf (s: RawSegment) : string = String.concat "." s.Key

/// Which work a CTS URN belongs to: urn:cts:greekLit:tlg0012.tlg001.perseus-grc2 → tlg0012.tlg001
let workOfUrn (urn: string) : string =
    let tail = urn.Split(':') |> Array.last
    let parts = tail.Split('.')
    if parts.Length >= 2 then parts.[0] + "." + parts.[1] else tail

/// Very frequent function words. A repeated phrase made only of these (καὶ
/// τὸν μὲν…) says nothing about borrowing, so the echo index ignores it.
let stopwords : Set<string> =
    set [
        "και"; "δε"; "τε"; "γαρ"; "μεν"; "ουν"; "αλλα"; "αλλ"; "ου"; "ουκ"; "ουχ"; "μη"; "ωσ"; "εν"; "εισ"; "εσ"
        "εκ"; "εξ"; "επι"; "επ"; "εφ"; "προσ"; "απο"; "απ"; "αφ"; "κατα"; "κατ"; "καθ"; "μετα"; "μετ"; "μεθ"
        "παρα"; "παρ"; "περι"; "υπο"; "υπ"; "υφ"; "δια"; "δι"; "ανα"; "αν"; "συν"; "ο"; "η"; "το"; "οι"; "αι"
        "τα"; "τον"; "την"; "του"; "τησ"; "τω"; "τη"; "των"; "τοισ"; "ταισ"; "τουσ"; "τασ"; "τι"; "τισ"
        "γε"; "δη"; "περ"; "αρα"; "αρ"; "ρα"; "κε"; "κεν"; "εστι"; "εστιν"; "ην"; "ει"; "εαν"; "οτι"; "ωσπερ"
        "αυτοσ"; "αυτον"; "αυτου"; "αυτω"; "αυτην"; "αυτησ"; "αυτων"; "αυτοισ"; "ουτοσ"; "τουτο"; "ταυτα"
        "εγω"; "συ"; "μοι"; "σοι"; "οι"; "με"; "σε"; "μιν"; "νιν"; "ημεισ"; "υμεισ"; "οσ"; "ον"; "ου"; "ων"
    ]

/// A word that can carry a match: long enough and not a function word.
let isContent (folded: string) : bool = folded.Length >= 4 && not (stopwords.Contains folded)
