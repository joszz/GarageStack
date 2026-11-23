import { shallowRef, watch, type Ref } from 'vue'
import { tripLogApi, type ElevationReading, type TripLogEntry } from '@/services/tripLogApi'

/**
 * The heights of the trip shown in the trip log's detail, for its elevation profile, fetched one
 * trip at a time as trips are picked. A trip picked again is not fetched again unless its span
 * changed, as an edit or a join can make it. A car that reports no height, or a profile that
 * cannot be had, leaves the list empty: the detail then shows no profile, and says nothing,
 * since a height is a nicety rather than something the trip log is for.
 */
export function useTripElevation(vin: Ref<string | null>, entry: Ref<TripLogEntry | null>) {
  const readings = shallowRef<readonly ElevationReading[]>([])
  const cache = new Map<string, readonly ElevationReading[]>()
  let shownKey: string | null = null

  watch(
    () =>
      vin.value && entry.value
        ? ([
            vin.value,
            entry.value.id,
            `${vin.value}|${entry.value.id}|${entry.value.startedAt}|${entry.value.endedAt}`,
          ] as const)
        : null,
    async (wanted) => {
      readings.value = []
      shownKey = wanted?.[2] ?? null
      if (!wanted) return
      const [car, id, key] = wanted
      const cached = cache.get(key)
      if (cached) {
        readings.value = cached
        return
      }
      try {
        const fetched = await tripLogApi.elevation(car, id)
        cache.set(key, fetched)
        if (shownKey === key) readings.value = fetched
      } catch {
        // No profile then; the rest of the detail stands.
      }
    },
    { immediate: true },
  )

  return readings
}
