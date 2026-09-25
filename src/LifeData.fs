/// The wiki's "Everyday life" articles (`#wiki/life/<slug>`): how people lived,
/// ate, worshipped, played, fell ill and died. Markdown in content/life/, the
/// text of record, bundled by vite.config.js's `guide-markdown` loader, which
/// cuts each file's "For review" notes, exactly as for the Start here guide.
module LifeData

open Fable.Core.JsInterop
open System.Text.RegularExpressions

type LifePage =
    { /// The file name without its number and extension ("the-household")
      Slug: string
      File: string
      Title: string
      /// A Greek word for the subject, shown beside the title in the index
      Greek: string
      Group: string
      /// The article's first paragraph, shown in the index and in search
      Summary: string
      /// Works quoted in the article (`read:` links), in order of first use
      Works: string list
      /// Authors linked in the article (`author:` links), in order of first use
      Authors: string list
      Markdown: string }

let private firstOf (pattern: string) (text: string) : string list =
    [ for m in Regex.Matches(text, pattern) -> m.Groups.[1].Value ] |> List.distinct

let private page (file: string) (group: string) (greek: string) (text: string) : LifePage =
    let summary =
        Markdown.parse text
        |> List.tryPick (function
            | Markdown.Para xs -> Some(Markdown.plainText xs)
            | _ -> None)
        |> Option.defaultValue ""
    { Slug = Regex.Replace(file, @"^\d+-|\.md$", "")
      File = file
      Title = Markdown.title text
      Greek = greek
      Group = group
      Summary = summary
      Works = firstOf @"\]\(read:(tlg\d+\.tlg\d+\w*)" text
      Authors = firstOf @"\]\(author:(tlg\d+)\)" text
      Markdown = text }

// importDefault needs a literal path, hence one line per file.
let pages: LifePage list =
    [ page "01-the-household.md" "Home and family" "Οἶκος" (importDefault "../content/life/01-the-household.md?guide")
      page "02-slavery.md" "Home and family" "Δουλεία" (importDefault "../content/life/02-slavery.md?guide")
      page "03-womens-lives.md" "Home and family" "Γυναῖκες" (importDefault "../content/life/03-womens-lives.md?guide")
      page "04-childhood-and-school.md" "Home and family" "Παῖδες" (importDefault "../content/life/04-childhood-and-school.md?guide")
      page "05-love-and-marriage.md" "Home and family" "Γάμος" (importDefault "../content/life/05-love-and-marriage.md?guide")
      page "06-old-age-and-death.md" "Home and family" "Θάνατος" (importDefault "../content/life/06-old-age-and-death.md?guide")
      page "07-food.md" "Body and table" "Σῖτος" (importDefault "../content/life/07-food.md?guide")
      page "08-wine-and-the-symposium.md" "Body and table" "Συμπόσιον" (importDefault "../content/life/08-wine-and-the-symposium.md?guide")
      page "09-clothes-and-washing.md" "Body and table" "Ἐσθής" (importDefault "../content/life/09-clothes-and-washing.md?guide")
      page "10-medicine.md" "Body and table" "Ἰατρική" (importDefault "../content/life/10-medicine.md?guide")
      page "11-sport.md" "Body and table" "Ἆθλα" (importDefault "../content/life/11-sport.md?guide")
      page "12-gods-at-home.md" "Gods, music and play" "Θυσία" (importDefault "../content/life/12-gods-at-home.md?guide")
      page "13-mysteries-and-oracles.md" "Gods, music and play" "Μυστήρια" (importDefault "../content/life/13-mysteries-and-oracles.md?guide")
      page "14-music.md" "Gods, music and play" "Μουσική" (importDefault "../content/life/14-music.md?guide")
      page "15-games-pets-and-jokes.md" "Gods, music and play" "Παιδιά" (importDefault "../content/life/15-games-pets-and-jokes.md?guide")
      page "16-magic-and-superstition.md" "Gods, music and play" "Κατάδεσμοι" (importDefault "../content/life/16-magic-and-superstition.md?guide")
      page "17-time-and-travel.md" "Gods, music and play" "Ὁδός" (importDefault "../content/life/17-time-and-travel.md?guide")
      page "18-plague-and-war.md" "The hard side" "Λοιμός" (importDefault "../content/life/18-plague-and-war.md?guide")
      page "19-exposure-exile-punishment.md" "The hard side" "Φυγή" (importDefault "../content/life/19-exposure-exile-punishment.md?guide")
      page "20-strange-but-true.md" "The hard side" "Θαυμάσια" (importDefault "../content/life/20-strange-but-true.md?guide") ]

/// The groups, in the order they first appear.
let groups: string list = pages |> List.map (fun p -> p.Group) |> List.distinct

let hashOf (slug: string) : string =
    if slug = "" then "#wiki/life" else "#wiki/life/" + slug

let tryFind (slug: string option) : LifePage option =
    match slug with
    | Some s -> pages |> List.tryFind (fun p -> p.Slug = s)
    | None -> None

/// A link in the Markdown to another article's file ("07-food.md") → its hash.
let hashOfFile (file: string) : string option =
    pages |> List.tryFind (fun p -> p.File = file) |> Option.map (fun p -> hashOf p.Slug)
