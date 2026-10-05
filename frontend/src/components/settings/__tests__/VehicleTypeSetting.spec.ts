import { beforeEach, describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import VehicleTypeSetting from '../VehicleTypeSetting.vue'

const { uiSettings, applyTypeDefaults } = vi.hoisted(() => ({
  uiSettings: { vehicleTypeOverride: 'auto' },
  applyTypeDefaults: vi.fn<(type: string) => void>(),
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    detectedVehicleType: 'unknown',
    vehicleConfig: {},
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
      en: { settings: { vehicleType: { auto: 'Automatic' } } },
      nl: { settings: { vehicleType: { auto: 'Automatisch' } } },
    },
  })
}

describe('VehicleTypeSetting', () => {
  beforeEach(() => {
    uiSettings.vehicleTypeOverride = 'auto'
    applyTypeDefaults.mockReset()
  })

  it('relabels the vehicle type options when the language changes', async () => {
    const plugin = i18n()
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [plugin] } })
    const autoLabel = () =>
      (
        wrapper.findComponent(Multiselect).props('options') as { value: string; label: string }[]
      ).find((option) => option.value === 'auto')?.label

    expect(autoLabel()).toBe('Automatic')

    plugin.global.locale.value = 'nl'
    await nextTick()

    expect(autoLabel()).toBe('Automatisch')
  })

  it('gives the drivetrain cards the chosen type defaults when the driver picks a type', () => {
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n()] } })

    wrapper.findComponent(Multiselect).vm.$emit('update:model-value', 'bev')

    expect(uiSettings.vehicleTypeOverride).toBe('bev')
    expect(applyTypeDefaults).toHaveBeenCalledExactlyOnceWith('bev')
  })
})
