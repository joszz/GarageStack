import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { commutePlacesApi, type CommutePlace } from '@/services/commutePlacesApi'
import type { TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import CommutePlacesSetting from '../CommutePlacesSetting.vue'

vi.mock('@/services/commutePlacesApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/commutePlacesApi')>()),
  commutePlacesApi: { list: vi.fn<() => Promise<CommutePlace[]>>().mockResolvedValue([]) },
}))

// The real dialog brings a map; what matters here is what it is opened with.
vi.mock('../../CommutePlacesModal.vue', () => ({
  // Marks the mock as a module, so the async loader takes its default export.
  __esModule: true,
  default: defineComponent({
    name: 'CommutePlacesModal',
    props: ['open', 'vin', 'pick', 'fallbackCenter'],
    emits: ['close'],
    setup: () => () => h('div', { class: 'places-modal' }),
  }),
}))

const car = { id: 1, vin: 'FAKEVN00000000001', vehicleType: 'hev' } as Vehicle

let pinia: Pinia

function render() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(CommutePlacesSetting, {
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('CommutePlacesSetting', () => {
  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    const vehicles = useVehicleStore(pinia)
    vehicles.vehicles = [car]
    vehicles.currentStatus = { latitude: 52.2554, longitude: 6.1639 } as TelemetrySnapshot
  })

  it('loads the dialog only once it is asked for', async () => {
    const wrapper = render()
    await flushPromises()

    expect(wrapper.find('.places-modal').exists()).toBe(false)
    expect(commutePlacesApi.list).not.toHaveBeenCalled()
  })

  it("opens the car's places at its last position, with no trip end to start from", async () => {
    const wrapper = render()

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(commutePlacesApi.list).toHaveBeenCalledWith(car.vin)
    const modal = wrapper.findComponent({ name: 'CommutePlacesModal' })
    expect(modal.props()).toMatchObject({
      open: true,
      vin: car.vin,
      pick: null,
      fallbackCenter: [52.2554, 6.1639],
    })

    modal.vm.$emit('close')
    await flushPromises()
    expect(modal.props('open')).toBe(false)
  })

  it('offers nothing to open without a car', () => {
    useVehicleStore(pinia).vehicles = []

    const wrapper = render()

    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
  })
})
