import { defineStore } from 'pinia'
import { reactive, toRefs, watch } from 'vue'
import type { Theme, Locale, VehicleTypeOverride } from './settingsShared'
import {
  osPreferredTheme,
  browserLocale,
  CAR_COLOR_SCHEMES,
  readLegacyBlob,
  persistSettings,
} from './settingsShared'
import { NOTIFICATION_CATEGORY_IDS } from '@/utils/notificationCategories'
import {
  DISTANCE_UNITS,
  FUEL_CONSUMPTION_UNITS,
  METRIC_UNITS,
  PRESSURE_UNITS,
  TEMPERATURE_UNITS,
  type UnitPreferences,
} from '@/utils/units'

export type { Theme, Locale, VehicleTypeOverride, CarColorScheme } from './settingsShared'
export { CAR_COLOR_SCHEMES }

const STORAGE_KEY = 'garagestack-settings-ui'

/**
 * The period every view starts on. Exported because a view that badges its filters as "changed"
 * has to compare against the same number the store defaults to, rather than its own copy of it.
 */
export const DEFAULT_FILTER_DAYS = 7

interface UiSettings {
  theme: Theme
  locale: Locale
  showCardInfoIcons: boolean
  placeNamesEnabled: boolean
  colorfulMaps: boolean
  carColorScheme: string
  vehicleTypeOverride: VehicleTypeOverride
  filterDays: number
  notificationTypeExclusions: string[]
  units: UnitPreferences
}

function defaultsFor(): UiSettings {
  return {
    theme: osPreferredTheme(),
    locale: browserLocale(),
    showCardInfoIcons: true,
    placeNamesEnabled: true,
    colorfulMaps: false,
    carColorScheme: 'orange',
    vehicleTypeOverride: 'auto',
    filterDays: DEFAULT_FILTER_DAYS,
    notificationTypeExclusions: [],
    // Metric for everyone rather than guessed from the browser language: plenty of people run an
    // English browser without driving in miles, and an update must not switch an install over.
    units: { ...METRIC_UNITS },
  }
}

// One-time migration: an earlier version stored `notificationTypeFilter` with whitelist
// semantics (empty = show all, non-empty = show only listed types). It was replaced by
// `notificationTypeExclusions` (blacklist semantics: empty = show all, non-empty = hide listed
// types) - a much better fit for "everything on, opt a couple out" - before seeing meaningful
// adoption, but it already shipped, so convert any stored whitelist into the equivalent
// exclusion list so a user's effective visible-category set doesn't change under them.
function migrateNotificationTypeExclusions(
  parsed: Record<string, unknown>,
  fallback: string[],
): string[] {
  if (Array.isArray(parsed.notificationTypeExclusions)) {
    return parsed.notificationTypeExclusions as string[]
  }
  if (Array.isArray(parsed.notificationTypeFilter) && parsed.notificationTypeFilter.length > 0) {
    const oldWhitelist = parsed.notificationTypeFilter as string[]
    return NOTIFICATION_CATEGORY_IDS.filter((id) => !oldWhitelist.includes(id))
  }
  return fallback
}

const THEMES: readonly Theme[] = ['dark', 'light']
const LOCALES: readonly Locale[] = ['en', 'nl']
const VEHICLE_TYPE_OVERRIDES: readonly VehicleTypeOverride[] = ['auto', 'hev', 'phev', 'bev']
const CAR_COLOR_SCHEME_IDS = CAR_COLOR_SCHEMES.map((s) => s.id)
// The API clamps every range to 90 days; anything up to a year is accepted here so an older
// build's stored value is not thrown away.
const MAX_FILTER_DAYS = 365

// localStorage is user-editable and survives old builds: an unknown value would otherwise be
// applied verbatim (an unstyled theme, a select with no matching option).
function oneOf<T>(value: unknown, allowed: readonly T[], fallback: T): T {
  return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback
}

function positiveDays(value: unknown, fallback: number): number {
  return typeof value === 'number' &&
    Number.isInteger(value) &&
    value > 0 &&
    value <= MAX_FILTER_DAYS
    ? value
    : fallback
}

// Each unit is checked on its own, so one unknown value (a unit an older build offered, say)
// falls back alone instead of resetting the others.
function parseUnits(raw: unknown, fallback: UnitPreferences): UnitPreferences {
  const units = typeof raw === 'object' && raw !== null ? (raw as Record<string, unknown>) : {}
  return {
    distance: oneOf(units.distance, DISTANCE_UNITS, fallback.distance),
    temperature: oneOf(units.temperature, TEMPERATURE_UNITS, fallback.temperature),
    pressure: oneOf(units.pressure, PRESSURE_UNITS, fallback.pressure),
    fuelConsumption: oneOf(units.fuelConsumption, FUEL_CONSUMPTION_UNITS, fallback.fuelConsumption),
  }
}

function parseUiFields(parsed: Record<string, unknown>, fallback: UiSettings): UiSettings {
  return {
    theme: oneOf(parsed.theme, THEMES, fallback.theme),
    locale: oneOf(parsed.locale, LOCALES, fallback.locale),
    showCardInfoIcons: parsed.showCardInfoIcons !== false,
    // On unless explicitly turned off, so an install from before this setting existed keeps
    // the behaviour it already had.
    placeNamesEnabled: parsed.placeNamesEnabled !== false,
    colorfulMaps: parsed.colorfulMaps === true,
    carColorScheme: oneOf(parsed.carColorScheme, CAR_COLOR_SCHEME_IDS, fallback.carColorScheme),
    vehicleTypeOverride: oneOf(
      parsed.vehicleTypeOverride,
      VEHICLE_TYPE_OVERRIDES,
      fallback.vehicleTypeOverride,
    ),
    filterDays: positiveDays(parsed.filterDays, fallback.filterDays),
    notificationTypeExclusions: migrateNotificationTypeExclusions(
      parsed,
      fallback.notificationTypeExclusions,
    ),
    units: parseUnits(parsed.units, fallback.units),
  }
}

function loadUiSettings(): UiSettings {
  const fallback = defaultsFor()
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return parseUiFields(JSON.parse(raw), fallback)
  } catch {
    // ignore parse errors
  }
  const legacy = readLegacyBlob()
  if (legacy) return parseUiFields(legacy, fallback)
  return fallback
}

function applyCarColors(id: string) {
  const scheme = CAR_COLOR_SCHEMES.find((s) => s.id === id) ?? CAR_COLOR_SCHEMES[0]!
  document.documentElement.style.setProperty('--car-primary', scheme.primary)
  document.documentElement.style.setProperty('--car-secondary', scheme.secondary)
}

export const useUiSettingsStore = defineStore('settingsUi', () => {
  const settings = reactive(loadUiSettings())
  persistSettings(STORAGE_KEY, settings)

  // The theme and the car's colours live on the document, so they follow the setting at once.
  watch(
    () => settings.theme,
    (theme) => {
      document.documentElement.dataset.theme = theme
    },
    { immediate: true },
  )
  watch(() => settings.carColorScheme, applyCarColors, { immediate: true })

  return toRefs(settings)
})
