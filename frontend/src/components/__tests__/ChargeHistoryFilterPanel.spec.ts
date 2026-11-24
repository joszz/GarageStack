import { afterEach, describe, expect, it } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import ChargeHistoryFilterPanel from '../ChargeHistoryFilterPanel.vue'
import type { ChargeHistoryDays } from '@/utils/chargeSessions'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

function mountPanel(period: ChargeHistoryDays) {
  return mount(ChargeHistoryFilterPanel, {
    props: { period },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, teleport: true } },
  })
}

describe('ChargeHistoryFilterPanel', () => {
  it('opens a filters dialog with the period to choose', async () => {
    const wrapper = mountPanel(90)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.get('.detail-modal__title').text()).toBe(en.common.filters)
    expect(dialog.text()).toContain(en.chargeHistory.periodDesc)
    expect(dialog.findAll('option').map((option) => option.text())).toEqual([
      'Last 30 days',
      'Last 90 days',
      'Last 365 days',
    ])
    expect((dialog.get('select').element as HTMLSelectElement).value).toBe('90')
  })

  it('hands back the period chosen as a number', async () => {
    const wrapper = mountPanel(90)
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    await wrapper.get('select').setValue('30')

    expect(wrapper.emitted('update:period')).toEqual([[30]])
  })

  it('counts a period other than the one the page starts on, on the button', () => {
    expect(mountPanel(90).find('.toolbar-panel__count').exists()).toBe(false)
    expect(mountPanel(365).get('.toolbar-panel__count').text()).toBe('1')
  })
})
