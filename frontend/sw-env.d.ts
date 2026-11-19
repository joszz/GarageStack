// The build-time settings the service worker reads. A page gets its types from vite/client
// (env.d.ts), which is written for a page and does not load into the worker's.
interface ImportMetaEnv {
  readonly VITE_MAP_STYLE_DARK?: string
  readonly VITE_MAP_STYLE_LIGHT?: string
  readonly VITE_MAP_STYLE_COLORFUL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
