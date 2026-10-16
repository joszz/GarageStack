import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { METRIC_UNITS } from '@/utils/units'
import DemoControlPanel from '../DemoControlPanel.vue'

const { setStatus } = vi.hoisted(() => ({
  setStatus: vi.fn<(vin: string, override: Record<string, unknown>) => Promise<void>>(),
}))
vi.mock('@/services/demoApi', () => ({ demoApi: { setStatus } }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const VIN = 'FAKEVN00000000001'

function render(values: Partial<TelemetrySnapshot> = {}) {
  const vehicles = useVehicleStore()
  vehicles.vehicles = [{ vin: VIN } as Vehicle]
  vehicles.currentStatus = values as TelemetrySnapshot
  vi.spyOn(vehicles, 'fetchStatus').mockResolvedValue()
  return mount(DemoControlPanel, {
    props: { open: true },
    global: { plugins: [i18n], stubs: { Transition: false } },
  })
}

describe('DemoControlPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useUiSettingsStore().units = { ...METRIC_UNITS }
    setStatus.mockReset().mockResolvedValue()
  })

  it('tells assistive tech which toggles are on', () => {
    const wrapper = render({ isLocked: true, engineRunning: false })
    const pressed = (label: string) =>
      wrapper
        .findAll('.demo-toggle-btn')
        .find((b) => b.text() === label)!
        .attributes('aria-pressed')

    expect(pressed(en.demo.locked)).toBe('true')
    expect(pressed(en.demo.engine)).toBe('false')
  })

  it('labels the temperature fields', () => {
    const wrapper = render()

    const input = wrapper.find('#demo-interior-temperature')
    expect(input.exists()).toBe(true)
    expect(wrapper.find('label[for="demo-interior-temperature"]').text()).toContain(
      en.demo.interior,
    )
  })

  it('shows and takes temperatures in the chosen unit, and sends them in Celsius', async () => {
    useUiSettingsStore().units = { ...METRIC_UNITS, temperature: 'fahrenheit' }
    const wrapper = render({ interiorTemperature: 20 })
    const input = wrapper.find<HTMLInputElement>('#demo-interior-temperature')
    expect(input.element.value).toBe('68')
    expect(wrapper.find('label[for="demo-interior-temperature"]').text()).toContain('°F')

    await input.setValue('77')
    await input.trigger('change')
    await flushPromises()

    expect(setStatus).toHaveBeenCalledWith(VIN, { interiorTemperature: 25 })
  })

  it('shows the speed in the chosen unit', () => {
    useUiSettingsStore().units = { ...METRIC_UNITS, distance: 'mi' }

    expect(render().text()).toContain('0 mph')
  })
})
