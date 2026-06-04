import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { notificationsApi } from '@/services/api'
import type { AppNotification } from '@/services/api'

vi.mock('@/services/api', () => ({
  notificationsApi: {
    list: vi.fn<() => Promise<AppNotification[]>>(),
    archive: vi.fn<(id: number) => Promise<void>>(),
    delete: vi.fn<(id: number) => Promise<void>>(),
  },
  // authApi is used by useAuthStore inside useNotifications
  authApi: {
    me: vi.fn().mockResolvedValue({ username: 'testuser', expiresAtUtc: null }),
    login: vi.fn(),
    logout: vi.fn(),
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
    setActivePinia(createPinia())
    vi.mocked(notificationsApi.list).mockReset()
    vi.mocked(notificationsApi.archive).mockReset()
    vi.mocked(notificationsApi.delete).mockReset()
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

  it('fetchNotifications() resets loading flag even when API throws', async () => {
    vi.mocked(notificationsApi.list).mockRejectedValue(new Error('Network error'))

    const { useNotifications } = await import('@/composables/useNotifications')
    const { fetchNotifications, loading } = useNotifications()
    await fetchNotifications().catch(() => {})

    expect(loading.value).toBe(false)
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
