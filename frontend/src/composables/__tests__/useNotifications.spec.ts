import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { notificationsApi } from '@/services/notificationsApi'
import { authApi } from '@/services/authApi'
import type { AppNotification } from '@/services/notificationsApi'

vi.mock('@/services/notificationsApi', () => ({
  notificationsApi: {
    list: vi.fn<() => Promise<AppNotification[]>>(),
    archive: vi.fn<(id: number) => Promise<void>>(),
    archiveAll: vi.fn<() => Promise<void>>(),
    delete: vi.fn<(id: number) => Promise<void>>(),
    deleteAll: vi.fn<() => Promise<void>>(),
  },
}))

// authApi is used by useAuthStore inside useNotifications
vi.mock('@/services/authApi', () => ({
  authApi: {
    me: vi
      .fn<() => Promise<{ username: string; expiresAtUtc: string | null }>>()
      .mockResolvedValue({ username: 'testuser', expiresAtUtc: null }),
    login: vi.fn<() => Promise<{ username: string; expiresAtUtc: string }>>(),
    logout: vi.fn<() => Promise<void>>(),
  },
}))

function makeNotification(overrides: Partial<AppNotification> = {}): AppNotification {
  return {
    id: 1,
    title: 'Test',
    body: 'Test body',
    createdAt: new Date().toISOString(),
    isArchived: false,
    category: null,
    ...overrides,
  }
}

// useNotifications uses module-level refs so we re-import fresh each test via vi.resetModules()
describe('useNotifications', () => {
  beforeEach(() => {
    vi.resetModules()
    localStorage.clear()
    setActivePinia(createPinia())
    vi.mocked(notificationsApi.list).mockReset()
    vi.mocked(notificationsApi.archive).mockReset()
    vi.mocked(notificationsApi.archiveAll).mockReset()
    vi.mocked(notificationsApi.delete).mockReset()
    vi.mocked(notificationsApi.deleteAll).mockReset()
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('does not auto-fetch when not authenticated', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    useNotifications()

    expect(vi.mocked(notificationsApi.list)).not.toHaveBeenCalled()
  })

  it('auto-fetches immediately when already authenticated', async () => {
    // Simulate a stored session so isAuthenticated is true on composable init.
    localStorage.setItem('garagestack-auth-username', 'testuser')
    localStorage.setItem('garagestack-auth-expires', new Date(Date.now() + 3_600_000).toISOString())
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    useNotifications()

    expect(vi.mocked(notificationsApi.list)).toHaveBeenCalledOnce()
  })

  it('auto-fetches when auth state changes to authenticated after login', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])
    vi.mocked(authApi.login).mockResolvedValue({
      username: 'testuser',
      expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
    })

    const { useNotifications } = await import('@/composables/useNotifications')
    const { useAuthStore } = await import('@/stores/auth')

    const auth = useAuthStore()
    useNotifications()

    expect(vi.mocked(notificationsApi.list)).not.toHaveBeenCalled()

    await auth.login('testuser', 'password')
    await nextTick()

    expect(vi.mocked(notificationsApi.list)).toHaveBeenCalledOnce()
  })

  it('clears notifications when auth state changes to unauthenticated', async () => {
    localStorage.setItem('garagestack-auth-username', 'testuser')
    localStorage.setItem('garagestack-auth-expires', new Date(Date.now() + 3_600_000).toISOString())
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 1 })])
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { useAuthStore } = await import('@/stores/auth')

    const auth = useAuthStore()
    const { notifications } = useNotifications()
    await vi.mocked(notificationsApi.list).mock.results[0]?.value
    await nextTick()

    await auth.logout()
    await nextTick()

    expect(notifications.value).toHaveLength(0)
  })

  it('unreadCount counts non-archived notifications', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, isArchived: false }),
      makeNotification({ id: 2, isArchived: true }),
      makeNotification({ id: 3, isArchived: false }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, unreadCount } = useNotifications()
    await fetchNotifications()

    expect(unreadCount.value).toBe(2)
  })

  it('shows every notification when notificationTypeExclusions is empty (no exclusions)', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, category: 'low-tyre' }),
      makeNotification({ id: 2, category: 'engine-start' }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, notifications } = useNotifications()
    await fetchNotifications()

    expect(notifications.value).toHaveLength(2)
  })

  it('hides notifications whose category is in notificationTypeExclusions', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: ['engine-start'] }),
    )
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, category: 'low-tyre' }),
      makeNotification({ id: 2, category: 'engine-start' }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, notifications } = useNotifications()
    await fetchNotifications()

    expect(notifications.value).toHaveLength(1)
    expect(notifications.value[0]?.id).toBe(1)
  })

  it('groups dynamic maintenance-<id> categories under the "maintenance" exclusion entry', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: ['maintenance'] }),
    )
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, category: 'maintenance-42' }),
      makeNotification({ id: 2, category: 'low-tyre' }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, notifications } = useNotifications()
    await fetchNotifications()

    expect(notifications.value).toHaveLength(1)
    expect(notifications.value[0]?.id).toBe(2)
  })

  it('unreadCount only counts non-archived notifications that pass the type filter', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: ['engine-start'] }),
    )
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, category: 'low-tyre', isArchived: false }),
      makeNotification({ id: 2, category: 'engine-start', isArchived: false }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, unreadCount } = useNotifications()
    await fetchNotifications()

    expect(unreadCount.value).toBe(1)
  })

  it('never hides notifications with a null or unrecognized category, even while a filter is active', async () => {
    localStorage.setItem(
      'garagestack-settings-ui',
      JSON.stringify({ notificationTypeExclusions: ['low-tyre'] }),
    )
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1, category: null }),
      makeNotification({ id: 2, category: 'some-future-category' }),
      makeNotification({ id: 3, category: 'low-tyre' }),
    ])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, notifications } = useNotifications()
    await fetchNotifications()

    expect(notifications.value).toHaveLength(2)
    expect(notifications.value.map((n) => n.id).sort()).toEqual([1, 2])
  })

  it('fetchNotifications() populates the notifications list', async () => {
    const items = [makeNotification({ id: 10 }), makeNotification({ id: 11 })]
    vi.mocked(notificationsApi.list).mockResolvedValue(items)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, notifications } = useNotifications()
    await fetchNotifications()

    expect(notifications.value).toHaveLength(2)
  })

  it('fetchNotifications() resets loading flag after completion', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, loading } = useNotifications()
    await fetchNotifications()

    expect(loading.value).toBe(false)
  })

  it('fetchNotifications() resets loading flag and sets fetchError when API throws', async () => {
    vi.mocked(notificationsApi.list).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, loading, fetchError } = useNotifications()
    await fetchNotifications()

    expect(loading.value).toBe(false)
    expect(fetchError.value).toBe('Network error')
  })

  it('archiveNotification() marks the notification as archived in-place', async () => {
    const item = makeNotification({ id: 5, isArchived: false })
    vi.mocked(notificationsApi.list).mockResolvedValue([item])
    vi.mocked(notificationsApi.archive).mockResolvedValue(undefined)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, archiveNotification, notifications } = useNotifications()
    await fetchNotifications()
    await archiveNotification(5)

    expect(notifications.value.find((n) => n.id === 5)?.isArchived).toBe(true)
  })

  it('archiveNotification() calls the API with the correct id', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 7 })])
    vi.mocked(notificationsApi.archive).mockResolvedValue(undefined)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, archiveNotification } = useNotifications()
    await fetchNotifications()
    await archiveNotification(7)

    expect(vi.mocked(notificationsApi.archive)).toHaveBeenCalledWith(7)
  })

  it('deleteNotification() removes the notification from the list', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([
      makeNotification({ id: 1 }),
      makeNotification({ id: 2 }),
    ])
    vi.mocked(notificationsApi.delete).mockResolvedValue(undefined)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, deleteNotification, notifications } = useNotifications()
    await fetchNotifications()
    await deleteNotification(1)

    expect(notifications.value).toHaveLength(1)
    expect(notifications.value[0]?.id).toBe(2)
  })

  it('deleteNotification() calls the API with the correct id', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 9 })])
    vi.mocked(notificationsApi.delete).mockResolvedValue(undefined)

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, deleteNotification } = useNotifications()
    await fetchNotifications()
    await deleteNotification(9)

    expect(vi.mocked(notificationsApi.delete)).toHaveBeenCalledWith(9)
  })

  it('archiveNotification() sets actionError and leaves the item unarchived when the API throws', async () => {
    const item = makeNotification({ id: 5, isArchived: false })
    vi.mocked(notificationsApi.list).mockResolvedValue([item])
    vi.mocked(notificationsApi.archive).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, archiveNotification, notifications, actionError } =
      useNotifications()
    await fetchNotifications()
    await archiveNotification(5)

    expect(actionError.value).toBe('Network error')
    expect(notifications.value.find((n) => n.id === 5)?.isArchived).toBe(false)
  })

  it('deleteNotification() sets actionError and keeps the item when the API throws', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 9 })])
    vi.mocked(notificationsApi.delete).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, deleteNotification, notifications, actionError } =
      useNotifications()
    await fetchNotifications()
    await deleteNotification(9)

    expect(actionError.value).toBe('Network error')
    expect(notifications.value).toHaveLength(1)
  })

  it('archiveAllNotifications() sets actionError when the API throws', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 1 })])
    vi.mocked(notificationsApi.archiveAll).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, archiveAllNotifications, notifications, actionError } =
      useNotifications()
    await fetchNotifications()
    await archiveAllNotifications()

    expect(actionError.value).toBe('Network error')
    expect(notifications.value[0]?.isArchived).toBe(false)
  })

  it('deleteAllNotifications() sets actionError when the API throws', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([makeNotification({ id: 1 })])
    vi.mocked(notificationsApi.deleteAll).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, deleteAllNotifications, notifications, actionError } =
      useNotifications()
    await fetchNotifications()
    await deleteAllNotifications()

    expect(actionError.value).toBe('Network error')
    expect(notifications.value).toHaveLength(1)
  })

  it('togglePanel() opens a closed panel', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { panelOpen, togglePanel } = useNotifications()
    expect(panelOpen.value).toBe(false)
    await togglePanel()
    expect(panelOpen.value).toBe(true)
  })

  it('togglePanel() closes an open panel', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { panelOpen, togglePanel } = useNotifications()
    await togglePanel()
    await togglePanel()
    expect(panelOpen.value).toBe(false)
  })

  it('togglePanel() fetches notifications when opening', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { togglePanel } = useNotifications()
    await togglePanel()

    expect(vi.mocked(notificationsApi.list)).toHaveBeenCalledOnce()
  })

  it('closePanel() sets panelOpen to false', async () => {
    vi.mocked(notificationsApi.list).mockResolvedValue([])

    const { useNotifications } = await import('@/composables/useNotifications')
    const { panelOpen, togglePanel, closePanel } = useNotifications()
    await togglePanel()
    closePanel()
    expect(panelOpen.value).toBe(false)
  })
})
