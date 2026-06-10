import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      registerType: 'autoUpdate',
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
            src: 'screenshot-desktop-home.webp',
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
  ],
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:5000',
    },
  },
  optimizeDeps: {
    include: ['leaflet', 'leaflet.heat'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
