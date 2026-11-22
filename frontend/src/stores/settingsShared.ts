import { watch } from 'vue'
import type { VehicleType } from './vehicle'
import { useSettingsSyncStore } from './settingsSync'
import type { SettingsSection } from '@/services/settingsApi'
import { ALL_CARD_IDS, defaultCards } from '@/cards/registry'
import type { CardConfig, CardId } from '@/cards/registry'

// The card registry owns what a card is (icon, defaults, data predicate); the settings stores
// own which ones are shown and in what order. Re-exported so store consumers keep a
// single import for both halves.
export { ALL_CARD_IDS, defaultCards }
export type { CardConfig, CardId }

export type VehicleTypeOverride = 'auto' | VehicleType
/** The theme a page is drawn in. */
export type Theme = 'dark' | 'light'
/** The theme setting: one of the two, or whichever the device prefers at the moment. */
export type ThemeChoice = Theme | 'system'
/**
 * An interface language. A code names only the language, and a country is added only where the
 * language has variants: `pt` is Brazilian Portuguese, `pt-PT` European Portuguese.
 */
export type Locale = 'en' | 'nl' | 'es' | 'pt' | 'pt-PT'

/**
 * The car's two paints as `#rrggbb`: the body (its sides, and the stripes between the roof ribs)
 * and the roof and bonnet over it. The presets make the second a shade darker.
 */
export interface CarColors {
  primary: string
  secondary: string
}

export interface CarColorScheme extends CarColors {
  id: string
}

/**
 * The paints offered, neutrals first and then the colours. Each keeps a mid enough tone to stand
 * apart from both the dark and the light card and road; the theme's --car-outline (main.css)
 * gives the darkest and lightest an edge where they meet a background of their own tone. Seven, so
 * they and the custom swatch fit one row on a phone. An id is stored in the settings, so one is
 * never renamed; a dropped one goes to RETIRED_CAR_COLOR_SCHEMES.
 */
export const CAR_COLOR_SCHEMES: CarColorScheme[] = [
  { id: 'white', primary: '#f4f5f6', secondary: '#d3d8dd' },
  { id: 'silver', primary: '#bfc6cd', secondary: '#959ea8' },
  { id: 'anthracite', primary: '#5a636d', secondary: '#434a53' },
  { id: 'black', primary: '#2c2f35', secondary: '#1b1d21' },
  { id: 'red', primary: '#d93030', secondary: '#b01f24' },
  { id: 'green', primary: '#2f9e5e', secondary: '#207646' },
  { id: 'blue', primary: '#2f80dc', secondary: '#1c5fb0' },
]

/**
 * Presets no longer offered, with the paints they had. A car stored on one is moved to the
 * custom scheme in those same paints, so it looks as it did. Orange was the default until then,
 * so it is also what every car that never had a colour chosen was stored on.
 */
const RETIRED_CAR_COLOR_SCHEMES: ReadonlyMap<string, CarColors> = new Map([
  ['orange', { primary: '#f9b233', secondary: '#f39200' }],
  ['purple', { primary: '#9348c4', secondary: '#6d2d9b' }],
])

/** The scheme a car starts on, and falls back to. */
export const DEFAULT_CAR_COLOR_SCHEME = 'red'

/** The scheme that paints the car in the user's own two colours rather than a preset. */
export const CUSTOM_CAR_COLOR_SCHEME = 'custom'

export const CAR_COLOR_SCHEME_IDS: readonly string[] = [
  ...CAR_COLOR_SCHEMES.map((s) => s.id),
  CUSTOM_CAR_COLOR_SCHEME,
]

/** A car's colour as stored: the scheme, and the custom pair it uses when custom is chosen. */
export interface CarColorChoice {
  carColorScheme: string
  carCustomColors: CarColors | null
}

/**
 * A stored colour choice, checked: an unknown scheme falls back, a malformed pair is dropped, and
 * a retired preset becomes the custom scheme in its old paints.
 */
export function parseCarColorChoice(
  rawScheme: unknown,
  rawCustom: unknown,
  fallbackScheme: string,
): CarColorChoice {
  const retired = typeof rawScheme === 'string' ? RETIRED_CAR_COLOR_SCHEMES.get(rawScheme) : null
  if (retired) return { carColorScheme: CUSTOM_CAR_COLOR_SCHEME, carCustomColors: { ...retired } }
  return {
    carColorScheme:
      typeof rawScheme === 'string' && CAR_COLOR_SCHEME_IDS.includes(rawScheme)
        ? rawScheme
        : fallbackScheme,
    carCustomColors: parseCarColors(rawCustom),
  }
}

const HEX_COLOR = /^#[0-9a-f]{6}$/i

/**
 * A stored pair of custom paints, or null when it is not one: a colour input only ever gives
 * `#rrggbb`, so anything else was edited by hand or saved by another build, and is not applied
 * to the page as it is.
 */
export function parseCarColors(raw: unknown): CarColors | null {
  if (typeof raw !== 'object' || raw === null) return null
  const { primary, secondary } = raw as Record<string, unknown>
  if (typeof primary !== 'string' || !HEX_COLOR.test(primary)) return null
  if (typeof secondary !== 'string' || !HEX_COLOR.test(secondary)) return null
  return { primary: primary.toLowerCase(), secondary: secondary.toLowerCase() }
}

/**
 * The paints a scheme stands for: a preset's own, or the custom pair. Custom without a pair, and
 * an id no preset has, fall back to the default scheme.
 */
export function resolveCarColors(schemeId: string, custom: CarColors | null): CarColors {
  if (schemeId === CUSTOM_CAR_COLOR_SCHEME && custom) return { ...custom }
  const scheme =
    CAR_COLOR_SCHEMES.find((s) => s.id === schemeId) ??
    CAR_COLOR_SCHEMES.find((s) => s.id === DEFAULT_CAR_COLOR_SCHEME)!
  return { primary: scheme.primary, secondary: scheme.secondary }
}

export type StatsInsightId =
  | 'periodDistance'
  | 'avgTripLength'
  | 'periodEnergy'
  | 'periodEfficiency'
  | 'climateUsage'
  | 'commutePattern'
  | 'batteryVoltageTrend'
  | 'parkingLocations'
  | 'electricShare'
  | 'avgSpeed'

export type StatsChartId = 'evChart' | 'tyreChart' | 'hybridSocChart' | 'dailyKwhChart'

export interface StatsItemConfig<T extends string> {
  id: T
  visible: boolean
}

export const ALL_STATS_INSIGHT_IDS: StatsInsightId[] = [
  'periodDistance',
  'avgTripLength',
  'periodEnergy',
  'periodEfficiency',
  'climateUsage',
  'commutePattern',
  'batteryVoltageTrend',
  'parkingLocations',
  'electricShare',
  'avgSpeed',
]

export const ALL_STATS_CHART_IDS: StatsChartId[] = [
  'evChart',
  'tyreChart',
  'hybridSocChart',
  'dailyKwhChart',
]

export function defaultStatsInsights(): StatsItemConfig<StatsInsightId>[] {
  return ALL_STATS_INSIGHT_IDS.map((id) => ({ id, visible: true }))
}

export function defaultStatsCharts(): StatsItemConfig<StatsChartId>[] {
  return ALL_STATS_CHART_IDS.map((id) => ({ id, visible: true }))
}

export function loadStatsItems<T extends string>(raw: unknown, allIds: T[]): StatsItemConfig<T>[] {
  const result: StatsItemConfig<T>[] = []
  const seen = new Set<T>()
  if (Array.isArray(raw)) {
    for (const item of raw) {
      if (
        typeof item?.id === 'string' &&
        (allIds as string[]).includes(item.id) &&
        !seen.has(item.id as T)
      ) {
        result.push({ id: item.id as T, visible: item.visible !== false })
        seen.add(item.id as T)
      }
    }
  }
  for (const id of allIds) {
    if (!seen.has(id)) result.push({ id, visible: true })
  }
  return result
}

/**
 * Brings a stored card list up to date with the registry: keeps the known cards in their stored
 * order and visibility, drops ids that no longer exist, and appends cards added since the list was
 * saved with their default visibility.
 */
export function migrateCards(raw: { id: string; visible: boolean }[]): CardConfig[] {
  const expanded: CardConfig[] = []
  const usedIds = new Set<CardId>()
  const defaultVisibility = new Map(defaultCards('unknown').map((c) => [c.id, c.visible]))

  for (const c of raw) {
    if ((ALL_CARD_IDS as string[]).includes(c.id)) {
      expanded.push({ id: c.id as CardId, visible: c.visible })
      usedIds.add(c.id as CardId)
    }
  }

  for (const id of ALL_CARD_IDS) {
    if (!usedIds.has(id)) {
      expanded.push({ id, visible: defaultVisibility.get(id) ?? true })
    }
  }

  return expanded
}

export function osPreferredTheme(): Theme {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

/**
 * The interface language this browser asks for. Portuguese is European for Portugal and the other
 * countries that write it the European way (pt-PT, pt-AO, pt-MZ, ...), and Brazilian (`pt`) for
 * pt-BR and for a plain "pt", which names no country.
 */
export function browserLocale(): Locale {
  const lang = navigator.language.toLowerCase()
  if (lang.startsWith('nl')) return 'nl'
  if (lang.startsWith('es')) return 'es'
  if (lang.startsWith('pt-') && !lang.startsWith('pt-br')) return 'pt-PT'
  return lang.startsWith('pt') ? 'pt' : 'en'
}

// Pre-split combined settings blob (single "garagestack-settings" key holding everything).
// Each of the three settings stores (UI / Dashboard / Map) reads its own slice from here once,
// on first load after the split, then persists to its own dedicated key from then on. The old
// key is intentionally left in place rather than deleted - with three independent stores there's
// no single point that's guaranteed to run after all three have migrated.
const LEGACY_STORAGE_KEY = 'garagestack-settings'

export function readLegacyBlob(): Record<string, unknown> | null {
  try {
    const raw = localStorage.getItem(LEGACY_STORAGE_KEY)
    if (!raw) return null
    const parsed: unknown = JSON.parse(raw)
    return typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

/**
 * A settings store's starting state: this browser's copy under `key`, else the blob from before
 * the stores were split, else the defaults. A copy that cannot be read counts as none.
 */
export function loadStoredSettings<T>(
  key: string,
  parse: (stored: Record<string, unknown>) => T,
  defaults: () => T,
): T {
  try {
    const raw = localStorage.getItem(key)
    if (raw) return parse(JSON.parse(raw) as Record<string, unknown>)
  } catch {
    // An unreadable copy is no copy.
  }
  const legacy = readLegacyBlob()
  return legacy ? parse(legacy) : defaults()
}

/** Whether this browser has stored settings under `key`, or before the split, in the legacy blob. */
export function hasStoredCopy(key: string): boolean {
  try {
    return localStorage.getItem(key) !== null || readLegacyBlob() !== null
  } catch {
    return false
  }
}

// Coalesces bursts of ref changes (drag-reordering, dragging a slider that sets two refs in the
// same tick) into a single localStorage write instead of one write per ref per tick. Registers a
// pagehide flush so a change made right before closing the tab is never lost.
export function createDebouncedSave(save: () => void, delayMs = 300): () => void {
  let timer: ReturnType<typeof setTimeout> | null = null
  function scheduleSave() {
    if (timer !== null) clearTimeout(timer)
    timer = setTimeout(() => {
      timer = null
      save()
    }, delayMs)
  }
  window.addEventListener('pagehide', () => {
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
      save()
    }
  })
  return scheduleSave
}

export interface PersistOptions<T extends object> {
  /** Where this browser keeps its own copy: what the page starts with, and all there is while signed out. */
  storageKey: string
  /** Where the signed-in account keeps its copy. */
  section: SettingsSection
  state: T
  /** Reads the account's copy the way the store reads its own: unknown values fall back. */
  parse: (raw: Record<string, unknown>) => T
  /** Runs after each write to this browser's copy. */
  onSave?: () => void
  /** Runs when the account's copy has been taken in. */
  onAdopted?: () => void
  /** Keys kept in this browser only, never on the account. */
  localKeys?: readonly (keyof T & string)[]
}

/**
 * Keeps a settings store's `state` in this browser and on the signed-in account. Any change,
 * however deep, is written whole to localStorage once the burst it belongs to is over, and the
 * keys it touched then go to the account (see settingsSync).
 */
export function persistSettings<T extends object>(options: PersistOptions<T>): void {
  const { storageKey, section, state, parse, onSave, onAdopted, localKeys } = options
  const sync = useSettingsSyncStore()
  const scheduleSave = createDebouncedSave(() => {
    localStorage.setItem(storageKey, JSON.stringify(state))
    onSave?.()
    sync.push(section)
  })
  watch(state, scheduleSave, { deep: true })
  sync.register({
    section,
    state,
    parse,
    hasOwnCopy: () => hasStoredCopy(storageKey),
    onAdopted,
    localKeys,
  })
}
