import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { useVehicleStore } from '@/stores/vehicle'
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
  const { loading, withLoading } = useLoadingTracker()
  const itemsError = ref<Error | null>(null)
  const actionError = ref<Error | null>(null)
  // The list belongs to one car: it is dropped when the user picks another, and an answer for the
  // car switched away from (a slow one, still on its way) is not kept.
  const vehicleStore = useVehicleStore()
  watch(
    () => vehicleStore.switchCount,
    () => {
      items.value = []
      logEntries.value = {}
    },
  )

  async function fetchItems(vin: string) {
    await withLoading(itemsError, async () => {
      const list = await maintenanceApi.list(vin)
      if (vehicleStore.isForAnotherCar(vin)) return
      items.value = list
    })
  }

  async function createItem(vin: string, req: CreateMaintenanceItemRequest) {
    await withLoading(actionError, async () => {
      const created = await maintenanceApi.create(vin, req)
      if (vehicleStore.isForAnotherCar(vin)) return
      items.value = [...items.value, created]
    })
  }

  async function updateItem(vin: string, id: number, req: UpdateMaintenanceItemRequest) {
    await withLoading(actionError, async () => {
      const updated = await maintenanceApi.update(vin, id, req)
      if (vehicleStore.isForAnotherCar(vin)) return
      items.value = items.value.map((i) => (i.id === id ? updated : i))
    })
  }

  async function deleteItem(vin: string, id: number) {
    await withLoading(actionError, async () => {
      await maintenanceApi.delete(vin, id)
      if (vehicleStore.isForAnotherCar(vin)) return
      items.value = items.value.filter((i) => i.id !== id)
      delete logEntries.value[id]
    })
  }

  async function fetchLog(vin: string, itemId: number) {
    await withLoading(actionError, async () => {
      const log = await maintenanceApi.listLog(vin, itemId)
      if (vehicleStore.isForAnotherCar(vin)) return
      logEntries.value = { ...logEntries.value, [itemId]: log }
    })
  }

  async function logService(vin: string, itemId: number, req: LogMaintenanceServiceRequest) {
    await withLoading(actionError, async () => {
      const { item, logEntry } = await maintenanceApi.logService(vin, itemId, req)
      if (vehicleStore.isForAnotherCar(vin)) return
      items.value = items.value.map((i) => (i.id === itemId ? item : i))
      const existing = logEntries.value[itemId]
      if (existing) logEntries.value = { ...logEntries.value, [itemId]: [logEntry, ...existing] }
    })
  }

  async function deleteLogEntry(vin: string, itemId: number, logId: number) {
    await withLoading(actionError, async () => {
      await maintenanceApi.deleteLogEntry(vin, itemId, logId)
      if (vehicleStore.isForAnotherCar(vin)) return
      const existing = logEntries.value[itemId]
      if (existing) {
        logEntries.value = { ...logEntries.value, [itemId]: existing.filter((l) => l.id !== logId) }
      }
      // Deleting a log entry can change the item's recomputed baseline/due status server-side,
      // so re-fetch the affected item rather than leaving stale due-status data in the list.
      const refreshed = await maintenanceApi.list(vin)
      if (vehicleStore.isForAnotherCar(vin)) return
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
