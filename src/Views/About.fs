module Views.About

open Feliz
open Types

let private cred (heading: ReactElement) (bodyParas: ReactElement list) (licence: ReactElement option) : ReactElement =
    Html.div [
        prop.className "cred"
        prop.children ([ Html.h3 [ prop.children [ heading ] ] ] @ bodyParas @ (licence |> Option.map (fun l -> Html.p [ prop.className "lic"; prop.children [ l ] ]) |> Option.toList))
    ]

let private extLink (href: string) (text: string) : ReactElement =
    Html.a [ prop.href (Router.href href); prop.target "_blank"; prop.rel "noopener"; prop.text text ]

let render (model: Model) : ReactElement =
    let nAuthors = model.Catalog.Authors.Length
    let nWorks = model.Catalog.Authors |> List.sumBy (fun a -> a.Works.Length)
    Html.div [
        prop.className "page about"
        prop.children [
            Html.h1 [ prop.className "ph"; prop.text "About & acknowledgments" ]
            Html.p [
                prop.className "ap-text"
                prop.children [
                    Html.text "Μάθησις ("
                    Html.i [ prop.text "mathēsis" ]
                    Html.text
                        ", \"learning\") is a reader for Ancient Greek with the translation beside the Greek, passage by passage. It also has a small reference wiki and a place to keep your own bookmarks and notes. It exists only because of the open projects credited below. None of the texts, dates or dictionary tools are ours, and this page sets out where each part comes from and on what terms."
                ]
            ]

            Html.h2 [ prop.className "sh"; prop.text "Texts" ]
            cred
                (React.Fragment [ Html.text "Perseus Digital Library — "; Html.i [ prop.text "canonical-greekLit" ] ])
                [ Html.p [
                      prop.text
                          "Gregory R. Crane, editor-in-chief, Tufts University. The Greek editions and translations here are the TEI XML files of the Perseus canonical-greekLit repository, most of them digitizations of out-of-copyright printed editions (Oxford Classical Texts, Teubner, Loeb Classical Library and others). Each file names its printed source and editor in its header, and the reader shows this beside every work."
                  ] ]
                (Some(
                    React.Fragment [
                        Html.text "Licence: Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0), as stated in each file; a few files carry an earlier CC BY-SA 3.0 notice. "
                        extLink "https://github.com/PerseusDL/canonical-greekLit" "github.com/PerseusDL/canonical-greekLit ↗"
                        Html.text " · "
                        extLink "https://www.perseus.tufts.edu/" "perseus.tufts.edu ↗"
                    ]
                ))
            cred
                (Html.text "Open Greek and Latin — First1KGreek (First Thousand Years of Greek)")
                [ Html.p [
                      prop.text
                          "A project of the Open Greek and Latin initiative (Universität Leipzig, Tufts University, Harvard's Center for Hellenic Studies and partners), providing TEI XML editions of Greek works from Homer to about 600 CE that were missing from Perseus, especially the later, Christian, medical and scientific authors."
                  ] ]
                (Some(
                    React.Fragment [
                        Html.text "Licence: CC BY-SA 4.0. "
                        extLink "https://github.com/OpenGreekAndLatin/First1KGreek" "github.com/OpenGreekAndLatin/First1KGreek ↗"
                        Html.text " · "
                        extLink "https://opengreekandlatin.org/" "opengreekandlatin.org ↗"
                    ]
                ))
            cred
                (Html.text "Editors and translators")
                [ Html.p [
                      prop.text
                          "Behind every text stands a scholar: Monro and Allen, Murray, Jebb, Burnet, Fowler, Godley, Hude, Bywater, Rackham, Kühn and many more. Their names appear in the edition pickers and on each author's wiki page under \"Editions in this collection\". The reader changes nothing in the texts themselves; it only re-divides them into the citable units the projects marked up, so that the Greek and the translation can be shown side by side."
                  ] ]
                None

            Html.h2 [ prop.className "sh"; prop.text "How the texts are changed here" ]
            Html.p [
                prop.className "ap-text"
                prop.text
                    "The ShareAlike licence asks us to say what we change. The TEI XML is read in your browser and rearranged into aligned passages, keyed by Canonical Text Services references (book and line, Stephanus page, chapter and section). Editorial notes and the critical apparatus are left out of the reading view, and line and page markers appear inline. The texts stay under their original CC BY-SA licence, and anything you copy out of the reader carries that licence with it."
            ]

            Html.h2 [ prop.className "sh"; prop.text "Reference data" ]
            cred
                (Html.text "Wikidata")
                [ Html.p [
                      prop.text "Authors' dates, birthplaces, occupations and links come from Wikidata, matched through its Thesaurus Linguae Graecae author identifiers, and are used to sort authors into eras and genres."
                  ] ]
                (Some(React.Fragment [ Html.text "Licence: CC0 1.0 (public domain dedication). "; extLink "https://www.wikidata.org/" "wikidata.org ↗" ]))
            cred
                (Html.text "Wikipedia")
                [ Html.p [ prop.text "Author pages link to the English Wikipedia article and, where this reader can reach the web, show its opening paragraph, credited as such." ] ]
                (Some(React.Fragment [ Html.text "Licence: CC BY-SA 4.0. "; extLink "https://en.wikipedia.org/" "en.wikipedia.org ↗" ]))
            cred
                (Html.text "Canonical Text Services (CTS)")
                [ Html.p [ prop.text "Citations follow the CTS URN scheme developed for the Homer Multitext project at Harvard's Center for Hellenic Studies, which both text collections use." ] ]
                (Some(extLink "https://cite-architecture.org/" "cite-architecture.org ↗"))
            cred
                (Html.text "Wiki articles")
                [ Html.p [
                      prop.children [
                          Html.text
                              "The wiki's articles (on authors and works, manuscript history, textual variants and standard editions, and the notes on each era) were written for this reader as short introductions drawing on the standard reference literature, above all the introductions to the critical editions named in each article and L. D. Reynolds and N. G. Wilson, "
                          Html.i [ prop.text "Scribes and Scholars" ]
                          Html.text ". They are summaries in our own words, not quotations, and come without warranty: for anything that matters, check the editions themselves."
                      ]
                  ] ]
                None

            Html.h2 [ prop.className "sh"; prop.text "Dictionaries and tools" ]
            cred
                (Html.text "Logeion")
                [ Html.p [
                      prop.text "Clicking a Greek word offers a lookup in Logeion, the University of Chicago's dictionary portal (LSJ, the Middle Liddell, Slater and others). The link opens Logeion's own site; nothing is copied from it."
                  ] ]
                (Some(extLink "https://logeion.uchicago.edu/" "logeion.uchicago.edu ↗"))
            cred
                (Html.text "Perseus Word Study Tool and Wiktionary")
                [ Html.p [ prop.text "Also offered as external links, for parsing a word's form and for inflection tables." ] ]
                (Some(
                    React.Fragment [
                        extLink "https://www.perseus.tufts.edu/hopper/morph" "Perseus morphology ↗"
                        Html.text " · "
                        extLink "https://en.wiktionary.org/" "en.wiktionary.org ↗"
                        Html.text " (CC BY-SA)"
                    ]
                ))

            Html.h2 [ prop.className "sh"; prop.text "Typefaces" ]
            cred
                (Html.text "Cardo, by David Perry")
                [ Html.p [ prop.text "The Greek text and the headings. Designed for classicists, with full polytonic Greek." ] ]
                (Some(Html.text "Licence: SIL Open Font License 1.1."))
            cred
                (Html.text "Source Serif 4, by Frank Grießhammer for Adobe")
                [ Html.p [ prop.text "The translation column, in a different serif from the Greek so the eye always knows which column it is in." ] ]
                (Some(Html.text "Licence: SIL Open Font License 1.1."))
            cred
                (Html.text "Inter, by Rasmus Andersson")
                [ Html.p [ prop.text "Menus, labels and the rest of the interface." ] ]
                (Some(Html.text "Licence: SIL Open Font License 1.1."))
            cred
                (Html.text "Noto Sans Greek, by Google")
                [ Html.p [ prop.text "The Greek in the optional sans-serif reading mode." ] ]
                (Some(Html.text "Licence: SIL Open Font License 1.1. All four typefaces are served by Google Fonts."))

            Html.h2 [ prop.className "sh"; prop.text "Privacy" ]
            Html.p [
                prop.className "ap-text"
                prop.text
                    "The reader has no accounts and no server of its own. Texts are downloaded from the projects' repositories, or read from a folder on your computer, when you open them. Your favourites, bookmarks and notes are stored in your browser and go nowhere unless you export them. Word lookups open external sites in a new tab. Two study lenses go online, and only when you open them: Map asks Wikidata about the places named in a passage and loads map tiles from the Digital Atlas of the Roman Empire, and Manuscript loads page images from the library that holds the manuscript. Both fetch their viewer code from cdnjs."
            ]

            Html.h2 [ prop.className "sh"; prop.text "Trademarks and reservations" ]
            Html.p [
                prop.className "ap-text"
                prop.text
                    "Perseus, Logeion, Loeb Classical Library, Oxford Classical Texts, Teubner and other names are the marks of their respective owners and are used only to identify the sources. This reader is an independent, non-commercial project and is not affiliated with or endorsed by any of them. If you believe any material here is credited wrongly or should not be included, please open an issue with the source repositories or remove the work from your copy."
            ]
            Html.p [ prop.className "quiet"; prop.text (sprintf "This collection: %d authors, %s works." nAuthors (nWorks.ToString("N0"))) ]
        ]
    ]
