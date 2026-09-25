// Leaflet first: its map chrome (zoom buttons, popups, attribution) is styled light-only, and
// main.css re-themes those same selectors. Same specificity, so the later import has to be ours.
import 'leaflet/dist/leaflet.css'
import './assets/main.css'

import { createApp, type Component } from 'vue'

if ('serviceWorker' in navigator) {
  let refreshing = false
  navigator.serviceWorker.addEventListener('controllerchange', () => {
    if (refreshing) return
    refreshing = true
    window.location.reload()
  })

  // If a SW install fails (e.g. stale precache manifest after deploy while
  // sw.js was HTTP-cached as immutable), unregister and reload so the next
  // load fetches a fresh sw.js and installs cleanly.
  const watchInstalling = (sw: ServiceWorker, reg: ServiceWorkerRegistration) =>
    sw.addEventListener('statechange', () => {
      if (sw.state === 'redundant') reg.unregister().then(() => window.location.reload())
    })

  window.addEventListener('load', () => {
    navigator.serviceWorker.getRegistration('/').then((reg) => {
      if (!reg) return
      if (reg.installing) watchInstalling(reg.installing, reg)
      reg.addEventListener('updatefound', () => {
        if (reg.installing) watchInstalling(reg.installing, reg)
      })
    })
  })
}
import { createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { config, library } from '@fortawesome/fontawesome-svg-core'
// FontAwesome injects this stylesheet into <head> at runtime by default, which the
// Content-Security-Policy blocks (style-src-elem allows only same-origin files and the one
// hashed inline style). Without it every icon renders at its container's size, so the CSS is
// imported here instead and the runtime injection switched off.
import '@fortawesome/fontawesome-svg-core/styles.css'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'

config.autoAddCss = false
import {
  faBars,
  faXmark,
  faCar,
  faGaugeHigh,
  faChartLine,
  faMap,
  faRotate,
  faGear,
  faRotateLeft,
  faBell,
  faBellSlash,
  faSpinner,
  faTriangleExclamation,
  faGasPump,
  faRoad,
  faRoadCircleCheck,
  faCamera,
  faDiamondTurnRight,
  faBolt,
  faPlug,
  faGauge,
  faBatteryThreeQuarters,
  faRoute,
  faPlugCircleBolt,
  faBatteryFull,
  faLeaf,
  faWind,
  faTemperatureHalf,
  faCheck,
  faCarRear,
  faThermometerHalf,
  faTemperatureLow,
  faCouch,
  faCarBurst,
  faBullhorn,
  faLockOpen,
  faLock,
  faWindowMaximize,
  faDoorOpen,
  faDatabase,
  faPercent,
  faWaveSquare,
  faBoltLightning,
  faPlugCircleCheck,
  faChevronRight,
  faChevronLeft,
  faAnglesLeft,
  faAnglesRight,
  faLightbulb,
  faCircle,
  faUser,
  faEye,
  faEyeSlash,
  faArrowRight,
  faArrowRightFromBracket,
  faArrowRightToBracket,
  faPlus,
  faPenToSquare,
  faBatteryHalf,
  faSun,
  faMoon,
  faCarSide,
  faFire,
  faLocationDot,
  faCircleInfo,
  faSliders,
  faLayerGroup,
  faBoxArchive,
  faTrash,
  faClock,
  faLocationArrow,
  faWifi,
  faTemperatureArrowUp,
  faCalendarCheck,
  faChargingStation,
  faFlask,
  faGripLines,
  faTag,
  faScrewdriverWrench,
  faCircleExclamation,
  faBatteryQuarter,
  faMobileScreen,
  faDownload,
} from '@fortawesome/free-solid-svg-icons'

import App from './App.vue'
import router from './router'
import { useUiSettingsStore } from './stores/settingsUi'
import { useAuthStore } from './stores/auth'
import { setUnauthorizedHandler, clearUnauthorizedState } from './services/apiCore'
import en from './locales/en.json'
import nl from './locales/nl.json'

library.add(
  faBars,
  faXmark,
  faCar,
  faGaugeHigh,
  faChartLine,
  faMap,
  faRotate,
  faGear,
  faRotateLeft,
  faBell,
  faBellSlash,
  faSpinner,
  faTriangleExclamation,
  faGasPump,
  faRoad,
  faRoadCircleCheck,
  faCamera,
  faDiamondTurnRight,
  faBolt,
  faPlug,
  faGauge,
  faBatteryThreeQuarters,
  faRoute,
  faPlugCircleBolt,
  faBatteryFull,
  faLeaf,
  faWind,
  faTemperatureHalf,
  faCheck,
  faCarRear,
  faThermometerHalf,
  faTemperatureLow,
  faCouch,
  faCarBurst,
  faBullhorn,
  faLockOpen,
  faLock,
  faWindowMaximize,
  faDoorOpen,
  faDatabase,
  faPercent,
  faWaveSquare,
  faBoltLightning,
  faPlugCircleCheck,
  faChevronRight,
  faChevronLeft,
  faAnglesLeft,
  faAnglesRight,
  faLightbulb,
  faCircle,
  faUser,
  faEye,
  faEyeSlash,
  faArrowRight,
  faArrowRightFromBracket,
  faArrowRightToBracket,
  faPlus,
  faPenToSquare,
  faBatteryHalf,
  faSun,
  faMoon,
  faCarSide,
  faFire,
  faLocationDot,
  faCircleInfo,
  faSliders,
  faLayerGroup,
  faBoxArchive,
  faTrash,
  faClock,
  faLocationArrow,
  faWifi,
  faTemperatureArrowUp,
  faCalendarCheck,
  faChargingStation,
  faFlask,
  faGripLines,
  faTag,
  faScrewdriverWrench,
  faCircleExclamation,
  faBatteryQuarter,
  faMobileScreen,
  faDownload,
)

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en, nl },
})

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

const settings = useUiSettingsStore()
i18n.global.locale.value = settings.locale

app.use(router)
app.use(i18n)
app.component('FontAwesomeIcon', FontAwesomeIcon as unknown as Component)

setUnauthorizedHandler(() => {
  const auth = useAuthStore()
  auth
    .logout()
    .catch(() => {})
    .finally(() => {
      router.replace({ name: 'login' }).finally(clearUnauthorizedState)
    })
})

router.isReady().then(() => {
  app.mount('#app')
})
