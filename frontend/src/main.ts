import './assets/main.css'
import 'leaflet/dist/leaflet.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { library } from '@fortawesome/fontawesome-svg-core'
import { FontAwesomeIcon } from '@fortawesome/vue-fontawesome'
import {
  faBars, faXmark, faCar, faGaugeHigh, faChartLine, faMap,
  faRotate, faGear, faRotateLeft, faBell, faBellSlash,
  faSpinner, faTriangleExclamation, faGasPump, faRoad, faBolt,
  faPlug, faGauge, faBatteryThreeQuarters, faRoute, faPlugCircleBolt,
  faBatteryFull, faLeaf, faWind, faTemperatureHalf, faCheck,
  faCarRear, faThermometerHalf, faTemperatureLow, faCouch,
  faCarBurst, faBullhorn, faLockOpen, faLock, faWindowMaximize,
  faDoorOpen, faDatabase, faPercent, faWaveSquare, faBoltLightning,
  faPlugCircleCheck, faChevronRight, faChevronLeft, faLightbulb, faCircle,
  faUser, faEye, faEyeSlash, faArrowRightFromBracket,
  faPlus, faPenToSquare, faBatteryHalf,
  faSun, faMoon, faCarSide,
  faFire, faLocationDot, faCircleInfo,
} from '@fortawesome/free-solid-svg-icons'

import App from './App.vue'
import router from './router'
import { useSettingsStore } from './stores/settings'
import en from './locales/en.json'
import nl from './locales/nl.json'

library.add(
  faBars, faXmark, faCar, faGaugeHigh, faChartLine, faMap,
  faRotate, faGear, faRotateLeft, faBell, faBellSlash,
  faSpinner, faTriangleExclamation, faGasPump, faRoad, faBolt,
  faPlug, faGauge, faBatteryThreeQuarters, faRoute, faPlugCircleBolt,
  faBatteryFull, faLeaf, faWind, faTemperatureHalf, faCheck,
  faCarRear, faThermometerHalf, faTemperatureLow, faCouch,
  faCarBurst, faBullhorn, faLockOpen, faLock, faWindowMaximize,
  faDoorOpen, faDatabase, faPercent, faWaveSquare, faBoltLightning,
  faPlugCircleCheck, faChevronRight, faChevronLeft, faLightbulb, faCircle,
  faUser, faEye, faEyeSlash, faArrowRightFromBracket,
  faPlus, faPenToSquare, faBatteryHalf,
  faSun, faMoon, faCarSide,
  faFire, faLocationDot, faCircleInfo,
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

router.isReady().then(() => {
  app.mount('#app')
})
