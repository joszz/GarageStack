import type { Locale } from '@/stores/settingsShared'
import { i18n } from '@/i18n'

/**
 * The Intl locale behind an interface language, for dates and times: the interface language
 * names only a language, and Intl needs a region to pick a date order and a clock.
 */
export function intlLocale(locale: Locale): string {
  return locale === 'nl' ? 'nl-NL' : 'en-US'
}

// Read through the i18n instance's reactive locale, so a computed or template that formats a
// value runs again when the interface language changes.
function activeLocale(): string {
  return intlLocale(i18n.global.locale.value as Locale)
}

/**
 * A number with a fixed count of decimals in the interface language: "12.3" in English, "12,3"
 * in Dutch. Thousands are grouped only when asked for, as on the odometer.
 */
export function formatNumber(value: number, decimals = 1, grouped = false): string {
  return value.toLocaleString(activeLocale(), {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
    useGrouping: grouped,
  })
}

function toDate(value: string | Date): Date {
  return typeof value === 'string' ? new Date(value) : value
}

/** A calendar date in the interface language. */
export function formatDate(value: string | Date, options?: Intl.DateTimeFormatOptions): string {
  return toDate(value).toLocaleDateString(activeLocale(), options)
}

/** A time of day in the interface language, with its own clock (24-hour in Dutch). */
export function formatTime(value: string | Date, options?: Intl.DateTimeFormatOptions): string {
  return toDate(value).toLocaleTimeString(activeLocale(), options)
}

/** A date and time in the interface language. */
export function formatDateTime(value: string | Date, options?: Intl.DateTimeFormatOptions): string {
  return toDate(value).toLocaleString(activeLocale(), options)
}
