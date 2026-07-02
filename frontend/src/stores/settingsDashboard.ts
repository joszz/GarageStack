import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { VehicleType } from './vehicle'
import type {
  CardConfig,
  CardId,
  StatsItemConfig,
  StatsInsightId,
  StatsChartId,
} from './settingsShared'
import {
  defaultCards,
  defaultStatsInsights,
  defaultStatsCharts,
  loadStatsItems,
  migrateCards,
  ALL_STATS_INSIGHT_IDS,
  ALL_STATS_CHART_IDS,
  readLegacyBlob,
  createDebouncedSave,
} from './settingsShared'

export type {
  CardId,
  CardConfig,
  StatsInsightId,
  StatsChartId,
  StatsItemConfig,
} from './settingsShared'
export { defaultCards, defaultStatsInsights, defaultStatsCharts }

const STORAGE_KEY = 'garagestack-settings-dashboard'

interface DashboardSettings {
  cards: CardConfig[]
  statsInsights: StatsItemConfig<StatsInsightId>[]
  statsCharts: StatsItemConfig<StatsChartId>[]
  showTyreDiagram: boolean
  showLocationMap: boolean
}

function defaultsFor(): DashboardSettings {
  return {
    cards: defaultCards('unknown'),
    statsInsights: defaultStatsInsights(),
    statsCharts: defaultStatsCharts(),
    showTyreDiagram: true,
    showLocationMap: true,
  }
}

// Ancient pre-"cards array" format, kept only so users who haven't opened the app since before
// that migration existed don't lose their card visibility on this second migration.
function cardsFromLegacyBlob(parsed: Record<string, unknown>): CardConfig[] {
  if (parsed.panels && !parsed.cards) {
    const p = parsed.panels as Record<string, boolean | undefined>
    const visMap: Partial<Record<CardId, boolean>> = {
      fuelLevel: p.showFuel,
      fuelRange: p.showFuel,
      evBattery: p.showEvBattery,
      charging: p.showCharging,
      sunRoof: p.showSunRoof,
      hvBattery: p.showHvPower,
      lights: p.showLights,
      efficiencyDistance: p.showEfficiency,
      efficiencyEnergy: p.showEfficiency,
      efficiencyCharge: p.showEfficiency,
      efficiencyRatio: p.showEfficiency,
    }
    return defaultCards('unknown').map((c) => ({ id: c.id, visible: visMap[c.id] ?? c.visible }))
  }
  if (Array.isArray(parsed.cards)) {
    return migrateCards(parsed.cards)
  }
  return defaultCards('unknown')
}

function loadDashboardSettings(): DashboardSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw)
      return {
        cards: Array.isArray(parsed.cards) ? migrateCards(parsed.cards) : defaultCards('unknown'),
        statsInsights: loadStatsItems(parsed.statsInsights, ALL_STATS_INSIGHT_IDS),
        statsCharts: loadStatsItems(parsed.statsCharts, ALL_STATS_CHART_IDS),
        showTyreDiagram: parsed.showTyreDiagram !== false,
        showLocationMap: parsed.showLocationMap !== false,
      }
    }
  } catch {
    // ignore parse errors
  }

  const legacy = readLegacyBlob()
  if (legacy) {
    return {
      cards: cardsFromLegacyBlob(legacy),
      statsInsights: loadStatsItems(legacy.statsInsights, ALL_STATS_INSIGHT_IDS),
      statsCharts: loadStatsItems(legacy.statsCharts, ALL_STATS_CHART_IDS),
      showTyreDiagram: legacy.showTyreDiagram !== false,
      showLocationMap: legacy.showLocationMap !== false,
    }
  }

  return defaultsFor()
}

export const useDashboardSettingsStore = defineStore('settingsDashboard', () => {
  const loaded = loadDashboardSettings()
  const cards = ref<CardConfig[]>(loaded.cards)
  const statsInsights = ref<StatsItemConfig<StatsInsightId>[]>(loaded.statsInsights)
  const statsCharts = ref<StatsItemConfig<StatsChartId>[]>(loaded.statsCharts)
  const showTyreDiagram = ref<boolean>(loaded.showTyreDiagram)
  const showLocationMap = ref<boolean>(loaded.showLocationMap)

  function save() {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        cards: cards.value,
        statsInsights: statsInsights.value,
        statsCharts: statsCharts.value,
        showTyreDiagram: showTyreDiagram.value,
        showLocationMap: showLocationMap.value,
      }),
    )
  }
  const scheduleSave = createDebouncedSave(save)

  watch(cards, scheduleSave, { deep: true })
  watch(statsInsights, scheduleSave, { deep: true })
  watch(statsCharts, scheduleSave, { deep: true })
  watch(showTyreDiagram, scheduleSave)
  watch(showLocationMap, scheduleSave)

  function resetCards(type: VehicleType | 'unknown' = 'unknown') {
    cards.value = defaultCards(type)
  }

  return {
    cards,
    statsInsights,
    statsCharts,
    showTyreDiagram,
    showLocationMap,
    resetCards,
  }
})
