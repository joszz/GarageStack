import { describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import VehicleTypeSetting from '../VehicleTypeSetting.vue'

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({ detectedVehicleType: 'unknown', vehicleConfig: {} }),
}))

vi.mock('@/stores/settingsUi', () => ({
  useUiSettingsStore: () => ({ vehicleTypeOverride: 'auto' }),
}))

describe('VehicleTypeSetting', () => {
  it('relabels the vehicle type options when the language changes', async () => {
    const i18n = createI18n({
      legacy: false,
      locale: 'en',
      missingWarn: false,
      fallbackWarn: false,
      messages: {
        en: { settings: { vehicleType: { auto: 'Automatic' } } },
        nl: { settings: { vehicleType: { auto: 'Automatisch' } } },
      },
    })
    const wrapper = shallowMount(VehicleTypeSetting, { global: { plugins: [i18n] } })
    const autoLabel = () =>
      (
        wrapper.findComponent(Multiselect).props('options') as { value: string; label: string }[]
      ).find((option) => option.value === 'auto')?.label

    expect(autoLabel()).toBe('Automatic')

    i18n.global.locale.value = 'nl'
    await nextTick()

    expect(autoLabel()).toBe('Automatisch')
  })
})
