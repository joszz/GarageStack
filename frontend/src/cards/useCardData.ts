import { computed } from 'vue'
import { useVehicleStore } from '@/stores/vehicle'
import type { CardDataContext } from './registry'

/**
 * The live context the registry's hasData predicates read, straight from the vehicle store.
 * Null until the first status arrives, which is also the point before which no card can claim
 * to have anything to show.
 */
export function useCardData() {
  const store = useVehicleStore()

  return computed((): CardDataContext | null => {
    const status = store.currentStatus
    if (!status) return null
    return {
      status,
      vehicleType: store.effectiveVehicleType,
      latestTrip: store.trips[store.trips.length - 1] ?? null,
    }
  })
}
