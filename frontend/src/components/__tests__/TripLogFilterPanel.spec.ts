import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripLogFilterPanel from '../TripLogFilterPanel.vue'
import type { TripLogPeriod } from '@/utils/tripLog'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

// The page opens on the current month: November 2026 here.
const thisMonth: TripLogPeriod = { year: 2026, month: 10 }

function mountPanel(props: { period: TripLogPeriod; showHidden: boolean; hiddenCount: number }) {
  return mount(TripLogFilterPanel, {
    props,
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, teleport: true } },
  })
}

let wrapper: ReturnType<typeof mountPanel>

function count() {
  return wrapper.find('.toolbar-panel__count').exists()
    ? wrapper.get('.toolbar-panel__count').text()
    : null
}

describe('TripLogFilterPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 10, 18, 12))
  })

  afterEach(() => {
    vi.useRealTimers()
    wrapper.unmount()
  })

  it('opens a filters dialog with the year and month, and the hidden trips switch', async () => {
    wrapper = mountPanel({ period: thisMonth, showHidden: false, hiddenCount: 2 })

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.get('.detail-modal__title').text()).toBe(en.common.filters)
    expect(dialog.findAll('select')).toHaveLength(2)
    expect(dialog.text()).toContain('Show 2 hidden trips')
  })

  it('leaves the hidden trips switch out while no trip is hidden', async () => {
    wrapper = mountPanel({ period: thisMonth, showHidden: false, hiddenCount: 0 })
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    expect(wrapper.find('input[role="switch"]').exists()).toBe(false)
  })

  it('hands back the period and the switch as they change', async () => {
    wrapper = mountPanel({ period: thisMonth, showHidden: false, hiddenCount: 1 })
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    const month = wrapper.findAll('select')[1]!
    ;(month.element as HTMLSelectElement).selectedIndex = 0
    await month.trigger('change')
    await wrapper.get('input[role="switch"]').setValue(true)

    expect(wrapper.emitted('update:period')).toEqual([[{ year: 2026, month: null }]])
    expect(wrapper.emitted('update:showHidden')).toEqual([[true]])
  })

  it('counts a period other than this month, and listing the hidden trips, on the button', () => {
    wrapper = mountPanel({ period: thisMonth, showHidden: false, hiddenCount: 1 })
    expect(count()).toBeNull()

    wrapper = mountPanel({ period: { year: 2026, month: null }, showHidden: true, hiddenCount: 1 })
    expect(count()).toBe('2')
  })

  it('does not count the switch while there is nothing hidden to list', () => {
    wrapper = mountPanel({ period: thisMonth, showHidden: true, hiddenCount: 0 })

    expect(count()).toBeNull()
  })
})
