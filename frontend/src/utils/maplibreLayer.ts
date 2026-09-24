// The lazy half of the vector basemap: importing this module pulls in MapLibre GL (the renderer)
// and its stylesheet, which together are far larger than the rest of the app. useBasemap imports
// it dynamically so only a page that actually shows a map downloads them.
//
// The plugin resolves 'leaflet' to the same mutable CJS export @/utils/leaflet shares, so the
// L it extends is the instance every other map module already uses.
import 'maplibre-gl/dist/maplibre-gl.css'
import { setWorkerUrl } from 'maplibre-gl'
// MapLibre decodes tiles in a worker whose URL it derives at runtime from its own module URL
// (`new URL('./maplibre-gl-worker.mjs', import.meta.url)`). A bundler cannot see through that,
// so it never emits the file and the worker 404s in a built app: the map stays empty while the
// console repeats "Worker failed to load". Vite bundles the worker for us here and hands over
// the emitted URL, which is what MapLibre uses when it is set.
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'

setWorkerUrl(maplibreWorkerUrl)

export { maplibreGL } from '@maplibre/maplibre-gl-leaflet'
