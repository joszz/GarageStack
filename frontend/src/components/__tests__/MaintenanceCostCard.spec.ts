import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import MaintenanceCostCard from '../MaintenanceCostCard.vue'
import type { MaintenanceCost } from '@/services/maintenanceApi'
import { useUiSettingsStore } from '@/stores/settingsUi'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function cost(performedAt: string, amount: number): MaintenanceCost {
  return { maintenanceItemId: 1, itemName: 'Oil change', performedAt, cost: amount }
}

function mountCard(costs: MaintenanceCost[]) {
  return mount(MaintenanceCostCard, {
    props: { costs },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('MaintenanceCostCard', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.useFakeTimers({ now: new Date(2026, 10, 23, 12) })
  })
  afterEach(() => vi.useRealTimers())

  it("shows this year's costs, with last year's and the total under them", () => {
    useUiSettingsStore().currency = 'EUR'
    const wrapper = mountCard([
      cost('2026-03-01T00:00:00', 89.95),
      cost('2025-06-01T00:00:00', 118.75),
      cost('2021-06-01T00:00:00', 685),
    ])

    expect(wrapper.text()).toContain(en.maintenance.costs.thisYear)
    expect(wrapper.text()).toContain('€89.95')
    expect(wrapper.text()).toContain('Last year €118.75 · All time €893.70')
  })

  it('shows nothing before a service has a cost', () => {
    expect(mountCard([]).find('.status-card').exists()).toBe(false)
  })
})
