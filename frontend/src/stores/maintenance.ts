import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Ref } from 'vue'
import {
  maintenanceApi,
  type MaintenanceItem,
  type MaintenanceLogEntry,
  type CreateMaintenanceItemRequest,
  type UpdateMaintenanceItemRequest,
  type LogMaintenanceServiceRequest,
} from '@/services/maintenanceApi'

export const useMaintenanceStore = defineStore('maintenance', () => {
  const items = ref<MaintenanceItem[]>([])
  const logEntries = ref<Record<number, MaintenanceLogEntry[]>>({})
  const loadingCount = ref(0)
  const loading = computed(() => loadingCount.value > 0)
  const itemsError = ref<string | null>(null)
  const actionError = ref<string | null>(null)

  async function withLoading(errorRef: Ref<string | null>, fn: () => Promise<void>) {
    loadingCount.value++
    errorRef.value = null
    try {
      await fn()
    } catch (e) {
      errorRef.value = String(e)
    } finally {
      loadingCount.value--
    }
  }

  async function fetchItems(vin: string) {
    await withLoading(itemsError, async () => {
      items.value = await maintenanceApi.list(vin)
    })
  }

  async function createItem(vin: string, req: CreateMaintenanceItemRequest) {
    await withLoading(actionError, async () => {
      const created = await maintenanceApi.create(vin, req)
      items.value = [...items.value, created]
    })
  }

  async function updateItem(vin: string, id: number, req: UpdateMaintenanceItemRequest) {
    await withLoading(actionError, async () => {
      const updated = await maintenanceApi.update(vin, id, req)
      items.value = items.value.map((i) => (i.id === id ? updated : i))
    })
  }

  async function deleteItem(vin: string, id: number) {
    await withLoading(actionError, async () => {
      await maintenanceApi.delete(vin, id)
      items.value = items.value.filter((i) => i.id !== id)
      delete logEntries.value[id]
    })
  }

  async function fetchLog(vin: string, itemId: number) {
    await withLoading(actionError, async () => {
      logEntries.value = {
        ...logEntries.value,
        [itemId]: await maintenanceApi.listLog(vin, itemId),
      }
    })
  }

  async function logService(vin: string, itemId: number, req: LogMaintenanceServiceRequest) {
    await withLoading(actionError, async () => {
      const { item, logEntry } = await maintenanceApi.logService(vin, itemId, req)
      items.value = items.value.map((i) => (i.id === itemId ? item : i))
      const existing = logEntries.value[itemId]
      if (existing) logEntries.value = { ...logEntries.value, [itemId]: [logEntry, ...existing] }
    })
  }

  async function deleteLogEntry(vin: string, itemId: number, logId: number) {
    await withLoading(actionError, async () => {
      await maintenanceApi.deleteLogEntry(vin, itemId, logId)
      const existing = logEntries.value[itemId]
      if (existing) {
        logEntries.value = { ...logEntries.value, [itemId]: existing.filter((l) => l.id !== logId) }
      }
      // Deleting a log entry can change the item's recomputed baseline/due status server-side,
      // so re-fetch the affected item rather than leaving stale due-status data in the list.
      const refreshed = await maintenanceApi.list(vin)
      items.value = refreshed
    })
  }

  const overdueItems = computed(() => items.value.filter((i) => i.dueStatus === 'overdue'))
  const dueSoonItems = computed(() => items.value.filter((i) => i.dueStatus === 'dueSoon'))
  const attentionItems = computed(() => [...overdueItems.value, ...dueSoonItems.value])

  return {
    items,
    logEntries,
    loading,
    itemsError,
    actionError,
    overdueItems,
    dueSoonItems,
    attentionItems,
    fetchItems,
    createItem,
    updateItem,
    deleteItem,
    fetchLog,
    logService,
    deleteLogEntry,
  }
})
