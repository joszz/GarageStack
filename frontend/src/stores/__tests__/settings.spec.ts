import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import { defaultCards } from '@/stores/settingsShared'
import { ALL_CARD_IDS } from '@/cards/registry'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { METRIC_UNITS } from '@/utils/units'

// Settings persistence is debounced (see createDebouncedSave in settingsShared.ts) so a burst of
// ref changes coalesces into one localStorage write - advance fake timers past the debounce
// window to observe it.
const SAVE_DEBOUNCE_MS = 300

const LEGACY_KEY = 'garagestack-settings'
const UI_KEY = 'garagestack-settings-ui'
const DASHBOARD_KEY = 'garagestack-settings-dashboard'
const MAP_KEY = 'garagestack-settings-map'

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

  it('applies the theme to the document at once, before it is saved', async () => {
    const store = useUiSettingsStore()
    expect(document.documentElement.dataset.theme).toBe('dark')

    store.theme = 'light'
    await nextTick()

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(localStorage.getItem(UI_KEY)).toBeNull()
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

  it('defaults placeNamesEnabled to on', () => {
    const store = useUiSettingsStore()
    expect(store.placeNamesEnabled).toBe(true)
  })

  it('keeps place names on for a stored blob written before the setting existed', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ theme: 'light' }))
    const store = useUiSettingsStore()
    expect(store.placeNamesEnabled).toBe(true)
  })

  it('persists placeNamesEnabled being turned off, and loads it back', async () => {
    const store = useUiSettingsStore()
    store.placeNamesEnabled = false
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).placeNamesEnabled).toBe(false)

    setActivePinia(createPinia())
    expect(useUiSettingsStore().placeNamesEnabled).toBe(false)
  })

  it('keeps maps themed unless colourful maps were turned on', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ theme: 'light' }))
    expect(useUiSettingsStore().colorfulMaps).toBe(false)
  })

  it('persists colourful maps being turned on, and loads it back', async () => {
    const store = useUiSettingsStore()
    store.colorfulMaps = true
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).colorfulMaps).toBe(true)

    setActivePinia(createPinia())
    expect(useUiSettingsStore().colorfulMaps).toBe(true)
  })

  it('defaults notificationTypeExclusions to an empty array (no exclusions, show all)', () => {
    const store = useUiSettingsStore()
    expect(store.notificationTypeExclusions).toEqual([])
  })

  it('persists notificationTypeExclusions changes to its own localStorage key', async () => {
    const store = useUiSettingsStore()
    store.notificationTypeExclusions = ['low-tyre', 'maintenance']
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved.notificationTypeExclusions).toEqual(['low-tyre', 'maintenance'])
  })

  it('loads notificationTypeExclusions from its own localStorage key on init', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ notificationTypeExclusions: ['charging-complete'] }),
    )
    const store = useUiSettingsStore()
    expect(store.notificationTypeExclusions).toEqual(['charging-complete'])
  })

  it('shows metric units until the browser asks for others', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ theme: 'light' }))
    expect(useUiSettingsStore().units).toEqual(METRIC_UNITS)
  })

  it('persists a unit change, and loads it back', async () => {
    const store = useUiSettingsStore()
    store.units.distance = 'mi'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).units.distance).toBe('mi')

    setActivePinia(createPinia())
    expect(useUiSettingsStore().units).toEqual({ ...METRIC_UNITS, distance: 'mi' })
  })

  it('falls back per unit, keeping the stored units it recognises', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ units: { distance: 'mi', temperature: 'kelvin', pressure: 'psi' } }),
    )
    expect(useUiSettingsStore().units).toEqual({
      distance: 'mi',
      temperature: 'celsius',
      pressure: 'psi',
      fuelConsumption: 'l100km',
    })
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
    expect(store.speedCamerasEnabled).toBe(false)
  })

  it('persists a field change to its own localStorage key', async () => {
    const store = useMapSettingsStore()
    store.routeOutlineEnabled = true
    store.chargingMinPowerKw = 50
    store.fuelTypeFilter = ['diesel']
    store.speedCamerasEnabled = true
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    const saved = JSON.parse(localStorage.getItem(MAP_KEY)!)
    expect(saved.routeOutlineEnabled).toBe(true)
    expect(saved.chargingMinPowerKw).toBe(50)
    expect(saved.fuelTypeFilter).toEqual(['diesel'])
    expect(saved.speedCamerasEnabled).toBe(true)
  })

  it('persists a change made inside a list, and writes a burst of changes once', async () => {
    const store = useMapSettingsStore()
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    store.fuelBrandFilter.push('Shell')
    store.chargingMinPowerKw = 50
    store.chargingMaxPowerKw = 150
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)

    expect(setItem.mock.calls.filter(([key]) => key === MAP_KEY)).toHaveLength(1)
    const saved = JSON.parse(localStorage.getItem(MAP_KEY)!)
    expect(saved.fuelBrandFilter).toEqual(['Shell'])
    expect(saved.chargingMaxPowerKw).toBe(150)
    setItem.mockRestore()
  })

  it('drops fuel types it does not know when restoring, so the layer cannot silently empty', () => {
    localStorage.setItem(
      MAP_KEY,
      JSON.stringify({ fuelTypeFilter: ['diesel', 'kerosene', 42, 'petrol'] }),
    )
    const store = useMapSettingsStore()
    expect(store.fuelTypeFilter).toEqual(['diesel', 'petrol'])
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
    expect(store.cards).toHaveLength(ALL_CARD_IDS.length)
    expect(store.cards.find((c) => c.id === 'sunRoof')!.visible).toBe(false)
  })

  describe('stored card list', () => {
    it('appends newly introduced cards using their default visibility when migrating old data', () => {
      // A saved list that only has known-new ids but is missing some
      localStorage.setItem(
        DASHBOARD_KEY,
        JSON.stringify({
          cards: [{ id: 'odometer', visible: true }],
        }),
      )
      const store = useDashboardSettingsStore()
      // Every registered card id should be present after migration fills in the gaps
      expect(store.cards).toHaveLength(ALL_CARD_IDS.length)
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

describe('useDashboardSettingsStore - hasSavedLayout', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('is false on a first visit with nothing stored', () => {
    expect(useDashboardSettingsStore().hasSavedLayout).toBe(false)
  })

  it('is true when a layout was stored under the current key', () => {
    localStorage.setItem(DASHBOARD_KEY, JSON.stringify({ cards: [] }))
    expect(useDashboardSettingsStore().hasSavedLayout).toBe(true)
  })

  it('is true for a returning user whose layout still sits in the legacy blob', () => {
    localStorage.setItem(LEGACY_KEY, JSON.stringify({ cards: [] }))
    expect(useDashboardSettingsStore().hasSavedLayout).toBe(true)
  })

  it('flips to true once the first change has been persisted', async () => {
    const dashboard = useDashboardSettingsStore()
    expect(dashboard.hasSavedLayout).toBe(false)
    dashboard.showLocationMap = false
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    expect(dashboard.hasSavedLayout).toBe(true)
  })
})

describe('useDashboardSettingsStore - layout', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  const ids = (cards: { id: string }[]) => cards.map((c) => c.id)
  const visible = (cards: { id: string; visible: boolean }[]) => ids(cards.filter((c) => c.visible))

  it('hides the cards a drivetrain has no use for, and leaves the rest alone', () => {
    const dashboard = useDashboardSettingsStore()
    dashboard.cards = defaultCards('unknown').map((c) =>
      c.id === 'speed' ? { ...c, visible: true } : c,
    )

    dashboard.hideCardsNotApplicable('bev')

    expect(visible(dashboard.cards)).not.toContain('fuelLevel')
    // Off by default for every type, but switched on by the user: that choice stays.
    expect(visible(dashboard.cards)).toContain('speed')
  })

  it('gives the drivetrain-dependent cards the new type defaults when the type changes', () => {
    const dashboard = useDashboardSettingsStore()
    dashboard.cards = defaultCards('bev')

    dashboard.applyTypeDefaults('hev')

    expect(visible(dashboard.cards)).toContain('fuelLevel')
    expect(visible(dashboard.cards)).not.toContain('charging')
    const firstHidden = dashboard.cards.findIndex((c) => !c.visible)
    expect(dashboard.cards.slice(firstHidden).every((c) => !c.visible)).toBe(true)
  })

  it('moves a hidden card behind the visible ones and a shown one to the end of them', () => {
    const dashboard = useDashboardSettingsStore()
    dashboard.cards = [
      { id: 'odometer', visible: true },
      { id: 'doors', visible: true },
      { id: 'sunRoof', visible: false },
    ]

    dashboard.toggleCard('odometer')
    expect(dashboard.cards).toEqual([
      { id: 'doors', visible: true },
      { id: 'sunRoof', visible: false },
      { id: 'odometer', visible: false },
    ])

    dashboard.toggleCard('sunRoof')
    expect(ids(dashboard.cards)).toEqual(['doors', 'sunRoof', 'odometer'])
    expect(visible(dashboard.cards)).toEqual(['doors', 'sunRoof'])
  })

  it('keeps the cards that do not apply behind a reordered edit grid', () => {
    const dashboard = useDashboardSettingsStore()
    dashboard.cards = defaultCards('bev')
    const offered = dashboard.applicableCards('bev')
    expect(ids(offered)).not.toContain('fuelLevel')

    dashboard.setApplicableCards('bev', [...offered].reverse())

    expect(ids(dashboard.cards).slice(0, offered.length)).toEqual(ids(offered).reverse())
    expect(ids(dashboard.cards).slice(offered.length)).toContain('fuelLevel')
  })

  it('resets to the drivetrain defaults with cards that have data first and the tyre diagram on', () => {
    const dashboard = useDashboardSettingsStore()
    dashboard.showTyreDiagram = false
    const withData = new Set(['doors', 'odometer'])

    dashboard.resetLayout('bev', (id) => withData.has(id))

    expect(ids(dashboard.cards).slice(0, 2).sort()).toEqual(['doors', 'odometer'])
    expect(dashboard.showTyreDiagram).toBe(true)
  })
})
