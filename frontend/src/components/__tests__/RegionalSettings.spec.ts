import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { setRegionalFormat } from '@/utils/format'
import RegionalSettings from '../RegionalSettings.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// ICU puts a narrow no-break space before AM and PM.
const plain = (text: string) => text.replace(/\s/g, ' ')

function mountSettings() {
  return mount(RegionalSettings, { global: { plugins: [i18n] } })
}

function automaticLabel(wrapper: ReturnType<typeof mountSettings>, id: string) {
  return plain(wrapper.find(`#${id} option[value="auto"]`).text())
}

describe('RegionalSettings', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  afterEach(() => setRegionalFormat({ region: null, hourCycle: 'h23' }))

  it('says what every automatic choice stands for in the detected region', () => {
    const wrapper = mountSettings()

    expect(automaticLabel(wrapper, 'settings-region')).toBe('Automatic: Netherlands')
    expect(automaticLabel(wrapper, 'settings-clock')).toBe('Automatic: 24-hour (14:05)')
    expect(automaticLabel(wrapper, 'settings-unit-distance')).toBe(
      'Automatic: Kilometres (km, km/h)',
    )
    expect(automaticLabel(wrapper, 'settings-unit-pressure')).toBe('Automatic: bar')
  })

  it('follows a chosen region in every choice left on automatic', async () => {
    const wrapper = mountSettings()
    await wrapper.find('#settings-region').setValue('US')

    expect(useUiSettingsStore().region).toBe('US')
    expect(automaticLabel(wrapper, 'settings-clock')).toBe('Automatic: 12-hour (2:05 PM)')
    expect(automaticLabel(wrapper, 'settings-unit-distance')).toBe('Automatic: Miles (mi, mph)')
    expect(automaticLabel(wrapper, 'settings-unit-fuelConsumption')).toBe(
      'Automatic: mpg (US gallon)',
    )
  })

  it('stores a chosen clock and unit, and offers going back to automatic', async () => {
    const wrapper = mountSettings()
    const store = useUiSettingsStore()

    await wrapper.find('#settings-clock').setValue('h12')
    await wrapper.find('#settings-unit-temperature').setValue('fahrenheit')
    expect(store.clock).toBe('h12')
    expect(store.units.temperature).toBe('fahrenheit')

    await wrapper.find('#settings-unit-temperature').setValue('auto')
    expect(store.units.temperature).toBe('auto')
  })
})
