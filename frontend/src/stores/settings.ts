import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { VehicleType } from './vehicle'

export type VehicleTypeOverride = 'auto' | VehicleType
export type Theme = 'dark' | 'light'
export type Locale = 'en' | 'nl'

export type StatsInsightId =
  | 'periodDistance' | 'avgTripLength' | 'climateUsage'
  | 'commutePattern' | 'batteryVoltageTrend' | 'parkingLocations' | 'electricShare'

export type StatsChartId = 'fuelChart' | 'evChart' | 'tyreChart' | 'hybridSocChart' | 'dailyKwhChart'

export interface StatsItemConfig<T extends string> { id: T; visible: boolean }

const ALL_STATS_INSIGHT_IDS: StatsInsightId[] = [
  'periodDistance', 'avgTripLength', 'climateUsage',
  'commutePattern', 'batteryVoltageTrend', 'parkingLocations', 'electricShare',
]

const ALL_STATS_CHART_IDS: StatsChartId[] = [
  'fuelChart', 'evChart', 'tyreChart', 'hybridSocChart', 'dailyKwhChart',
]

export function defaultStatsInsights(): StatsItemConfig<StatsInsightId>[] {
  return ALL_STATS_INSIGHT_IDS.map(id => ({ id, visible: true }))
}

export function defaultStatsCharts(): StatsItemConfig<StatsChartId>[] {
  return ALL_STATS_CHART_IDS.map(id => ({ id, visible: true }))
}

function loadStatsItems<T extends string>(raw: unknown, allIds: T[]): StatsItemConfig<T>[] {
  const result: StatsItemConfig<T>[] = []
  const seen = new Set<T>()
  if (Array.isArray(raw)) {
    for (const item of raw) {
      if (typeof item?.id === 'string' && (allIds as string[]).includes(item.id) && !seen.has(item.id as T)) {
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

export type CardId =
  | 'fuelLevel' | 'fuelRange'
  | 'evBattery' | 'charging'
  | 'odometer' | 'battery12v'
  | 'doors' | 'windows' | 'sunRoof'
  | 'climate' | 'hvBattery' | 'findMyCar' | 'lights'
  | 'efficiencyDistance' | 'efficiencyEnergy' | 'efficiencyCharge' | 'efficiencyRatio'

const ALL_CARD_IDS: CardId[] = [
  'fuelLevel', 'fuelRange', 'evBattery', 'charging', 'odometer', 'battery12v',
  'doors', 'windows', 'sunRoof', 'climate', 'hvBattery', 'findMyCar', 'lights',
  'efficiencyDistance', 'efficiencyEnergy', 'efficiencyCharge', 'efficiencyRatio',
]

export interface CardConfig {
  id: CardId
  visible: boolean
}

export interface AppSettings {
  cards: CardConfig[]
  statsInsights: StatsItemConfig<StatsInsightId>[]
  statsCharts: StatsItemConfig<StatsChartId>[]
  vehicleTypeOverride: VehicleTypeOverride
  theme: Theme
  locale: Locale
  filterDays: number
}

function osPreferredTheme(): Theme {
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

function browserLocale(): Locale {
  const lang = navigator.language.toLowerCase()
  return lang.startsWith('nl') ? 'nl' : 'en'
}

export function defaultCards(type: VehicleType | 'unknown' = 'unknown'): CardConfig[] {
  const all: CardConfig[] = [
    { id: 'fuelLevel',          visible: type !== 'bev' },
    { id: 'fuelRange',          visible: type !== 'bev' },
    { id: 'evBattery',          visible: true },
    { id: 'charging',           visible: type === 'phev' || type === 'bev' },
    { id: 'odometer',           visible: true },
    { id: 'battery12v',         visible: true },
    { id: 'doors',              visible: true },
    { id: 'windows',            visible: true },
    { id: 'sunRoof',            visible: false },
    { id: 'climate',            visible: true },
    { id: 'hvBattery',          visible: true },
    { id: 'findMyCar',          visible: true },
    { id: 'lights',             visible: true },
    { id: 'efficiencyDistance', visible: true },
    { id: 'efficiencyEnergy',   visible: true },
    { id: 'efficiencyCharge',   visible: type !== 'hev' },
    { id: 'efficiencyRatio',    visible: true },
  ]
  return [...all.filter(c => c.visible), ...all.filter(c => !c.visible)]
}

const BASE_STORAGE_KEY = 'garagestack-settings'

const defaults: AppSettings = {
  cards: defaultCards('unknown'),
  statsInsights: defaultStatsInsights(),
  statsCharts: defaultStatsCharts(),
  vehicleTypeOverride: 'auto',
  theme: osPreferredTheme(),
  locale: browserLocale(),
  filterDays: 7,
}

function migrateCards(raw: { id: string; visible: boolean }[]): CardConfig[] {
  const expanded: CardConfig[] = []
  const usedNewIds = new Set<CardId>()
  const rawIds = new Set(raw.map(c => c.id))
  const defaultVisibility = new Map(defaultCards('unknown').map(c => [c.id, c.visible]))

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

function loadFromKey(key: string): AppSettings {
  try {
    const raw = localStorage.getItem(key)
    if (raw) {
      const parsed = JSON.parse(raw)

      if (parsed.panels && !parsed.cards) {
        const p = parsed.panels
        const visMap: Partial<Record<CardId, boolean>> = {
          fuelLevel:          p.showFuel,
          fuelRange:          p.showFuel,
          evBattery:          p.showEvBattery,
          charging:           p.showCharging,
          sunRoof:            p.showSunRoof,
          hvBattery:          p.showHvPower,
          lights:             p.showLights,
          efficiencyDistance: p.showEfficiency,
          efficiencyEnergy:   p.showEfficiency,
          efficiencyCharge:   p.showEfficiency,
          efficiencyRatio:    p.showEfficiency,
        }
        return {
          cards: defaultCards('unknown').map(c => ({ id: c.id, visible: visMap[c.id] ?? c.visible })),
          statsInsights: defaultStatsInsights(),
          statsCharts: defaultStatsCharts(),
          vehicleTypeOverride: parsed.vehicleTypeOverride ?? defaults.vehicleTypeOverride,
          theme: parsed.theme ?? defaults.theme,
          locale: parsed.locale ?? defaults.locale,
          filterDays: parsed.filterDays ?? defaults.filterDays,
        }
      }

      if (Array.isArray(parsed.cards)) {
        return {
          cards: migrateCards(parsed.cards),
          statsInsights: loadStatsItems(parsed.statsInsights, ALL_STATS_INSIGHT_IDS),
          statsCharts: loadStatsItems(parsed.statsCharts, ALL_STATS_CHART_IDS),
          vehicleTypeOverride: parsed.vehicleTypeOverride ?? defaults.vehicleTypeOverride,
          theme: parsed.theme ?? defaults.theme,
          locale: parsed.locale ?? defaults.locale,
          filterDays: parsed.filterDays ?? defaults.filterDays,
        }
      }
    }
  } catch {
    // ignore parse errors
  }

  try {
    const oldRaw = localStorage.getItem('garagestack-panel-settings')
    if (oldRaw) {
      localStorage.removeItem('garagestack-panel-settings')
    }
  } catch {
    // ignore
  }

  return { ...defaults, cards: [...defaults.cards] }
}

export const useSettingsStore = defineStore('settings', () => {
  const loaded = loadFromKey(BASE_STORAGE_KEY)
  const cards = ref<CardConfig[]>(loaded.cards)
  const vehicleTypeOverride = ref<VehicleTypeOverride>(loaded.vehicleTypeOverride)
  const theme = ref<Theme>(loaded.theme)
  const locale = ref<Locale>(loaded.locale)
  const filterDays = ref<number>(loaded.filterDays)
  const statsInsights = ref<StatsItemConfig<StatsInsightId>[]>(loaded.statsInsights)
  const statsCharts = ref<StatsItemConfig<StatsChartId>[]>(loaded.statsCharts)

  document.documentElement.dataset.theme = theme.value

  function save() {
    localStorage.setItem(BASE_STORAGE_KEY, JSON.stringify({
      cards: cards.value,
      statsInsights: statsInsights.value,
      statsCharts: statsCharts.value,
      vehicleTypeOverride: vehicleTypeOverride.value,
      theme: theme.value,
      locale: locale.value,
      filterDays: filterDays.value,
    }))
  }

  watch(cards, save, { deep: true })
  watch(statsInsights, save, { deep: true })
  watch(statsCharts, save, { deep: true })
  watch(vehicleTypeOverride, save)
  watch(locale, save)
  watch(filterDays, save)
  watch(theme, (val) => {
    document.documentElement.dataset.theme = val
    save()
  })

  function resetCards(type: VehicleType | 'unknown' = 'unknown') {
    cards.value = defaultCards(type)
  }

  return { cards, statsInsights, statsCharts, vehicleTypeOverride, theme, locale, filterDays, resetCards }
})
