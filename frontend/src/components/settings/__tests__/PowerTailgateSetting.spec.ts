import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import PowerTailgateSetting from '../PowerTailgateSetting.vue'

const car: Vehicle = {
  id: 1,
  vin: 'FAKEVN00000000001',
  model: 'Test car',
  series: null,
  createdAt: '2026-01-01T00:00:00Z',
  vehicleType: 'bev',
  hvBatteryCapacityKwh: null,
  supportsTargetSoc: false,
  hasPowerTailgate: false,
}

let pinia: Pinia

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  useVehicleStore(pinia).vehicles = [{ ...car }]
})

function render() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(PowerTailgateSetting, {
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('PowerTailgateSetting', () => {
  it('shows what the vehicle has saved', () => {
    useVehicleStore(pinia).vehicles = [{ ...car, hasPowerTailgate: true }]

    const input = render().get<HTMLInputElement>('input[type=checkbox]')

    expect(input.element.checked).toBe(true)
  })

  it('saves the switch on the active vehicle', async () => {
    const store = useVehicleStore(pinia)
    const save = vi.spyOn(store, 'setPowerTailgate').mockResolvedValue()
    const wrapper = render()

    await wrapper.get('input[type=checkbox]').setValue(true)
    await flushPromises()

    expect(save).toHaveBeenCalledWith('FAKEVN00000000001', true)
    expect(wrapper.find('[role=alert]').exists()).toBe(false)
  })

  it('says why saving failed, and switches back', async () => {
    const store = useVehicleStore(pinia)
    vi.spyOn(store, 'setPowerTailgate').mockRejectedValue(new Error('Network error'))
    const wrapper = render()

    await wrapper.get('input[type=checkbox]').setValue(true)
    await flushPromises()

    expect(wrapper.get('[role=alert]').text()).not.toBe('')
    expect(wrapper.get<HTMLInputElement>('input[type=checkbox]').element.checked).toBe(false)
  })
})
