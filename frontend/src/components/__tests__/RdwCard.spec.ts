import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { RdwInfo, RdwRecall } from '@/services/rdwApi'
import { useRdwStore } from '@/stores/rdw'
import RdwCard from '../RdwCard.vue'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const global = {
  plugins: [i18n],
  stubs: { DetailModal: { template: '<div><slot /></div>' }, FontAwesomeIcon: true },
}

function recall(overrides: Partial<RdwRecall> = {}): RdwRecall {
  return {
    code: 'MGP230061',
    open: true,
    defect: 'De aardingskabel van de hoogvoltkachel is mogelijk niet goed vastgezet.',
    risk: 'De isolatiebekleding kan vlam vatten.',
    repair: 'De dealer zet de aardingskabel vast.',
    severity: 'ERN',
    firstSeenAt: '2026-10-16T08:00:00Z',
    ...overrides,
  }
}

function info(overrides: Partial<RdwInfo> = {}): RdwInfo {
  return {
    found: true,
    make: 'MG',
    model: 'MG EHS PLUG-IN HYBRID',
    apkExpiresOn: '2026-11-09',
    apkDaysLeft: 21,
    wamInsured: true,
    openRecallIndicator: true,
    recalls: [recall()],
    fetchedAt: '2026-10-19T06:00:00Z',
    lastError: null,
    ...overrides,
  }
}

function mountWith(lookup: RdwInfo | null, licensePlate = 'HPK98B') {
  const store = useRdwStore()
  store.available = true
  store.status = { licensePlate, info: lookup }
  return mount(RdwCard, { global })
}

describe('RdwCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('shows the days the APK has left, and the open recalls under it', () => {
    const card = mountWith(info()).find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('21 days left')
    expect(card.find('.status-card__note').text()).toBe('1 open recall')
    expect(card.classes()).toContain('status-card--warning')
  })

  it('spells each open recall out with its risk and repair', () => {
    const wrapper = mountWith(info())
    const recallText = wrapper.find('.rdw-recall').text()

    expect(recallText).toContain('MGP230061')
    expect(recallText).toContain('Serious')
    expect(recallText).toContain('De isolatiebekleding kan vlam vatten.')
    expect(recallText).toContain('De dealer zet de aardingskabel vast.')
  })

  it('lists repaired recalls apart from the open ones', () => {
    const wrapper = mountWith(info({ recalls: [recall({ code: 'MGP200235', open: false })] }))

    expect(wrapper.findAll('.rdw-recall')).toHaveLength(0)
    expect(wrapper.text()).toContain('The RDW lists no open recalls for this car.')
    expect(wrapper.text()).toContain('MGP200235')
  })

  it('says when the RDW was asked', () => {
    const meta = mountWith(info())
      .findAll('.rdw-card__meta')
      .map((line) => line.text())

    expect(meta.some((line) => line.startsWith('From the RDW'))).toBe(true)
  })

  it('is green with a month or more to go and nothing to repair', () => {
    const card = mountWith(info({ apkDaysLeft: 200, recalls: [] })).find('.status-card')

    expect(card.classes()).toContain('status-card--success')
    expect(card.find('.status-card__note').text()).toBe('No open recalls')
  })

  it('is red once the APK has run out', () => {
    const card = mountWith(info({ apkDaysLeft: -3, recalls: [] })).find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('Expired')
    expect(card.classes()).toContain('status-card--danger')
  })

  it('is red without insurance on record, and says so', () => {
    const wrapper = mountWith(info({ apkDaysLeft: 200, recalls: [], wamInsured: false }))

    expect(wrapper.find('.status-card').classes()).toContain('status-card--danger')
    expect(wrapper.text()).toContain('None on record')
  })

  it('reads the last day as the last day', () => {
    expect(
      mountWith(info({ apkDaysLeft: 0 }))
        .find('.status-card__value')
        .text(),
    ).toBe('Last day')
  })

  it('says the RDW does not know the plate', () => {
    const wrapper = mountWith(info({ found: false, recalls: [] }), 'ZZ999Z')

    expect(wrapper.find('.status-card__value').text()).toBe('Plate unknown')
    expect(wrapper.text()).toContain('ZZ999Z')
  })

  it('waits for a first lookup', () => {
    const card = mountWith(null).find('.status-card')

    expect(card.find('.status-card__value').text()).toBe('Not looked up yet')
    expect(card.find('.status-card__note').text()).toBe('Plate HPK98B')
  })

  it('says when the last attempt failed', () => {
    const wrapper = mountWith(info({ lastError: 'The RDW answered 500' }))

    expect(wrapper.find('.text-danger').text()).toContain('The last attempt to ask the RDW failed')
  })
})
