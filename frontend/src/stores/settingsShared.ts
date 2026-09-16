import type { VehicleType } from './vehicle'
import { ALL_CARD_IDS, defaultCards } from '@/cards/registry'
import type { CardConfig, CardId } from '@/cards/registry'

// The card registry owns what a card is (icon, defaults, data predicate); the settings stores
// own which ones this browser shows and in what order. Re-exported so store consumers keep a
// single import for both halves.
export { ALL_CARD_IDS, defaultCards }
export type { CardConfig, CardId }

export type VehicleTypeOverride = 'auto' | VehicleType
export type Theme = 'dark' | 'light'
export type Locale = 'en' | 'nl'

export interface CarColorScheme {
  id: string
  primary: string
  secondary: string
}

export const CAR_COLOR_SCHEMES: CarColorScheme[] = [
  { id: 'orange', primary: '#f9b233', secondary: '#f39200' },
  { id: 'red', primary: '#e53935', secondary: '#c62828' },
  { id: 'blue', primary: '#1e88e5', secondary: '#1565c0' },
  { id: 'silver', primary: '#cfd8dc', secondary: '#90a4ae' },
  { id: 'white', primary: '#eceff1', secondary: '#b0bec5' },
  { id: 'anthracite', primary: '#455a64', secondary: '#263238' },
  { id: 'green', primary: '#43a047', secondary: '#2e7d32' },
  { id: 'purple', primary: '#8e24aa', secondary: '#6a1b9a' },
]

export type StatsInsightId =
  | 'periodDistance'
  | 'avgTripLength'
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

export function migrateCards(raw: { id: string; visible: boolean }[]): CardConfig[] {
  const expanded: CardConfig[] = []
  const usedNewIds = new Set<CardId>()
  const rawIds = new Set(raw.map((c) => c.id))
  const defaultVisibility = new Map(defaultCards('unknown').map((c) => [c.id, c.visible]))

  for (const c of raw) {
    switch (c.id) {
      case 'fuel':
        if (!rawIds.has('fuelLevel')) {
          expanded.push({ id: 'fuelLevel', visible: c.visible })
          usedNewIds.add('fuelLevel')
        }
        if (!rawIds.has('fuelRange')) {
          expanded.push({ id: 'fuelRange', visible: c.visible })
          usedNewIds.add('fuelRange')
        }
        break
      case 'doors':
        expanded.push({ id: 'doors', visible: c.visible })
        usedNewIds.add('doors')
        if (!rawIds.has('windows')) {
          expanded.push({ id: 'windows', visible: c.visible })
          usedNewIds.add('windows')
        }
        break
      case 'efficiency':
        if (!rawIds.has('efficiencyDistance')) {
          expanded.push({ id: 'efficiencyDistance', visible: c.visible })
          usedNewIds.add('efficiencyDistance')
        }
        if (!rawIds.has('efficiencyEnergy')) {
          expanded.push({ id: 'efficiencyEnergy', visible: c.visible })
          usedNewIds.add('efficiencyEnergy')
        }
        if (!rawIds.has('efficiencyCharge')) {
          expanded.push({ id: 'efficiencyCharge', visible: c.visible })
          usedNewIds.add('efficiencyCharge')
        }
        if (!rawIds.has('efficiencyRatio')) {
          expanded.push({ id: 'efficiencyRatio', visible: c.visible })
          usedNewIds.add('efficiencyRatio')
        }
        break
      default:
        if ((ALL_CARD_IDS as string[]).includes(c.id)) {
          expanded.push({ id: c.id as CardId, visible: c.visible })
          usedNewIds.add(c.id as CardId)
        }
    }
  }

  for (const id of ALL_CARD_IDS) {
    if (!usedNewIds.has(id)) {
      expanded.push({ id, visible: defaultVisibility.get(id) ?? true })
    }
  }

  return expanded
}

export function osPreferredTheme(): Theme {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

export function browserLocale(): Locale {
  const lang = navigator.language.toLowerCase()
  return lang.startsWith('nl') ? 'nl' : 'en'
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
