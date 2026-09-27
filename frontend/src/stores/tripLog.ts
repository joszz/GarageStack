import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { asError } from '@/utils/errors'
import { resolveInRounds } from '@/utils/resolveInRounds'
import { GEOCODE_MAX_ATTEMPTS, GEOCODE_RETRY_DELAY_MS } from '@/services/mapApi'
import {
  tripLogApi,
  MAX_TRIPS_PER_PLACES_REQUEST,
  MAX_TRIPS_PER_PURPOSE_CHANGE,
  type TripLogEntry,
  type TripPlaces,
  type TripPurpose,
} from '@/services/tripLogApi'
import { missingPlaces, periodRange, purposeTotals, type TripLogPeriod } from '@/utils/tripLog'

export const useTripLogStore = defineStore('tripLog', () => {
  // shallowRef: entries are only ever replaced, never mutated in place.
  const entries = shallowRef<TripLogEntry[]>([])
  const { loading, withLoading } = useLoadingTracker()
  const loadError = ref<Error | null>(null)
  const actionError = ref<Error | null>(null)
  /** Trips with a change on its way to the server, so their controls can wait for it. */
  const saving = ref<ReadonlySet<number>>(new Set())
  const placesResolving = ref(false)
  /** False once the deployment has said it has geocoding switched off. */
  const placesAvailable = ref(true)

  // Each load gets a number, and only the newest may fill the list: paging through months
  // quickly must not let a slow answer for an earlier month land last.
  let latestLoad = 0
  let placesStopped = false

  const totals = computed(() => purposeTotals(entries.value))

  async function fetchLog(vin: string, period: TripLogPeriod) {
    const load = ++latestLoad
    const { from, to } = periodRange(period)
    await withLoading(loadError, async () => {
      const loaded = await tripLogApi.log(vin, from.toISOString(), to.toISOString())
      if (load === latestLoad) entries.value = loaded
    })
  }

  function markSaving(ids: readonly number[], on: boolean) {
    const next = new Set(saving.value)
    for (const id of ids) {
      if (on) next.add(id)
      else next.delete(id)
    }
    saving.value = next
  }

  function patchEntries(patch: (entry: TripLogEntry) => TripLogEntry) {
    entries.value = entries.value.map(patch)
  }

  /** Records a trip's purpose and notes. Resolves to false, with `actionError` set, when it failed. */
  async function updateEntry(
    vin: string,
    id: number,
    purpose: TripPurpose | null,
    notes: string | null,
  ): Promise<boolean> {
    markSaving([id], true)
    actionError.value = null
    try {
      const updated = await tripLogApi.update(vin, id, purpose, notes)
      // Places this page looked up but the server does not keep ("nothing mapped here") stay
      // as they are rather than being looked up all over again.
      patchEntries((entry) =>
        entry.id === id
          ? {
              ...updated,
              startPlace: updated.startPlace ?? entry.startPlace,
              endPlace: updated.endPlace ?? entry.endPlace,
            }
          : entry,
      )
      return true
    } catch (e) {
      actionError.value = asError(e)
      return false
    } finally {
      markSaving([id], false)
    }
  }

  /** Gives every trip on the page that has no purpose yet this one. */
  async function classifyUnclassified(vin: string, purpose: TripPurpose): Promise<boolean> {
    const ids = entries.value.filter((entry) => entry.purpose === null).map((entry) => entry.id)
    if (ids.length === 0) return true

    markSaving(ids, true)
    actionError.value = null
    try {
      for (let i = 0; i < ids.length; i += MAX_TRIPS_PER_PURPOSE_CHANGE) {
        const chunk = ids.slice(i, i + MAX_TRIPS_PER_PURPOSE_CHANGE)
        await tripLogApi.setPurpose(vin, chunk, purpose)
        const changed = new Set(chunk)
        patchEntries((entry) => (changed.has(entry.id) ? { ...entry, purpose } : entry))
      }
      return true
    } catch (e) {
      actionError.value = asError(e)
      return false
    } finally {
      markSaving(ids, false)
    }
  }

  function applyPlaces(found: readonly TripPlaces[]) {
    const byId = new Map(found.map((places) => [places.id, places]))
    patchEntries((entry) => {
      const places = byId.get(entry.id)
      return places
        ? {
            ...entry,
            startPlace: places.startPlace ?? entry.startPlace,
            endPlace: places.endPlace ?? entry.endPlace,
          }
        : entry
    })
  }

  /**
   * Looks up the addresses the listed trips are still missing, a batch per round with a pause
   * between rounds, until every one is known or given up on. The server keeps what it finds, so
   * a trip is looked up only once. It works on whatever the list holds at each round, so paging
   * to another month mid-way carries on with that month's trips.
   */
  async function resolvePlaces(vin: string, language: string) {
    if (!placesAvailable.value) return
    // A run already going picks up whatever the list now holds; it only has to be told it is
    // wanted again, in case it was stopped and is finishing its last round.
    placesStopped = false
    if (placesResolving.value) return
    placesResolving.value = true
    const attempts = new Map<number, number>()
    const wanted = (entry: TripLogEntry) =>
      missingPlaces(entry) && (attempts.get(entry.id) ?? 0) < GEOCODE_MAX_ATTEMPTS

    try {
      await resolveInRounds<TripLogEntry>(
        {
          active: () => !placesStopped,
          nextBatch: () => entries.value.filter(wanted).slice(0, MAX_TRIPS_PER_PLACES_REQUEST),
          ask: async (batch) => {
            const result = await tripLogApi.resolvePlaces(
              vin,
              batch.map((entry) => entry.id),
              language,
            )
            if (!result.available) {
              placesAvailable.value = false
              return null
            }
            applyPlaces(result.trips)
            const current = new Map(entries.value.map((entry) => [entry.id, entry]))
            return batch.filter((entry) => {
              const now = current.get(entry.id)
              return now !== undefined && missingPlaces(now)
            })
          },
          countAttempt: (entry) => attempts.set(entry.id, (attempts.get(entry.id) ?? 0) + 1),
          hasMore: () => entries.value.some(wanted),
          onEnd: () => {
            placesResolving.value = false
          },
        },
        GEOCODE_RETRY_DELAY_MS,
      )
    } catch {
      // Offline or signed out: the addresses fill in on the next visit instead.
    }
  }

  /** Stops the lookups after the current round, for when the page closes or place names go off. */
  function stopResolvingPlaces() {
    placesStopped = true
  }

  return {
    entries,
    loading,
    loadError,
    actionError,
    saving,
    placesResolving,
    placesAvailable,
    totals,
    fetchLog,
    updateEntry,
    classifyUnclassified,
    resolvePlaces,
    stopResolvingPlaces,
  }
})
