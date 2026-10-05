import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { settingsApi, type AccountSettings } from '@/services/settingsApi'
import { PULL_INTERVAL_MS, useSettingsSyncStore } from '@/stores/settingsSync'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useMapSettingsStore } from '@/stores/settingsMap'
import { useDashboardSettingsStore } from '@/stores/settingsDashboard'
import { METRIC_UNITS } from '@/utils/units'

vi.mock('@/services/settingsApi', () => ({
  settingsApi: {
    load: vi.fn<() => Promise<AccountSettings>>(),
    save: vi.fn<() => Promise<void>>(),
  },
}))

const load = vi.mocked(settingsApi.load)
const save = vi.mocked(settingsApi.save)

// Local writes wait out a burst of changes (see createDebouncedSave) before the account hears.
const SAVE_DEBOUNCE_MS = 300
const UI_KEY = 'garagestack-settings-ui'
const MAP_KEY = 'garagestack-settings-map'

/** Lets a local write, and the save it sends, run to the end. */
async function settle() {
  await vi.advanceTimersByTimeAsync(SAVE_DEBOUNCE_MS)
  for (let i = 0; i < 10; i++) await Promise.resolve()
}

/** An answer from the server that stays out until the test gives it. */
function holdLoad() {
  let answer!: (settings: AccountSettings) => void
  load.mockReturnValue(new Promise<AccountSettings>((resolve) => (answer = resolve)))
  return (settings: AccountSettings) => answer(settings)
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  load.mockReset().mockResolvedValue({})
  save.mockReset().mockResolvedValue(undefined)
  vi.useFakeTimers()
})

afterEach(() => {
  // Takes its page listeners with it, so a store a test left running cannot answer the next
  // test's visibility change.
  useSettingsSyncStore().$dispose()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('settings kept on the account', () => {
  it('takes in the account copy once keeping starts', async () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ theme: 'dark', locale: 'en' }))
    load.mockResolvedValue({ ui: { theme: 'light', locale: 'nl' } })
    const ui = useUiSettingsStore()

    await useSettingsSyncStore().start()

    expect(ui.theme).toBe('light')
    expect(ui.locale).toBe('nl')
    await settle()
    expect(save).not.toHaveBeenCalled()
  })

  it('keeps a change made while the account copy was on its way, and sends it', async () => {
    const answer = holdLoad()
    const ui = useUiSettingsStore()
    const ready = useSettingsSyncStore().start()

    ui.locale = 'nl'
    answer({ ui: { theme: 'light', locale: 'en' } })
    await ready

    expect(ui.theme).toBe('light')
    expect(ui.locale).toBe('nl')
    await vi.waitFor(() => expect(save).toHaveBeenCalledExactlyOnceWith('ui', { locale: 'nl' }))
  })

  it("fills an account that has none yet from this browser's own copy", async () => {
    localStorage.setItem(MAP_KEY, JSON.stringify({ heatmapEnabled: false }))
    const map = useMapSettingsStore()

    await useSettingsSyncStore().start()

    await vi.waitFor(() => expect(save).toHaveBeenCalledOnce())
    const [section, changes] = save.mock.calls[0]!
    expect(section).toBe('map')
    // Every key, so the account holds the whole section from here on.
    expect(Object.keys(changes).sort()).toEqual(Object.keys(map.$state).sort())
    expect(changes.heatmapEnabled).toBe(false)
  })

  it('leaves an empty account alone when this browser has nothing of its own either', async () => {
    useUiSettingsStore()

    await useSettingsSyncStore().start()
    await settle()

    expect(save).not.toHaveBeenCalled()
  })

  it('sends only the keys that changed', async () => {
    load.mockResolvedValue({ ui: { theme: 'dark' } })
    const ui = useUiSettingsStore()
    await useSettingsSyncStore().start()

    ui.units.distance = 'mi'
    await settle()

    expect(save).toHaveBeenCalledExactlyOnceWith('ui', {
      units: { ...METRIC_UNITS, distance: 'mi' },
    })
  })

  it("keeps the notification checklist on this device, since it decides this browser's push", async () => {
    localStorage.setItem(UI_KEY, JSON.stringify({ notificationTypeExclusions: ['low-tyre'] }))
    load.mockResolvedValue({ ui: { theme: 'light', notificationTypeExclusions: ['engine-start'] } })
    const ui = useUiSettingsStore()
    await useSettingsSyncStore().start()

    expect(ui.theme).toBe('light')
    expect(ui.notificationTypeExclusions).toEqual(['low-tyre'])

    ui.notificationTypeExclusions = []
    ui.locale = 'nl'
    await settle()

    expect(save).toHaveBeenCalledExactlyOnceWith('ui', { locale: 'nl' })
  })

  it('sends nothing before keeping starts or after it stops', async () => {
    const ui = useUiSettingsStore()
    const sync = useSettingsSyncStore()

    ui.theme = 'light'
    await settle()
    expect(save).not.toHaveBeenCalled()

    // The change made while signed out is this browser's own copy now, so it fills the account.
    await sync.start()
    await settle()
    expect(save).toHaveBeenCalledOnce()
    expect(save.mock.calls[0]![1].theme).toBe('light')

    sync.stop()
    ui.locale = 'nl'
    await settle()
    expect(save).toHaveBeenCalledOnce()
  })

  it('sends a change that could not be saved again with the next one', async () => {
    const ui = useUiSettingsStore()
    await useSettingsSyncStore().start()
    save.mockRejectedValueOnce(new Error('offline'))

    ui.theme = 'light'
    await settle()
    ui.locale = 'nl'
    await settle()

    expect(save).toHaveBeenCalledTimes(2)
    expect(save).toHaveBeenLastCalledWith('ui', { theme: 'light', locale: 'nl' })
  })

  it('gives a store first used after the account copy came in that copy at once', async () => {
    load.mockResolvedValue({ dashboard: { showLocationMap: false } })
    useUiSettingsStore()
    await useSettingsSyncStore().start()

    const dashboard = useDashboardSettingsStore()

    expect(dashboard.showLocationMap).toBe(false)
    // A layout saved on another device is the user's own, not a first visit's to rearrange.
    expect(dashboard.hasSavedLayout).toBe(true)
  })

  it('replaces an account copy this build cannot read with its own', async () => {
    load.mockResolvedValue({ dashboard: { cards: [null] } })
    const dashboard = useDashboardSettingsStore()

    await useSettingsSyncStore().start()

    await vi.waitFor(() => expect(save).toHaveBeenCalledOnce())
    expect(save.mock.calls[0]![1].cards).toEqual(JSON.parse(JSON.stringify(dashboard.cards)))
  })

  it('looks for changes made on other devices when the page comes back into view', async () => {
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    load.mockResolvedValue({ ui: { theme: 'dark' } })
    const ui = useUiSettingsStore()
    await useSettingsSyncStore().start()
    load.mockResolvedValue({ ui: { theme: 'light' } })

    // Straight back: too soon to ask again.
    document.dispatchEvent(new Event('visibilitychange'))
    expect(load).toHaveBeenCalledOnce()

    vi.advanceTimersByTime(PULL_INTERVAL_MS)
    document.dispatchEvent(new Event('visibilitychange'))

    await vi.waitFor(() => expect(ui.theme).toBe('light'))
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('drops an answer that arrives after signing out', async () => {
    const answer = holdLoad()
    const ui = useUiSettingsStore()
    const sync = useSettingsSyncStore()
    const ready = sync.start()

    sync.stop()
    answer({ ui: { theme: 'light' } })
    await ready

    expect(ui.theme).toBe('dark')
  })
})
