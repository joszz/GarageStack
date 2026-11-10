import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { tripLogApi, type TripEditor, type TripLogEntry } from '@/services/tripLogApi'
import type { TripPoint } from '@/services/vehicleApi'
import { logEntry } from '@/services/__tests__/tripLogFixtures'
import { useTripRoute } from '../useTripRoute'

vi.mock('@/services/tripLogApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/services/tripLogApi')>()
  return { ...actual, tripLogApi: { editor: vi.fn<typeof actual.tripLogApi.editor>() } }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const point = (latitude: number): TripPoint => ({
  recordedAt: '2026-09-24T12:40:00.000Z',
  latitude,
  longitude: 6,
  speed: 50,
})

function editor(id: number, points: TripPoint[]): TripEditor {
  return {
    id,
    points,
    originalPoints: null,
    editedAt: null,
    previousStop: null,
    nextStop: null,
    suggestion: false,
    notBefore: null,
    notAfter: null,
    maxPoints: 2000,
  }
}

function mountRoute(initial: TripLogEntry | null) {
  const entry = ref<TripLogEntry | null>(initial)
  let route: ReturnType<typeof useTripRoute> | undefined
  mount(
    defineComponent({
      setup() {
        route = useTripRoute(ref('FAKEVN00000000001'), entry)
        return () => h('div')
      },
    }),
    { global: { plugins: [i18n] } },
  )
  return { entry, route: route! }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('useTripRoute', () => {
  it('reloads a route when phone fusion changes it without a manual edit', async () => {
    vi.mocked(tripLogApi.editor).mockImplementation(async (_, id) => editor(id, [point(id)]))
    const { entry } = mountRoute(logEntry({ id: 1 }))
    await flushPromises()
    entry.value = logEntry({ id: 1, phoneFusionAt: '2026-01-01T12:00:00Z' })
    await flushPromises()
    expect(tripLogApi.editor).toHaveBeenCalledTimes(2)
  })
  it('loads nothing without a trip', () => {
    const { route } = mountRoute(null)

    expect(tripLogApi.editor).not.toHaveBeenCalled()
    expect(route.points.value).toBeNull()
  })

  it('loads the picked trip, and does not load a trip picked before again', async () => {
    vi.mocked(tripLogApi.editor).mockImplementation(async (_, id) => editor(id, [point(id)]))
    const { entry, route } = mountRoute(logEntry({ id: 1 }))
    await flushPromises()

    entry.value = logEntry({ id: 2 })
    await flushPromises()
    entry.value = logEntry({ id: 1 })
    await flushPromises()

    expect(vi.mocked(tripLogApi.editor).mock.calls.map(([, id]) => id)).toEqual([1, 2])
    expect(route.points.value).toEqual([point(1)])
  })

  it('loads a trip again once its route was edited', async () => {
    vi.mocked(tripLogApi.editor).mockImplementation(async (_, id) => editor(id, [point(id)]))
    const { entry } = mountRoute(logEntry({ id: 1 }))
    await flushPromises()

    entry.value = logEntry({ id: 1, isEdited: true })
    await flushPromises()

    expect(tripLogApi.editor).toHaveBeenCalledTimes(2)
  })

  it('does not let a slow answer for an earlier trip replace the one picked since', async () => {
    let answerFirst: (value: TripEditor) => void = () => {}
    vi.mocked(tripLogApi.editor)
      .mockImplementationOnce(() => new Promise((resolve) => (answerFirst = resolve)))
      .mockImplementationOnce(async (_, id) => editor(id, [point(2)]))
    const { entry, route } = mountRoute(logEntry({ id: 1 }))

    entry.value = logEntry({ id: 2 })
    await flushPromises()
    answerFirst(editor(1, [point(1)]))
    await flushPromises()

    expect(route.points.value).toEqual([point(2)])
  })
})
