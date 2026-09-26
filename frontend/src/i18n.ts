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
