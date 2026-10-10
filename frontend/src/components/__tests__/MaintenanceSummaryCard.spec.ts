import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { MaintenanceItem } from '@/services/maintenanceApi'
import { maintenanceItem } from '@/services/__tests__/maintenanceFixtures'
import { useMaintenanceStore } from '@/stores/maintenance'
import MaintenanceSummaryCard from '../MaintenanceSummaryCard.vue'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn<(to: unknown) => void>() }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function cardWith(items: MaintenanceItem[]) {
  useMaintenanceStore().items = items
  return mount(MaintenanceSummaryCard, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  }).find('.status-card')
}

describe('MaintenanceSummaryCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('names the worst state, overdue before due soon', () => {
    const card = cardWith([
      maintenanceItem({ id: 1, dueStatus: 'dueSoon' }),
      maintenanceItem({ id: 2, dueStatus: 'overdue' }),
    ])

    expect(card.find('.status-card__value').text()).toBe('Overdue')
    expect(card.classes()).toContain('status-card--danger')
  })

  it('reads Due soon when nothing is overdue yet', () => {
    const card = cardWith([maintenanceItem({ dueStatus: 'dueSoon' })])

    expect(card.find('.status-card__value').text()).toBe('Due soon')
    expect(card.classes()).toContain('status-card--warning')
  })

  it('reads OK when nothing needs attention', () => {
    const card = cardWith([maintenanceItem(), maintenanceItem({ id: 2, dueStatus: 'unknown' })])

    expect(card.find('.status-card__value').text()).toBe('OK')
    expect(card.classes()).toContain('status-card--success')
  })
})
