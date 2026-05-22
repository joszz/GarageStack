import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { vehicleApi, type Vehicle, type TelemetrySnapshot, type Trip } from '@/services/api'

export type VehicleType = 'hev' | 'phev' | 'bev' | 'unknown'

export const useVehicleStore = defineStore('vehicle', () => {
  const vehicles = ref<Vehicle[]>([])
  const currentStatus = ref<TelemetrySnapshot | null>(null)
  const vehicleConfig = ref<Record<string, string>>({})
  const history = ref<TelemetrySnapshot[]>([])
  const trips = ref<Trip[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function fetchVehicles() {
    loading.value = true
    error.value = null
    try {
      const result = await vehicleApi.list()
      console.log('[vehicle] fetchVehicles response:', result)
      vehicles.value = result
    } catch (e) {
      console.error('[vehicle] fetchVehicles error:', e)
      error.value = String(e)
    } finally {
      loading.value = false
    }
  }

  async function fetchStatus(vin: string) {
    loading.value = true
    error.value = null
    try {
      currentStatus.value = await vehicleApi.status(vin)
    } catch (e) {
      error.value = String(e)
    } finally {
      loading.value = false
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
    loading.value = true
    error.value = null
    try {
      const result = await vehicleApi.history(vin, from, to)
      console.log('[vehicle] fetchHistory result:', result?.length, 'items', 'from=', from)
      history.value = result
    } catch (e) {
      console.error('[vehicle] fetchHistory error:', e)
      error.value = String(e)
    } finally {
      loading.value = false
    }
  }

  async function fetchTrips(vin: string, from?: string, to?: string) {
    loading.value = true
    error.value = null
    try {
      trips.value = await vehicleApi.trips(vin, from, to)
    } catch (e) {
      error.value = String(e)
    } finally {
      loading.value = false
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

  return { vehicles, currentStatus, vehicleConfig, detectedVehicleType, history, trips, loading, error, fetchVehicles, fetchStatus, fetchConfig, fetchHistory, fetchTrips }
})
