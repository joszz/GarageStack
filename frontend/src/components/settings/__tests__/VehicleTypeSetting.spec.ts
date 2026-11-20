import { beforeEach, describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { flushPromises, shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import VehicleTypeSetting from '../VehicleTypeSetting.vue'

// By name: a generic component's prop types do not survive findComponent.
const settingsSelect = { name: 'SettingsSelect' }

const { uiSettings, applyTypeDefaults, detected, car, saveSettings, connection } = vi.hoisted(
  () => ({
    uiSettings: { vehicleTypeOverride: 'auto' },
    applyTypeDefaults: vi.fn<(type: string) => void>(),
    detected: { type: 'unknown', config: {} as Record<string, string> },
    // The type in effect as the real store works it out: the car's own, else the browser's older one.
    car: { override: null as string | null },
    saveSettings: vi.fn<(vin: string, changes: Record<string, unknown>) => Promise<void>>(),
    connection: { reachable: true },
  }),
)

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    activeVin: 'FAKEVN00000000001',
    get detectedVehicleType() {
      return detected.type
    },
    get vehicleConfig() {
      return detected.config
    },
    get vehicleTypeOverride() {
      return (
        car.override ??
        (uiSettings.vehicleTypeOverride === 'auto' ? null : uiSettings.vehicleTypeOverride)
      )
    },
    get effectiveVehicleType() {
      return this.vehicleTypeOverride ?? detected.type
    },
    saveSettings,
  }),
}))

vi.mock('@/composables/useConnection', () => ({
  useConnection: () => connection,
}))

vi.mock('@/stores/settingsUi', () => ({
  useUiSettingsStore: () => uiSettings,
}))

vi.mock('@/stores/settingsDashboard', () => ({
  useDashboardSettingsStore: () => ({ applyTypeDefaults }),
}))

function i18n() {
  return createI18n({
    legacy: false,
    locale: 'en',
    missingWarn: false,
    fallbackWarn: false,
    messages: {
      en: {
        settings: {
          vehicleType: {
            auto: 'Automatic',
            desc: 'Reported: {type}',
            notDetected: 'Not detected',
          },
        },
      },
      nl: { settings: { vehicleType: { auto: 'Automatisch' } } },
    },
  })
}

describe('VehicleTypeSetting', () => {
  beforeEach(() => {
    uiSettings.vehicleTypeOverride = 'auto'
    car.override = null
    connection.reachable = true
    saveSettings.mockReset()
    saveSettings.mockImplementation(async (_vin, changes) => {
      car.override = (changes.vehicleType as string | null) ?? null
    })
    applyTypeDefaults.mockReset()
    detected.type = 'unknown'
    detected.config = {}
  })

  // The detection sits in the description, so the row needs no second one to show it.
  it('says what the gateway detected, with the series it came from', () => {
    const desc = () =>
      shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })
        .findComponent(settingsSelect)
        .props('desc')

    expect(desc()).toBe('Not detected')
    detected.type = 'bev'
    expect(desc()).toBe('Reported: BEV')
    detected.config = { hw_version: 'MG_BEV_1.0' }
    expect(desc()).toBe('Reported: BEV (MG_BEV_1.0)')
  })

  it('relabels the vehicle type options when the language changes', async () => {
    const plugin = i18n()
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [plugin] } })
    const autoLabel = () =>
      (
        wrapper.findComponent(settingsSelect).props('options') as { value: string; label: string }[]
      ).find((option) => option.value === 'auto')?.label

    expect(autoLabel()).toBe('Automatic')

    plugin.global.locale.value = 'nl'
    await nextTick()

    expect(autoLabel()).toBe('Automatisch')
  })

  it('saves the chosen type with the car and gives the drivetrain cards its defaults', async () => {
    uiSettings.vehicleTypeOverride = 'phev'
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    wrapper.findComponent(settingsSelect).vm.$emit('update:model-value', 'bev')
    await flushPromises()

    expect(saveSettings).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      vehicleType: 'bev',
    })
    // The car holds it now, so the browser's older choice is cleared.
    expect(uiSettings.vehicleTypeOverride).toBe('auto')
    expect(applyTypeDefaults).toHaveBeenCalledExactlyOnceWith('bev')
  })

  it('clears the battery size when the car is set to a plain hybrid', async () => {
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    wrapper.findComponent(settingsSelect).vm.$emit('update:model-value', 'hev')
    await flushPromises()

    expect(saveSettings).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      vehicleType: 'hev',
      hvBatteryCapacityKwh: null,
    })
  })

  it('puts the car back on the detected type with auto', async () => {
    car.override = 'phev'
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    wrapper.findComponent(settingsSelect).vm.$emit('update:model-value', 'auto')
    await flushPromises()

    expect(saveSettings).toHaveBeenCalledExactlyOnceWith('FAKEVN00000000001', {
      vehicleType: null,
    })
  })

  // A one-car install that chose its type before the type moved to the car keeps it.
  it('shows a type saved in the browser settings until the car has one', () => {
    uiSettings.vehicleTypeOverride = 'hev'
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    expect(wrapper.findComponent(settingsSelect).props('modelValue')).toBe('hev')
  })

  it('cannot be changed while the server is out of reach', () => {
    connection.reachable = false
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    expect(wrapper.findComponent(settingsSelect).props('disabled')).toBe(true)
  })
})
