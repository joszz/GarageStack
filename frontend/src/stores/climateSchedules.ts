import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { useVehicleStore } from '@/stores/vehicle'
import {
  climateScheduleApi,
  type ClimateSchedule,
  type ClimateScheduleRequest,
} from '@/services/climateScheduleApi'

/** The schedule's settings without what the server keeps about its runs. */
export function toRequest(schedule: ClimateSchedule): ClimateScheduleRequest {
  return {
    name: schedule.name,
    enabled: schedule.enabled,
    startTime: schedule.startTime,
    days: schedule.days,
    timeZoneId: schedule.timeZoneId,
    mode: schedule.mode,
    temperatureC: schedule.temperatureC,
    rearDefroster: schedule.rearDefroster,
    seatLeftLevel: schedule.seatLeftLevel,
    seatRightLevel: schedule.seatRightLevel,
    onlyBelowC: schedule.onlyBelowC,
    onlyAboveC: schedule.onlyAboveC,
  }
}

// Earliest time of day first, as the server lists them; "HH:mm" sorts as text.
function byStartTime(a: ClimateSchedule, b: ClimateSchedule): number {
  return a.startTime.localeCompare(b.startTime) || a.id - b.id
}

export const useClimateSchedulesStore = defineStore('climateSchedules', () => {
  const schedules = ref<ClimateSchedule[]>([])
  const { loading, withLoading } = useLoadingTracker()
  const itemsError = ref<Error | null>(null)
  const actionError = ref<Error | null>(null)
  // The list belongs to one car: it is dropped when the user picks another, and an answer for the
  // car switched away from (a slow one, still on its way) is not kept.
  const vehicleStore = useVehicleStore()
  watch(
    () => vehicleStore.switchCount,
    () => {
      schedules.value = []
    },
  )

  async function fetchSchedules(vin: string) {
    await withLoading(itemsError, async () => {
      const list = await climateScheduleApi.list(vin)
      if (vehicleStore.isForAnotherCar(vin)) return
      schedules.value = list
    })
  }

  async function createSchedule(vin: string, req: ClimateScheduleRequest) {
    await withLoading(actionError, async () => {
      const created = await climateScheduleApi.create(vin, req)
      if (vehicleStore.isForAnotherCar(vin)) return
      schedules.value = [...schedules.value, created].sort(byStartTime)
    })
  }

  async function updateSchedule(vin: string, id: number, req: ClimateScheduleRequest) {
    await withLoading(actionError, async () => {
      const updated = await climateScheduleApi.update(vin, id, req)
      if (vehicleStore.isForAnotherCar(vin)) return
      schedules.value = schedules.value.map((s) => (s.id === id ? updated : s)).sort(byStartTime)
    })
  }

  async function setEnabled(vin: string, schedule: ClimateSchedule, enabled: boolean) {
    await updateSchedule(vin, schedule.id, { ...toRequest(schedule), enabled })
  }

  async function deleteSchedule(vin: string, id: number) {
    await withLoading(actionError, async () => {
      await climateScheduleApi.delete(vin, id)
      if (vehicleStore.isForAnotherCar(vin)) return
      schedules.value = schedules.value.filter((s) => s.id !== id)
    })
  }

  const activeSchedules = computed(() => schedules.value.filter((s) => s.enabled))

  /** The switched-on schedule that runs first. */
  const nextSchedule = computed((): ClimateSchedule | null =>
    activeSchedules.value
      .filter((s) => s.nextRunUtc !== null)
      .reduce<ClimateSchedule | null>(
        (first, s) => (first === null || s.nextRunUtc! < first.nextRunUtc! ? s : first),
        null,
      ),
  )

  return {
    schedules,
    loading,
    itemsError,
    actionError,
    activeSchedules,
    nextSchedule,
    fetchSchedules,
    createSchedule,
    updateSchedule,
    setEnabled,
    deleteSchedule,
  }
})
