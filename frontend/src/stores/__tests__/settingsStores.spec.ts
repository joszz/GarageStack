import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { nextTick } from 'vue'
import { browserLocale, defaultCards } from '@/stores/settingsShared'
import { ALL_CARD_IDS } from '@/cards/registry'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { formatTime, setRegionalFormat } from '@/utils/format'
import { AUTO_UNITS, METRIC_UNITS } from '@/utils/units'

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
    vi.unstubAllGlobals()
    setRegionalFormat({ region: null, hourCycle: 'h23' })
  })

  it.each([
    ['pt-BR', 'pt'],
    ['pt-br', 'pt'],
    ['pt', 'pt'],
    ['pt-PT', 'pt-PT'],
    ['pt-AO', 'pt-PT'],
  ] as const)('detects %s from the browser as %s', (language, locale) => {
    vi.stubGlobal('navigator', { language })

    expect(browserLocale()).toBe(locale)
  })

  it('detects Spanish from the browser language', () => {
    vi.stubGlobal('navigator', { language: 'es-ES' })

    expect(browserLocale()).toBe('es')
  })

  it('follows the device by default, dark when it reports no light preference', () => {
    const store = useUiSettingsStore()
    expect(store.theme).toBe('system')
    expect(store.effectiveTheme).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('switches with the device while the theme is set to system', async () => {
    let light = false
    let onChange: () => void = () => {}
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: light && query === '(prefers-color-scheme: light)',
      media: query,
      addEventListener: (_type: string, listener: () => void) => (onChange = listener),
      removeEventListener: () => {},
    }))
    const store = useUiSettingsStore()

    light = true
    onChange()
    await nextTick()
    expect(document.documentElement.dataset.theme).toBe('light')

    store.theme = 'dark'
    await nextTick()
    expect(document.documentElement.dataset.theme).toBe('dark')

    // A chosen theme stays put when the device changes its mind.
    onChange()
    await nextTick()
    expect(store.effectiveTheme).toBe('dark')
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
    expect(store.mapPeriod).toEqual({ preset: 'last30', from: null, to: null })
  })

  it('loads settings from its own localStorage key on init', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({
        vehicleTypeOverride: 'bev',
        theme: 'light',
        locale: 'nl',
        mapPeriod: { preset: 'lastMonth', from: null, to: null },
      }),
    )
    const store = useUiSettingsStore()
    expect(store.theme).toBe('light')
    expect(store.locale).toBe('nl')
    expect(store.vehicleTypeOverride).toBe('bev')
    expect(store.mapPeriod).toEqual({ preset: 'lastMonth', from: null, to: null })
  })

  it('carries the map days an older build stored over, but not its old default of 7', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ filterDays: 90 }))
    expect(useUiSettingsStore().mapPeriod.preset).toBe('last90')

    setActivePinia(createPinia())
    localStorage.setItem(UI_KEY, JSON.stringify({ filterDays: 7 }))
    expect(useUiSettingsStore().mapPeriod.preset).toBe('last30')
  })

  it('keeps the map off periods longer than it can draw', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ mapPeriod: { preset: 'lastYear', from: null, to: null } }),
    )
    expect(useUiSettingsStore().mapPeriod.preset).toBe('last30')
  })

  it.each(['pt', 'pt-PT'] as const)('keeps a stored %s locale when loading settings', (locale) => {
    localStorage.setItem(UI_KEY, JSON.stringify({ locale }))

    expect(useUiSettingsStore().locale).toBe(locale)
  })

  it('keeps a stored Spanish locale when loading settings', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ locale: 'es' }))

    expect(useUiSettingsStore().locale).toBe('es')
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

  it('opens the statistics on the last 30 days', () => {
    expect(useUiSettingsStore().statsPeriod).toEqual({ preset: 'last30', from: null, to: null })
  })

  it('carries a stored 7, 30 or 90 days over to the statistics period', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ filterDays: 90 }))
    expect(useUiSettingsStore().statsPeriod.preset).toBe('last90')
  })

  it('leaves the statistics on 30 days for map days it has no preset for', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ filterDays: 14 }))
    expect(useUiSettingsStore().statsPeriod.preset).toBe('last30')
  })

  it('loads a stored custom period, and ignores one it cannot read', () => {
    const period = { preset: 'custom', from: '2025-03-01', to: '2025-08-31' }
    localStorage.setItem(UI_KEY, JSON.stringify({ statsPeriod: period, filterDays: 7 }))
    expect(useUiSettingsStore().statsPeriod).toEqual(period)

    setActivePinia(createPinia())
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ statsPeriod: { preset: 'custom', from: 'soon' } }),
    )
    expect(useUiSettingsStore().statsPeriod.preset).toBe('last30')
  })

  it('saves the statistics period apart from the map period', async () => {
    const store = useUiSettingsStore()
    store.statsPeriod = { preset: 'lastYear', from: null, to: null }
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)

    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved.statsPeriod.preset).toBe('lastYear')
    expect(saved.mapPeriod.preset).toBe('last30')
  })

  it('opens the trip log on the current month, without hidden trips', () => {
    const store = useUiSettingsStore()
    expect(store.tripLogPeriod).toBeNull()
    expect(store.tripLogShowHidden).toBe(false)
  })

  it('loads a stored trip log period, and drops one it cannot show', () => {
    vi.setSystemTime(new Date(2026, 10, 15))
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ tripLogPeriod: { year: 2025, month: null }, tripLogShowHidden: true }),
    )
    let store = useUiSettingsStore()
    expect(store.tripLogPeriod).toEqual({ year: 2025, month: null })
    expect(store.tripLogShowHidden).toBe(true)

    for (const period of [{ year: 2015, month: 3 }, { year: 2026, month: 12 }, 'soon']) {
      setActivePinia(createPinia())
      localStorage.setItem(UI_KEY, JSON.stringify({ tripLogPeriod: period }))
      store = useUiSettingsStore()
      expect(store.tripLogPeriod).toBeNull()
    }
  })

  it('opens the charge history on 90 days and the logs on everything from Information', () => {
    const store = useUiSettingsStore()
    expect(store.chargeHistoryPeriod).toEqual({ preset: 'last90', from: null, to: null })
    expect(store.logSource).toBe('all')
    expect(store.logLevel).toBe('Information')
  })

  it('loads the stored charge history and log filters, and falls back on unknown ones', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({
        chargeHistoryPeriod: { preset: 'lastYear', from: null, to: null },
        logSource: 'worker',
        logLevel: 'Error',
      }),
    )
    let store = useUiSettingsStore()
    expect(store.chargeHistoryPeriod.preset).toBe('lastYear')
    expect(store.logSource).toBe('worker')
    expect(store.logLevel).toBe('Error')

    setActivePinia(createPinia())
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ chargeHistoryDays: 14, logSource: 'mqtt', logLevel: 'Verbose' }),
    )
    store = useUiSettingsStore()
    expect(store.chargeHistoryPeriod.preset).toBe('last90')
    expect(store.logSource).toBe('all')
    expect(store.logLevel).toBe('Information')
  })

  it('carries the charge history days an older build stored over where a preset has them', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ chargeHistoryDays: 30 }))
    expect(useUiSettingsStore().chargeHistoryPeriod.preset).toBe('last30')

    setActivePinia(createPinia())
    localStorage.setItem(UI_KEY, JSON.stringify({ chargeHistoryDays: 365 }))
    expect(useUiSettingsStore().chargeHistoryPeriod.preset).toBe('last90')
  })

  it('saves the trip log, charge history and log filters', async () => {
    vi.setSystemTime(new Date(2026, 10, 15))
    const store = useUiSettingsStore()
    store.tripLogPeriod = { year: 2026, month: 2 }
    store.tripLogShowHidden = true
    store.chargeHistoryPeriod = { preset: 'thisYear', from: null, to: null }
    store.logLevel = 'Warning'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)

    const saved = JSON.parse(localStorage.getItem(UI_KEY)!)
    expect(saved).toMatchObject({
      tripLogPeriod: { year: 2026, month: 2 },
      tripLogShowHidden: true,
      chargeHistoryPeriod: { preset: 'thisYear', from: null, to: null },
      logLevel: 'Warning',
    })
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

  describe('car colour', () => {
    const paint = () => {
      const style = document.documentElement.style
      return {
        primary: style.getPropertyValue('--car-primary'),
        secondary: style.getPropertyValue('--car-secondary'),
      }
    }

    it('paints the car orange until another colour is chosen', () => {
      const store = useUiSettingsStore()

      expect(store.carColorScheme).toBe('orange')
      expect(store.carCustomColors).toBeNull()
      expect(paint()).toStrictEqual({ primary: '#f9b233', secondary: '#f39200' })
    })

    it('paints the document in a chosen preset at once', async () => {
      const store = useUiSettingsStore()

      store.chooseCarColorScheme('black')
      await nextTick()

      expect(paint()).toStrictEqual({ primary: '#2c2f35', secondary: '#1b1d21' })
    })

    it('starts the custom colours from the paints on screen, the first time only', async () => {
      const store = useUiSettingsStore()
      store.chooseCarColorScheme('blue')

      store.chooseCarColorScheme('custom')
      await nextTick()
      expect(store.carCustomColors).toStrictEqual({ primary: '#2f80dc', secondary: '#1c5fb0' })

      store.carCustomColors = { primary: '#123456', secondary: '#abcdef' }
      store.chooseCarColorScheme('green')
      store.chooseCarColorScheme('custom')
      await nextTick()
      expect(paint()).toStrictEqual({ primary: '#123456', secondary: '#abcdef' })
    })

    it('saves the custom colours, and loads them back', async () => {
      const store = useUiSettingsStore()
      store.chooseCarColorScheme('custom')
      store.carCustomColors = { primary: '#102030', secondary: '#405060' }
      await nextTick()
      vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)

      setActivePinia(createPinia())
      const loaded = useUiSettingsStore()
      expect(loaded.carColorScheme).toBe('custom')
      expect(loaded.carColors).toStrictEqual({ primary: '#102030', secondary: '#405060' })
    })

    it.each([
      ['not a colour', { primary: 'red', secondary: '#405060' }],
      ['an injected value', { primary: '#102030; background: url(x)', secondary: '#405060' }],
      ['a short hex', { primary: '#123', secondary: '#405060' }],
      ['half a pair', { primary: '#102030' }],
    ])('ignores stored custom colours that are %s', (_, carCustomColors) => {
      localStorage.setItem(UI_KEY, JSON.stringify({ carColorScheme: 'custom', carCustomColors }))

      const store = useUiSettingsStore()
      expect(store.carCustomColors).toBeNull()
      // Custom without a pair falls back to the default paints rather than none.
      expect(store.carColors).toStrictEqual({ primary: '#f9b233', secondary: '#f39200' })
    })

    it('keeps stored custom colours in lower case', () => {
      localStorage.setItem(
        UI_KEY,
        JSON.stringify({ carCustomColors: { primary: '#ABCDEF', secondary: '#0A0B0C' } }),
      )

      expect(useUiSettingsStore().carCustomColors).toStrictEqual({
        primary: '#abcdef',
        secondary: '#0a0b0c',
      })
    })

    it('falls back to orange for a scheme it does not know', () => {
      localStorage.setItem(UI_KEY, JSON.stringify({ carColorScheme: 'tartan' }))

      expect(useUiSettingsStore().carColorScheme).toBe('orange')
    })

    it('keeps a car stored on the retired purple preset looking as it did', () => {
      const purple = { primary: '#9348c4', secondary: '#6d2d9b' }
      localStorage.setItem(
        UI_KEY,
        JSON.stringify({
          carColorScheme: 'purple',
          carCustomColors: { primary: '#000000', secondary: '#000000' },
        }),
      )

      const store = useUiSettingsStore()
      expect(store.carColorScheme).toBe('custom')
      expect(store.carCustomColors).toStrictEqual(purple)
      expect(paint()).toStrictEqual(purple)
    })

    // While orange was retired, every car on it (the default) was moved to custom orange.
    it("puts a car on custom colours in exactly a preset's paints back on that preset", () => {
      localStorage.setItem(
        UI_KEY,
        JSON.stringify({
          carColorScheme: 'custom',
          carCustomColors: { primary: '#f9b233', secondary: '#f39200' },
        }),
      )

      const store = useUiSettingsStore()
      expect(store.carColorScheme).toBe('orange')
      expect(paint()).toStrictEqual({ primary: '#f9b233', secondary: '#f39200' })
    })

    it('keeps custom colours that only come close to a preset', () => {
      const almost = { primary: '#f9b233', secondary: '#f39201' }
      localStorage.setItem(
        UI_KEY,
        JSON.stringify({ carColorScheme: 'custom', carCustomColors: almost }),
      )

      const store = useUiSettingsStore()
      expect(store.carColorScheme).toBe('custom')
      expect(store.carCustomColors).toStrictEqual(almost)
    })

    it('moves each car on a retired preset to custom on its own', () => {
      localStorage.setItem(
        UI_KEY,
        JSON.stringify({
          carColorScheme: 'blue',
          vehicleSettings: {
            FAKEVN00000000001: { carColorScheme: 'purple' },
            FAKEVN00000000002: { carColorScheme: 'black' },
          },
        }),
      )

      expect(useUiSettingsStore().vehicleSettings).toStrictEqual({
        FAKEVN00000000001: {
          carColorScheme: 'custom',
          carCustomColors: { primary: '#9348c4', secondary: '#6d2d9b' },
        },
        FAKEVN00000000002: { carColorScheme: 'black', carCustomColors: null },
      })
    })

    it('does not mistake an inherited property name for a retired preset', () => {
      localStorage.setItem(UI_KEY, JSON.stringify({ carColorScheme: 'constructor' }))

      expect(useUiSettingsStore().carColorScheme).toBe('orange')
    })
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

  it('leaves the region, the clock and every unit on automatic until one is chosen', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ theme: 'light' }))
    const store = useUiSettingsStore()

    expect(store.region).toBe('auto')
    expect(store.clock).toBe('auto')
    expect(store.units).toEqual(AUTO_UNITS)
  })

  it('keeps the units an install stored before automatic existed', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ units: METRIC_UNITS }))
    expect(useUiSettingsStore().units).toEqual(METRIC_UNITS)
  })

  it('shows what the detected region uses for everything left on automatic', () => {
    const store = useUiSettingsStore()

    expect(store.detectedRegion).toBe('NL')
    expect(store.effectiveRegion).toBe('NL')
    expect(store.effectiveHourCycle).toBe('h23')
    expect(store.effectiveUnits).toEqual(METRIC_UNITS)
  })

  it('follows a chosen region, except where a clock or unit is chosen too', () => {
    const store = useUiSettingsStore()
    store.region = 'US'

    expect(store.effectiveHourCycle).toBe('h12')
    expect(store.effectiveUnits).toEqual({
      distance: 'mi',
      temperature: 'fahrenheit',
      pressure: 'psi',
      fuelConsumption: 'mpgUs',
      energyConsumption: 'miPerKwh',
    })

    store.clock = 'h23'
    store.units.distance = 'km'
    expect(store.effectiveHourCycle).toBe('h23')
    expect(store.effectiveUnits.distance).toBe('km')
  })

  it('hands the region and the clock to every date and time at once', async () => {
    const store = useUiSettingsStore()
    const moment = new Date(2026, 8, 26, 14, 5)
    store.region = 'US'
    await nextTick()
    expect(formatTime(moment).replace(/\s/g, ' ')).toBe('2:05 PM')

    store.clock = 'h23'
    await nextTick()
    expect(formatTime(moment)).toBe('14:05')
  })

  it('falls back to automatic for a region or clock it does not know', () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ region: 'XX', clock: 'h11' }))
    const store = useUiSettingsStore()

    expect(store.region).toBe('auto')
    expect(store.clock).toBe('auto')
  })

  it('persists a unit change, and loads it back', async () => {
    const store = useUiSettingsStore()
    store.units.distance = 'mi'
    store.units.energyConsumption = 'kwhPer100Mi'
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).units.distance).toBe('mi')
    expect(JSON.parse(localStorage.getItem(UI_KEY)!).units.energyConsumption).toBe('kwhPer100Mi')

    setActivePinia(createPinia())
    expect(useUiSettingsStore().units).toEqual({
      ...AUTO_UNITS,
      distance: 'mi',
      energyConsumption: 'kwhPer100Mi',
    })
  })

  it('falls back per unit, keeping the stored units it recognises', () => {
    localStorage.setItem(
      UI_KEY,
      JSON.stringify({ units: { distance: 'mi', temperature: 'kelvin', pressure: 'psi' } }),
    )
    expect(useUiSettingsStore().units).toEqual({
      distance: 'mi',
      temperature: 'auto',
      pressure: 'psi',
      fuelConsumption: 'auto',
      energyConsumption: 'auto',
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

  it('remembers whether the speed key was folded, and leaves it to the screen until then', async () => {
    expect(useMapSettingsStore().legendExpanded).toBeNull()

    useMapSettingsStore().legendExpanded = false
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    setActivePinia(createPinia())

    expect(useMapSettingsStore().legendExpanded).toBe(false)
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

  it('shows the per-month statistics table until it is hidden, and remembers that', async () => {
    const store = useDashboardSettingsStore()
    expect(store.showStatsMonths).toBe(true)

    store.showStatsMonths = false
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)

    expect(JSON.parse(localStorage.getItem(DASHBOARD_KEY)!).showStatsMonths).toBe(false)
  })

  it('keeps the map behind the dashboard off until it is switched on, and remembers that', async () => {
    const store = useDashboardSettingsStore()
    expect(store.mapBackdrop).toBe(false)

    store.mapBackdrop = true
    await nextTick()
    vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
    setActivePinia(createPinia())

    expect(useDashboardSettingsStore().mapBackdrop).toBe(true)
  })

  it('leaves the map behind the dashboard as it is on a layout reset, as it is no part of the layout', () => {
    const store = useDashboardSettingsStore()
    store.mapBackdrop = true
    store.resetLayout('bev', () => true)
    expect(store.mapBackdrop).toBe(true)
  })

  describe('car diagram and location map', () => {
    const ids = (store: ReturnType<typeof useDashboardSettingsStore>) =>
      store.overview.map((part) => part.id)

    it('shows the car diagram before the location map until they are swapped', () => {
      const store = useDashboardSettingsStore()
      expect(store.overview).toEqual([
        { id: 'car', visible: true },
        { id: 'map', visible: true },
      ])
    })

    it('keeps the swapped order across a reload', async () => {
      const store = useDashboardSettingsStore()
      store.overview = [...store.overview].reverse()
      expect(ids(store)).toEqual(['map', 'car'])

      await nextTick()
      vi.advanceTimersByTime(SAVE_DEBOUNCE_MS)
      setActivePinia(createPinia())

      expect(ids(useDashboardSettingsStore())).toEqual(['map', 'car'])
    })

    it('hides and shows each of the two on its own, wherever it stands', () => {
      const store = useDashboardSettingsStore()
      store.mapFirst = true
      store.toggleOverview('map')
      expect(store.showLocationMap).toBe(false)
      expect(store.overview).toEqual([
        { id: 'map', visible: false },
        { id: 'car', visible: true },
      ])

      store.toggleOverview('car')
      expect(store.showTyreDiagram).toBe(false)
      store.toggleOverview('map')
      expect(store.showLocationMap).toBe(true)
    })

    it('puts the car diagram first again on a layout reset', () => {
      const store = useDashboardSettingsStore()
      store.mapFirst = true
      store.resetLayout('bev', () => true)
      expect(ids(store)).toEqual(['car', 'map'])
    })
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
    expect(ui.mapPeriod.preset).toBe('last30')

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

  // A first visit starts from the unknown-car defaults; the dashboard takes the drivetrain's own
  // once it is known, or a plug-in car would never show its charging controls.
  it('shows the plug-in cards when a first layout turns out to be for a plug-in car', () => {
    const dashboard = useDashboardSettingsStore()
    expect(visible(dashboard.cards)).not.toContain('chargingSession')

    dashboard.applyTypeDefaults('bev')

    expect(visible(dashboard.cards)).toEqual(
      expect.arrayContaining(['chargingSession', 'batteryHeating']),
    )
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
