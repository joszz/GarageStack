import { afterEach, describe, expect, it } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import PeriodFilterPanel from '../PeriodFilterPanel.vue'
import { CHARGE_HISTORY_PERIOD, presetPeriod, type Period } from '@/utils/period'
import { InlineTeleport } from '@/__tests__/inlineTeleport'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

enableAutoUnmount(afterEach)

function mountPanel(period: Period) {
  return mount(PeriodFilterPanel, {
    props: { modelValue: period, options: CHARGE_HISTORY_PERIOD, desc: 'Sessions listed' },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, teleport: InlineTeleport } },
  })
}

describe('PeriodFilterPanel', () => {
  it('opens a filters dialog with the period', async () => {
    const wrapper = mountPanel(CHARGE_HISTORY_PERIOD.fallback)

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.get('.detail-modal__title').text()).toBe(en.common.filters)
    expect(dialog.text()).toContain('Sessions listed')
    expect((dialog.get('select').element as HTMLSelectElement).value).toBe('last90')
  })

  it('hands back the period chosen', async () => {
    const wrapper = mountPanel(CHARGE_HISTORY_PERIOD.fallback)
    await wrapper.get('.toolbar-panel__btn').trigger('click')

    await wrapper.get('select').setValue('thisYear')

    expect(wrapper.emitted('update:modelValue')).toEqual([[presetPeriod('thisYear')]])
  })

  it('counts a period other than the one the page starts on, on the button', () => {
    expect(mountPanel(presetPeriod('last90')).find('.toolbar-panel__count').exists()).toBe(false)
    expect(mountPanel(presetPeriod('lastYear')).get('.toolbar-panel__count').text()).toBe('1')
  })
})
