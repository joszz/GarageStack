import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import { useUiSettingsStore } from '@/stores/settingsUi'
import CarColorPicker from '../CarColorPicker.vue'

let pinia: Pinia

beforeEach(() => {
  localStorage.clear()
  pinia = createPinia()
  setActivePinia(pinia)
})

function render() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(CarColorPicker, {
    global: { plugins: [pinia, i18n], stubs: { FontAwesomeIcon: true } },
  })
}

const swatch = (wrapper: ReturnType<typeof render>, name: string) =>
  wrapper.get(`button[aria-label="${name}"]`)

describe('CarColorPicker', () => {
  it('offers every preset and a custom choice, with the current one pressed', () => {
    const wrapper = render()

    expect(wrapper.findAll('button.car-color-swatch')).toHaveLength(10)
    expect(swatch(wrapper, 'Orange').attributes('aria-pressed')).toBe('true')
    expect(swatch(wrapper, 'Black').attributes('aria-pressed')).toBe('false')
  })

  it('shows the colour pickers only while custom is chosen', async () => {
    const wrapper = render()
    expect(wrapper.find('input[type=color]').exists()).toBe(false)

    await swatch(wrapper, 'Your own colours').trigger('click')

    const inputs = wrapper.findAll<HTMLInputElement>('input[type=color]')
    expect(inputs.map((i) => i.element.value)).toStrictEqual(['#f9b233', '#f39200'])
    expect(wrapper.text()).toContain('Primary colour')
    expect(wrapper.text()).toContain('Secondary colour')

    await swatch(wrapper, 'Red').trigger('click')
    expect(wrapper.find('input[type=color]').exists()).toBe(false)
  })

  it('paints the car in the picked colour, leaving the other as it was', async () => {
    const wrapper = render()
    await swatch(wrapper, 'Your own colours').trigger('click')

    await wrapper.findAll('input[type=color]')[1]!.setValue('#00ff00')

    expect(useUiSettingsStore(pinia).carCustomColors).toStrictEqual({
      primary: '#f9b233',
      secondary: '#00ff00',
    })
  })

  it('shows a rainbow on the custom swatch until a pair is picked', async () => {
    const wrapper = render()
    const custom = () => swatch(wrapper, 'Your own colours')
    expect(custom().classes()).toContain('car-color-swatch--unset')

    await custom().trigger('click')

    expect(custom().classes()).not.toContain('car-color-swatch--unset')
    expect(custom().attributes('style')).toContain('--swatch-p: #f9b233')
  })
})
