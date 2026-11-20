import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { useVehicleStore } from '@/stores/vehicle'
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
  /**
   * Counts the changes saved, each of which may have re-labelled trips: the trip log page reloads
   * on it, whether the change came from its own dialog or from the settings.
   */
  const revision = ref(0)
  // The list belongs to one car: it is dropped when the user picks another, and an answer for the
  // car switched away from (a slow one, still on its way) is not kept.
  const vehicleStore = useVehicleStore()
  watch(
    () => vehicleStore.switchCount,
    () => {
      places.value = []
    },
  )

  async function fetchPlaces(vin: string) {
    await withLoading(loadError, async () => {
      const list = await commutePlacesApi.list(vin)
      if (vehicleStore.isForAnotherCar(vin)) return
      places.value = list
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
      revision.value++
      // The list comes back in the API's order (home first), which a local splice would not keep.
      const list = await commutePlacesApi.list(vin)
      if (vehicleStore.isForAnotherCar(vin)) return
      places.value = list
    })
    return change
  }

  async function removePlace(vin: string, id: number): Promise<CommutePlaceChange | null> {
    let change: CommutePlaceChange | null = null
    await withLoading(actionError, async () => {
      change = await commutePlacesApi.remove(vin, id)
      revision.value++
      if (vehicleStore.isForAnotherCar(vin)) return
      places.value = places.value.filter((p) => p.id !== id)
    })
    return change
  }

  return {
    places,
    loading,
    loadError,
    actionError,
    revision,
    fetchPlaces,
    savePlace,
    removePlace,
  }
})
