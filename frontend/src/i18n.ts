import { watch } from 'vue'
import { createI18n } from 'vue-i18n'
import type { Locale } from '@/stores/settingsShared'
import en from './locales/en.json'

// Typed with every language, not only the one loaded up front: vue-i18n takes the locales it
// accepts from the keys of this object.
const messages: Partial<Record<Locale, typeof en>> = { en }

/**
 * The app's one i18n instance. Its locale is the interface language, which number and date
 * formatting in utils/format follows too, so a switch changes both at once.
 *
 * English ships with the app: it is the default and the fallback for any key another language
 * lacks. Every other language is a chunk of its own that setLocale fetches when it is first
 * chosen, so nobody downloads the messages of a language they do not read.
 */
export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages,
})

const loadMessages: Record<Exclude<Locale, 'en'>, () => Promise<{ default: typeof en }>> = {
  nl: () => import('./locales/nl.json'),
}

// The language asked for last. A download that finishes after the user has moved on to another
// language must not switch the interface back.
let requested: Locale = 'en'

/**
 * Switches the interface language, first fetching its messages when this page load has not
 * needed them yet. Never rejects: if the messages cannot be fetched (offline, before the service
 * worker has cached them), the interface stays in the language it was in.
 */
export async function setLocale(locale: Locale): Promise<void> {
  requested = locale
  if (locale !== 'en' && !i18n.global.availableLocales.includes(locale)) {
    try {
      const messages = await loadMessages[locale]()
      i18n.global.setLocaleMessage(locale, messages.default)
    } catch (error) {
      console.warn(`[i18n] could not load the ${locale} messages`, error)
      return
    }
    if (requested !== locale) return
  }
  i18n.global.locale.value = locale
}

// The page names its language, so a screen reader reads Dutch in a Dutch voice and the browser
// hyphenates a long word (a dashboard tile's label) by that language's rules.
watch(
  i18n.global.locale,
  (locale) => {
    document.documentElement.lang = locale
  },
  { immediate: true },
)
