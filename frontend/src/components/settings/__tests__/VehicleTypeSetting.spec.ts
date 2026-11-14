import { beforeEach, describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import VehicleTypeSetting from '../VehicleTypeSetting.vue'

// By name: a generic component's prop types do not survive findComponent.
const settingsSelect = { name: 'SettingsSelect' }

const { uiSettings, applyTypeDefaults, detected } = vi.hoisted(() => ({
  uiSettings: { vehicleTypeOverride: 'auto' },
  applyTypeDefaults: vi.fn<(type: string) => void>(),
  detected: { type: 'unknown', config: {} as Record<string, string> },
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    get detectedVehicleType() {
      return detected.type
    },
    get vehicleConfig() {
      return detected.config
    },
    // Follows the override, as the real store's does.
    get effectiveVehicleType() {
      return uiSettings.vehicleTypeOverride === 'auto' ? 'unknown' : uiSettings.vehicleTypeOverride
    },
  }),
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

  it('gives the drivetrain cards the chosen type defaults when the driver picks a type', () => {
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    wrapper.findComponent(settingsSelect).vm.$emit('update:model-value', 'bev')

    expect(uiSettings.vehicleTypeOverride).toBe('bev')
    expect(applyTypeDefaults).toHaveBeenCalledExactlyOnceWith('bev')
  })
})
