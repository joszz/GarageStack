import { watch } from 'vue'
import { createI18n } from 'vue-i18n'
import en from './locales/en.json'
import nl from './locales/nl.json'

/**
 * The app's one i18n instance. Its locale is the interface language, which number and date
 * formatting in utils/format follows too, so a switch changes both at once.
 */
export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en, nl },
})

// The page names its language, so a screen reader reads Dutch in a Dutch voice and the browser
// hyphenates a long word (a dashboard tile's label) by that language's rules.
watch(
  i18n.global.locale,
  (locale) => {
    document.documentElement.lang = locale
  },
  { immediate: true },
)
