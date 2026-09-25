import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { Theme, Locale, VehicleTypeOverride } from './settingsShared'
import {
  osPreferredTheme,
  browserLocale,
  CAR_COLOR_SCHEMES,
  readLegacyBlob,
  createDebouncedSave,
} from './settingsShared'
import { NOTIFICATION_CATEGORY_IDS } from '@/utils/notificationCategories'

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
  carColorScheme: string
  vehicleTypeOverride: VehicleTypeOverride
  filterDays: number
  notificationTypeExclusions: string[]
}

function defaultsFor(): UiSettings {
  return {
    theme: osPreferredTheme(),
    locale: browserLocale(),
    showCardInfoIcons: true,
    placeNamesEnabled: true,
    carColorScheme: 'orange',
    vehicleTypeOverride: 'auto',
    filterDays: DEFAULT_FILTER_DAYS,
    notificationTypeExclusions: [],
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

function parseUiFields(parsed: Record<string, unknown>, fallback: UiSettings): UiSettings {
  return {
    theme: oneOf(parsed.theme, THEMES, fallback.theme),
    locale: oneOf(parsed.locale, LOCALES, fallback.locale),
    showCardInfoIcons: parsed.showCardInfoIcons !== false,
    // On unless explicitly turned off, so an install from before this setting existed keeps
    // the behaviour it already had.
    placeNamesEnabled: parsed.placeNamesEnabled !== false,
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
  const loaded = loadUiSettings()
  const theme = ref<Theme>(loaded.theme)
  const locale = ref<Locale>(loaded.locale)
  const showCardInfoIcons = ref<boolean>(loaded.showCardInfoIcons)
  const placeNamesEnabled = ref<boolean>(loaded.placeNamesEnabled)
  const carColorScheme = ref<string>(loaded.carColorScheme)
  const vehicleTypeOverride = ref<VehicleTypeOverride>(loaded.vehicleTypeOverride)
  const filterDays = ref<number>(loaded.filterDays)
  const notificationTypeExclusions = ref<string[]>(loaded.notificationTypeExclusions)

  document.documentElement.dataset.theme = theme.value
  applyCarColors(carColorScheme.value)

  function save() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        theme: theme.value,
        locale: locale.value,
        showCardInfoIcons: showCardInfoIcons.value,
        placeNamesEnabled: placeNamesEnabled.value,
        carColorScheme: carColorScheme.value,
        vehicleTypeOverride: vehicleTypeOverride.value,
        filterDays: filterDays.value,
        notificationTypeExclusions: notificationTypeExclusions.value,
      }),
    )
  }
  const scheduleSave = createDebouncedSave(save)

  watch(showCardInfoIcons, scheduleSave)
  watch(placeNamesEnabled, scheduleSave)
  watch(vehicleTypeOverride, scheduleSave)
  watch(locale, scheduleSave)
  watch(filterDays, scheduleSave)
  watch(notificationTypeExclusions, scheduleSave, { deep: true })
  watch(theme, (val) => {
    document.documentElement.dataset.theme = val
    scheduleSave()
  })
  watch(carColorScheme, (val) => {
    applyCarColors(val)
    scheduleSave()
  })

  return {
    theme,
    locale,
    showCardInfoIcons,
    placeNamesEnabled,
    carColorScheme,
    vehicleTypeOverride,
    filterDays,
    notificationTypeExclusions,
  }
})
