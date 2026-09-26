import type { App, Component } from 'vue'
import { config, library } from '@fortawesome/fontawesome-svg-core'
// FontAwesome injects this stylesheet into <head> at runtime by default, which the
// Content-Security-Policy blocks (style-src-elem allows only same-origin files and the one
// hashed inline style). Without it every icon renders at its container's size, so the CSS is
// imported here instead and the runtime injection switched off.
import '@fortawesome/fontawesome-svg-core/styles.css'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import * as icons from './icons'

config.autoAddCss = false

/** Registers the app's icons and the `<font-awesome-icon>` component. */
export function installFontAwesome(app: App): void {
  library.add(...Object.values(icons))
  app.component('FontAwesomeIcon', FontAwesomeIcon as unknown as Component)
}
