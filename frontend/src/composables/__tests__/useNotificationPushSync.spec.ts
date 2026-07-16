import { describe, it, expect, beforeEach, vi } from 'vitest'
import { ref, nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { NOTIFICATION_CATEGORY_IDS } from '@/utils/notificationCategories'

const mockTogglePush = vi.fn<() => Promise<void>>()
const mockPushState = ref<'unknown' | 'subscribed' | 'unsubscribed' | 'denied'>('unknown')
let mockPushSupported = true

vi.mock('@/composables/usePush', () => ({
  usePush: () => ({
    pushSupported: mockPushSupported,
    pushState: mockPushState,
    togglePush: mockTogglePush,
  }),
}))

describe('useNotificationPushSync', () => {
  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
    localStorage.clear()
    mockTogglePush.mockReset()
    mockPushState.value = 'unknown'
    mockPushSupported = true
  })

  it('does not call togglePush on initial mount, even if desired state disagrees with actual state', async () => {
    mockPushState.value = 'unsubscribed'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    useNotificationPushSync()
    await nextTick()

    expect(mockTogglePush).not.toHaveBeenCalled()
  })

  it('calls togglePush on the very first checklist interaction, regardless of which box', async () => {
    mockPushState.value = 'unsubscribed'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })

  it('does not call togglePush again for changes that keep the same exclusion count', async () => {
    mockPushState.value = 'subscribed'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()
    settings.notificationTypeExclusions = ['engine-start']
    await nextTick()
    settings.notificationTypeExclusions = ['low-tyre', 'engine-start']
    await nextTick()

    expect(mockTogglePush).not.toHaveBeenCalled()
  })

  it('calls togglePush to unsubscribe when deselecting every type', async () => {
    mockPushState.value = 'subscribed'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = [...NOTIFICATION_CATEGORY_IDS]
    await nextTick()

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })

  it('calls togglePush to resubscribe when selecting at least one type after deselect-all', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: [...NOTIFICATION_CATEGORY_IDS] }),
    )
    mockPushState.value = 'unsubscribed'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = []
    await nextTick()

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })

  it('does not call togglePush while pushState is denied', async () => {
    mockPushState.value = 'denied'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()

    expect(mockTogglePush).not.toHaveBeenCalled()
  })

  it('registers no push side effects when pushSupported is false', async () => {
    mockPushSupported = false
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = [...NOTIFICATION_CATEGORY_IDS]
    await nextTick()

    expect(mockTogglePush).not.toHaveBeenCalled()
  })

  it('showPermissionDeniedNotice is true only when push is supported and permission was denied', async () => {
    mockPushSupported = true
    mockPushState.value = 'denied'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(true)

    mockPushState.value = 'subscribed'
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(false)

    mockPushSupported = false
    mockPushState.value = 'denied'
    expect(useNotificationPushSync().showPermissionDeniedNotice.value).toBe(false)
  })

  it('still attempts togglePush on first interaction if pushState is still unknown (async initPushState race)', async () => {
    mockPushState.value = 'unknown'
    const { useNotificationPushSync } = await import('@/composables/useNotificationPushSync')
    const { useUiSettingsStore } = await import('@/stores/settingsUi')
    useNotificationPushSync()
    const settings = useUiSettingsStore()

    settings.notificationTypeExclusions = ['low-tyre']
    await nextTick()

    expect(mockTogglePush).toHaveBeenCalledOnce()
  })
})
