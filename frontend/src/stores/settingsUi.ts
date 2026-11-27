import { defineStore } from 'pinia'
import { computed, reactive, ref, toRefs, watch } from 'vue'
import type { CarColors, Theme, ThemeChoice, Locale, VehicleTypeOverride } from './settingsShared'
import {
  osPreferredTheme,
  browserLocale,
  CAR_COLOR_SCHEMES,
  CUSTOM_CAR_COLOR_SCHEME,
  DEFAULT_CAR_COLOR_SCHEME,
  loadStoredSettings,
  parseCarColorChoice,
  persistSettings,
  resolveCarColors,
} from './settingsShared'
import { setRegionalFormat } from '@/utils/format'
import { isCurrency, regionCurrency, type Currency } from '@/utils/currency'
import { applyTheme } from '@/utils/theme'
import {
  CHARGE_HISTORY_PERIOD,
  MAP_PERIOD,
  parsePeriod,
  presetPeriod,
  STATS_PERIOD,
  type Period,
  type PeriodOptions,
  type PeriodPreset,
} from '@/utils/period'
import { parseTripLogPeriod, type TripLogPeriod } from '@/utils/tripLog'
import {
  DEFAULT_LOG_LEVEL,
  LOG_LEVEL_CHOICES,
  LOG_SOURCE_CHOICES,
  type LogLevel,
  type LogSourceChoice,
} from '@/services/logsApi'
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
  ENERGY_CONSUMPTION_UNITS,
  FUEL_CONSUMPTION_UNITS,
  PRESSURE_UNITS,
  resolveUnits,
  TEMPERATURE_UNITS,
  type UnitSettings,
} from '@/utils/units'

export type {
  Theme,
  Locale,
  VehicleTypeOverride,
  CarColors,
  CarColorScheme,
} from './settingsShared'
export { CAR_COLOR_SCHEMES, CUSTOM_CAR_COLOR_SCHEME }

/** The region dates, the clock and "auto" units follow: the detected one, or one chosen instead. */
export type RegionSetting = 'auto' | Region
export type ClockSetting = 'auto' | HourCycle

export const CLOCK_SETTINGS: readonly ClockSetting[] = ['auto', 'h23', 'h12']

/** The currency costs show in, or "auto" for the one the region pays in. */
export type CurrencySetting = 'auto' | Currency

const STORAGE_KEY = 'garagestack-settings-ui'

/**
 * The browser settings that belong to one car rather than to the whole account. Only the colour:
 * the type override describes the car, so it is kept with the car on the server.
 */
export interface VehicleUiSettings {
  carColorScheme: string
  /** The car's own custom paints; without them it shares the account-wide pair. */
  carCustomColors?: CarColors | null
}

interface UiSettings {
  theme: ThemeChoice
  locale: Locale
  showCardInfoIcons: boolean
  placeNamesEnabled: boolean
  colorfulMaps: boolean
  /** Whether the installed app keeps the screen on while it is in view. This device only. */
  keepScreenOn: boolean
  /** Whether the installed app records the phone's GPS for the car's trips. This device only. */
  recordPhoneGps: boolean
  carColorScheme: string
  /** The paints of the custom scheme, null until it is first chosen. */
  carCustomColors: CarColors | null
  vehicleTypeOverride: VehicleTypeOverride
  /** Each page's own period, of the presets it offers (utils/period). */
  mapPeriod: Period
  statsPeriod: Period
  /** The trip log's month or year; null follows the calendar to the current month. */
  tripLogPeriod: TripLogPeriod | null
  /** Whether the trip log lists the hidden trips too. */
  tripLogShowHidden: boolean
  chargeHistoryPeriod: Period
  /** The log viewer's files and the lowest level it shows. */
  logSource: LogSourceChoice
  logLevel: LogLevel
  notificationTypeExclusions: string[]
  /** The opt-in notification types this device switched on (the weekly and monthly summaries). */
  notificationTypeInclusions: string[]
  /**
   * The notification types this device switched off for one car only, by vehicle id. They come on
   * top of notificationTypeExclusions, which leave a type out for every car.
   */
  notificationTypeExclusionsByVehicle: Record<string, string[]>
  /**
   * The colour of each car, by VIN, used only while the account has more than one car. A car
   * without an entry, and every one-car install, uses carColorScheme above. vehicleTypeOverride
   * above is from before the type moved to the car's own settings, and only counts with one car.
   */
  vehicleSettings: Record<string, VehicleUiSettings>
  region: RegionSetting
  clock: ClockSetting
  units: UnitSettings
  currency: CurrencySetting
}

function defaultsFor(): UiSettings {
  return {
    theme: 'system',
    locale: browserLocale(),
    showCardInfoIcons: true,
    placeNamesEnabled: true,
    colorfulMaps: false,
    keepScreenOn: false,
    recordPhoneGps: false,
    carColorScheme: DEFAULT_CAR_COLOR_SCHEME,
    carCustomColors: null,
    vehicleTypeOverride: 'auto',
    mapPeriod: { ...MAP_PERIOD.fallback },
    statsPeriod: { ...STATS_PERIOD.fallback },
    tripLogPeriod: null,
    tripLogShowHidden: false,
    chargeHistoryPeriod: { ...CHARGE_HISTORY_PERIOD.fallback },
    logSource: 'all',
    logLevel: DEFAULT_LOG_LEVEL,
    notificationTypeExclusions: [],
    notificationTypeInclusions: [],
    notificationTypeExclusionsByVehicle: {},
    vehicleSettings: {},
    // Everything follows the region the browser is in, which its time zone tells far better than
    // its language does. An install from before these settings keeps the units it stored.
    region: 'auto',
    clock: 'auto',
    units: { ...AUTO_UNITS },
    currency: 'auto',
  }
}

const THEMES: readonly ThemeChoice[] = ['dark', 'light', 'system']
const LOCALES: readonly Locale[] = ['en', 'nl', 'es', 'pt', 'pt-PT']
const VEHICLE_TYPE_OVERRIDES: readonly VehicleTypeOverride[] = ['auto', 'hev', 'phev', 'bev']
// localStorage is user-editable and survives old builds, and the account's copy may have been
// saved by an older or newer build on another device: an unknown value would otherwise be applied
// verbatim (an unstyled theme, a select with no matching option).
function oneOf<T>(value: unknown, allowed: readonly T[], fallback: T): T {
  return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback
}

// A page's period, or the one an older build kept as a number of days: the days in `carried` come
// back as the preset of the same name, so nobody finds a page on another period than they left it.
const DAYS_PRESETS: Record<number, Exclude<PeriodPreset, 'custom'>> = {
  7: 'last7',
  30: 'last30',
  90: 'last90',
}

function parsePagePeriod(
  raw: unknown,
  storedDays: unknown,
  options: PeriodOptions,
  carried: readonly number[],
): Period {
  const period = parsePeriod(raw, options.presets)
  if (period) return period
  const days = storedDays as number
  return carried.includes(days) ? presetPeriod(DAYS_PRESETS[days]!) : { ...options.fallback }
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
    energyConsumption: oneOf(
      units.energyConsumption,
      orAuto(ENERGY_CONSUMPTION_UNITS),
      fallback.energyConsumption,
    ),
  }
}

// An account holds a handful of cars at most; the cap only keeps a hand-edited copy from
// growing without bound.
const MAX_VEHICLE_SETTINGS = 50
const MAX_VIN_LENGTH = 32

// Each car's entry is checked on its own, and each of its values falls back to the account-wide
// one, so one bad value never resets another car.
function parseVehicleSettings(
  raw: unknown,
  fallback: Pick<UiSettings, 'carColorScheme'>,
): Record<string, VehicleUiSettings> {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return {}
  const result: Record<string, VehicleUiSettings> = {}
  for (const [vin, value] of Object.entries(raw).slice(0, MAX_VEHICLE_SETTINGS)) {
    if (!vin || vin.length > MAX_VIN_LENGTH) continue
    if (typeof value !== 'object' || value === null) continue
    const entry = value as Record<string, unknown>
    result[vin] = parseCarColorChoice(
      entry.carColorScheme,
      entry.carCustomColors,
      fallback.carColorScheme,
    )
  }
  return result
}

// The cars are the account's, so the cap is the one above; a key that is not a vehicle id, or a
// car with no type switched off, is dropped rather than kept.
function parseVehicleNotificationExclusions(raw: unknown): Record<string, string[]> {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return {}
  const result: Record<string, string[]> = {}
  for (const [vehicleId, value] of Object.entries(raw).slice(0, MAX_VEHICLE_SETTINGS)) {
    if (!/^[1-9]\d{0,8}$/.test(vehicleId) || !Array.isArray(value)) continue
    const types = [
      ...new Set((value as unknown[]).filter((v): v is string => typeof v === 'string')),
    ]
    if (types.length > 0) result[vehicleId] = types
  }
  return result
}

function parseRegion(raw: unknown, fallback: RegionSetting): RegionSetting {
  return raw === 'auto' || isRegion(raw) ? raw : fallback
}

function parseUiFields(parsed: Record<string, unknown>, fallback: UiSettings): UiSettings {
  const carColor = parseCarColorChoice(
    parsed.carColorScheme,
    parsed.carCustomColors,
    fallback.carColorScheme,
  )
  return {
    theme: oneOf(parsed.theme, THEMES, fallback.theme),
    locale: oneOf(parsed.locale, LOCALES, fallback.locale),
    showCardInfoIcons: parsed.showCardInfoIcons !== false,
    // On unless explicitly turned off, so an install from before this setting existed keeps
    // the behaviour it already had.
    placeNamesEnabled: parsed.placeNamesEnabled !== false,
    colorfulMaps: parsed.colorfulMaps === true,
    keepScreenOn: parsed.keepScreenOn === true,
    recordPhoneGps: parsed.recordPhoneGps === true,
    ...carColor,
    vehicleTypeOverride: oneOf(
      parsed.vehicleTypeOverride,
      VEHICLE_TYPE_OVERRIDES,
      fallback.vehicleTypeOverride,
    ),
    // The map's old default of 7 days is not carried, since nobody chose it: the map starts on
    // 30 days now, as the statistics do.
    mapPeriod: parsePagePeriod(parsed.mapPeriod, parsed.filterDays, MAP_PERIOD, [30, 90]),
    // The statistics shared the map's days until they had a period of their own.
    statsPeriod: parsePagePeriod(parsed.statsPeriod, parsed.filterDays, STATS_PERIOD, [7, 30, 90]),
    tripLogPeriod: parseTripLogPeriod(parsed.tripLogPeriod),
    tripLogShowHidden: parsed.tripLogShowHidden === true,
    // The old 365 days has no preset of its own, so it falls back to the 90 days it started on.
    chargeHistoryPeriod: parsePagePeriod(
      parsed.chargeHistoryPeriod,
      parsed.chargeHistoryDays,
      CHARGE_HISTORY_PERIOD,
      [30, 90],
    ),
    logSource: oneOf(parsed.logSource, LOG_SOURCE_CHOICES, fallback.logSource),
    logLevel: oneOf(parsed.logLevel, LOG_LEVEL_CHOICES, fallback.logLevel),
    notificationTypeExclusions: Array.isArray(parsed.notificationTypeExclusions)
      ? (parsed.notificationTypeExclusions as string[])
      : fallback.notificationTypeExclusions,
    notificationTypeInclusions: Array.isArray(parsed.notificationTypeInclusions)
      ? (parsed.notificationTypeInclusions as unknown[]).filter(
          (value): value is string => typeof value === 'string',
        )
      : fallback.notificationTypeInclusions,
    notificationTypeExclusionsByVehicle: parseVehicleNotificationExclusions(
      parsed.notificationTypeExclusionsByVehicle,
    ),
    vehicleSettings: parseVehicleSettings(parsed.vehicleSettings, carColor),
    region: parseRegion(parsed.region, fallback.region),
    clock: oneOf(parsed.clock, CLOCK_SETTINGS, fallback.clock),
    units: parseUnits(parsed.units, fallback.units),
    currency:
      parsed.currency === 'auto' || isCurrency(parsed.currency)
        ? parsed.currency
        : fallback.currency,
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

function applyCarColors({ primary, secondary }: CarColors) {
  document.documentElement.style.setProperty('--car-primary', primary)
  document.documentElement.style.setProperty('--car-secondary', secondary)
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
    // computer must not unsubscribe a phone, or subscribe it without a tap there. Keeping the
    // screen on suits a phone in a holder, not every computer the account signs in on, and only
    // the phone that travels in the car should record GPS.
    localKeys: [
      'notificationTypeExclusions',
      'notificationTypeInclusions',
      'notificationTypeExclusionsByVehicle',
      'keepScreenOn',
      'recordPhoneGps',
    ],
  })

  // What this device prefers, kept current: "system" follows it when the OS switches at sunset.
  const osTheme = ref<Theme>(osPreferredTheme())
  const osQuery = window.matchMedia('(prefers-color-scheme: light)')
  osQuery.addEventListener?.('change', () => (osTheme.value = osPreferredTheme()))

  /** The theme the page is drawn in: the setting, with "system" resolved on this device. */
  const effectiveTheme = computed<Theme>(() =>
    settings.theme === 'system' ? osTheme.value : settings.theme,
  )

  // The theme and the car's colours live on the document, so they follow the setting at once.
  watch(effectiveTheme, (theme) => applyTheme(theme), { immediate: true })

  // The car whose own colour applies, set by the vehicle store: the active car once the account
  // has more than one. Null with a single car, or before the vehicle list is in, and then the
  // account-wide colour stands, as it did before there was a choice of car.
  const colorVin = ref<string | null>(null)
  function setColorVin(vin: string | null) {
    colorVin.value = vin
  }

  // Read and written for that car: its own entry when it has one, otherwise the account-wide
  // value. The first change made for a car gives it its own entry.
  function perVehicle<K extends keyof VehicleUiSettings>(key: K) {
    return computed<UiSettings[K]>({
      get: () => {
        const vin = colorVin.value
        const own: VehicleUiSettings[K] | undefined = vin
          ? settings.vehicleSettings[vin]?.[key]
          : undefined
        return (own || settings[key]) as UiSettings[K]
      },
      set: (value) => {
        const vin = colorVin.value
        if (!vin) {
          ;(settings as Pick<UiSettings, K>)[key] = value
          return
        }
        const current = settings.vehicleSettings[vin] ?? {
          carColorScheme: settings.carColorScheme,
          carCustomColors: settings.carCustomColors,
        }
        // Replaced whole, so the account sync sees the section change.
        settings.vehicleSettings = {
          ...settings.vehicleSettings,
          [vin]: { ...current, [key]: value },
        }
      },
    })
  }
  const carColorScheme = perVehicle('carColorScheme')
  const carCustomColors = perVehicle('carCustomColors')

  /** The paints the car is drawn in now, whichever scheme they come from. */
  const carColors = computed(() => resolveCarColors(carColorScheme.value, carCustomColors.value))
  watch(carColors, applyCarColors, { immediate: true })

  /**
   * Picks a scheme. Custom chosen for the first time starts from the paints on screen, so the
   * colour pickers open on the car as it looks rather than on black.
   */
  function chooseCarColorScheme(id: string) {
    if (id === CUSTOM_CAR_COLOR_SCHEME && !carCustomColors.value) {
      carCustomColors.value = carColors.value
    }
    carColorScheme.value = id
  }

  // Detected once per page load: what this device says, so "auto" can differ between devices
  // while every choice made instead follows the account.
  const detectedRegion = computed(() => detected)
  const effectiveRegion = computed(() => (settings.region === 'auto' ? detected : settings.region))
  const effectiveHourCycle = computed(() =>
    settings.clock === 'auto' ? regionHourCycle(effectiveRegion.value) : settings.clock,
  )
  const effectiveUnits = computed(() => resolveUnits(settings.units, effectiveRegion.value))
  const effectiveCurrency = computed(() =>
    settings.currency === 'auto' ? regionCurrency(effectiveRegion.value) : settings.currency,
  )

  // Dates and times are written by plain functions (utils/format), which follow this at once.
  watch(
    [effectiveRegion, effectiveHourCycle],
    ([region, hourCycle]) => setRegionalFormat({ region, hourCycle }),
    { immediate: true },
  )

  return {
    ...toRefs(settings),
    // After the spread, so they stand in for the account-wide values of the same name.
    carColorScheme,
    carCustomColors,
    carColors,
    chooseCarColorScheme,
    setColorVin,
    effectiveTheme,
    detectedRegion,
    effectiveRegion,
    effectiveHourCycle,
    effectiveUnits,
    effectiveCurrency,
  }
})
