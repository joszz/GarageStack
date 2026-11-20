import { ref, watch } from 'vue'
import { useVehicleStore } from '@/stores/vehicle'
import { vehicleApi, type TyrePressureThresholds } from '@/services/vehicleApi'

export type { TyrePressureThresholds }

export const DEFAULT_TYRE_PRESSURE_THRESHOLDS: TyrePressureThresholds = {
  lowBar: 2.2,
  goodBar: 2.6,
  highBar: 3.2,
  dropBar: 0.15,
}

// Module-scoped so every component/composable calling useTyrePressureThresholds() shares the
// same fetch and the same reactive value, instead of each issuing its own request. Kept for one
// car at a time: the one on screen, whose own settings can move them.
const thresholds = ref<TyrePressureThresholds>({ ...DEFAULT_TYRE_PRESSURE_THRESHOLDS })
let fetchPromise: Promise<void> | null = null
let fetchedFor: string | null = null

function load(vin: string | null): Promise<void> {
  if (!fetchPromise || fetchedFor !== vin) {
    fetchedFor = vin
    const request = vehicleApi
      .tyrePressureThresholds(vin ?? undefined)
      .then((result) => {
        if (fetchPromise === request) thresholds.value = result
      })
      .catch(() => {
        // Keep what is shown on failure (e.g. offline, not yet authenticated); retry on next call.
        if (fetchPromise === request) fetchPromise = null
      })
    fetchPromise = request
  }
  return fetchPromise
}

/** Fetches the car's thresholds again, after its settings changed. */
export function reloadTyrePressureThresholds(vin: string | null): Promise<void> {
  fetchPromise = null
  return load(vin)
}

// Asked for once per page load: for the car once its VIN is known, and for the deployment's own
// thresholds only once the list has come back without a car. Until then the built-in defaults
// stand, as they would while the request is on its way.
export function useTyrePressureThresholds() {
  const vehicleStore = useVehicleStore()
  watch(
    () => vehicleStore.activeVin ?? (vehicleStore.vehiclesListed ? null : undefined),
    (vin) => {
      if (vin !== undefined) void load(vin)
    },
    { immediate: true },
  )
  return thresholds
}

export type PressureVariant = 'ok' | 'warning' | 'danger' | 'unknown'

export function pressureVariant(bar: number | null, t: TyrePressureThresholds): PressureVariant {
  if (bar === null) return 'unknown'
  if (bar < t.lowBar) return 'danger'
  if (bar < t.goodBar) return 'warning'
  if (bar > t.highBar) return 'danger'
  return 'ok'
}
