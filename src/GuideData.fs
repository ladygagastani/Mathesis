/// The "Start here" beginner's guide. The pages are Markdown files in
/// content/start-here/, the text of record, which the editor reviews and
/// changes; they are bundled as strings by vite.config.js's `guide-markdown`
/// loader, which also cuts each file's "For review" notes.
module GuideData

open Fable.Core.JsInterop

type GuidePage =
    { /// "" for the contents page, otherwise the file name without its
      /// number and extension ("alphabet-and-sounds"), used in `#start/<slug>`
      Slug: string
      Title: string
      /// The part of the guide the step belongs to ("" for the contents page)
      Part: string
      /// The page's first paragraph, which says what the step teaches; shown
      /// under the step's title in the guide's path
      Summary: string
      Markdown: string }

let private page (file: string) (part: string) (text: string) : GuidePage =
    let slug =
        System.Text.RegularExpressions.Regex.Replace(file, @"^\d+-|\.md$", "")
        |> fun s -> if s = "start-here" then "" else s
    let summary =
        Markdown.parse text
        |> List.tryPick (function
            | Markdown.Para xs -> Some(Markdown.plainText xs)
            | _ -> None)
        |> Option.defaultValue ""
    { Slug = slug; Title = Markdown.title text; Part = part; Summary = summary; Markdown = text }

// importDefault needs a literal path, hence one line per file.
let pages: GuidePage list =
    [ page "00-start-here.md" "" (importDefault "../content/start-here/00-start-here.md?guide")
      page "01-alphabet-and-sounds.md" "Letters and sounds" (importDefault "../content/start-here/01-alphabet-and-sounds.md?guide")
      page "02-vowels-and-diphthongs.md" "Letters and sounds" (importDefault "../content/start-here/02-vowels-and-diphthongs.md?guide")
      page "03-breathings-accents-punctuation.md" "Letters and sounds" (importDefault "../content/start-here/03-breathings-accents-punctuation.md?guide")
      page "04-how-dictionaries-list-words.md" "The dictionary" (importDefault "../content/start-here/04-how-dictionaries-list-words.md?guide")
      page "05-looking-up-a-word.md" "The dictionary" (importDefault "../content/start-here/05-looking-up-a-word.md?guide")
      page "06-reading-a-word-study.md" "Word studies" (importDefault "../content/start-here/06-reading-a-word-study.md?guide")
      page "07-word-studies-people-home-world.md" "Word studies" (importDefault "../content/start-here/07-word-studies-people-home-world.md?guide")
      page "08-word-studies-mind-city-verbs.md" "Word studies" (importDefault "../content/start-here/08-word-studies-mind-city-verbs.md?guide") ]

/// The numbered steps, without the contents page.
let steps: (int * GuidePage) list = pages |> List.indexed |> List.tail

let hashOf (slug: string) : string =
    if slug = "" then "#start" else "#start/" + slug

/// Pages merged into others when the guide was shortened, so old links land
/// where their content went.
let private movedSlugs =
    Map
        [ "which-greek", ""
          "which-dictionary", "looking-up-a-word"
          "a-lookup-step-by-step", "looking-up-a-word" ]

let tryFind (slug: string option) : GuidePage option =
    let s = slug |> Option.defaultValue ""
    let s = movedSlugs.TryFind s |> Option.defaultValue s
    pages |> List.tryFind (fun p -> p.Slug = s)

/// A link in the Markdown to another page's file ("03-….md") → its hash.
let hashOfFile (file: string) : string option =
    pages
    |> List.tryFind (fun p -> if p.Slug = "" then file = "00-start-here.md" else file.EndsWith("-" + p.Slug + ".md"))
    |> Option.map (fun p -> hashOf p.Slug)

/// "step 3" in the running text refers to the guide's own numbered steps.
let hashOfStep (n: int) : string option =
    if n >= 1 && n < pages.Length then Some(hashOf pages.[n].Slug) else None
