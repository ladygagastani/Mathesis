import { defineConfig } from 'vite'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'

// style.css is served straight out of public/, so unlike the JS bundle it never
// gets a hashed filename and its URL never changes. A host or browser that
// cached it would keep serving the old stylesheet after a re-upload, so the
// built page asks for it by content hash instead.
const stampStylesheetVersion = () => ({
  name: 'stamp-stylesheet-version',
  enforce: 'post',
  apply: 'build',
  transformIndexHtml(html) {
    const hash = createHash('sha256').update(readFileSync('public/style.css')).digest('hex').slice(0, 8)
    return html.replace(/(href="[^"]*style\.css)"/, `$1?v=${hash}"`)
  }
})

// The "Start here" pages are Markdown in content/start-here/, imported as
// `…md?guide`. Each file ends with an editor's "For review" list that must
// never ship, so it is cut here — at build time, not in the view — and the rest
// becomes a plain string module.
const guideMarkdown = () => ({
  name: 'guide-markdown',
  load(id) {
    const [file, query] = id.split('?')
    if (query !== 'guide') return null
    this.addWatchFile(file)
    const text = readFileSync(file, 'utf8').replace(/\r\n/g, '\n')
    const cut = text.search(/^## For review \(not for publication\)/m)
    const body = (cut < 0 ? text : text.slice(0, cut)).replace(/\n-{3,}\s*$/, '\n').trimEnd() + '\n'
    return `export default ${JSON.stringify(body)}`
  }
})

export default defineConfig(({ command }) => ({
  plugins: [stampStylesheetVersion(), guideMarkdown()],
  root: '.',
  publicDir: 'public',
  // Relative asset paths so the built dist/index.html also works when opened
  // straight from disk (file://), not only when served from a web root.
  base: command === 'build' ? './' : '/',
  build: {
    rollupOptions: {
      output: {
        // One 645 kB script was over Vite's 500 kB warning. Split along the
        // lines that change at different rates, so each file stays small and a
        // browser keeps the ones that did not change across updates:
        //   vendor — React and ReactDOM        (changes almost never)
        //   fable  — the F# runtime, Elmish, Feliz, Thoth (changes with a toolchain upgrade)
        //   guide  — the "Start here" Markdown (changes when the guide is edited)
        //   index  — the app itself            (changes with every release)
        manualChunks(id) {
          const p = id.replace(/\\/g, '/')
          if (p.includes('/node_modules/')) return 'vendor'
          if (p.includes('/fable_modules/')) return 'fable'
          if (p.includes('/content/start-here/')) return 'guide'
        }
      }
    }
  },
  server: {
    // Take the assigned port when one is given, so a second session can run
    // its own dev server instead of colliding on 5173. Nothing here needs a
    // fixed origin: all network access is outbound (GitHub raw, Wikipedia),
    // and the app has no accounts, callbacks or webhooks pointing back at it.
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    watch: {
      ignored: ['**/src/obj/**', '**/src/bin/**', '**/fable_modules/**/*.fs', '**/*.fsproj']
    }
  }
}))
