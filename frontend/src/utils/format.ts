import { shallowRef } from 'vue'
import type { Locale } from '@/stores/settingsShared'
import { i18n } from '@/i18n'
import type { HourCycle, Region } from '@/utils/region'

/**
 * The Intl locale behind an interface language when no region is known: the interface language
 * names only a language, and Intl needs a region to pick a date order.
 */
function languageLocale(locale: Locale): string {
  return locale === 'nl' ? 'nl-NL' : 'en-US'
}

// Read through the i18n instance's reactive locale, so a computed or template that formats a
// value runs again when the interface language changes.
function activeLanguage(): Locale {
  return i18n.global.locale.value as Locale
}

/**
 * The locale numbers are written in: the interface language's, so English reads "12.3" wherever
 * the browser is. Charts and the CSV export take it too, so their numbers match the page.
 */
export function numberLocale(): string {
  return languageLocale(activeLanguage())
}

/** The region and clock dates and times follow, as the UI settings store settles them. */
export interface RegionalFormat {
  region: Region | null
  hourCycle: HourCycle
}

// Until the UI settings store hands over its region and clock (and in tests that never create
// the store), the interface language's own date order on a 24-hour clock.
const regional = shallowRef<RegionalFormat>({ region: null, hourCycle: 'h23' })

export function setRegionalFormat(format: RegionalFormat): void {
  regional.value = format
}

/** The clock every time on the page is shown on. */
export function activeHourCycle(): HourCycle {
  return regional.value.hourCycle
}

const regionalLocales = new Map<string, string>()

/**
 * The locale for dates and times: the interface language as written in the region, so English in
 * the Netherlands reads "09/10/2026" and English in the United States "10/9/2026". A pair Intl
 * has no conventions for falls back to international English, or to the language's own.
 */
function dateLocale(): string {
  const language = activeLanguage()
  const { region } = regional.value
  if (region === null) return languageLocale(language)
  const tag = `${language}-${region}`
  let locale = regionalLocales.get(tag)
  if (locale === undefined) {
    const supported = new Intl.DateTimeFormat(tag).resolvedOptions().locale === tag
    locale = supported ? tag : language === 'en' ? 'en-001' : languageLocale(language)
    regionalLocales.set(tag, locale)
  }
  return locale
}

/** The options on a clock, by default the chosen one, without a leading zero on 12 hours ("9:05 AM"). */
function onClock(
  options: Intl.DateTimeFormatOptions,
  hourCycle: HourCycle = activeHourCycle(),
): Intl.DateTimeFormatOptions {
  const withClock = { ...options, hourCycle }
  if (hourCycle === 'h12' && options.hour === '2-digit') withClock.hour = 'numeric'
  return withClock
}

/**
 * A number with a fixed count of decimals in the interface language: "12.3" in English, "12,3"
 * in Dutch. Thousands are grouped only when asked for, as on the odometer.
 */
export function formatNumber(value: number, decimals = 1, grouped = false): string {
  return value.toLocaleString(numberLocale(), {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
    useGrouping: grouped,
  })
}

function toDate(value: string | Date): Date {
  return typeof value === 'string' ? new Date(value) : value
}

const TIME: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }
const DATE_TIME: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  ...TIME,
}

/** A calendar date in the interface language, in the region's date order. */
export function formatDate(value: string | Date, options?: Intl.DateTimeFormatOptions): string {
  return toDate(value).toLocaleDateString(dateLocale(), options)
}

/** A time of day on the chosen clock: "14:05" or "2:05 PM". */
export function formatTime(
  value: string | Date,
  options: Intl.DateTimeFormatOptions = TIME,
): string {
  return toDate(value).toLocaleTimeString(dateLocale(), onClock(options))
}

/** A date and a time of day, on the chosen clock: "9 Oct 2026, 14:05". */
export function formatDateTime(
  value: string | Date,
  options: Intl.DateTimeFormatOptions = DATE_TIME,
): string {
  return toDate(value).toLocaleString(dateLocale(), onClock(options))
}

/** The start of an hour of the day on the chosen clock: "14:00" or "2:00 PM". */
export function formatHour(hour: number): string {
  return formatTime(new Date(2024, 0, 1, hour))
}

/**
 * A time of day kept as "HH:mm" (a climate schedule, or a schedule the gateway reports) on the
 * chosen clock. Anything else is shown as it came, since the gateway's text is not ours to trust.
 */
export function formatTimeOfDay(hhmm: string): string {
  const match = /^(\d{1,2}):(\d{2})/.exec(hhmm)
  const hours = Number(match?.[1])
  const minutes = Number(match?.[2])
  if (!match || hours > 23 || minutes > 59) return hhmm
  return formatTime(new Date(2024, 0, 1, hours, minutes))
}

/** An afternoon time on the given clock, whichever is chosen, for a settings choice to show: "14:05". */
export function clockExample(hourCycle: HourCycle): string {
  return new Date(2024, 0, 1, 14, 5).toLocaleTimeString(dateLocale(), onClock(TIME, hourCycle))
}

/** What the 12-hour clock calls the morning and the afternoon in the interface language. */
export function dayPeriodNames(): { am: string; pm: string } {
  const format = new Intl.DateTimeFormat(dateLocale(), { hour: 'numeric', hourCycle: 'h12' })
  const name = (hour: number) =>
    format.formatToParts(new Date(2024, 0, 1, hour)).find((p) => p.type === 'dayPeriod')?.value ??
    ''
  return { am: name(1), pm: name(13) }
}
