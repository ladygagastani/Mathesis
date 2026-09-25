// Runs after `vite build` (see vite.config.js) and turns the one-page app into
// a site with one address per page:
//
//   dist/data/catalog.txt, dist/data/meta.json
//       the catalogue and author metadata, taken out of index.html so the other
//       pages can load them once and share them (the front page keeps its copy
//       inline, so dist/index.html still works opened straight from disk)
//   dist/<page>/index.html
//       a small copy of the page for each address the app knows (wiki pages,
//       authors, works, the guide…), with that page's own title, description
//       and share-preview tags, so search engines list every page and a link
//       pasted into a chat shows what it is
//   dist/404.html
//       any other address (a forum thread, a passage) is sent to the front
//       page with the address kept, and the app opens it there
//   dist/sitemap.xml, dist/robots.txt
//
// The address of the published site is VITE_SITE_URL (default: GitHub Pages).

import { readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { gunzipSync } from 'node:zlib'

const SITE_NAME = 'Μάθησις'
const SUFFIX = ' — ' + SITE_NAME

const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/** Plain text of a Markdown paragraph: links, emphasis and tags removed. */
const plain = md => md
  .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
  .replace(/<\/?u>/g, '')
  .replace(/\*\*([^*]+)\*\*/g, '$1')
  .replace(/\*([^*]+)\*/g, '$1')
  .replace(/\\\*/g, '*')
  .replace(/\s+/g, ' ')
  .trim()

/** Title (the "# " line) and first paragraph of a guide or wiki Markdown page. */
function mdInfo (file) {
  const text = readFileSync(file, 'utf8').replace(/\r\n/g, '\n')
  const title = (text.match(/^# (.+)$/m) || [, ''])[1].trim()
  const paras = text.split(/\n\s*\n/).map(p => p.trim()).filter(p => p && !p.startsWith('#') && !p.startsWith('---'))
  return { title: plain(title), summary: plain(paras[0] || '') }
}

/** Cuts a description to about 160 characters, at a word. */
function clip (s, n = 158) {
  s = s.replace(/\s+/g, ' ').trim()
  if (s.length <= n) return s
  const cut = s.slice(0, n)
  return cut.slice(0, cut.lastIndexOf(' ')).replace(/[,;:.\s]+$/, '') + '…'
}

const yearText = y => y == null ? '' : y < 0 ? `${-y} BCE` : `${y} CE`

export function generateSitePages (dist, root) {
  const site = (process.env.VITE_SITE_URL || 'https://ladygagastani.github.io/Mathesis/').replace(/\/?$/, '/')
  const indexPath = join(dist, 'index.html')
  let index = readFileSync(indexPath, 'utf8')

  // -- the data, out of the page ---------------------------------------------
  const catMatch = index.match(/<script id="catalog"[^>]*>([\s\S]*?)<\/script>\s*/)
  const metaMatch = index.match(/<script id="meta"[^>]*>([\s\S]*?)<\/script>\s*/)
  if (!catMatch || !metaMatch) throw new Error('site-pages: catalog/meta blobs not found in dist/index.html')
  mkdirSync(join(dist, 'data'), { recursive: true })
  writeFileSync(join(dist, 'data', 'catalog.txt'), catMatch[1].trim())
  writeFileSync(join(dist, 'data', 'meta.json'), metaMatch[1].trim())
  const catalog = JSON.parse(gunzipSync(Buffer.from(catMatch[1].trim(), 'base64')).toString('utf8'))
  const meta = JSON.parse(metaMatch[1])
  const template = index.replace(catMatch[0], '').replace(metaMatch[0], '')

  // English titles the app shows instead of the catalogue's Latin ones
  const jsonFs = readFileSync(join(root, 'src', 'Json.fs'), 'utf8')
  const english = {}
  for (const m of jsonFs.matchAll(/"(tlg\d+\.\w+|stoa\w+\.\w+)",\s*"([^"]+)"/g)) english[m[1]] = m[2]
  const titleOf = w => english[w.id] || w.title

  // -- the pages ---------------------------------------------------------------
  const pages = []   // { path, title, desc, noindex }
  const add = (path, title, desc, noindex = false) => pages.push({ path, title, desc: clip(desc), noindex })

  const nWorks = catalog.reduce((n, a) => n + a.works.length, 0)
  const nTranslated = catalog.reduce((n, a) => n + a.works.filter(w => w.texts.some(t => t.lang === 'eng')).length, 0)
  const homeDesc = `Read Ancient Greek texts with an English translation alongside: ${nWorks.toLocaleString('en')} works by ${catalog.length} authors, from Homer to Byzantium, with a wiki, a beginner's guide and study tools.`
  add('', SITE_NAME + ' — Ancient Greek Reader', homeDesc)
  add('library', 'Library' + SUFFIX, `Every text in the reader: ${nWorks.toLocaleString('en')} works by ${catalog.length} authors, ${nTranslated.toLocaleString('en')} of them with an English translation. Browse by author, title or era.`)
  add('study', 'Study' + SUFFIX, 'Learn to read Ancient Greek: a beginner\'s guide in eight steps, from the letters and their sounds to word studies, with lessons and exercises to practise with.')
  add('about', 'About' + SUFFIX, 'The projects, scholars and licences this Ancient Greek reader is built on: the Perseus Digital Library, First1KGreek, and the editions behind the texts.')
  add('forum', 'Forum' + SUFFIX, 'Discuss passages, ask questions about Ancient Greek, suggest improvements and report bugs.')
  add('forum/rules', 'Community rules — Forum' + SUFFIX, 'The rules of the forum: be kind to beginners, argue with the reading and not the reader, cite your text. How to report a post or hide someone.')
  add('privacy', 'Privacy' + SUFFIX, 'What this Ancient Greek reader stores, where, and who can see it: no advertising, no analytics, no tracking cookies. How to delete your account.')

  // pages that belong to one reader (not for search engines), and the forms
  // accounts are switched on when the build has a server to talk to
  const accounts = !!(process.env.VITE_SUPABASE_URL && process.env.VITE_SUPABASE_ANON_KEY)
  const personal = 'Your own page on this device' + (accounts ? ': sign in to keep it in step across devices.' : '.')
  add('lib', 'My library' + SUFFIX, 'Your bookmarks, notes, saved words and places. ' + personal, true)
  for (const [tab, name] of [['words', 'Words'], ['places', 'Places'], ['favourites', 'Favourites'], ['notes', 'Notes']])
    add('lib/' + tab, name + ' — My library' + SUFFIX, `Your ${name.toLowerCase()} in My library. ` + personal, true)
  add('account', 'Your account' + SUFFIX, accounts ? 'Sign in to keep your library in step across devices and to post in the forum.' : 'Accounts are not switched on yet.', true)
  add('forum/t', 'Forum' + SUFFIX, 'A discussion in the forum.', true)
  add('forum/new', 'New thread — Forum' + SUFFIX, 'Start a discussion in the forum.', true)
  const contentFs = readFileSync(join(root, 'src', 'Content.fs'), 'utf8')
  const boards = contentFs.slice(contentFs.indexOf('let forumBoards'))
  for (const m of boards.matchAll(/\{ Id = "([a-z]+)"\s+Name = "([^"]+)"/g))
    add('forum/' + m[1], m[2] + ' — Forum' + SUFFIX, `The ${m[2]} board of the forum: discussions about Ancient Greek texts and this site.`)

  // Study: the lessons
  for (const [slug, name] of [['welcome', 'Welcome'], ['preface', 'Preface'], ['letters', 'The letters'], ['alphabet', 'Friends and False Friends'],
    ['declension', 'The First Declension'], ['declension/done', 'The First Declension'], ['sounds', 'The Music of the Accent'],
    ['iliad', 'The Wrath, Iliad 1.1–5'], ['myth', 'A myth']])
    add('study/' + slug, name + ' — Study' + SUFFIX, `${name}: a lesson in reading Ancient Greek, with exercises to practise with. Just for fun, nothing is graded.`, slug === 'declension/done')

  // the guide
  const guideDir = join(root, 'content', 'start-here')
  for (const f of readdirSync(guideDir).filter(f => f.endsWith('.md')).sort()) {
    const slug = f.replace(/^\d+-|\.md$/g, '')
    const { title, summary } = mdInfo(join(guideDir, f))
    if (slug === 'start-here') add('start', 'Start here' + ' — Study' + SUFFIX, summary || 'A beginner\'s guide to reading Ancient Greek.')
    else add('start/' + slug, title + ' — Study' + SUFFIX, summary)
  }
  // pages merged into others when the guide was shortened; old links still land
  for (const old of ['which-greek', 'which-dictionary', 'a-lookup-step-by-step'])
    add('start/' + old, 'Start here — Study' + SUFFIX, 'A beginner\'s guide to reading Ancient Greek.', true)

  // the wiki
  add('wiki', 'Wiki' + SUFFIX, 'A reference for readers of Ancient Greek: authors and their lives, the eras of the language, how the texts reached us, editions, and everyday life in ancient Greece.')
  add('wiki/authors', 'Authors' + SUFFIX, `Lives and timelines of ${catalog.length} Greek authors, by era and by genre.`)
  for (const e of meta.eras || [])
    add('wiki/authors/era/' + e.id, `${e.name} authors` + SUFFIX, `Greek authors of the ${e.name} period, with their dates and works.`)
  add('wiki/authors/era/undated', 'Undated authors' + SUFFIX, 'Greek authors whose dates are unknown, with their works.')
  const wikiFs = readFileSync(join(root, 'src', 'WikiData.fs'), 'utf8')
  const genreDefs = wikiFs.slice(wikiFs.indexOf('let private genreDefs'), wikiFs.indexOf('let genres'))
  for (const m of genreDefs.matchAll(/"([a-z]+)", "([^"]+)", "([^"]+)",/g))
    add('wiki/authors/genre/' + m[1], m[2] + ' — Authors' + SUFFIX, `Greek ${m[2].toLowerCase()}: their lives, dates and works.`)
  add('wiki/authors/genre/other', 'Other authors' + SUFFIX, 'Greek authors who fit none of the other groups.')
  add('wiki/eras', 'Eras of Greek' + SUFFIX, 'From Homeric epic to Byzantine Greek: the periods of the Greek language and literature, and the authors of each.')
  for (const e of meta.eras || []) {
    const span = e.from <= -9000 ? `to ${yearText(e.to)}` : `${yearText(e.from)} to ${yearText(e.to)}`
    add('wiki/eras/' + e.id, e.name + ' — Eras of Greek' + SUFFIX, `The ${e.name} period of Greek (${span}): its language, its authors and their works.`)
  }
  add('wiki/manuscripts', 'Manuscripts & transmission' + SUFFIX, 'How the Greek texts reached us: papyri, medieval codices and the key manuscripts, author by author.')
  add('wiki/variants', 'Textual variants' + SUFFIX, 'Lines added later, lines ancient editors doubted, disputed works and other puzzles in the Greek texts.')
  add('wiki/editions', 'Editions & translations' + SUFFIX, 'The printed editions behind each Greek text in the reader, and the editions scholars cite.')
  add('wiki/life', 'Everyday life' + SUFFIX, 'How the ancient Greeks lived, the good and the grim: households, slavery, food and wine, clothes, medicine, sport, gods, music, games, magic, plague and war, from the texts themselves.')
  const lifeDir = join(root, 'content', 'life')
  for (const f of readdirSync(lifeDir).filter(f => f.endsWith('.md')).sort()) {
    const { title, summary } = mdInfo(join(lifeDir, f))
    add('wiki/life/' + f.replace(/^\d+-|\.md$/g, ''), title + ' — Everyday life' + SUFFIX, summary)
  }

  // authors and their works
  for (const a of catalog) {
    const m = (meta.authors || {})[a.id] || {}
    const dates = m.birth != null || m.death != null ? ` (${[yearText(m.birth), yearText(m.death)].filter(Boolean).join('–')})` : ''
    const about = m.desc ? m.desc.charAt(0).toUpperCase() + m.desc.slice(1) + '. ' : ''
    add('author/' + a.id, a.name + SUFFIX,
      `${a.name}${dates}. ${about}Life and timeline, and ${a.works.length === 1 ? 'one work' : a.works.length + ' works'} to read in Greek${a.works.some(w => w.texts.some(t => t.lang === 'eng')) ? ' with an English translation' : ''}.`)
    for (const w of a.works) {
      const t = titleOf(w)
      const tr = w.texts.some(t => t.lang === 'eng')
      add(w.id, `${t} — ${a.name}${SUFFIX}`,
        `Read ${a.name}, ${t}, in Greek${tr ? ' with an English translation side by side' : ''}. Click any word to look it up.`)
    }
  }

  // -- write them ------------------------------------------------------------
  const image = site + 'og-card.png'
  const headFor = (p) => {
    const url = site + (p.path ? p.path + '/' : '')
    return [
      `<title>${esc(p.title)}</title>`,
      `<meta name="description" content="${esc(p.desc)}">`,
      ...(p.noindex ? ['<meta name="robots" content="noindex">'] : []),
      `<link rel="canonical" href="${esc(url)}">`,
      `<meta property="og:site_name" content="${SITE_NAME}">`,
      `<meta property="og:type" content="${p.path.startsWith('wiki/life/') || p.path.startsWith('start/') ? 'article' : 'website'}">`,
      `<meta property="og:title" content="${esc(p.title.replace(SUFFIX, ''))}">`,
      `<meta property="og:description" content="${esc(p.desc)}">`,
      `<meta property="og:url" content="${esc(url)}">`,
      `<meta property="og:image" content="${esc(image)}">`,
      `<meta property="og:image:width" content="1200">`,
      `<meta property="og:image:height" content="630">`,
      `<meta property="og:image:alt" content="Μάθησις, an Ancient Greek reader">`,
      `<meta name="twitter:card" content="summary_large_image">`
    ].join('\n')
  }
  const fill = (html, p, depth) => {
    const prefix = depth === 0 ? './' : '../'.repeat(depth)
    return html
      .replace(/<title>[^<]*<\/title>/, headFor(p))
      .replace(/(href|src)="\.\//g, `$1="${prefix}`)
      // what a reader without JavaScript (and some crawlers) sees
      .replace('<div id="root"></div>', `<div id="root"><noscript><h1>${esc(p.title.replace(SUFFIX, ''))}</h1><p>${esc(p.desc)}</p><p>This site needs JavaScript to show the texts.</p></noscript></div>`)
  }

  for (const p of pages) {
    if (p.path === '') {
      writeFileSync(indexPath, fill(index, p, 0))
      continue
    }
    const file = join(dist, p.path, 'index.html')
    mkdirSync(dirname(file), { recursive: true })
    writeFileSync(file, fill(template, p, p.path.split('/').length))
  }

  // any other address: back to the front page with the address kept (the
  // front page's first script puts it back before the app starts)
  writeFileSync(join(dist, '404.html'), `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>${SITE_NAME}</title>
<meta name="robots" content="noindex">
<script>
// On GitHub Pages the site lives one folder down (/Mathesis/); elsewhere at the root.
(function (l) {
  var keep = /\\.github\\.io$/.test(l.hostname) ? 1 : 0
  var parts = l.pathname.split('/')
  var base = parts.slice(0, 1 + keep).join('/') + '/'
  var rest = parts.slice(1 + keep).join('/').replace(/&/g, '~and~')
  var query = l.search ? '&' + l.search.slice(1).replace(/&/g, '~and~') : ''
  l.replace(base + '?/' + rest + query + l.hash)
})(window.location)
</script></head><body><p>Opening <a href="./">${SITE_NAME}</a>…</p></body></html>
`)

  writeFileSync(join(dist, 'sitemap.xml'),
    '<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n' +
    pages.filter(p => !p.noindex).map(p => `<url><loc>${esc(site + (p.path ? p.path + '/' : ''))}</loc></url>`).join('\n') +
    '\n</urlset>\n')
  writeFileSync(join(dist, 'robots.txt'), `User-agent: *\nAllow: /\n\nSitemap: ${site}sitemap.xml\n`)

  return pages.length
}
