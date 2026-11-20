import { defineStore } from 'pinia'
import { ref, shallowRef, computed, nextTick, type ShallowRef } from 'vue'
import {
  vehicleApi,
  type Vehicle,
  type VehicleType,
  type SettableVehicleType,
  type VehicleSettings,
  type VehicleSettingsValues,
  type TelemetrySnapshot,
  type DailyStat,
  type Trip,
  type TripSummary,
  type CommandResult,
} from '@/services/vehicleApi'
import { browserTimeZone, localDateKey } from '@/utils/dates'
import { counterScale, type CounterScale } from '@/utils/energy'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useAuthStore } from '@/stores/auth'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { rememberVin } from '@/utils/lastVin'
import { readLastKnownState, saveLastKnownState } from '@/utils/lastKnownState'

// Defined with the API contract it comes from; re-exported here because every view reads the
// vehicle's type through this store.
export type { VehicleType }

/**
 * Stores an answer unless it equals the one already held. A refetch that finds nothing new (back
 * on the tab after a moment, a push about a reading already shown) then keeps the old object, so
 * nothing that reads it computes or renders again: on the dashboard that is every card.
 */
function keepIfUnchanged<T>(target: ShallowRef<T>, answer: T): void {
  if (JSON.stringify(target.value) !== JSON.stringify(answer)) target.value = answer
}

export const useVehicleStore = defineStore('vehicle', () => {
  const uiSettings = useUiSettingsStore()

  // The car as this browser last loaded it, shown until the server answers, and instead of
  // nothing when it cannot be reached. Only for a session still held: the copy is forgotten on
  // sign-out, and a stale one must not show before the login page.
  const saved = useAuthStore().isAuthenticated ? readLastKnownState() : null
  // True while the car on screen is that saved copy rather than what the server said this visit.
  const showingSavedState = ref(saved !== null)
  // The saved list is only a stand-in: the first fetchVehicles still asks the server.
  let vehiclesFromSave = saved !== null

  const vehicles = ref<Vehicle[]>(saved?.vehicles ?? [])
  const vehicleConfig = ref<Record<string, string>>(saved?.config ?? {})
  // shallowRef: these are only ever replaced wholesale on fetch, never mutated
  // field-by-field, so deep reactivity on every GPS point/telemetry snapshot is wasted work.
  const currentStatus = shallowRef<TelemetrySnapshot | null>(saved?.status ?? null)
  const dailyStats = shallowRef<DailyStat[]>([])
  // Today's row of those statistics for the dashboard, kept apart from the statistics page's
  // period. The API checks today's energy counter against every reading of the day, which the
  // live status alone cannot (utils/energy).
  const todayStat = shallowRef<DailyStat | null>(null)
  let todayStatRequest = 0
  let todayStatVin: string | null = null
  const trips = shallowRef<Trip[]>([])
  // Separate from `trips`, which the map fills with a period of trips and all their fixes: the
  // dashboard needs only the newest trip, and the statistics page only the figures of a period.
  const latestTrip = shallowRef<Trip | null>(saved?.latestTrip ?? null)
  const tripSummaries = shallowRef<TripSummary[]>([])
  const { loading, withLoading } = useLoadingTracker()
  const sendingCount = ref(0)
  const anySending = computed(() => sendingCount.value > 0)
  // Each fetch action gets its own error ref so concurrent calls (e.g. Promise.all on
  // mount) can't have one action's success silently overwrite another action's error.
  const vehiclesError = ref<Error | null>(null)
  const statusError = ref<Error | null>(null)
  const dailyStatsError = ref<Error | null>(null)
  const tripsError = ref<Error | null>(null)
  const latestTripError = ref<Error | null>(null)
  const tripSummariesError = ref<Error | null>(null)
  const lastUpdated = ref<Date | null>(null)

  // Reactive one-shot flags set for a single tick when specific state transitions occur.
  const tripJustCompleted = ref(false)
  const chargingJustCompleted = ref(false)

  // The gateway's latest answer to a command. Each answer is a new object, so a watcher sees it
  // even when it repeats the previous one; useVehicleCommand picks out the ones it is waiting on.
  const lastCommandResult = shallowRef<CommandResult | null>(null)

  // The vehicle list rarely changes during a session, so every view (Dashboard, Map, Statistics)
  // calling fetchVehicles() on mount would otherwise re-fetch it on every navigation. Skip the
  // request once it's already populated unless the caller explicitly asks for a fresh copy.
  async function fetchVehicles(force = false) {
    if (!force && !vehiclesFromSave && vehicles.value.length > 0) return
    await withLoading(vehiclesError, async () => {
      vehicles.value = await vehicleApi.list()
      vehiclesFromSave = false
      if (activeVin.value) rememberVin(activeVin.value)
      saveState()
    })
  }

  // Every answer the dashboard shows is written through to the saved copy, so it is as fresh as
  // the last thing the server said. Nothing is saved before the car is known.
  function saveState() {
    if (!activeVin.value) return
    saveLastKnownState({
      vehicles: vehicles.value,
      status: currentStatus.value,
      config: vehicleConfig.value,
      latestTrip: latestTrip.value,
    })
  }

  function takeStatus(snapshot: TelemetrySnapshot) {
    keepIfUnchanged(currentStatus, snapshot)
    showingSavedState.value = false
    lastUpdated.value = new Date()
    saveState()
  }

  // Kept on the vehicle by the API, so the list copy is updated in place rather than re-fetched.
  async function setPowerTailgate(vin: string, enabled: boolean) {
    const saved = await vehicleApi.setPowerTailgate(vin, enabled)
    vehicles.value = vehicles.value.map((v) =>
      v.vin === vin ? { ...v, hasPowerTailgate: saved.enabled } : v,
    )
  }

  // The car's own settings with the deployment's defaults, for the settings page. Only the
  // active car's: whichever car is edited is the one the page names.
  const vehicleSettings = shallowRef<VehicleSettings | null>(null)

  async function fetchSettings(vin: string) {
    vehicleSettings.value = await vehicleApi.settings(vin)
  }

  // The list is fetched again after a save: the battery size it carries is the one in effect,
  // which the server works out from the car's type and the deployment's default.
  async function saveSettings(vin: string, changes: Partial<VehicleSettingsValues>) {
    vehicleSettings.value = await vehicleApi.updateSettings(vin, changes)
    await fetchVehicles(true)
  }

  async function fetchStatus(vin: string) {
    await withLoading(statusError, async () => {
      takeStatus(await vehicleApi.status(vin))
    })
  }

  async function fetchConfig(vin: string) {
    try {
      vehicleConfig.value = await vehicleApi.config(vin)
      saveState()
    } catch {
      // config may not exist yet
    }
  }

  async function fetchDailyStats(vin: string, from: string, to: string) {
    await withLoading(dailyStatsError, async () => {
      dailyStats.value = await vehicleApi.dailyStats(vin, from, to, browserTimeZone())
    })
  }

  // Fetched again with every new status, so answers can overlap: only the latest request's answer
  // is kept. A failure keeps the previous row, and the dashboard falls back to the live reading.
  async function fetchTodayStat(vin: string) {
    const request = ++todayStatRequest
    if (vin !== todayStatVin) {
      todayStat.value = null
      todayStatVin = vin
    }
    const today = localDateKey(new Date())
    try {
      const days = await vehicleApi.dailyStats(vin, today, today, browserTimeZone())
      if (request === todayStatRequest) todayStat.value = days[0] ?? null
    } catch {
      // Not worth an error on the dashboard: its cards still have the live status.
    }
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

    takeStatus(snapshot)
  }

  function notifyTripCompleted() {
    tripJustCompleted.value = true
    nextTick(() => {
      tripJustCompleted.value = false
    })
  }

  function applyCommandResult(result: CommandResult) {
    lastCommandResult.value = result
  }

  async function fetchTrips(vin: string, from?: string, to?: string) {
    await withLoading(tripsError, async () => {
      trips.value = await vehicleApi.trips(vin, from, to)
    })
  }

  async function fetchLatestTrip(vin: string) {
    await withLoading(latestTripError, async () => {
      keepIfUnchanged(latestTrip, (await vehicleApi.latestTrip(vin)) ?? null)
      saveState()
    })
  }

  async function fetchTripSummaries(vin: string, from?: string, to?: string) {
    await withLoading(tripSummariesError, async () => {
      tripSummaries.value = await vehicleApi.tripSummaries(vin, from, to)
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

  // The type set for this car in its settings on the server, or one saved in this account's
  // browser settings before the type moved there. That older one belongs to no car in particular,
  // so it stands only while there is a single car to apply it to.
  const vehicleTypeOverride = computed((): SettableVehicleType | null => {
    const own = activeVehicle.value?.vehicleTypeOverride
    if (own) return own
    const legacy = uiSettings.vehicleTypeOverride
    return legacy !== 'auto' && legacy !== 'unknown' && vehicles.value.length <= 1 ? legacy : null
  })

  // What every view should treat the car as: the user's manual override when set, otherwise
  // the detected type. Defined once here rather than in each view.
  const effectiveVehicleType = computed(
    (): VehicleType => vehicleTypeOverride.value ?? detectedVehicleType.value,
  )

  /** Where the car was last seen, for a map with nothing else to start at; null before a fix. */
  const currentPosition = computed((): [number, number] | null => {
    const status = currentStatus.value
    return status?.latitude != null && status.longitude != null
      ? [status.latitude, status.longitude]
      : null
  })

  // The deployment's answer for how big the traction battery really is, or null when it has not
  // been told. Read alongside the snapshot's own kWh figures in utils/energy.
  const hvBatteryCapacityKwh = computed(
    (): number | null => activeVehicle.value?.hvBatteryCapacityKwh ?? null,
  )

  // How the energy counters read on this car, against the pack size the gateway reports.
  const energyCounterScale = computed((): CounterScale =>
    counterScale(
      effectiveVehicleType.value,
      hvBatteryCapacityKwh.value,
      currentStatus.value?.hvTotalCapacityKwh ?? null,
    ),
  )

  return {
    vehicles,
    activeVehicle,
    activeVin,
    currentStatus,
    currentPosition,
    vehicleConfig,
    detectedVehicleType,
    vehicleTypeOverride,
    effectiveVehicleType,
    vehicleSettings,
    hvBatteryCapacityKwh,
    energyCounterScale,
    dailyStats,
    todayStat,
    trips,
    latestTrip,
    tripSummaries,
    loading,
    anySending,
    sendingCount,
    vehiclesError,
    statusError,
    dailyStatsError,
    tripsError,
    latestTripError,
    tripSummariesError,
    lastUpdated,
    showingSavedState,
    tripJustCompleted,
    chargingJustCompleted,
    lastCommandResult,
    fetchVehicles,
    setPowerTailgate,
    fetchSettings,
    saveSettings,
    fetchStatus,
    fetchConfig,
    fetchDailyStats,
    fetchTodayStat,
    fetchTrips,
    fetchLatestTrip,
    fetchTripSummaries,
    applyLiveStatus,
    notifyTripCompleted,
    applyCommandResult,
  }
})
