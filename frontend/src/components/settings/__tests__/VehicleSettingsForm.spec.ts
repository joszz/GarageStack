import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { ApiError } from '@/services/apiCore'
import type { Vehicle, VehicleSettings, VehicleType } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import VehicleSettingsForm from '../VehicleSettingsForm.vue'

vi.mock('@/composables/useTyrePressureThresholds', () => ({
  reloadTyrePressureThresholds: vi.fn<(vin: string | null) => Promise<void>>().mockResolvedValue(),
}))

const empty = {
  hvBatteryCapacityKwh: null,
  tyreLowBar: null,
  tyreGoodBar: null,
  tyreHighBar: null,
  tyreDropBar: null,
  auxBatteryLowVolt: null,
  lowFuelPercent: null,
  lowEvPercent: null,
  vehicleType: null,
}

const settings: VehicleSettings = {
  values: { ...empty, tyreLowBar: 2.4 },
  defaults: {
    ...empty,
    tyreLowBar: 2.2,
    tyreGoodBar: 2.6,
    tyreHighBar: 3.2,
    tyreDropBar: 0.15,
    auxBatteryLowVolt: 12,
    lowFuelPercent: 15,
    lowEvPercent: 20,
  },
}

function car(vehicleType: VehicleType): Vehicle {
  return {
    id: 1,
    vin: 'FAKEVN00000000001',
    model: 'MG HS',
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType,
    vehicleTypeOverride: null,
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

let pinia: Pinia

function setUp(type: VehicleType) {
  const store = useVehicleStore(pinia)
  store.vehicles = [car(type)]
  vi.spyOn(store, 'fetchSettings').mockImplementation(async () => {
    store.vehicleSettings = settings
  })
  return store
}

beforeEach(() => {
  pinia = createPinia()
  setActivePinia(pinia)
  useUiSettingsStore(pinia).units.pressure = 'bar'
})

async function render() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  const wrapper = mount(VehicleSettingsForm, {
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true } },
  })
  await flushPromises()
  return wrapper
}

const field = (wrapper: Awaited<ReturnType<typeof render>>, key: string) =>
  wrapper.find<HTMLInputElement>(`input[data-field=${key}]`)

describe('VehicleSettingsForm', () => {
  it('names the car being edited, and shows each default as its placeholder', async () => {
    setUp('bev')
    const wrapper = await render()

    expect(wrapper.text()).toContain('Settings for MG HS')
    expect(field(wrapper, 'tyreLowBar').element.value).toBe('2.4')
    expect(field(wrapper, 'tyreGoodBar').element.value).toBe('')
    expect(field(wrapper, 'tyreGoodBar').attributes('placeholder')).toBe('2.60')
    expect(field(wrapper, 'hvBatteryCapacityKwh').attributes('placeholder')).toBe(
      en.settings.vehicleSettings.noDefault,
    )
  })

  it('offers no battery field for a plain hybrid', async () => {
    setUp('hev')
    const wrapper = await render()

    expect(field(wrapper, 'hvBatteryCapacityKwh').exists()).toBe(false)
    expect(field(wrapper, 'lowEvPercent').exists()).toBe(false)
    expect(field(wrapper, 'lowFuelPercent').exists()).toBe(true)
  })

  it('sends only what changed, a cleared field as null', async () => {
    const store = setUp('phev')
    const save = vi.spyOn(store, 'saveSettings').mockResolvedValue()
    const wrapper = await render()

    await field(wrapper, 'tyreLowBar').setValue('')
    await field(wrapper, 'hvBatteryCapacityKwh').setValue('21.4')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(save).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      hvBatteryCapacityKwh: 21.4,
      tyreLowBar: null,
    })
  })

  it('takes tyre pressures in the chosen unit and sends them in bar', async () => {
    useUiSettingsStore(pinia).units.pressure = 'psi'
    const store = setUp('bev')
    const save = vi.spyOn(store, 'saveSettings').mockResolvedValue()
    const wrapper = await render()

    expect(field(wrapper, 'tyreLowBar').element.value).toBe('34.8')
    await field(wrapper, 'tyreHighBar').setValue('45')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(save).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', { tyreHighBar: 3.103 })
  })

  it('says why saving failed', async () => {
    const store = setUp('bev')
    vi.spyOn(store, 'saveSettings').mockRejectedValue(
      new ApiError(400, '/api/vehicles/FAKEVN00000000001/settings', {
        code: 'vehicleSettings.tyreOrder',
      }),
    )
    const wrapper = await render()

    await field(wrapper, 'tyreLowBar').setValue('3')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('[role=alert]').text()).toBe(en.errors.codes.vehicleSettings.tyreOrder)
  })
})
