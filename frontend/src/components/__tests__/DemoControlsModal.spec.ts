import { beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot, Vehicle } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { METRIC_UNITS } from '@/utils/units'
import DemoControlsModal from '../DemoControlsModal.vue'

const { setStatus } = vi.hoisted(() => ({
  setStatus: vi.fn<(vin: string, override: Record<string, unknown>) => Promise<void>>(),
}))
vi.mock('@/services/demoApi', () => ({
  demoApi: { setStatus, setGateway: vi.fn<(signedIn: boolean) => Promise<void>>() },
}))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const ZS = { id: 1, vin: 'FAKEVN00000000001', model: 'MG ZS EV', vehicleType: 'bev' } as Vehicle
const HS = { id: 2, vin: 'FAKEVN00000000002', model: 'MG HS PHEV', vehicleType: 'phev' } as Vehicle

enableAutoUnmount(beforeEach)

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  useUiSettingsStore().units = { ...METRIC_UNITS }
  setStatus.mockReset().mockResolvedValue()
})

function render(values: Partial<TelemetrySnapshot> = {}, cars: Vehicle[] = [ZS], shown = cars[0]!) {
  const vehicles = useVehicleStore()
  vehicles.vehicles = cars
  vehicles.selectVehicle(shown.vin)
  vehicles.currentStatus = values as TelemetrySnapshot
  vi.spyOn(vehicles, 'fetchStatus').mockResolvedValue()
  return mount(DemoControlsModal, {
    props: { open: true },
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { Teleport: true, FontAwesomeIcon: true } },
  })
}

const switchFor = (wrapper: ReturnType<typeof render>, label: string) =>
  wrapper.get<HTMLInputElement>(`input[role="switch"][aria-label="${label}"]`)

const sliderFor = (wrapper: ReturnType<typeof render>, label: string) =>
  wrapper.get<HTMLInputElement>(`input[type="range"][aria-label="${label}"]`)

async function openTab(wrapper: ReturnType<typeof render>, label: string) {
  await wrapper
    .findAll('[role="tab"]')
    .find((tab) => tab.text().includes(label))!
    .trigger('click')
}

describe('DemoControlsModal', () => {
  it('shows each switch as the car reports it, and flips it for the car shown', async () => {
    const wrapper = render({ isLocked: true, driverDoorOpen: false })

    expect(switchFor(wrapper, en.demo.locked).element.checked).toBe(true)
    await switchFor(wrapper, en.demo.locked).trigger('change')
    await flushPromises()

    expect(setStatus).toHaveBeenCalledWith(ZS.vin, { isLocked: false })
  })

  it('changes the second car when that one is shown, and says which car in the title', async () => {
    const wrapper = render({ isLocked: false }, [ZS, HS], HS)

    expect(wrapper.get('.detail-modal__title').text()).toBe(`MG HS PHEV · ${en.demo.controlPanel}`)
    await switchFor(wrapper, en.demo.locked).trigger('change')
    await flushPromises()

    expect(setStatus).toHaveBeenCalledWith(HS.vin, { isLocked: true })
  })

  // The title names the car, too long to share its row with five tabs on a phone, and the rows
  // sit side by side where the dialog is wide enough.
  it('puts its tabs under the title on a phone and its rows in columns', () => {
    const wrapper = render()

    expect(wrapper.get('.detail-modal').classes()).toContain('detail-modal--navigation-below')
    expect(wrapper.get('.detail-modal').classes()).toContain('detail-modal--fit-content')
    const lists = wrapper.findAll('.detail-list')
    expect(lists.length).toBeGreaterThan(0)
    expect(lists.every((list) => list.classes('detail-list--columns'))).toBe(true)
  })

  // Every tab stays mounted in one place, so the dialog keeps the tallest one's height; only the
  // chosen one shows.
  it('shows only the chosen tab, keeping the others in place', async () => {
    const wrapper = render()
    const shown = () =>
      wrapper
        .findAll('[role="tabpanel"]')
        .filter((panel) => !panel.classes('tab-panels__panel--inactive'))
        .map((panel) => panel.attributes('aria-labelledby'))

    expect(wrapper.findAll('.tab-panels > [role="tabpanel"]')).toHaveLength(5)
    expect(shown()).toEqual([expect.stringMatching(/-tab-car$/)])

    await openTab(wrapper, en.demo.tabs.tyres)
    expect(shown()).toEqual([expect.stringMatching(/-tab-tyres$/)])
  })

  it('names no car in the title while there is only one', () => {
    expect(render().get('.detail-modal__title').text()).toBe(en.demo.controlPanel)
  })

  it('sends a slider once it is let go, in the metric unit, and reads it out in the chosen one', async () => {
    useUiSettingsStore().units = { ...METRIC_UNITS, temperature: 'fahrenheit' }
    const wrapper = render({ interiorTemperature: 20 })
    await openTab(wrapper, en.demo.tabs.climate)
    const slider = sliderFor(wrapper, en.demo.interior)

    expect(slider.attributes('aria-valuetext')).toContain('°F')
    // Dragged: the slider moves, nothing is sent yet.
    slider.element.value = '25'
    await slider.trigger('input')
    expect(setStatus).not.toHaveBeenCalled()

    await slider.trigger('change')
    await flushPromises()
    expect(setStatus).toHaveBeenCalledWith(ZS.vin, { interiorTemperature: 25 })
  })

  it('sets the lights as one switch position, the lamps below it lit as well', async () => {
    const wrapper = render({ lightsSide: true })
    const chip = (label: string) => wrapper.findAll('.toggle-chip').find((c) => c.text() === label)!

    expect(chip(en.demo.sidelights).attributes('aria-pressed')).toBe('true')
    await chip(en.demo.dippedBeam).trigger('click')
    await flushPromises()

    expect(setStatus).toHaveBeenCalledWith(ZS.vin, {
      lightsMainBeam: false,
      lightsDippedBeam: true,
      lightsSide: true,
    })
  })

  it('puts a parked car on the road, and reads one with a journey under way as driving', async () => {
    const parked = render({ speed: 0, currentJourneyDistance: 0 })
    await openTab(parked, en.demo.tabs.driving)
    const chip = (label: string) => parked.findAll('.toggle-chip').find((c) => c.text() === label)!

    expect(chip(en.demo.parked).attributes('aria-pressed')).toBe('true')
    await chip(en.demo.driving).trigger('click')
    await flushPromises()
    expect(setStatus).toHaveBeenCalledWith(ZS.vin, { driving: true })
  })

  it('offers the fuel level only on a car that may have a tank', async () => {
    const battery = render({}, [ZS])
    expect(battery.find(`input[aria-label="${en.demo.fuel}"]`).exists()).toBe(false)
    battery.unmount()

    const hybrid = render({ fuelLevelPercent: 40 }, [ZS, HS], HS)
    expect(sliderFor(hybrid, en.demo.fuel).element.value).toBe('40')
  })

  it('leaves the charging power alone until the car charges', async () => {
    const idle = render({ isCharging: false })
    expect(sliderFor(idle, en.demo.chargingPower).element.disabled).toBe(true)
    idle.unmount()

    const charging = render({ isCharging: true, chargingType: 'AC', obcPowerSinglePhase: 6.8 })
    expect(sliderFor(charging, en.demo.chargingPower).element.disabled).toBe(false)
    expect(sliderFor(charging, en.demo.chargingPower).element.value).toBe('6.8')
  })

  it('says so when the demo car does not take a change', async () => {
    setStatus.mockRejectedValue(new Error('offline'))
    const wrapper = render({ isLocked: true })

    await switchFor(wrapper, en.demo.locked).trigger('change')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain(en.demo.failed)
  })
})
