// Leaflet first: its map chrome (zoom buttons, popups, attribution) is styled light-only, and
// main.css re-themes those same selectors. Same specificity, so the later import has to be ours.
import 'leaflet/dist/leaflet.css'
import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { installFontAwesome } from './plugins/fontAwesome'
import { watchServiceWorkerUpdates } from './plugins/serviceWorker'
import App from './App.vue'
import router from './router'
import { useUiSettingsStore } from './stores/settingsUi'
import { useAuthStore } from './stores/auth'
import { setUnauthorizedHandler, clearUnauthorizedState } from './services/apiCore'
import { i18n } from './i18n'

watchServiceWorkerUpdates()

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

const settings = useUiSettingsStore()
i18n.global.locale.value = settings.locale

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

router.isReady().then(() => {
  app.mount('#app')
})
