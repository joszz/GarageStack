import './assets/main.css'
import 'leaflet/dist/leaflet.css'

import { createApp } from 'vue'

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
import { library } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
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
  faArrowRightFromBracket,
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
  faLifeRing,
  faTag,
} from '@fortawesome/free-solid-svg-icons'

import App from './App.vue'
import router from './router'
import { useSettingsStore } from './stores/settings'
import { useAuthStore } from './stores/auth'
import { setUnauthorizedHandler, clearUnauthorizedState } from './services/api'
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
  faArrowRightFromBracket,
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
  faLifeRing,
  faTag,
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

const settings = useSettingsStore()
i18n.global.locale.value = settings.locale

app.use(router)
app.use(i18n)
app.component('FontAwesomeIcon', FontAwesomeIcon)

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
