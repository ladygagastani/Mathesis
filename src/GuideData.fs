/// The "Start here" beginner's guide. The pages are Markdown files in
/// content/start-here/, the text of record, which the editor reviews and
/// changes; they are bundled as strings by vite.config.js's `guide-markdown`
/// loader, which also cuts each file's "For review" notes.
module GuideData

open Fable.Core.JsInterop

type GuidePage =
    { /// "" for the contents page, otherwise the file name without its
      /// number and extension ("alphabet-and-sounds"), used in `#wiki/start/<slug>`
      Slug: string
      Title: string
      Markdown: string }

let private page (file: string) (text: string) : GuidePage =
    let slug =
        System.Text.RegularExpressions.Regex.Replace(file, @"^\d+-|\.md$", "")
        |> fun s -> if s = "start-here" then "" else s
    { Slug = slug; Title = Markdown.title text; Markdown = text }

// importDefault needs a literal path, hence one line per file.
let pages: GuidePage list =
    [ page "00-start-here.md" (importDefault "../content/start-here/00-start-here.md?guide")
      page "01-which-greek.md" (importDefault "../content/start-here/01-which-greek.md?guide")
      page "02-alphabet-and-sounds.md" (importDefault "../content/start-here/02-alphabet-and-sounds.md?guide")
      page "03-breathings-accents-punctuation.md" (importDefault "../content/start-here/03-breathings-accents-punctuation.md?guide")
      page "04-how-dictionaries-list-words.md" (importDefault "../content/start-here/04-how-dictionaries-list-words.md?guide")
      page "05-which-dictionary.md" (importDefault "../content/start-here/05-which-dictionary.md?guide")
      page "06-a-lookup-step-by-step.md" (importDefault "../content/start-here/06-a-lookup-step-by-step.md?guide")
      page "07-reading-a-word-study.md" (importDefault "../content/start-here/07-reading-a-word-study.md?guide")
      page "08-word-studies-people-home-world.md" (importDefault "../content/start-here/08-word-studies-people-home-world.md?guide")
      page "09-word-studies-mind-city-verbs.md" (importDefault "../content/start-here/09-word-studies-mind-city-verbs.md?guide") ]

let hashOf (slug: string) : string =
    if slug = "" then "#wiki/start" else "#wiki/start/" + slug

let tryFind (slug: string option) : GuidePage option =
    let s = slug |> Option.defaultValue ""
    pages |> List.tryFind (fun p -> p.Slug = s)

/// A link in the Markdown to another page's file ("03-….md") → its hash.
let hashOfFile (file: string) : string option =
    pages
    |> List.tryFind (fun p -> if p.Slug = "" then file = "00-start-here.md" else file.EndsWith("-" + p.Slug + ".md"))
    |> Option.map (fun p -> hashOf p.Slug)

/// "page 3" in the running text refers to the guide's own numbered pages.
let hashOfPageNumber (n: int) : string option =
    if n >= 1 && n < pages.Length then Some(hashOf pages.[n].Slug) else None
