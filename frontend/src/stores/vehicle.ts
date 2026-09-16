import { defineStore } from 'pinia'
import { ref, shallowRef, computed, nextTick } from 'vue'
import {
  vehicleApi,
  type Vehicle,
  type VehicleType,
  type TelemetrySnapshot,
  type TelemetryHistoryPoint,
  type Trip,
} from '@/services/vehicleApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useLoadingTracker } from '@/composables/useLoadingTracker'

// Defined with the API contract it comes from; re-exported here because every view reads the
// vehicle's type through this store.
export type { VehicleType }

export const useVehicleStore = defineStore('vehicle', () => {
  const uiSettings = useUiSettingsStore()

  const vehicles = ref<Vehicle[]>([])
  const currentStatus = ref<TelemetrySnapshot | null>(null)
  const vehicleConfig = ref<Record<string, string>>({})
  // shallowRef: these are only ever replaced wholesale on fetch, never mutated
  // field-by-field, so deep reactivity on every GPS point/telemetry snapshot is wasted work.
  const history = shallowRef<TelemetryHistoryPoint[]>([])
  const trips = shallowRef<Trip[]>([])
  const { loading, withLoading } = useLoadingTracker()
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

  // GarageStack follows one car. Every view needs the same one, so "the first vehicle" is
  // resolved here instead of being re-derived from the list by index in each component.
  const activeVehicle = computed((): Vehicle | null => vehicles.value[0] ?? null)
  const activeVin = computed((): string | null => activeVehicle.value?.vin ?? null)

  // Detected by the API from the vehicle's reported hardware version, so the rule lives in one
  // place rather than once per client. Unknown until a vehicle has been fetched.
  const detectedVehicleType = computed(
    (): VehicleType => activeVehicle.value?.vehicleType ?? 'unknown',
  )

  // What every view should treat the car as: the user's manual override when set, otherwise
  // the detected type. Defined once here rather than in each view.
  const effectiveVehicleType = computed((): VehicleType => {
    const override = uiSettings.vehicleTypeOverride
    return override === 'auto' ? detectedVehicleType.value : override
  })

  return {
    vehicles,
    activeVehicle,
    activeVin,
    currentStatus,
    vehicleConfig,
    detectedVehicleType,
    effectiveVehicleType,
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
