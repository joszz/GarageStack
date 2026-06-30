import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

/**
 * Converts the main CSS bundle from render-blocking to non-blocking.
 *
 * Strategy (no external deps, strict-CSP-compatible):
 *   1. Inline a tiny <style> that sets the dark background so there is no
 *      white flash before the full CSS activates.  Its SHA-256 hash is fixed
 *      and added to style-src-elem in both nginx.conf files.
 *   2. Change <link rel="stylesheet"> → <link rel="preload" as="style"> so
 *      the browser fetches CSS at high priority without blocking the
 *      first paint.
 *   3. Inject a small <script defer> (hash in script-src / script-src-elem)
 *      that promotes the preload back to a stylesheet.  defer means it runs
 *      before the Vue module but after HTML parsing, so the CSS is almost
 *      always already in the preload cache when it fires.
 *   4. Add a <noscript> fallback for the (theoretical) no-JS case.
 *
 * Inline style hash  (style-src-elem):
 *   sha256-IjDBH01/QEhAptpG1bqDVKT5WXiu63d/qZH2r6WdVO0=
 * Inline script hash (script-src / script-src-elem):
 *   sha256-MsyHDz2Q9MM6DjmYjpACXTrSgtFF+tutkPYzSbPmHwA=
 */
function deferNonCriticalCss(): Plugin {
  const INLINE_STYLE = 'body,html{background:#0f1117}'
  const ACTIVATION_SCRIPT =
    "document.querySelectorAll('link[rel=preload][as=style]').forEach(function(l){l.rel='stylesheet'})"

  return {
    name: 'defer-non-critical-css',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler(html: string): string {
        const cssLinkRe = /<link rel="stylesheet" crossorigin href="([^"]+\.css)">/g
        const hrefs: string[] = []
        let m: RegExpExecArray | null
        while ((m = cssLinkRe.exec(html)) !== null) hrefs.push(m[1])
        if (hrefs.length === 0) return html

        html = html.replace(
          /<link rel="stylesheet" crossorigin href="([^"]+\.css)">/g,
          '<link rel="preload" as="style" crossorigin href="$1">',
        )

        const noscriptLinks = hrefs
          .map((h) => `<link rel="stylesheet" crossorigin href="${h}">`)
          .join('')

        const injection = [
          `<style>${INLINE_STYLE}</style>`,
          `<script defer>${ACTIVATION_SCRIPT}</script>`,
          `<noscript>${noscriptLinks}</noscript>`,
        ].join('\n    ')

        return html.replace('\n  </head>', `\n    ${injection}\n  </head>`)
      },
    },
  }
}

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'script-defer',
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      manifest: {
        name: 'GarageStack',
        short_name: 'GarageStack',
        description: 'MG vehicle telemetry dashboard',
        theme_color: '#0f1117',
        background_color: '#0f1117',
        display: 'standalone',
        icons: [
          { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png', purpose: 'any' },
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png', purpose: 'any maskable' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'any maskable' },
        ],
        shortcuts: [
          {
            name: 'Map',
            short_name: 'Map',
            url: '/map',
            icons: [{ src: 'shortcut-map-96x96.png', sizes: '96x96', type: 'image/png' }],
          },
          {
            name: 'Statistics',
            short_name: 'Stats',
            url: '/statistics',
            icons: [{ src: 'shortcut-stats-96x96.png', sizes: '96x96', type: 'image/png' }],
          },
        ],
        screenshots: [
          {
            src: 'screenshot-mobile-home.webp',
            sizes: '375x667',
            type: 'image/webp',
            form_factor: 'narrow',
          },
          {
            src: 'screenshot-mobile-map.webp',
            sizes: '375x667',
            type: 'image/webp',
            form_factor: 'narrow',
          },
          {
            src: 'screenshot-mobile-statistics.webp',
            sizes: '375x667',
            type: 'image/webp',
            form_factor: 'narrow',
          },
          {
            src: 'screenshot-desktop-home.webp',
            sizes: '1269x1038',
            type: 'image/webp',
            form_factor: 'wide',
          },
          {
            src: 'screenshot-desktop-map.webp',
            sizes: '1269x1038',
            type: 'image/webp',
            form_factor: 'wide',
          },
          {
            src: 'screenshot-desktop-statistics.webp',
            sizes: '1269x1038',
            type: 'image/webp',
            form_factor: 'wide',
          },
        ],
      },
      injectManifest: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg}'],
      },
    }),
    deferNonCriticalCss(),
  ],
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5000',
      '/hubs': {
        target: 'http://127.0.0.1:5000',
        ws: true,
      },
    },
  },
  optimizeDeps: {
    include: ['leaflet', 'leaflet.heat', 'leaflet.markercluster'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
