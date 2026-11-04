import { computed, ref, shallowRef, type Ref } from 'vue'
import { tripLogApi, type TripEditor } from '@/services/tripLogApi'
import type { TripPoint } from '@/services/vehicleApi'
import {
  MIN_POINTS,
  routeKm,
  sameRoute,
  stopIsElsewhere,
  toInput,
  withEnd,
  withNextStop,
  withPointAdded,
  withPointRemoved,
  withPreviousStop,
  withStart,
  type TripLimits,
} from '@/utils/tripEdit'

/** What a tap on the map moves: the start, the end, or the points in between. */
export type TripEditMode = 'start' | 'end' | 'points'

export const TRIP_EDIT_MODES: readonly TripEditMode[] = ['start', 'end', 'points']

/**
 * The route editor's state for one saved trip: the route being edited, an undo history of every
 * step, and the calls that load and save it. Every operation is one of utils/tripEdit's, so this
 * holds no geometry of its own.
 */
export function useTripEditor(vin: Ref<string | null>, tripId: Ref<number>) {
  const editor = shallowRef<TripEditor | null>(null)
  const points = shallowRef<TripPoint[]>([])
  const history = shallowRef<TripPoint[][]>([])
  const mode = ref<TripEditMode>('start')
  const selected = ref<number | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const error = shallowRef<unknown>(null)

  const limits = computed<TripLimits>(() => ({
    notBefore: editor.value?.notBefore ?? null,
    notAfter: editor.value?.notAfter ?? null,
  }))

  /** The route as the car recorded it, edited since or not. */
  const recorded = computed(() => editor.value?.originalPoints ?? editor.value?.points ?? [])

  /** The route as last saved, before this visit's changes. */
  const saved = computed(() => editor.value?.points ?? [])

  const dirty = computed(() => !sameRoute(points.value, saved.value))
  const isRecorded = computed(() => sameRoute(points.value, recorded.value))
  const canUndo = computed(() => history.value.length > 0)
  const distanceBeforeKm = computed(() => routeKm(saved.value))
  const distanceAfterKm = computed(() => routeKm(points.value))
  const canUsePreviousStop = computed(() =>
    stopIsElsewhere(points.value[0], editor.value?.previousStop ?? null),
  )
  const canUseNextStop = computed(() =>
    stopIsElsewhere(points.value[points.value.length - 1], editor.value?.nextStop ?? null),
  )
  /** The trip probably left from the parking spot before it, and still does not. */
  const suggestion = computed(() => (editor.value?.suggestion ?? false) && canUsePreviousStop.value)
  const canRemove = computed(() => selected.value !== null && points.value.length > MIN_POINTS)
  /** Adding stops where the server would refuse the route for having too many points. */
  const canAdd = computed(() => points.value.length < (editor.value?.maxPoints ?? Infinity))

  function show(next: TripEditor) {
    editor.value = next
    points.value = next.points
    history.value = []
    selected.value = null
  }

  async function load() {
    if (!vin.value) return
    loading.value = true
    error.value = null
    try {
      show(await tripLogApi.editor(vin.value, tripId.value))
    } catch (e) {
      error.value = e
    } finally {
      loading.value = false
    }
  }

  // Every change is a step undo can take back.
  function change(next: TripPoint[]) {
    if (sameRoute(next, points.value)) return
    history.value = [...history.value, points.value]
    points.value = next
    error.value = null
  }

  /** Moves the start or the end there, or in points mode adds a point there. */
  function placeAt(latitude: number, longitude: number) {
    if (points.value.length < MIN_POINTS) return
    if (mode.value === 'start') {
      moveStart(latitude, longitude)
    } else if (mode.value === 'end') {
      moveEnd(latitude, longitude)
    } else if (canAdd.value) {
      const added = withPointAdded(points.value, latitude, longitude)
      change(added.points)
      selected.value = added.index
    }
  }

  function moveStart(latitude: number, longitude: number) {
    change(withStart(points.value, latitude, longitude, limits.value))
  }

  function moveEnd(latitude: number, longitude: number) {
    change(withEnd(points.value, latitude, longitude, limits.value))
  }

  /** Picks a point to remove; the start and the end are moved, not removed. */
  function selectPoint(index: number) {
    selected.value =
      index > 0 && index < points.value.length - 1 && selected.value !== index ? index : null
  }

  function removeSelected() {
    if (selected.value === null) return
    change(withPointRemoved(points.value, selected.value))
    selected.value = null
  }

  function usePreviousStop() {
    const stop = editor.value?.previousStop
    if (stop) change(withPreviousStop(points.value, stop, limits.value))
  }

  function useNextStop() {
    const stop = editor.value?.nextStop
    if (stop) change(withNextStop(points.value, stop, limits.value))
  }

  function undo() {
    const steps = history.value
    if (steps.length === 0) return
    points.value = steps[steps.length - 1]!
    history.value = steps.slice(0, -1)
    selected.value = null
    error.value = null
  }

  /** Back to the route the car recorded, as a step that can be undone; saving then makes it so. */
  function resetToRecorded() {
    change(recorded.value)
    selected.value = null
  }

  /** Saves the route; true once the server has it. A route back as recorded resets the trip. */
  async function save(): Promise<boolean> {
    if (!vin.value || !editor.value) return false
    saving.value = true
    error.value = null
    try {
      const wasEdited = editor.value.originalPoints !== null
      show(
        isRecorded.value && wasEdited
          ? await tripLogApi.resetPoints(vin.value, tripId.value)
          : await tripLogApi.setPoints(vin.value, tripId.value, toInput(points.value)),
      )
      return true
    } catch (e) {
      error.value = e
      return false
    } finally {
      saving.value = false
    }
  }

  return {
    editor,
    points,
    recorded,
    mode,
    selected,
    loading,
    saving,
    error,
    dirty,
    isRecorded,
    canUndo,
    canRemove,
    canAdd,
    canUsePreviousStop,
    canUseNextStop,
    suggestion,
    distanceBeforeKm,
    distanceAfterKm,
    load,
    placeAt,
    moveStart,
    moveEnd,
    selectPoint,
    removeSelected,
    usePreviousStop,
    useNextStop,
    undo,
    resetToRecorded,
    save,
  }
}
