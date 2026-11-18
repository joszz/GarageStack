import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import TripLogSummaryModal from '../TripLogSummaryModal.vue'
import type { PeriodSummary } from '@/utils/tripLog'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const summary: PeriodSummary = {
  trips: 2,
  km: 85,
  drivingMs: 68 * 60_000,
  averageTripKm: 42.5,
  longestTripKm: 50,
  averageSpeedKmh: 75,
}

function mountModal(props: { open: boolean; summary: PeriodSummary }) {
  return mount(TripLogSummaryModal, {
    props,
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, teleport: true } },
  })
}

function figures(wrapper: ReturnType<typeof mountModal>) {
  return wrapper.findAll('.trip-log-summary__item').map((item) => ({
    label: item.get('.trip-log-summary__label').text(),
    value: item.get('.trip-log-summary__value').text(),
    description: item.get('.trip-log-summary__description').text(),
  }))
}

describe('TripLogSummaryModal', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shows nothing while closed', () => {
    const wrapper = mountModal({ open: false, summary })

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('lists the driving figures, each with what it means', () => {
    const wrapper = mountModal({ open: true, summary })

    expect(wrapper.get('.detail-modal__title').text()).toBe(en.tripLog.summary.label)
    expect(figures(wrapper)).toEqual([
      {
        label: en.tripLog.summary.drivingTime,
        value: '1h 8m',
        description: en.tripLog.cardDesc.drivingTime,
      },
      {
        label: en.tripLog.summary.averageTrip,
        value: '42.5km',
        description: en.tripLog.cardDesc.averageTrip,
      },
      {
        label: en.tripLog.summary.longestTrip,
        value: '50.0km',
        description: en.tripLog.cardDesc.longestTrip,
      },
      {
        label: en.tripLog.summary.averageSpeed,
        value: '75km/h',
        description: en.tripLog.cardDesc.averageSpeed,
      },
    ])
  })

  it('shows a dash for every figure of a period without trips', () => {
    const wrapper = mountModal({
      open: true,
      summary: {
        trips: 0,
        km: 0,
        drivingMs: 0,
        averageTripKm: null,
        longestTripKm: null,
        averageSpeedKmh: null,
      },
    })

    expect(figures(wrapper).map((f) => f.value)).toEqual(['-', '-', '-', '-'])
  })

  it('asks to close from its close button', async () => {
    const wrapper = mountModal({ open: true, summary })

    await wrapper.get('.dialog-close').trigger('click')

    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
