import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import TripEditPanel from '../TripEditPanel.vue'
import en from '@/locales/en.json'
import type { TripEditMode } from '@/composables/useTripEditor'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

type Props = InstanceType<typeof TripEditPanel>['$props']

function mountPanel(props: Partial<Props> = {}) {
  return mount(TripEditPanel, {
    props: {
      mode: 'start' as TripEditMode,
      distanceBeforeKm: 11.1,
      distanceAfterKm: 12.3,
      startLabel: 'Brink 2, Deventer',
      endLabel: 'Grote Markt 1, Zwolle',
      suggestion: false,
      canUsePreviousStop: true,
      canUseNextStop: false,
      canRemove: false,
      canUndo: false,
      canReset: false,
      dirty: false,
      saving: false,
      errorText: null,
      ...props,
    },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

function button(wrapper: ReturnType<typeof mountPanel>, text: string) {
  return wrapper
    .findAll('button')
    .find((b) => b.text() === text || b.attributes('aria-label') === text)!
}

describe('TripEditPanel', () => {
  it('shows the distance before and after, and the ends', () => {
    const text = mountPanel().text()

    expect(text).toContain('11.1 km to 12.3 km')
    expect(text).toContain('Brink 2, Deventer')
    expect(text).toContain('Grote Markt 1, Zwolle')
  })

  it('offers the parking spot fix as a banner while the trip still needs it', async () => {
    const wrapper = mountPanel({ suggestion: true })

    const banner = wrapper.find('.trip-edit-panel__suggestion')
    expect(banner.text()).toContain(en.tripEdit.suggestion)
    await banner.find('button').trigger('click')

    expect(wrapper.emitted('usePreviousStop')).toHaveLength(1)
    // The banner is the one way to it while it shows; no second button, and no other tool.
    expect(wrapper.find('.trip-edit-panel__tools').exists()).toBe(false)
    expect(mountPanel().find('.trip-edit-panel__suggestion').exists()).toBe(false)
  })

  it('switches what a tap moves, with the tool and hint for each', async () => {
    const wrapper = mountPanel()

    expect(wrapper.text()).toContain(en.tripEdit.hint.start)
    expect(button(wrapper, en.tripEdit.usePreviousStop).exists()).toBe(true)

    const points = wrapper.findAll('button').find((b) => b.text() === en.tripEdit.mode.points)!
    await points.trigger('click')
    expect(wrapper.emitted('update:mode')).toEqual([['points']])

    await wrapper.setProps({ mode: 'points' })
    expect(wrapper.text()).toContain(en.tripEdit.hint.points)
    expect(button(wrapper, en.tripEdit.removePoint).attributes('disabled')).toBeDefined()

    await wrapper.setProps({ mode: 'end' })
    expect(button(wrapper, en.tripEdit.useNextStop).attributes('disabled')).toBeDefined()
  })

  it('saves only when there is something to save, and says why it failed', async () => {
    const clean = mountPanel()
    const changed = mountPanel({ dirty: true, canUndo: true, canReset: true, errorText: 'No.' })

    expect(button(clean, en.tripEdit.save).attributes('disabled')).toBeDefined()
    expect(button(clean, en.tripEdit.undo).attributes('disabled')).toBeDefined()
    await button(changed, en.tripEdit.save).trigger('click')
    await button(changed, en.tripEdit.undo).trigger('click')
    await button(changed, en.tripEdit.reset).trigger('click')
    await button(changed, en.common.cancel).trigger('click')

    expect(changed.emitted('save')).toHaveLength(1)
    expect(changed.emitted('undo')).toHaveLength(1)
    expect(changed.emitted('reset')).toHaveLength(1)
    expect(changed.emitted('cancel')).toHaveLength(1)
    expect(changed.find('[role="alert"]').text()).toBe('No.')
  })

  it('holds the buttons while saving', () => {
    const saving = mountPanel({ dirty: true, canUndo: true, saving: true })

    expect(button(saving, en.tripEdit.save).attributes('disabled')).toBeDefined()
    expect(button(saving, en.tripEdit.undo).attributes('disabled')).toBeDefined()
  })
})
