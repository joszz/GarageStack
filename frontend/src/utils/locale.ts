import type { Locale } from '@/stores/settingsShared'

/**
 * The language an interface locale is written in, without its country: "pt" for both pt-BR and
 * pt-PT. OpenStreetMap names places per language (`name:pt`), so map labels and place names ask
 * for this, and dates pair it with the region the user is in.
 */
export function localeLanguage(locale: Locale): string {
  return locale.split('-')[0] ?? locale
}
