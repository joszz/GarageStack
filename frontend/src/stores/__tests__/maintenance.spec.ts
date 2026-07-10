import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useMaintenanceStore } from '@/stores/maintenance'
import type {
  MaintenanceItem,
  MaintenanceLogEntry,
  LogMaintenanceServiceResponse,
} from '@/services/maintenanceApi'

vi.mock('@/services/maintenanceApi', () => ({
  maintenanceApi: {
    list: vi.fn<() => Promise<MaintenanceItem[]>>().mockResolvedValue([]),
    create: vi.fn<() => Promise<MaintenanceItem>>(),
    update: vi.fn<() => Promise<MaintenanceItem>>(),
    delete: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    listLog: vi.fn<() => Promise<MaintenanceLogEntry[]>>().mockResolvedValue([]),
    logService: vi.fn<() => Promise<LogMaintenanceServiceResponse>>(),
    deleteLogEntry: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  },
}))

function item(overrides: Partial<MaintenanceItem> = {}): MaintenanceItem {
  return {
    id: 1,
    vehicleId: 1,
    name: 'Oil change',
    notes: null,
    intervalKm: 10000,
    intervalMonths: null,
    lastServiceDate: null,
    lastServiceOdometerKm: null,
    dueStatus: 'ok',
    nextDueDate: null,
    nextDueOdometerKm: null,
    daysRemaining: null,
    kmRemaining: null,
    createdAt: '2026-01-01',
    ...overrides,
  }
}

describe('useMaintenanceStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('fetchItems populates items on success', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockResolvedValue([item()])
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')
    expect(store.items).toHaveLength(1)
    expect(store.itemsError).toBeNull()
  })

  it('fetchItems sets itemsError on failure', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockRejectedValue(new Error('Network error'))
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')
    expect(store.itemsError).toContain('Network error')
    expect(store.items).toHaveLength(0)
  })

  it('createItem appends the created item', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.create).mockResolvedValue(item({ id: 2, name: 'Tyre rotation' }))
    const store = useMaintenanceStore()
    await store.createItem('VIN1', { name: 'Tyre rotation', intervalKm: 10000 })
    expect(store.items).toHaveLength(1)
    expect(store.items[0]?.name).toBe('Tyre rotation')
  })

  it('updateItem replaces the matching item in place', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockResolvedValue([item({ id: 1, name: 'Oil change' })])
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')

    vi.mocked(maintenanceApi.update).mockResolvedValue(item({ id: 1, name: 'Oil change (5w30)' }))
    await store.updateItem('VIN1', 1, { name: 'Oil change (5w30)' })

    expect(store.items).toHaveLength(1)
    expect(store.items[0]?.name).toBe('Oil change (5w30)')
  })

  it('deleteItem removes the item and its cached log entries', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockResolvedValue([item({ id: 1 })])
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')
    store.logEntries[1] = [
      {
        id: 9,
        maintenanceItemId: 1,
        performedAt: '2026-01-01',
        odometerKm: null,
        notes: null,
        createdAt: '2026-01-01',
      },
    ]

    await store.deleteItem('VIN1', 1)

    expect(store.items).toHaveLength(0)
    expect(store.logEntries[1]).toBeUndefined()
  })

  it('logService updates the item and prepends the log entry when history is loaded', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockResolvedValue([item({ id: 1, dueStatus: 'overdue' })])
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')
    store.logEntries[1] = []

    vi.mocked(maintenanceApi.logService).mockResolvedValue({
      item: item({ id: 1, dueStatus: 'ok', lastServiceDate: '2026-07-10' }),
      logEntry: {
        id: 3,
        maintenanceItemId: 1,
        performedAt: '2026-07-10',
        odometerKm: 12000,
        notes: null,
        createdAt: '2026-07-10',
      },
    })
    await store.logService('VIN1', 1, { performedAt: '2026-07-10', odometerKm: 12000 })

    expect(store.items[0]?.dueStatus).toBe('ok')
    expect(store.logEntries[1]).toHaveLength(1)
    expect(store.logEntries[1]?.[0]?.id).toBe(3)
  })

  it('overdueItems and attentionItems order overdue before dueSoon', async () => {
    const { maintenanceApi } = await import('@/services/maintenanceApi')
    vi.mocked(maintenanceApi.list).mockResolvedValue([
      item({ id: 1, dueStatus: 'ok' }),
      item({ id: 2, dueStatus: 'dueSoon' }),
      item({ id: 3, dueStatus: 'overdue' }),
    ])
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')

    expect(store.overdueItems.map((i) => i.id)).toEqual([3])
    expect(store.dueSoonItems.map((i) => i.id)).toEqual([2])
    expect(store.attentionItems.map((i) => i.id)).toEqual([3, 2])
  })

  it('clears loading flag after fetch completes', async () => {
    const store = useMaintenanceStore()
    await store.fetchItems('VIN1')
    expect(store.loading).toBe(false)
  })
})
