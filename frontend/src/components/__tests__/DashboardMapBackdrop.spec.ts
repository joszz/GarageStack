import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import type { TelemetrySnapshot, Trip, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import DashboardMapBackdrop from '../DashboardMapBackdrop.vue'

const push = vi.fn<(to: { name: string; query?: Record<string, string> }) => void>()
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/composables/useBasemap', () => ({ useBasemap: () => undefined }))

const observers: TestResizeObserver[] = []
class TestResizeObserver implements ResizeObserver {
  readonly targets = new Set<Element>()
  constructor(readonly callback: ResizeObserverCallback) {
    observers.push(this)
  }
  observe(target: Element) {
    this.targets.add(target)
  }
  unobserve(target: Element) {
    this.targets.delete(target)
  }
  disconnect() {
    this.targets.clear()
  }
}

enableAutoUnmount(afterEach)

const car = { id: 1, vin: 'FAKEVN00000000001', model: 'MG' } as Vehicle
const parked = {
  vehicleId: 1,
  latitude: 52.09,
  longitude: 5.12,
  heading: 90,
  currentJourneyDistance: 0,
  speed: 0,
  engineRunning: false,
  lastVehicleStateAt: null,
} as TelemetrySnapshot

// jsdom lays nothing out: the map and the spot say where they are, as a browser would. The map is
// 1000 by 800 at the page's top left, and the spot a 400 px square in its top right corner.
const MAP_BOX = new DOMRect(0, 0, 1000, 800)
let spotBox = new DOMRect(600, 0, 400, 400)

function spotElement(): HTMLElement {
  const spot = document.createElement('div')
  spot.getBoundingClientRect = () => spotBox
  return spot
}

let pinia: Pinia

async function mountBackdrop(spot: HTMLElement | null = spotElement()) {
  const wrapper = mount(DashboardMapBackdrop, {
    props: { spot },
    attachTo: document.body,
    global: { plugins: [pinia] },
  })
  await flushPromises()
  return wrapper
}

/** Where the marker stands in the map's container, as Leaflet placed it (without 3D transforms). */
function markerPoint(wrapper: Awaited<ReturnType<typeof mountBackdrop>>) {
  const icon = wrapper.get<HTMLElement>('.leaflet-marker-icon').element
  const pane = wrapper.get<HTMLElement>('.leaflet-map-pane').element
  const at = (el: HTMLElement) => [
    parseFloat(el.style.left || '0'),
    parseFloat(el.style.top || '0'),
  ]
  const [x, y] = at(icon)
  const [paneX, paneY] = at(pane)
  return [x! + paneX!, y! + paneY!]
}

describe('DashboardMapBackdrop', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    const store = useVehicleStore(pinia)
    store.vehicles = [car]
    store.currentStatus = parked
    spotBox = new DOMRect(600, 0, 400, 400)
    observers.length = 0
    vi.clearAllMocks()
    vi.stubGlobal('ResizeObserver', TestResizeObserver)
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
      this: HTMLElement,
    ) {
      return this.classList.contains('map-backdrop__map') ? MAP_BOX : new DOMRect()
    })
    // Leaflet takes the map's size from the container's client size.
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (
      this: HTMLElement,
    ) {
      return this.classList.contains('map-backdrop__map') ? MAP_BOX.width : 0
    })
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
      this: HTMLElement,
    ) {
      return this.classList.contains('map-backdrop__map') ? MAP_BOX.height : 0
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('parks the car in the middle of the spot, not of the map', async () => {
    const wrapper = await mountBackdrop()

    expect(markerPoint(wrapper)).toEqual([800, 200])
  })

  it('draws the credits in the corner of the spot, where no card covers them', async () => {
    const wrapper = await mountBackdrop()

    const style = wrapper.get<HTMLElement>('.map-backdrop__map').element.style
    expect(style.getPropertyValue('--spot-right')).toBe('0px')
    expect(style.getPropertyValue('--spot-bottom')).toBe('400px')
    expect(style.getPropertyValue('--spot-width')).toBe('400px')
  })

  it('follows the spot when the layout moves it', async () => {
    const spot = spotElement()
    const wrapper = await mountBackdrop(spot)

    spotBox = new DOMRect(0, 400, 400, 400)
    const observer = observers.find((o) => o.targets.has(spot))!
    observer.callback([], observer)

    expect(markerPoint(wrapper)).toEqual([200, 600])
  })

  it('opens the full map from the pin', async () => {
    const wrapper = await mountBackdrop()

    await wrapper.get('.leaflet-marker-icon').trigger('click')

    expect(push).toHaveBeenCalledWith({ name: 'map' })
  })

  it('shows the car on its way, with the route so far, while a trip is driven', async () => {
    const store = useVehicleStore(pinia)
    store.currentStatus = { ...parked, currentJourneyDistance: 3 }
    store.latestTrip = {
      points: [
        { latitude: 52.08, longitude: 5.1 },
        { latitude: 52.09, longitude: 5.12 },
      ],
    } as unknown as Trip
    const wrapper = await mountBackdrop()

    expect(wrapper.find('.trip-marker--active').exists()).toBe(true)
    expect(wrapper.find('.leaflet-overlay-pane path').exists()).toBe(true)
    // The route is fitted around the car, which stays in the middle of the spot.
    expect(markerPoint(wrapper)).toEqual([800, 200])
  })

  it('draws no map while the car has no position', async () => {
    useVehicleStore(pinia).currentStatus = { ...parked, latitude: null, longitude: null }
    const wrapper = await mountBackdrop()

    expect(wrapper.find('.map-backdrop__map').exists()).toBe(false)
  })
})
