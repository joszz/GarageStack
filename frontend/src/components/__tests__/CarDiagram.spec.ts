import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import CarDiagram from '../CarDiagram.vue'

// The dialogs chart with Chart.js; stand-ins that say whether they are open, and what they show,
// and remember whether they were open the moment they mounted.
const openAtMount = vi.hoisted(() => [] as boolean[])
const modalStub = (name: string) =>
  defineComponent({
    name,
    props: { open: Boolean, level: { type: String, default: null } },
    setup: (props) => {
      openAtMount.push(props.open)
      return () =>
        h('div', { class: name, 'data-open': String(props.open), 'data-level': props.level })
    },
  })

vi.mock('@/components/TyreHistoryModal.vue', () => ({ default: modalStub('tyre-history') }))
vi.mock('@/components/LevelHistoryModal.vue', () => ({ default: modalStub('level-history') }))

vi.mock('@/composables/useTyrePressureThresholds', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/composables/useTyrePressureThresholds')>()
  const { ref } = await import('vue')
  return {
    ...actual,
    useTyrePressureThresholds: () => ref(actual.DEFAULT_TYRE_PRESSURE_THRESHOLDS),
  }
})

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountDiagram() {
  return mount(CarDiagram, {
    props: {
      frontLeft: 2.4,
      frontRight: 2.4,
      rearLeft: 2.4,
      rearRight: 2.4,
      fuelLevelPercent: 60,
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

beforeEach(() => {
  setActivePinia(createPinia())
  openAtMount.length = 0
})

describe('CarDiagram', () => {
  it('leaves the history dialogs out until one is asked for', () => {
    const wrapper = mountDiagram()

    expect(wrapper.find('.tyre-history').exists()).toBe(false)
    expect(wrapper.find('.level-history').exists()).toBe(false)
  })

  it("opens the tyre history from a tyre's pressure, mounted closed first", async () => {
    const wrapper = mountDiagram()

    await wrapper.find('.tyre-label').trigger('click')
    await flushPromises()

    // Mounted closed, then opened: its fade-in and its fetch both start from the change.
    expect(openAtMount).toEqual([false])
    expect(wrapper.find('.tyre-history').attributes('data-open')).toBe('true')
  })

  it('opens the level history for the level that was tapped', async () => {
    const wrapper = mountDiagram()

    await wrapper.find('.diagram-level--fuel').trigger('click')
    await flushPromises()

    const dialog = wrapper.find('.level-history')
    expect(dialog.attributes('data-open')).toBe('true')
    expect(dialog.attributes('data-level')).toBe('fuel')
  })

  it('closes a dialog it opened and keeps it mounted for the fade-out', async () => {
    const wrapper = mountDiagram()
    await wrapper.find('.tyre-label').trigger('click')
    await flushPromises()

    wrapper.findComponent({ name: 'tyre-history' }).vm.$emit('close')
    await flushPromises()

    expect(wrapper.find('.tyre-history').attributes('data-open')).toBe('false')
  })
})
