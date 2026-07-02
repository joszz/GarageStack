import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import { defaultCards } from '@/stores/settingsShared'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useMapSettingsStore } from '@/stores/settingsMap'

// Settings persistence is debounced (see createDebouncedSave in settingsShared.ts) so a burst of
// ref changes coalesces into one localStorage write - advance fake timers past the debounce
// window to observe it.
const SAVE_DEBOUNCE_MS = 300

const LEGACY_KEY = 'garagestack-settings'
const UI_KEY = 'garagestack-settings-ui'
const DASHBOARD_KEY = 'garagestack-settings-dashboard'
const MAP_KEY = 'garagestack-settings-map'

describe('defaultCards', () => {
  it('includes all 22 card ids', () => {
    expect(defaultCards()).toHaveLength(23)
  })

  it('places visible cards before hidden ones', () => {
    const cards = defaultCards('bev')
    const firstHidden = cards.findIndex((c) => !c.visible)
    const cardsFromFirstHidden = firstHidden >= 0 ? cards.slice(firstHidden) : []
    expect(cardsFromFirstHidden.some((c) => c.visible)).toBe(false)
  })

  it('hev: hides charging, efficiencyCharge, and plug-only cards; shows fuel', () => {
    const cards = defaultCards('hev')
    expect(cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'charging')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'efficiencyCharge')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'remainingCharge')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'chargingSession')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'batteryHeating')!.visible).toBe(false)
  })

  it('bev: hides speed by default (already shown by the overview gauge); shows activeTrip, remainingCharge, chargingSession, batteryHeating', () => {
    const cards = defaultCards('bev')
    expect(cards.find((c) => c.id === 'speed')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'activeTrip')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'remainingCharge')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'chargingSession')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'batteryHeating')!.visible).toBe(true)
  })

  it('bev: hides fuel cards, shows charging and efficiencyCharge', () => {
    const cards = defaultCards('bev')
    expect(cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'fuelRange')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'charging')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'efficiencyCharge')!.visible).toBe(true)
  })

  it('phev: shows both fuel and charging', () => {
    const cards = defaultCards('phev')
    expect(cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(true)
    expect(cards.find((c) => c.id === 'charging')!.visible).toBe(true)
  })

  it('unknown: hides charging, shows fuel', () => {
    const cards = defaultCards('unknown')
    expect(cards.find((c) => c.id === 'charging')!.visible).toBe(false)
    expect(cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(true)
  })
})

describe('useUiSettingsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('defaults to dark theme when matchMedia reports no light preference', () => {
    const store = useUiSettingsStore()
    expect(store.theme).toBe('dark')
  })

  it('persists theme change to its own localStorage key', async () => {
    const store = useUiSettingsStore()
    store.theme = 'light'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved.theme).toBe('light')
  })

  it('persists locale change to its own localStorage key', async () => {
    const store = useUiSettingsStore()
    store.locale = 'nl'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved.locale).toBe('nl')
  })

  it('falls back to defaults when localStorage is empty', () => {
    const store = useUiSettingsStore()
    expect(store.vehicleTypeOverride).toBe('auto')
    expect(store.filterDays).toBe(7)
  })

  it('loads settings from its own localStorage key on init', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({
        vehicleTypeOverride: 'bev',
        theme: 'light',
        locale: 'nl',
        filterDays: 14,
      }),
    )
    const store = useUiSettingsStore()
    expect(store.theme).toBe('light')
    expect(store.locale).toBe('nl')
    expect(store.vehicleTypeOverride).toBe('bev')
    expect(store.filterDays).toBe(14)
  })
})

describe('useMapSettingsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('defaults to heatmap on, everything else off', () => {
    const store = useMapSettingsStore()
    expect(store.heatmapEnabled).toBe(true)
    expect(store.routeOutlineEnabled).toBe(false)
    expect(store.chargingStationsEnabled).toBe(false)
  })

  it('persists a field change to its own localStorage key', async () => {
    const store = useMapSettingsStore()
    store.routeOutlineEnabled = true
    store.chargingMinPowerKw = 50
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(MAP_KEY)!)
    expect(saved.routeOutlineEnabled).toBe(true)
    expect(saved.chargingMinPowerKw).toBe(50)
  })
})

describe('useDashboardSettingsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('persists location map visibility change to its own localStorage key', async () => {
    const store = useDashboardSettingsStore()
    store.showLocationMap = false
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(DASHBOARD_KEY)!)
    expect(saved.showLocationMap).toBe(false)
  })

  it('defaults showLocationMap to true when localStorage is empty', () => {
    const store = useDashboardSettingsStore()
    expect(store.showLocationMap).toBe(true)
  })

  it('persists card visibility changes to its own localStorage key', async () => {
    const store = useDashboardSettingsStore()
    const firstCard = store.cards[0]
    expect(firstCard).toBeDefined()
    if (!firstCard) throw new Error('Expected a first card to exist')
    firstCard.visible = !firstCard.visible
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(DASHBOARD_KEY)!)
    const savedFirstCard = saved.cards[0]
    expect(savedFirstCard).toBeDefined()
    if (!savedFirstCard) throw new Error('Expected a saved first card to exist')
    expect(savedFirstCard.visible).toBe(firstCard.visible)
  })

  it('falls back to defaults when localStorage is empty', () => {
    const store = useDashboardSettingsStore()
    expect(store.cards).toHaveLength(23)
    expect(store.cards.find((c) => c.id === 'sunRoof')!.visible).toBe(false)
  })

  describe('card migration', () => {
    it('expands legacy fuel card into fuelLevel + fuelRange with same visibility', () => {
      localStorage.setItem(
        DASHBOARD_KEY,
        JSON.stringify({
          cards: [
            { id: 'fuel', visible: false },
            { id: 'odometer', visible: true },
          ],
        }),
      )
      const store = useDashboardSettingsStore()
      expect(store.cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'fuelRange')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'odometer')!.visible).toBe(true)
    })

    it('expands legacy efficiency card into four efficiency cards', () => {
      localStorage.setItem(
        DASHBOARD_KEY,
        JSON.stringify({
          cards: [{ id: 'efficiency', visible: false }],
        }),
      )
      const store = useDashboardSettingsStore()
      expect(store.cards.find((c) => c.id === 'efficiencyDistance')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'efficiencyEnergy')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'efficiencyCharge')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'efficiencyRatio')!.visible).toBe(false)
    })

    it('migrates legacy doors card and inserts windows alongside it', () => {
      localStorage.setItem(
        DASHBOARD_KEY,
        JSON.stringify({
          cards: [{ id: 'doors', visible: false }],
        }),
      )
      const store = useDashboardSettingsStore()
      expect(store.cards.find((c) => c.id === 'doors')!.visible).toBe(false)
      expect(store.cards.find((c) => c.id === 'windows')).toBeTruthy()
    })

    it('appends newly introduced cards using their default visibility when migrating old data', () => {
      // A saved list that only has known-new ids but is missing some
      localStorage.setItem(
        DASHBOARD_KEY,
        JSON.stringify({
          cards: [{ id: 'odometer', visible: true }],
        }),
      )
      const store = useDashboardSettingsStore()
      // All 23 card ids should be present after migration fills in the gaps
      expect(store.cards).toHaveLength(23)
      expect(store.cards.find((c) => c.id === 'sunRoof')!.visible).toBe(false)
    })
  })
})

// Before the settings store was split into three (UI / Dashboard / Map), everything lived under
// one "garagestack-settings" blob. Each new store falls back to reading its own slice out of that
// legacy blob the first time it runs (its own dedicated key doesn't exist yet).
describe('legacy combined-blob migration', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('splits a pre-split combined blob across all three stores', () => {
    localStorage.setItem(
      LEGACY_KEY,
      JSON.stringify({
        cards: defaultCards('bev'),
        vehicleTypeOverride: 'bev',
        theme: 'light',
        locale: 'nl',
        filterDays: 14,
        showLocationMap: false,
        routeOutlineEnabled: true,
        heatmapEnabled: false,
        chargingMinPowerKw: 22,
      }),
    )

    const ui = useUiSettingsStore()
    expect(ui.theme).toBe('light')
    expect(ui.locale).toBe('nl')
    expect(ui.vehicleTypeOverride).toBe('bev')
    expect(ui.filterDays).toBe(14)

    const dashboard = useDashboardSettingsStore()
    expect(dashboard.showLocationMap).toBe(false)
    expect(dashboard.cards.find((c) => c.id === 'charging')!.visible).toBe(true)

    const map = useMapSettingsStore()
    expect(map.routeOutlineEnabled).toBe(true)
    expect(map.heatmapEnabled).toBe(false)
    expect(map.chargingMinPowerKw).toBe(22)
  })

  it('migrates the ancient panels object format into the dashboard store', () => {
    localStorage.setItem(
      LEGACY_KEY,
      JSON.stringify({
        panels: {
          showFuel: false,
          showEvBattery: true,
          showCharging: false,
          showHvPower: true,
          showLights: false,
          showEfficiency: true,
          showSunRoof: true,
        },
        theme: 'light',
      }),
    )
    const dashboard = useDashboardSettingsStore()
    expect(dashboard.cards.find((c) => c.id === 'fuelLevel')!.visible).toBe(false)
    expect(dashboard.cards.find((c) => c.id === 'fuelRange')!.visible).toBe(false)
    expect(dashboard.cards.find((c) => c.id === 'efficiencyDistance')!.visible).toBe(true)
    expect(dashboard.cards.find((c) => c.id === 'sunRoof')!.visible).toBe(true)

    const ui = useUiSettingsStore()
    expect(ui.theme).toBe('light')
  })

  it('does not touch the legacy key once split (each store writes to its own key)', async () => {
    localStorage.setItem(LEGACY_KEY, JSON.stringify({ theme: 'light' }))
    const ui = useUiSettingsStore()
    ui.locale = 'nl'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    // The legacy blob is left in place (not deleted), and the new key now holds the live state.
    expect(localStorage.getItem(LEGACY_KEY)).not.toBeNull()
    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved.locale).toBe('nl')
    expect(saved.theme).toBe('light')
  })
})
