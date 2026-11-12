import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router'
import en from '@/locales/en.json'
import TripEditView from '../TripEditView.vue'
import TripEditMap from '@/components/TripEditMap.vue'
import { tripLogApi, type TripEditor } from '@/services/tripLogApi'
import { vehicleApi, type TripPoint, type Vehicle } from '@/services/vehicleApi'
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

vi.mock('@/services/vehicleApi', () => ({
  vehicleApi: { list: vi.fn<() => Promise<Vehicle[]>>() },
}))

vi.mock('@/composables/useReverseGeocode', () => ({
  useReverseGeocode: () => ({
    requestPlaces: () => {},
    placeFor: () => null,
  }),
}))

enableAutoUnmount(afterEach)

const api = vi.mocked(tripLogApi)
const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'bev' } as Vehicle

const at = (minutes: number) =>
  new Date(Date.parse('2026-01-01T10:00:00.000Z') + minutes * 60_000).toISOString()
const fix = (minutes: number, latitude: number): TripPoint => ({
  recordedAt: at(minutes),
  latitude,
  longitude: 5,
  speed: 50,
})
const route = [fix(0, 52.0), fix(5, 52.05), fix(10, 52.1)]

function editorOf(overrides: Partial<TripEditor> = {}): TripEditor {
  return {
    id: 7,
    points: route,
    originalPoints: null,
    editedAt: null,
    previousStop: null,
    nextStop: null,
    suggestion: false,
    notBefore: at(-600),
    notAfter: at(60),
    maxPoints: 203,
    ...overrides,
  }
}

const Log = defineComponent({ render: () => h('p', { class: 'log' }, 'log') })

let router: Router

async function mountView() {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/trips', name: 'tripLog', component: Log },
      { path: '/trips/:id(\\d+)/edit', name: 'tripEdit', component: TripEditView },
    ],
  })
  await router.push('/trips')
  await router.push('/trips/7/edit')
  const wrapper = mount(defineComponent({ render: () => h(RouterView) }), {
    global: {
      plugins: [i18n, createPinia(), router],
      stubs: { FontAwesomeIcon: true, TripEditMap: true },
    },
  })
  await flushPromises()
  return wrapper
}

function button(wrapper: Awaited<ReturnType<typeof mountView>>, text: string) {
  return wrapper.findAll('button').find((b) => b.text() === text)!
}

// Moves the start the way the map would after a drag.
async function moveStart(wrapper: Awaited<ReturnType<typeof mountView>>) {
  wrapper.findComponent(TripEditMap).vm.$emit('moveStart', 51.99, 5)
  await flushPromises()
}

describe('TripEditView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(vehicleApi.list).mockResolvedValue([car])
    api.editor.mockResolvedValue(editorOf())
  })

  afterEach(() => vi.unstubAllGlobals())

  it('loads the trip of the active car and shows the editor', async () => {
    const wrapper = await mountView()

    expect(api.editor).toHaveBeenCalledWith(car.vin, 7)
    expect(wrapper.findComponent(TripEditMap).exists()).toBe(true)
    expect(wrapper.text()).toContain(en.tripEdit.title)
  })

  it('says the trip cannot be edited when the server does not have it', async () => {
    api.editor.mockRejectedValue(new ApiError(404, '/editor'))

    const wrapper = await mountView()

    expect(wrapper.findComponent(TripEditMap).exists()).toBe(false)
    expect(wrapper.text()).toContain(en.tripEdit.backToLog)
  })

  it('leaves without asking while nothing changed', async () => {
    const confirm = vi.fn<(message?: string) => boolean>()
    vi.stubGlobal('confirm', confirm)
    const wrapper = await mountView()

    await button(wrapper, en.common.cancel).trigger('click')
    await flushPromises()

    expect(confirm).not.toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('tripLog')
  })

  it('asks before dropping a changed route, and stays when told to', async () => {
    const confirm = vi.fn<(message?: string) => boolean>().mockReturnValue(false)
    vi.stubGlobal('confirm', confirm)
    const wrapper = await mountView()
    await moveStart(wrapper)

    await button(wrapper, en.common.cancel).trigger('click')
    await flushPromises()

    expect(confirm).toHaveBeenCalledWith(en.tripEdit.leaveConfirm)
    expect(router.currentRoute.value.name).toBe('tripEdit')

    confirm.mockReturnValue(true)
    await router.push('/trips')
    expect(router.currentRoute.value.name).toBe('tripLog')
  })

  it('asks the browser to hold a reload while the route is changed', async () => {
    const wrapper = await mountView()
    const clean = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(clean)
    expect(clean.defaultPrevented).toBe(false)

    await moveStart(wrapper)
    const changed = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(changed)

    expect(changed.defaultPrevented).toBe(true)
  })

  it('goes back without asking once the save went through', async () => {
    const confirm = vi.fn<(message?: string) => boolean>()
    vi.stubGlobal('confirm', confirm)
    // The server answers with the trip as it now stands, which is what the editor then shows.
    api.setPoints.mockResolvedValue(editorOf({ points: [fix(0, 51.99), ...route.slice(1)] }))
    const wrapper = await mountView()
    await moveStart(wrapper)

    await button(wrapper, en.tripEdit.save).trigger('click')
    await flushPromises()

    expect(api.setPoints).toHaveBeenCalled()
    expect(confirm).not.toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('tripLog')
  })
})
