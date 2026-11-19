import { computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useVehicleStore } from '@/stores/vehicle'
import { useReverseGeocode } from '@/composables/useReverseGeocode'
import { addressLabel } from '@/utils/places'
import { carLocation } from '@/utils/carLocation'

/**
 * Where the active car is, as the dashboard shows it: its position, the trip it is driving (if
 * any), and the way to the full map. Shared by the location card and
 * the map behind the dashboard, so both tell the same story.
 */
export function useCarLocation() {
  const router = useRouter()
  const store = useVehicleStore()

  const status = computed(() => store.currentStatus)
  const location = computed(() => carLocation(store.activeVehicle, status.value))
  const hasLocation = computed(() => location.value !== null)
  const center = computed<[number, number]>(() => [
    location.value?.latitude ?? 0,
    location.value?.longitude ?? 0,
  ])

  // The API serves the still-open segment as the latest trip while the car hasn't parked for 5+
  // minutes, so a positive currentJourneyDistance means that trip is the one being driven.
  const activeTrip = computed(() => {
    if (status.value?.vehicleId !== store.activeVehicle?.id) return null
    const dist = status.value?.currentJourneyDistance
    if (dist == null || dist <= 0) return null
    return store.latestTrip
  })

  function openFullMap() {
    // Same deep-link MapView already supports for the "active trip" dashboard card - it
    // auto-selects the last trip in the list, which is the in-progress one when there is one.
    if (activeTrip.value) {
      router.push({ name: 'map', query: { selectLatest: '1' } })
    } else {
      router.push({ name: 'map' })
    }
  }

  return {
    status,
    location,
    hasLocation,
    center,
    activeTrip,
    openFullMap,
  }
}

export type CarLocationState = ReturnType<typeof useCarLocation>

/**
 * The street the car is parked in, so the dashboard says where it is rather than only showing it.
 * Only while parked: on the move this would ask for a new address on every position update, and
 * name a street the car has already left. The map draws the live route instead.
 */
export function useParkedAddress({ location, center, activeTrip }: CarLocationState) {
  const { requestPlaces, placeFor, placeNamesEnabled, placesResolving } = useReverseGeocode()

  const parkedAt = computed(() =>
    activeTrip.value || !location.value || location.value.mayBeMoving ? null : center.value,
  )

  watch(
    parkedAt,
    (point) => {
      if (point) requestPlaces([{ lat: point[0], lng: point[1] }], 'address')
    },
    { immediate: true },
  )

  const address = computed(() =>
    addressLabel(placeFor(parkedAt.value?.[0], parkedAt.value?.[1], 'address')),
  )

  const addressPending = computed(
    () =>
      parkedAt.value !== null && !address.value && placeNamesEnabled.value && placesResolving.value,
  )

  return { address, addressPending }
}
