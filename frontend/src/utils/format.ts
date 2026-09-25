import type { Locale } from '@/stores/settingsShared'

// Centralizes numeric-to-string formatting so every display value goes through one function.
// Today this just replaces ~20 duplicated toFixed() call sites; it's also a single place to
// switch to locale-aware formatting (Intl.NumberFormat) later, since toFixed() always renders
// with a '.' decimal separator regardless of the active locale (en/nl).
export function formatNumber(value: number, decimals = 1): string {
  return value.toFixed(decimals)
}

/**
 * The Intl locale behind an interface language, for dates and times: the interface language
 * names only a language, and Intl needs a region to pick a date order and a clock.
 */
export function intlLocale(locale: Locale): string {
  return locale === 'nl' ? 'nl-NL' : 'en-US'
}
