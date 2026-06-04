import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { vehicleApi, type Vehicle, type TelemetrySnapshot, type Trip, type LastTripSummary } from '@/services/api'

export type VehicleType = 'hev' | 'phev' | 'bev' | 'unknown'

export const useVehicleStore = defineStore('vehicle', () => {
  const vehicles = ref<Vehicle[]>([])
  const currentStatus = ref<TelemetrySnapshot | null>(null)
  const vehicleConfig = ref<Record<string, string>>({})
  const history = ref<TelemetrySnapshot[]>([])
  const trips = ref<Trip[]>([])
  const lastTrip = ref<LastTripSummary | null>(null)
  const loadingCount = ref(0)
  const loading = computed(() => loadingCount.value > 0)
  const error = ref<string | null>(null)
  const lastUpdated = ref<Date | null>(null)

  async function fetchVehicles() {
    loadingCount.value++
    error.value = null
    try {
      const result = await vehicleApi.list()
      vehicles.value = result
    } catch (e) {
      error.value = String(e)
    } finally {
      loadingCount.value--
    }
  }

  async function fetchStatus(vin: string) {
    loadingCount.value++
    error.value = null
    try {
      currentStatus.value = await vehicleApi.status(vin)
      lastUpdated.value = new Date()
    } catch (e) {
      error.value = String(e)
    } finally {
      loadingCount.value--
    }
  }

  async function fetchConfig(vin: string) {
    try {
      vehicleConfig.value = await vehicleApi.config(vin)
    } catch {
      // config may not exist yet
    }
  }

  async function fetchHistory(vin: string, from?: string, to?: string) {
    loadingCount.value++
    error.value = null
    try {
      const result = await vehicleApi.history(vin, from, to)
      history.value = result
    } catch (e) {
      error.value = String(e)
    } finally {
      loadingCount.value--
    }
  }

  async function fetchLastTrip(vin: string) {
    try {
      lastTrip.value = (await vehicleApi.lastTrip(vin)) ?? null
    } catch {
      // non-critical, leave stale value
    }
  }

  async function fetchTrips(vin: string, from?: string, to?: string) {
    loadingCount.value++
    error.value = null
    try {
      trips.value = await vehicleApi.trips(vin, from, to)
    } catch (e) {
      error.value = String(e)
    } finally {
      loadingCount.value--
    }
  }

  // Derived from hw_version in vehicleConfig, set by HA discovery MQTT messages
  const detectedVehicleType = computed((): VehicleType => {
    const hw = (vehicleConfig.value['hw_version'] ?? '').toUpperCase()
    if (!hw) return 'unknown'
    if (hw.includes('PHEV')) return 'phev'
    if (hw.includes('HEV')) return 'hev'
    if (hw.includes('BEV') || hw.includes('EV')) return 'bev'
    return 'unknown'
  })

  return {
    vehicles,
    currentStatus,
    vehicleConfig,
    detectedVehicleType,
    history,
    trips,
    lastTrip,
    loading,
    error,
    lastUpdated,
    fetchVehicles,
    fetchStatus,
    fetchConfig,
    fetchHistory,
    fetchTrips,
    fetchLastTrip,
  }
})
