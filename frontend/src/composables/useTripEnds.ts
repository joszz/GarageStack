import { computed, type Ref } from 'vue'
import type { Place } from '@/services/mapApi'
import type { TripLogEntry } from '@/services/tripLogApi'
import type { CommutePlace } from '@/services/commutePlacesApi'
import { endLabel } from '@/utils/tripLog'
import { commutePlaceAt } from '@/utils/commutePlaces'

/** One end of a trip as the trip log shows it. */
export interface TripEnd {
  /** The address is on its way: show a placeholder rather than coordinates that flash past. */
  pending: boolean
  /** The address, or the coordinates when there is none to be had; null while pending. */
  label: string | null
  /** Home or work, when the end is at one of them. */
  commutePlace: CommutePlace | null
  lat: number
  lng: number
}

export interface TripEndsOptions {
  entry: Ref<TripLogEntry>
  /** False when place names are switched off: the ends then read as coordinates. */
  placesShown: Ref<boolean>
  /** True while addresses are being looked up, so a missing one shows a placeholder. */
  placesResolving: Ref<boolean>
  commutePlaces: Ref<readonly CommutePlace[]>
}

/** Where a trip in the log left from and went to, shared by its row and its detail. */
export function useTripEnds({
  entry,
  placesShown,
  placesResolving,
  commutePlaces,
}: TripEndsOptions) {
  function tripEnd(place: Place | null, lat: number, lng: number): TripEnd {
    const pending = placesShown.value && place === null && placesResolving.value
    return {
      pending,
      label: pending ? null : endLabel(placesShown.value ? place : null, lat, lng),
      commutePlace: commutePlaceAt(commutePlaces.value, lat, lng),
      lat,
      lng,
    }
  }

  const from = computed(() =>
    tripEnd(entry.value.startPlace, entry.value.startLatitude, entry.value.startLongitude),
  )
  const to = computed(() =>
    tripEnd(entry.value.endPlace, entry.value.endLatitude, entry.value.endLongitude),
  )

  return { from, to }
}
