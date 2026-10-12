import { defineStore } from 'pinia'
import { ref } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import {
  commutePlacesApi,
  type CommutePlace,
  type CommutePlaceChange,
  type CommutePlaceInput,
} from '@/services/commutePlacesApi'

export const useCommutePlacesStore = defineStore('commutePlaces', () => {
  const places = ref<CommutePlace[]>([])
  const { loading, withLoading } = useLoadingTracker()
  const loadError = ref<Error | null>(null)
  const actionError = ref<Error | null>(null)

  async function fetchPlaces(vin: string) {
    await withLoading(loadError, async () => {
      places.value = await commutePlacesApi.list(vin)
    })
  }

  /**
   * Adds a place, or replaces the one with `id`. Resolves to how many trips it re-labelled, or
   * null with `actionError` set when it failed.
   */
  async function savePlace(
    vin: string,
    input: CommutePlaceInput,
    id?: number,
  ): Promise<CommutePlaceChange | null> {
    let change: CommutePlaceChange | null = null
    await withLoading(actionError, async () => {
      change =
        id === undefined
          ? await commutePlacesApi.create(vin, input)
          : await commutePlacesApi.update(vin, id, input)
      // The list comes back in the API's order (home first), which a local splice would not keep.
      places.value = await commutePlacesApi.list(vin)
    })
    return change
  }

  async function removePlace(vin: string, id: number): Promise<CommutePlaceChange | null> {
    let change: CommutePlaceChange | null = null
    await withLoading(actionError, async () => {
      change = await commutePlacesApi.remove(vin, id)
      places.value = places.value.filter((p) => p.id !== id)
    })
    return change
  }

  return { places, loading, loadError, actionError, fetchPlaces, savePlace, removePlace }
})
