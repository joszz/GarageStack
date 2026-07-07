import * as LModule from 'leaflet'

// Vite wraps CJS modules in a frozen ESM namespace - `import * as LModule` gives that frozen
// namespace. Plugins like leaflet.heat and leaflet.markercluster patch the actual mutable CJS
// export (LModule.default), so every consumer must resolve through this same shared instance
// rather than re-importing 'leaflet' directly, or plugin-added methods won't be visible on it.
export const L = ((LModule as unknown as { default?: typeof LModule }).default ??
  LModule) as typeof LModule

export type { Map as LeafletMap } from 'leaflet'
