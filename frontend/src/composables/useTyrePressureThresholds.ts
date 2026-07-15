import { ref } from 'vue'
import { vehicleApi, type TyrePressureThresholds } from '@/services/vehicleApi'

export type { TyrePressureThresholds }

export const DEFAULT_TYRE_PRESSURE_THRESHOLDS: TyrePressureThresholds = {
  lowBar: 2.2,
  goodBar: 2.6,
  highBar: 3.2,
}

// Module-scoped so every component/composable calling useTyrePressureThresholds() shares the
// same fetch and the same reactive value, instead of each issuing its own request.
const thresholds = ref<TyrePressureThresholds>({ ...DEFAULT_TYRE_PRESSURE_THRESHOLDS })
let fetchPromise: Promise<void> | null = null

function load(): Promise<void> {
  if (!fetchPromise) {
    fetchPromise = vehicleApi
      .tyrePressureThresholds()
      .then((result) => {
        thresholds.value = result
      })
      .catch(() => {
        // Keep defaults on failure (e.g. offline, not yet authenticated); retry on next call.
        fetchPromise = null
      })
  }
  return fetchPromise
}

export function useTyrePressureThresholds() {
  void load()
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
