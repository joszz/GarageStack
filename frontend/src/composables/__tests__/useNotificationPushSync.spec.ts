import { describe, it, expect, beforeEach, vi } from 'vitest'
import { ref, nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'

const mockTogglePush = vi.fn<() => Promise<void>>()
const mockSyncExclusions = vi.fn<() => Promise<void>>()
const mockPushState = ref<'unknown' | 'subscribed' | 'unsubscribed' | 'denied'>('unknown')
const mockPushError = ref(false)
let mockPushSupported = true
type PushOptions = {
  excludedCategories?: () => readonly string[]
  excludedCategoriesByVehicle?: () => Readonly<Record<string, readonly string[]>>
}
let passedOptions: PushOptions | undefined

vi.mock('@/composables/usePush', () => ({
  usePush: (options?: PushOptions) => {
    passedOptions = options
    return {
      pushSupported: mockPushSupported,
      pushState: mockPushState,
      pushError: mockPushError,
      togglePush: mockTogglePush,
      syncExclusions: mockSyncExclusions,
    }
  },
}))

async function setUp() {
  const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
  const { useUiSettingsStore } = await import('@/stores/settingsUi')
  const sync = useNotificationPushSync()
  return { sync, settings: useUiSettingsStore() }
}

describe('useNotificationPushSync', () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
    localStorage.clear()
    mockTogglePush.mockReset()
    mockSyncExclusions.mockReset()
    mockPushState.value = 'unknown'
    mockPushError.value = false
    mockPushSupported = true
    passedOptions = undefined
  })

  it('switching push on subscribes, the one way a permission prompt can come up', async () => {
    mockPushState.value = 'unsubscribed'
    const { sync } = await setUp()

    sync.pushEnabled.value = true

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })

  it('switching push off unsubscribes', async () => {
    mockPushState.value = 'subscribed'
    const { sync } = await setUp()

    sync.pushEnabled.value = false

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })

  it('reads as on only while subscribed', async () => {
    const { sync } = await setUp()

    mockPushState.value = 'subscribed'
    expect(sync.pushEnabled.value).toBe(true)
    mockPushState.value = 'unsubscribed'
    expect(sync.pushEnabled.value).toBe(false)
  })

  it.each(['unknown', 'denied'] as const)('disables the switch while push is %s', async (state) => {
    mockPushState.value = state
    const { sync } = await setUp()

    expect(sync.pushSwitchDisabled.value).toBe(true)
  })

  it('never subscribes or unsubscribes from the checklist', async () => {
    mockPushState.value = 'unsubscribed'
    const { settings } = await setUp()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()
    settings.notificationTypeExclusions = []
    await nextTick()

    expect(mockTogglePush).not.toHaveBeenCalled()
  })

  it('tells the server whenever a different set of types is switched off', async () => {
    mockPushState.value = 'subscribed'
    const { settings } = await setUp()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()
    settings.notificationTypeExclusions = ['engine-start']
    await nextTick()

    expect(mockSyncExclusions).toHaveBeenCalledTimes(2)
  })

  it('leaves the server alone when the same types arrive in another order', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: ['engine-start', 'low-tyre'] }),
    )
    const { settings } = await setUp()

    settings.notificationTypeExclusions = ['low-tyre', 'engine-start']
    await nextTick()

    expect(mockSyncExclusions).not.toHaveBeenCalled()
  })

  it('hands usePush the checklist, so every registration carries it', async () => {
    const { settings } = await setUp()

    settings.notificationTypeExclusions = ['maintenance']
    settings.notificationTypeExclusionsByVehicle = { '2': ['low-tyre'] }

    expect(passedOptions?.excludedCategories?.()).toEqual(['maintenance'])
    expect(passedOptions?.excludedCategoriesByVehicle?.()).toEqual({ '2': ['low-tyre'] })
  })

  it('tells the server whenever a car has a different set of types switched off', async () => {
    mockPushState.value = 'subscribed'
    const { settings } = await setUp()

    settings.notificationTypeExclusionsByVehicle = { '2': ['low-tyre'] }
    await nextTick()
    settings.notificationTypeExclusionsByVehicle = { '2': ['low-tyre'], '3': ['engine-start'] }
    await nextTick()
    settings.notificationTypeExclusionsByVehicle = {}
    await nextTick()

    expect(mockSyncExclusions).toHaveBeenCalledTimes(3)
  })

  it('leaves the server alone when the same car types arrive in another order', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({
        notificationTypeExclusionsByVehicle: { '2': ['low-tyre', 'engine-start'], '3': ['rdw'] },
      }),
    )
    const { settings } = await setUp()

    settings.notificationTypeExclusionsByVehicle = {
      '3': ['rdw'],
      '2': ['engine-start', 'low-tyre'],
    }
    await nextTick()

    expect(mockSyncExclusions).not.toHaveBeenCalled()
  })

  it('registers no push side effects when pushSupported is false', async () => {
    mockPushSupported = false
    const { settings } = await setUp()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()

    expect(mockSyncExclusions).not.toHaveBeenCalled()
  })

  it('showPermissionDeniedNotice is true only when push is supported and permission was denied', async () => {
    mockPushState.value = 'denied'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(true)

    mockPushState.value = 'subscribed'
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(false)

    mockPushSupported = false
    mockPushState.value = 'denied'
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(false)
  })
})
