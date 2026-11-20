import type { Locale } from '@/stores/settingsShared'

/**
 * The language an interface locale is written in, without its country: "pt" for both pt and
 * pt-PT. OpenStreetMap names places per language (`name:pt`), so map labels and place names ask
 * for this, and dates pair it with the region the user is in.
 */
export function localeLanguage(locale: Locale): string {
  return locale.split('-')[0] ?? locale
}

/**
 * The language tag an interface locale stands for, for `<html lang>` and Intl: plain `pt` is
 * Brazilian Portuguese, so it becomes pt-BR, and the others already say what they are.
 */
export function localeTag(locale: Locale): string {
  return locale === 'pt' ? 'pt-BR' : locale
}
