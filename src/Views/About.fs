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

let private go (dispatch: Msg -> unit) (hash: string) (text: string) : ReactElement =
    Html.a [
        prop.href (Router.href hash)
        prop.text text
        prop.onClick (fun e ->
            e.preventDefault ()
            dispatch (Navigate(hash, false)))
    ]

let render (model: Model) (dispatch: Msg -> unit) : ReactElement =
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
                (Html.text "Gentium Book Plus, by SIL International")
                [ Html.p [ prop.text "The Greek text and the headings. Made for scholars, with full polytonic Greek." ] ]
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
                (Html.text "Noto Sans, by Google")
                [ Html.p [ prop.text "The text in the optional sans-serif reading mode." ] ]
                (Some(Html.text "Licence: SIL Open Font License 1.1. All four typefaces are served by Google Fonts."))

            Html.h2 [ prop.id "licence"; prop.className "sh"; prop.text "Our own writing: licence" ]
            Html.p [
                prop.className "ap-text"
                prop.children [
                    Html.text "Everything written for this site (the wiki and Everyday life articles, the Start here guide, the Study lessons, and the other explanations on its pages) is licensed under "
                    extLink "https://creativecommons.org/licenses/by-sa/4.0/" "Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) ↗"
                    Html.text ", the same licence as the Perseus and First1KGreek texts. You may copy it, adapt it and share it, for any purpose, as long as you credit Μάθησις with a link to the page and share what you make under the same licence."
                ]
            ]
            Html.p [
                prop.className "ap-text"
                prop.text
                    "The Greek texts and translations keep the licences set out above, and so do the quotations from them in the articles. Posts in the forum belong to the people who wrote them. Wikipedia summaries shown on author pages are Wikipedia's, under its own CC BY-SA licence."
            ]

            Html.h2 [ prop.className "sh"; prop.text "Privacy" ]
            Html.p [
                prop.className "ap-text"
                prop.children [
                    Html.text "No advertising, no analytics and no tracking cookies. Your library stays in your browser unless you make an account to keep it in step between devices. "
                    go dispatch "#privacy" "What is stored, where, and how to delete it"
                    Html.text "."
                ]
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

// ---------------------------------------------------------------------------
// privacy (`#privacy`)
// ---------------------------------------------------------------------------

let privacy (model: Model) (dispatch: Msg -> unit) : ReactElement =
    // While accounts are switched off (no server configured) the page says so
    // instead of describing what an account would store.
    let accounts = model.Account.Configured
    let section (title: string) (children: ReactElement list) =
        Html.section [ prop.children (Html.h2 [ prop.className "sh"; prop.text title ] :: children) ]
    let para (text: string) = Html.p [ prop.className "ap-text"; prop.text text ]
    let row (what: string) (why: string) = Html.li [ Html.b [ prop.text (what + ": ") ]; Html.text why ]
    Html.div [
        prop.className "page privacy"
        prop.children [
            Html.h1 [ prop.className "ph"; prop.text "Privacy" ]
            Html.p [
                prop.className "ap-text lead-p"
                prop.text (
                    "In short: no advertising, no analytics, no tracking cookies, and nothing to buy. "
                    + (if accounts then "You can use everything except the forum without an account, and then what you do here stays in your browser."
                       else "There are no accounts: what you do here stays in your browser.")
                )
            ]

            section "Kept in your browser" [
                para (
                    "These are saved on your own device, in the browser's storage for this site. Nobody else can see them"
                    + (if accounts then ", and they are not sent anywhere unless you make an account (below)." else ", and they are not sent anywhere.")
                )
                Html.ul [
                    prop.className "ap-text"
                    prop.children [
                        row "Your library" "favourites, bookmarks and their notes, saved words and their review schedule, saved places, author notes"
                        row "Your reading" "the texts you opened recently and where you were, and your recent searches"
                        row "Your settings" "columns, typeface, text size, line spacing, theme, text source, and which sections you folded"
                        row "Study" "how far you have got in the lessons"
                        row "The forum" "the people you chose to hide"
                        row "Folders and ZIP files" "if you connected texts on your computer, the browser remembers which folder, so it can ask to open it again"
                        if accounts then row "Your sign-in" "if you have an account, the key that keeps you signed in"
                    ]
                ]
                Html.p [
                    prop.className "ap-text"
                    prop.children [
                        Html.text "To remove them, use "
                        Html.b [ prop.text "Clear everything" ]
                        Html.text " at the foot of "
                        go dispatch "#lib" "My library"
                        Html.text " (for the library), or clear this site's data in your browser's settings (for all of it)."
                    ]
                ]
            ]

            if not accounts then
                section "Accounts" [
                    para "Accounts aren't switched on yet, so the site keeps nothing about you anywhere but your own browser. When they are, this page will say what an account stores and how to delete it."
                ]
            if accounts then
             section "If you make an account" [
                para "An account is only needed to post in the forum and to keep your library in step between devices. It is held by Supabase, the service that runs the site's database and sign-in. It stores:"
                Html.ul [
                    prop.className "ap-text"
                    prop.children [
                        row "Your email address" "to send you sign-in codes. It is never shown to anyone, and we send nothing else to it."
                        row "The name you choose" "shown on your forum posts"
                        row "A copy of your library" "so it syncs. Only you can read it, not even other signed-in readers."
                        row "Your forum posts" "public: anyone can read them"
                        row "Reports you send" "seen only by the moderators"
                    ]
                ]
                para "There are no passwords. Sign-in codes are sent by email, so the email service that delivers them also sees your address."
            ]

            if accounts then
             section "Deleting your account" [
                Html.p [
                    prop.className "ap-text"
                    prop.children [
                        Html.text "On "
                        go dispatch "#account" "your account page"
                        Html.text ", Delete account removes, at once and for good: your sign-in and email address, your name, the synced copy of your library, every thread and reply you posted (a thread goes with the replies others wrote in it), and any reports you sent. The library in this browser stays, until you clear it."
                    ]
                ]
            ]

            section "Other sites your browser talks to" [
                para "Some parts of the site load things from other services. Your browser connects to them directly, so each of them sees your internet address, as any website does. Each has its own privacy policy."
                Html.ul [
                    prop.className "ap-text"
                    prop.children [
                        row "GitHub Pages" "hosts the site itself"
                        row "Google Fonts" "the typefaces"
                        row "GitHub" "the Greek texts and translations, fetched from the Perseus and First1KGreek collections when you open a work"
                        row "Wikipedia" "the summary on an author's page"
                        row "Wikidata, map tiles and cdnjs" "only when you open the Map lens"
                        row "Library image servers and cdnjs" "only when you open the Manuscript lens"
                        row "Logeion, Perseus and Wiktionary" "only when you click through to look up a word"
                    ]
                ]
            ]

            section "Questions" [
                Html.p [
                    prop.className "ap-text"
                    prop.children [
                        Html.text "Ask in the forum's "
                        go dispatch "#forum/suggestions" "Suggestions"
                        Html.text " board, or read "
                        go dispatch "#forum/rules" "the community rules"
                        Html.text ". What we owe the projects this site is built on is on "
                        go dispatch "#about" "About & acknowledgments"
                        Html.text "."
                    ]
                ]
                Html.p [ prop.className "quiet"; prop.text "Last updated 25 September 2026." ]
            ]
        ]
    ]

// ---------------------------------------------------------------------------
// page not found
// ---------------------------------------------------------------------------

let notFound (model: Model) (dispatch: Msg -> unit) (hash: string) : ReactElement =
    let shown = Router.url hash
    Html.div [
        prop.className "page not-found"
        prop.children [
            Html.p [ prop.className "nf-grc grc"; prop.lang "grc"; prop.ariaHidden true; prop.text "οὐκ ἔστιν" ]
            Html.h1 [ prop.className "ph"; prop.text "There is no page here" ]
            Html.p [
                prop.className "ap-text"
                prop.children [
                    Html.text "Nothing on this site lives at "
                    Html.code [ prop.text shown ]
                    Html.text ". The link may be mistyped, or the page may have moved."
                ]
            ]
            Html.div [
                prop.className "f-cta"
                prop.children [
                    Html.button [
                        prop.className "btn primary"
                        prop.text "Search the site"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            dispatch (Search_ OpenSearch))
                    ]
                    Html.a [
                        prop.className "btn"
                        prop.href (Router.href "#")
                        prop.text "Home"
                        prop.onClick (fun e ->
                            e.preventDefault ()
                            dispatch (Navigate("#", false)))
                    ]
                ]
            ]
            Html.p [
                prop.className "ap-text"
                prop.children [
                    Html.text "Or go to the "
                    go dispatch "#library" "Library"
                    Html.text ", "
                    go dispatch "#study" "Study"
                    Html.text " or the "
                    go dispatch "#wiki" "Wiki"
                    Html.text ". If a link on this site brought you here, please "
                    go dispatch "#forum/bugs" "tell us in Bug reports"
                    Html.text "."
                ]
            ]
        ]
    ]
