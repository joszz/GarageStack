import { defineStore } from 'pinia'
import { computed, reactive, toRefs, watch } from 'vue'
import type { Theme, Locale, VehicleTypeOverride } from './settingsShared'
import {
  osPreferredTheme,
  browserLocale,
  CAR_COLOR_SCHEMES,
  loadStoredSettings,
  persistSettings,
} from './settingsShared'
import { setRegionalFormat } from '@/utils/format'
import { applyTheme } from '@/utils/theme'
import { DEFAULT_STATS_PERIOD, isStatsPeriod, type StatsPeriod } from '@/utils/statsPeriod'
import {
  detectRegion,
  isRegion,
  regionHourCycle,
  type HourCycle,
  type Region,
} from '@/utils/region'
import {
  AUTO_UNITS,
  DISTANCE_UNITS,
  FUEL_CONSUMPTION_UNITS,
  PRESSURE_UNITS,
  resolveUnits,
  TEMPERATURE_UNITS,
  type UnitSettings,
} from '@/utils/units'

export type { Theme, Locale, VehicleTypeOverride, CarColorScheme } from './settingsShared'
export { CAR_COLOR_SCHEMES }

/** The region dates, the clock and "auto" units follow: the detected one, or one chosen instead. */
export type RegionSetting = 'auto' | Region
export type ClockSetting = 'auto' | HourCycle

export const CLOCK_SETTINGS: readonly ClockSetting[] = ['auto', 'h23', 'h12']

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
  /** The statistics page's period; the map keeps filterDays. */
  statsPeriod: StatsPeriod
  notificationTypeExclusions: string[]
  region: RegionSetting
  clock: ClockSetting
  units: UnitSettings
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
    statsPeriod: { ...DEFAULT_STATS_PERIOD },
    notificationTypeExclusions: [],
    // Everything follows the region the browser is in, which its time zone tells far better than
    // its language does. An install from before these settings keeps the units it stored.
    region: 'auto',
    clock: 'auto',
    units: { ...AUTO_UNITS },
  }
}

const THEMES: readonly Theme[] = ['dark', 'light']
const LOCALES: readonly Locale[] = ['en', 'nl']
const VEHICLE_TYPE_OVERRIDES: readonly VehicleTypeOverride[] = ['auto', 'hev', 'phev', 'bev']
const CAR_COLOR_SCHEME_IDS = CAR_COLOR_SCHEMES.map((s) => s.id)
// The map's trips are clamped to 90 days by the API; anything up to a year is accepted here so an
// older build's stored value is not thrown away.
const MAX_FILTER_DAYS = 365

// localStorage is user-editable and survives old builds, and the account's copy may have been
// saved by an older or newer build on another device: an unknown value would otherwise be applied
// verbatim (an unstyled theme, a select with no matching option).
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

// The statistics page shared the map's days until it had a period of its own: a stored choice of
// 7, 30 or 90 days carries over, so nobody finds the page on another period than they left it.
function parseStatsPeriod(raw: unknown, storedDays: unknown, fallback: StatsPeriod): StatsPeriod {
  if (isStatsPeriod(raw)) {
    return raw.preset === 'custom'
      ? { preset: 'custom', from: raw.from, to: raw.to }
      : { preset: raw.preset, from: null, to: null }
  }
  const carried = { 7: 'last7', 30: 'last30', 90: 'last90' }[storedDays as number] as
    StatsPeriod['preset'] | undefined
  return carried ? { preset: carried, from: null, to: null } : { ...fallback }
}

function orAuto<T>(allowed: readonly T[]): readonly ('auto' | T)[] {
  return ['auto', ...allowed]
}

// Each unit is checked on its own, so one unknown value (a unit an older build offered, say)
// falls back alone instead of resetting the others.
function parseUnits(raw: unknown, fallback: UnitSettings): UnitSettings {
  const units = typeof raw === 'object' && raw !== null ? (raw as Record<string, unknown>) : {}
  return {
    distance: oneOf(units.distance, orAuto(DISTANCE_UNITS), fallback.distance),
    temperature: oneOf(units.temperature, orAuto(TEMPERATURE_UNITS), fallback.temperature),
    pressure: oneOf(units.pressure, orAuto(PRESSURE_UNITS), fallback.pressure),
    fuelConsumption: oneOf(
      units.fuelConsumption,
      orAuto(FUEL_CONSUMPTION_UNITS),
      fallback.fuelConsumption,
    ),
  }
}

function parseRegion(raw: unknown, fallback: RegionSetting): RegionSetting {
  return raw === 'auto' || isRegion(raw) ? raw : fallback
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
    statsPeriod: parseStatsPeriod(parsed.statsPeriod, parsed.filterDays, fallback.statsPeriod),
    notificationTypeExclusions: Array.isArray(parsed.notificationTypeExclusions)
      ? (parsed.notificationTypeExclusions as string[])
      : fallback.notificationTypeExclusions,
    region: parseRegion(parsed.region, fallback.region),
    clock: oneOf(parsed.clock, CLOCK_SETTINGS, fallback.clock),
    units: parseUnits(parsed.units, fallback.units),
  }
}

function loadUiSettings(): UiSettings {
  const fallback = defaultsFor()
  return loadStoredSettings(
    STORAGE_KEY,
    (stored) => parseUiFields(stored, fallback),
    () => fallback,
  )
}

function applyCarColors(id: string) {
  const scheme = CAR_COLOR_SCHEMES.find((s) => s.id === id) ?? CAR_COLOR_SCHEMES[0]!
  document.documentElement.style.setProperty('--car-primary', scheme.primary)
  document.documentElement.style.setProperty('--car-secondary', scheme.secondary)
}

export const useUiSettingsStore = defineStore('settingsUi', () => {
  const settings = reactive(loadUiSettings())
  const detected = detectRegion()
  persistSettings({
    storageKey: STORAGE_KEY,
    section: 'ui',
    state: settings,
    parse: (raw) => parseUiFields(raw, defaultsFor()),
    // The notification checklist decides whether this browser is subscribed to push
    // (useNotificationPushSync), so it stays with the device: unticking everything on one
    // computer must not unsubscribe a phone, or subscribe it without a tap there.
    localKeys: ['notificationTypeExclusions'],
  })

  // The theme and the car's colours live on the document, so they follow the setting at once.
  watch(
    () => settings.theme,
    (theme) => applyTheme(theme),
    { immediate: true },
  )
  watch(() => settings.carColorScheme, applyCarColors, { immediate: true })

  // Detected once per page load: what this device says, so "auto" can differ between devices
  // while every choice made instead follows the account.
  const detectedRegion = computed(() => detected)
  const effectiveRegion = computed(() => (settings.region === 'auto' ? detected : settings.region))
  const effectiveHourCycle = computed(() =>
    settings.clock === 'auto' ? regionHourCycle(effectiveRegion.value) : settings.clock,
  )
  const effectiveUnits = computed(() => resolveUnits(settings.units, effectiveRegion.value))

  // Dates and times are written by plain functions (utils/format), which follow this at once.
  watch(
    [effectiveRegion, effectiveHourCycle],
    ([region, hourCycle]) => setRegionalFormat({ region, hourCycle }),
    { immediate: true },
  )

  return {
    ...toRefs(settings),
    detectedRegion,
    effectiveRegion,
    effectiveHourCycle,
    effectiveUnits,
  }
})
