import { ref, shallowRef, watch, type Ref } from 'vue'
import { tripLogApi, type TripLogEntry } from '@/services/tripLogApi'
import type { TripPoint } from '@/services/vehicleApi'
import { useAsyncRequest } from './useAsyncRequest'

/**
 * The fixes of the trip shown in the trip log's detail, for its map and speed chart. The log
 * itself comes without them, so they are fetched one trip at a time as trips are picked, from the
 * route editor's endpoint, which serves the route as it is now. A trip picked again is not
 * fetched again unless its route changed: an edit or a join moves its end or marks it edited.
 */
export function useTripRoute(vin: Ref<string | null>, entry: Ref<TripLogEntry | null>) {
  const points = shallowRef<readonly TripPoint[] | null>(null)
  const { loading, error, run } = useAsyncRequest()
  const cache = new Map<string, readonly TripPoint[]>()
  const shownKey = ref<string | null>(null)

  const keyOf = (trip: TripLogEntry) => `${trip.id}|${trip.endedAt}|${trip.isEdited}`

  watch(
    () => (vin.value && entry.value ? [vin.value, entry.value.id, keyOf(entry.value)] : null),
    async (wanted) => {
      if (!wanted) {
        shownKey.value = null
        points.value = null
        return
      }
      const [car, id, key] = wanted as [string, number, string]
      shownKey.value = key
      const cached = cache.get(key)
      if (cached) {
        points.value = cached
        return
      }
      // The previous trip's route must not stand in for this one while it loads.
      points.value = null
      await run(
        () => tripLogApi.editor(car, id),
        (editor) => {
          cache.set(key, editor.points)
          if (shownKey.value === key) points.value = editor.points
        },
      )
    },
    { immediate: true },
  )

  return { points, loading, error }
}
