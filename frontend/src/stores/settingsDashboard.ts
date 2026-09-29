import { defineStore } from 'pinia'
import { reactive, ref, toRefs } from 'vue'
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
  persistSettings,
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

function storedCards(parsed: Record<string, unknown>): CardConfig[] {
  return Array.isArray(parsed.cards) ? migrateCards(parsed.cards) : defaultCards('unknown')
}

function loadDashboardSettings(): DashboardSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) {
      const parsed = JSON.parse(raw)
      return {
        cards: storedCards(parsed),
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
      cards: storedCards(legacy),
      statsInsights: loadStatsItems(legacy.statsInsights, ALL_STATS_INSIGHT_IDS),
      statsCharts: loadStatsItems(legacy.statsCharts, ALL_STATS_CHART_IDS),
      showTyreDiagram: legacy.showTyreDiagram !== false,
      showLocationMap: legacy.showLocationMap !== false,
    }
  }

  return defaultsFor()
}

// Card ids whose default visibility differs between drivetrains, derived from defaultCards. When
// the drivetrain changes, these follow the new type's defaults.
const TYPE_SPECIFIC_CARD_IDS: ReadonlySet<CardId> = (() => {
  const knownTypes: VehicleType[] = ['hev', 'phev', 'bev']
  const unknownMap = new Map(defaultCards('unknown').map((c) => [c.id, c.visible]))
  return new Set(
    knownTypes.flatMap((type) =>
      defaultCards(type)
        .filter((c) => c.visible !== unknownMap.get(c.id))
        .map((c) => c.id),
    ),
  )
})()

/**
 * Cards that do not apply to a drivetrain at all (fuel on a BEV). Cards that are merely off by
 * default for every type (the sunroof, the speed) are not among them, so they stay the user's to
 * switch on and keep that choice across reloads.
 */
export function cardsHiddenByType(type: VehicleType): ReadonlySet<CardId> {
  if (type === 'unknown') return new Set()
  return new Set(
    defaultCards(type)
      .filter((c) => !c.visible && TYPE_SPECIFIC_CARD_IDS.has(c.id))
      .map((c) => c.id),
  )
}

function visibleFirst(cards: CardConfig[]): CardConfig[] {
  return [...cards.filter((c) => c.visible), ...cards.filter((c) => !c.visible)]
}

// Visible cards that have something to show first, then visible but empty ones, then hidden ones.
function byData(cards: CardConfig[], hasData: (id: CardId) => boolean): CardConfig[] {
  return [
    ...cards.filter((c) => c.visible && hasData(c.id)),
    ...cards.filter((c) => c.visible && !hasData(c.id)),
    ...cards.filter((c) => !c.visible),
  ]
}

// Whether this browser has ever stored a dashboard layout, under the current key or the
// pre-split blob. The dashboard uses it to tell a first visit (where it may order cards by
// which ones have data) from a returning user whose ordering must be left alone.
function hasPersistedLayout(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) !== null || readLegacyBlob() !== null
  } catch {
    return false
  }
}

export const useDashboardSettingsStore = defineStore('settingsDashboard', () => {
  const settings = reactive(loadDashboardSettings())
  const hasSavedLayout = ref(hasPersistedLayout())
  persistSettings(STORAGE_KEY, settings, () => {
    hasSavedLayout.value = true
  })

  function resetCards(type: VehicleType | 'unknown' = 'unknown') {
    settings.cards = defaultCards(type)
  }

  /** Hides the cards that do not apply to the drivetrain; the user's other choices stay. */
  function hideCardsNotApplicable(type: VehicleType) {
    const hidden = cardsHiddenByType(type)
    if (!settings.cards.some((c) => hidden.has(c.id) && c.visible)) return
    settings.cards = visibleFirst(
      settings.cards.map((c) => (hidden.has(c.id) ? { ...c, visible: false } : c)),
    )
  }

  /** After a drivetrain change: the cards that depend on it take the new type's defaults. */
  function applyTypeDefaults(type: VehicleType) {
    if (type === 'unknown') return
    const defaults = new Map(defaultCards(type).map((c) => [c.id, c.visible]))
    settings.cards = visibleFirst(
      settings.cards.map((c) =>
        TYPE_SPECIFIC_CARD_IDS.has(c.id) ? { ...c, visible: defaults.get(c.id) ?? c.visible } : c,
      ),
    )
  }

  /**
   * Shows or hides a card. A hidden card moves behind the visible ones, and a card shown again
   * goes to the end of them, so the visible order the user arranged stays as it was.
   */
  function toggleCard(id: CardId) {
    const cards = settings.cards
    const idx = cards.findIndex((c) => c.id === id)
    if (idx === -1) return
    const [card] = cards.splice(idx, 1)
    card!.visible = !card!.visible
    if (!card!.visible) {
      cards.push(card!)
    } else {
      const firstHidden = cards.findIndex((c) => !c.visible)
      cards.splice(firstHidden === -1 ? cards.length : firstHidden, 0, card!)
    }
  }

  /**
   * The order the edit grid offers: the cards that apply to the drivetrain. Setting it puts the
   * ones that do not apply back behind them.
   */
  function applicableCards(type: VehicleType): CardConfig[] {
    const hidden = cardsHiddenByType(type)
    return settings.cards.filter((c) => !hidden.has(c.id))
  }

  function setApplicableCards(type: VehicleType, cards: CardConfig[]) {
    const hidden = cardsHiddenByType(type)
    settings.cards = [...cards, ...settings.cards.filter((c) => hidden.has(c.id))]
  }

  /** Puts the cards with something to show first, for a first visit. */
  function orderByData(hasData: (id: CardId) => boolean) {
    settings.cards = byData(settings.cards, hasData)
  }

  /** Back to the drivetrain's default layout, with the tyre diagram showing again. */
  function resetLayout(type: VehicleType, hasData: (id: CardId) => boolean) {
    settings.cards = byData(defaultCards(type), hasData)
    settings.showTyreDiagram = true
  }

  return {
    ...toRefs(settings),
    hasSavedLayout,
    resetCards,
    hideCardsNotApplicable,
    applyTypeDefaults,
    toggleCard,
    applicableCards,
    setApplicableCards,
    orderByData,
    resetLayout,
  }
})
