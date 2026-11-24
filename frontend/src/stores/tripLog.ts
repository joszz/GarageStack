import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { useLoadingTracker } from '@/composables/useLoadingTracker'
import { asError } from '@/utils/errors'
import {
  tripLogApi,
  MAX_TRIPS_PER_PURPOSE_CHANGE,
  type TripLogEntry,
  type TripPurpose,
} from '@/services/tripLogApi'
import { periodRange, periodSummary, purposeTotals, type TripLogPeriod } from '@/utils/tripLog'
import { lookUpTripPlaces, withPlaces } from '@/utils/tripLogPlaces'

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

  /** The trips that count: everything but the hidden ones, which stay listable to show again. */
  const visibleEntries = computed(() => entries.value.filter((entry) => !entry.isHidden))
  const hiddenCount = computed(() => entries.value.length - visibleEntries.value.length)

  const totals = computed(() => purposeTotals(visibleEntries.value))
  const summary = computed(() => periodSummary(visibleEntries.value))

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
      patchEntries((entry) => (entry.id === id ? keepPlaces(updated, entry) : entry))
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
    const ids = visibleEntries.value
      .filter((entry) => entry.purpose === null)
      .map((entry) => entry.id)
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

  // Places this page looked up but the server does not keep ("nothing mapped here") stay as they
  // are rather than being looked up all over again.
  function keepPlaces(updated: TripLogEntry, entry: TripLogEntry): TripLogEntry {
    return {
      ...updated,
      startPlace: updated.startPlace ?? entry.startPlace,
      endPlace: updated.endPlace ?? entry.endPlace,
    }
  }

  /** Hides a trip from the map, the statistics and these totals, or shows it again. */
  async function setHidden(vin: string, id: number, hidden: boolean): Promise<boolean> {
    markSaving([id], true)
    actionError.value = null
    try {
      const updated = await tripLogApi.setHidden(vin, id, hidden)
      patchEntries((entry) => (entry.id === id ? keepPlaces(updated, entry) : entry))
      return true
    } catch (e) {
      actionError.value = asError(e)
      return false
    } finally {
      markSaving([id], false)
    }
  }

  /**
   * Joins a trip with the saved trip after it. The joined trip takes this one's place in the list
   * and the next one goes; the server removes it the same way.
   */
  async function mergeWithNext(vin: string, id: number): Promise<boolean> {
    const index = entries.value.findIndex((entry) => entry.id === id)
    const next = entries.value[index + 1]
    markSaving(next ? [id, next.id] : [id], true)
    actionError.value = null
    try {
      const merged = await tripLogApi.mergeWithNext(vin, id)
      entries.value = entries.value
        .filter((entry) => entry.id !== next?.id)
        .map((entry) => (entry.id === id ? keepPlaces(merged, entry) : entry))
      return true
    } catch (e) {
      actionError.value = asError(e)
      return false
    } finally {
      markSaving(next ? [id, next.id] : [id], false)
    }
  }

  /**
   * Undoes the join that made this trip: the two trips the server puts back take its place in the
   * list, in the order they were driven. The second may start in the next period, so the page
   * loads the log again afterwards, which also looks up the addresses they are missing.
   */
  async function unjoin(vin: string, id: number): Promise<boolean> {
    markSaving([id], true)
    actionError.value = null
    try {
      const restored = await tripLogApi.unjoin(vin, id)
      entries.value = entries.value.flatMap((entry) => (entry.id === id ? restored : [entry]))
      return true
    } catch (e) {
      actionError.value = asError(e)
      return false
    } finally {
      markSaving([id], false)
    }
  }

  /**
   * Looks up the addresses the listed trips are still missing (see lookUpTripPlaces). It works on
   * whatever the list holds at each round, so paging to another month mid-way carries on with
   * that month's trips.
   */
  async function resolvePlaces(vin: string, language: string) {
    if (!placesAvailable.value) return
    // A run already going picks up whatever the list now holds; it only has to be told it is
    // wanted again, in case it was stopped and is finishing its last round.
    placesStopped = false
    if (placesResolving.value) return
    placesResolving.value = true

    try {
      const available = await lookUpTripPlaces(vin, language, {
        entries: () => entries.value,
        apply: (found) => {
          entries.value = withPlaces(entries.value, found)
        },
        active: () => !placesStopped,
        onEnd: () => {
          placesResolving.value = false
        },
      })
      if (!available) placesAvailable.value = false
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
    visibleEntries,
    hiddenCount,
    loading,
    loadError,
    actionError,
    saving,
    placesResolving,
    placesAvailable,
    totals,
    summary,
    fetchLog,
    updateEntry,
    classifyUnclassified,
    setHidden,
    mergeWithNext,
    unjoin,
    resolvePlaces,
    stopResolvingPlaces,
  }
})
