import { defineStore } from 'pinia'
import { ref, shallowRef, computed, nextTick } from 'vue'
import type { Ref } from 'vue'
import { vehicleApi, type Vehicle, type TelemetrySnapshot, type Trip } from '@/services/vehicleApi'

export type VehicleType = 'hev' | 'phev' | 'bev' | 'unknown'

export const useVehicleStore = defineStore('vehicle', () => {
  const vehicles = ref<Vehicle[]>([])
  const currentStatus = ref<TelemetrySnapshot | null>(null)
  const vehicleConfig = ref<Record<string, string>>({})
  // shallowRef: these are only ever replaced wholesale on fetch, never mutated
  // field-by-field, so deep reactivity on every GPS point/telemetry snapshot is wasted work.
  const history = shallowRef<TelemetrySnapshot[]>([])
  const trips = shallowRef<Trip[]>([])
  const loadingCount = ref(0)
  const loading = computed(() => loadingCount.value > 0)
  const sendingCount = ref(0)
  const anySending = computed(() => sendingCount.value > 0)
  // Each fetch action gets its own error ref so concurrent calls (e.g. Promise.all on
  // mount) can't have one action's success silently overwrite another action's error.
  const vehiclesError = ref<string | null>(null)
  const statusError = ref<string | null>(null)
  const historyError = ref<string | null>(null)
  const tripsError = ref<string | null>(null)
  const lastUpdated = ref<Date | null>(null)

  // Reactive one-shot flags set for a single tick when specific state transitions occur.
  const tripJustCompleted = ref(false)
  const chargingJustCompleted = ref(false)

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

  // The vehicle list rarely changes during a session, so every view (Dashboard, Map, Statistics)
  // calling fetchVehicles() on mount would otherwise re-fetch it on every navigation. Skip the
  // request once it's already populated unless the caller explicitly asks for a fresh copy.
  async function fetchVehicles(force = false) {
    if (!force && vehicles.value.length > 0) return
    await withLoading(vehiclesError, async () => {
      vehicles.value = await vehicleApi.list()
    })
  }

  async function fetchStatus(vin: string) {
    await withLoading(statusError, async () => {
      currentStatus.value = await vehicleApi.status(vin)
      lastUpdated.value = new Date()
    })
  }

  async function fetchConfig(vin: string) {
    try {
      vehicleConfig.value = await vehicleApi.config(vin)
    } catch {
      // config may not exist yet
    }
  }

  async function fetchHistory(vin: string, from?: string, to?: string) {
    await withLoading(historyError, async () => {
      history.value = await vehicleApi.history(vin, from, to)
    })
  }

  function applyLiveStatus(snapshot: TelemetrySnapshot) {
    const prev = currentStatus.value

    // Charging complete: was charging, cable still connected, now stopped
    if (
      prev?.isCharging === true &&
      snapshot.isCharging === false &&
      snapshot.chargerConnected === true
    ) {
      chargingJustCompleted.value = true
      nextTick(() => {
        chargingJustCompleted.value = false
      })
    }

    currentStatus.value = snapshot
    lastUpdated.value = new Date()
  }

  function notifyTripCompleted() {
    tripJustCompleted.value = true
    nextTick(() => {
      tripJustCompleted.value = false
    })
  }

  async function fetchTrips(vin: string, from?: string, to?: string) {
    await withLoading(tripsError, async () => {
      trips.value = await vehicleApi.trips(vin, from, to)
    })
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
    loading,
    anySending,
    sendingCount,
    vehiclesError,
    statusError,
    historyError,
    tripsError,
    lastUpdated,
    tripJustCompleted,
    chargingJustCompleted,
    fetchVehicles,
    fetchStatus,
    fetchConfig,
    fetchHistory,
    fetchTrips,
    applyLiveStatus,
    notifyTripCompleted,
  }
})
