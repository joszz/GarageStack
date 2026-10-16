// Leaflet first: its map chrome (zoom buttons, popups, attribution) is styled light-only, and
// main.css re-themes those same selectors. Same specificity, so the later import has to be ours.
import 'leaflet/dist/leaflet.css'
import './assets/main.css'

import { createApp, watch } from 'vue'
import { createPinia } from 'pinia'
import { installFontAwesome } from './plugins/fontAwesome'
import { watchServiceWorkerUpdates } from './plugins/serviceWorker'
import App from './App.vue'
import router from './router'
import { useUiSettingsStore } from './stores/settingsUi'
import { useAuthStore } from './stores/auth'
import { setUnauthorizedHandler, clearUnauthorizedState } from './services/apiCore'
import { i18n, setLocale } from './i18n'
import { installErrorReporting } from './utils/errorReporting'

watchServiceWorkerUpdates()

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

// Errors in someone's browser reach the API's log, so a bug only they hit is not invisible.
installErrorReporting(app, () => useAuthStore().isAuthenticated)

const settings = useUiSettingsStore()
// The interface language follows the setting from the start, so it also catches the account's
// copy the router takes in before the first view mounts. Asked for now, a language other than
// English downloads alongside the session check rather than after it.
watch(
  () => settings.locale,
  (locale) => void setLocale(locale),
  { immediate: true },
)

app.use(router)
app.use(i18n)
installFontAwesome(app)

setUnauthorizedHandler(() => {
  const auth = useAuthStore()
  auth
    .logout()
    .catch(() => {})
    .finally(() => {
      router.replace({ name: 'login' }).finally(clearUnauthorizedState)
    })
})

// The first view mounts in its final language: the one the account settled on, if it differs.
router
  .isReady()
  .then(() => setLocale(settings.locale))
  .then(() => {
    app.mount('#app')
  })
