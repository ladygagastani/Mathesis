# Μάθησις: Ancient Greek Reader

*Τὸ γὰρ μανθάνειν ἡδίστη ἀνάγκη*: "For learning is the sweetest necessity."

A web app for reading Ancient Greek texts with an aligned English translation,
clickable words with dictionary lookup, a reference wiki, a beginner's guide, and a
personal library of bookmarks, notes, vocabulary flashcards and saved places. The
reading all happens in the browser. Optional accounts sync the library between
devices and open a forum; they need a small Supabase project, which is set up
as described in [`supabase/README.md`](supabase/README.md). Without it the site
works as before, with everything saved in the reader's own browser.

Live site: https://ladygagastani.github.io/Mathesis (also https://mathesis-zeta.vercel.app)

## Texts and licences

The texts come from two open collections and are fetched from GitHub when a work
is opened:

- **Perseus Digital Library**, `canonical-greekLit`, CC BY-SA 4.0
- **Open Greek and Latin**, `First1KGreek`, CC BY-SA 4.0

Full attribution is on the app's About & Acknowledgments page.

## What's in this repository

| Folder / file | What it is |
|---|---|
| `src/` | The app itself, written in F#. Fable turns it into JavaScript. `App.fsproj` lists the files in the order they're compiled. |
| `public/style.css` | All the styling. It is copied into the built site as-is. |
| `content/start-here/` | The "Start here" guide pages, written in Markdown. |
| `index.html` | The page shell, including the built-in catalogue of works. |
| `vite.config.js` | Settings for Vite, the tool that bundles everything into the finished site. |
| `package.json` | The JavaScript packages and the `build` / `start` commands. |
| `.config/dotnet-tools.json` | Pins the Fable version (5.17.2). |
| `.github/workflows/pages.yml` | How GitHub builds and publishes the site to GitHub Pages. |
| `vercel.json`, `scripts/vercel-build.sh` | How Vercel builds and publishes the site. |
| `supabase/` | The database tables and access rules for accounts, sync and the forum, and how to set them up. |
| `CLAUDE.md` | The design and architecture guide for AI coding agents working on the app. |

The built site (`dist/`) is not stored here: it is recreated by every build.

## Building it yourself

You need [Node.js](https://nodejs.org) 18+ and the [.NET SDK](https://dotnet.microsoft.com/download) 10.

```
npm ci                 # install the JavaScript packages
dotnet tool restore    # install Fable
npm run build          # build the site into dist/
npm start              # or: run it locally with live reload
```

## Publishing

Every upload to `main` publishes the site in two places:

- **GitHub Pages**: the workflow in `.github/workflows/pages.yml` builds the
  site on GitHub's machines and publishes `dist/`. Progress shows in the
  repository's Actions tab.
- **Vercel**: Vercel runs `scripts/vercel-build.sh`, which installs .NET on
  Vercel's build machine, compiles the F# and bundles the site into `dist/`.
