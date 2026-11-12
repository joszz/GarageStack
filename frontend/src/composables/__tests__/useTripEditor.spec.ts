import { describe, it, expect, beforeEach, vi } from 'vitest'
import { ref } from 'vue'
import { useTripEditor } from '../useTripEditor'
import { tripLogApi, type TripEditor } from '@/services/tripLogApi'
import type { TripPoint } from '@/services/vehicleApi'
import { ApiError } from '@/services/apiCore'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return {
    ...actual,
    tripLogApi: {
      editor: vi.fn<typeof actual.tripLogApi.editor>(),
      setPoints: vi.fn<typeof actual.tripLogApi.setPoints>(),
      resetPoints: vi.fn<typeof actual.tripLogApi.resetPoints>(),
    },
  }
})

const api = vi.mocked(tripLogApi)

const at = (minutes: number) =>
  new Date(Date.parse('2026-01-01T10:00:00.000Z') + minutes * 60_000).toISOString()
const fix = (minutes: number, latitude: number): TripPoint => ({
  recordedAt: at(minutes),
  latitude,
  longitude: 5,
  speed: 50,
})
const route = [fix(0, 52.0), fix(5, 52.05), fix(10, 52.1)]

// Parked 1.1 km before the first fix the evening before, as a trip saved before anchoring.
function editorOf(overrides: Partial<TripEditor> = {}): TripEditor {
  return {
    id: 7,
    points: route,
    originalPoints: null,
    editedAt: null,
    previousStop: { latitude: 51.99, longitude: 5, recordedAt: at(-600), suggestedAt: at(-4) },
    nextStop: { latitude: 52.1, longitude: 5, recordedAt: at(60), suggestedAt: at(10) },
    suggestion: true,
    notBefore: at(-600),
    notAfter: at(60),
    maxPoints: 203,
    ...overrides,
  }
}

async function loaded(editor = editorOf()) {
  api.editor.mockResolvedValue(editor)
  const editing = useTripEditor(ref('VIN1'), ref(7))
  await editing.load()
  return editing
}

describe('useTripEditor', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads the trip, with nothing to save or undo yet', async () => {
    const editing = await loaded()

    expect(api.editor).toHaveBeenCalledWith('VIN1', 7)
    expect(editing.points.value).toEqual(route)
    expect(editing.dirty.value).toBe(false)
    expect(editing.canUndo.value).toBe(false)
    expect(editing.suggestion.value).toBe(true)
    // The next trip starts where this one ends, so there is no other spot to end at.
    expect(editing.canUseNextStop.value).toBe(false)
  })

  it('keeps the newest trip when an older answer arrives after it', async () => {
    let answerFirst: (value: TripEditor) => void = () => {}
    api.editor
      .mockImplementationOnce(() => new Promise<TripEditor>((resolve) => (answerFirst = resolve)))
      .mockResolvedValueOnce(editorOf({ points: route.slice(0, 2) }))
    const tripId = ref(7)
    const editing = useTripEditor(ref('VIN1'), tripId)

    const first = editing.load()
    tripId.value = 8
    await editing.load()
    answerFirst(editorOf())
    await first

    expect(editing.points.value).toEqual(route.slice(0, 2))
    expect(editing.loading.value).toBe(false)
  })

  it('keeps the failure when the trip cannot be loaded', async () => {
    api.editor.mockRejectedValue(new ApiError(404, '/editor'))
    const editing = useTripEditor(ref('VIN1'), ref(7))

    await editing.load()

    expect(editing.editor.value).toBeNull()
    expect(editing.error.value).toBeInstanceOf(ApiError)
  })

  it('moves what the mode says with a tap, each tap a step undo takes back', async () => {
    const editing = await loaded()

    editing.placeAt(51.99, 5)
    editing.mode.value = 'end'
    editing.placeAt(52.11, 5)

    expect(editing.points.value).toHaveLength(5)
    expect(editing.points.value[0]!.latitude).toBe(51.99)
    expect(editing.points.value[4]!.latitude).toBe(52.11)
    expect(editing.distanceAfterKm.value).toBeGreaterThan(editing.distanceBeforeKm.value)

    editing.undo()
    expect(editing.points.value).toHaveLength(4)
    editing.undo()
    expect(editing.points.value).toEqual(route)
    expect(editing.dirty.value).toBe(false)
  })

  it('adds a point in points mode and selects it, and removes the selected one', async () => {
    const editing = await loaded()
    editing.mode.value = 'points'

    editing.placeAt(52.025, 5.001)
    expect(editing.selected.value).toBe(1)
    expect(editing.canRemove.value).toBe(true)

    editing.removeSelected()
    expect(editing.points.value).toEqual(route)
    expect(editing.selected.value).toBeNull()
  })

  it('selects only the points between the ends, and a second tap lets go', async () => {
    const editing = await loaded()

    editing.selectPoint(0)
    expect(editing.selected.value).toBeNull()
    editing.selectPoint(1)
    expect(editing.selected.value).toBe(1)
    editing.selectPoint(1)
    expect(editing.selected.value).toBeNull()
  })

  it('stops adding points at the most the server takes', async () => {
    const editing = await loaded(editorOf({ maxPoints: 3 }))
    editing.mode.value = 'points'

    editing.placeAt(52.025, 5.001)

    expect(editing.points.value).toEqual(route)
  })

  it('starts the trip at the previous parking spot with one tap, then stops offering it', async () => {
    const editing = await loaded()

    editing.usePreviousStop()

    expect(editing.points.value[0]).toMatchObject({ latitude: 51.99, recordedAt: at(-4) })
    expect(editing.suggestion.value).toBe(false)
    expect(editing.canUsePreviousStop.value).toBe(false)
  })

  it('saves the route and shows the trip as the server now has it', async () => {
    const editing = await loaded()
    editing.usePreviousStop()
    const edited = editing.points.value
    api.setPoints.mockResolvedValue(editorOf({ points: edited, originalPoints: route }))

    expect(await editing.save()).toBe(true)

    expect(api.setPoints).toHaveBeenCalledWith(
      'VIN1',
      7,
      edited.map(({ recordedAt, latitude, longitude }) => ({ recordedAt, latitude, longitude })),
    )
    expect(editing.dirty.value).toBe(false)
    expect(editing.canUndo.value).toBe(false)
  })

  it('resets a trip put back as recorded, rather than saving it as an edit', async () => {
    const edited = [fix(-4, 51.99), ...route]
    const editing = await loaded(editorOf({ points: edited, originalPoints: route }))
    api.resetPoints.mockResolvedValue(editorOf())

    editing.resetToRecorded()
    expect(editing.isRecorded.value).toBe(true)
    expect(await editing.save()).toBe(true)

    expect(api.resetPoints).toHaveBeenCalledWith('VIN1', 7)
    expect(api.setPoints).not.toHaveBeenCalled()
  })

  it('keeps the route and the failure when the server refuses it', async () => {
    const editing = await loaded()
    editing.usePreviousStop()
    api.setPoints.mockRejectedValue(new ApiError(400, '/points', { code: 'trip.pointTooFast' }))

    expect(await editing.save()).toBe(false)

    expect(editing.dirty.value).toBe(true)
    expect((editing.error.value as ApiError).code).toBe('trip.pointTooFast')
  })
})
