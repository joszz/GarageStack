import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import en from '@/locales/en.json'
import { dawarichApi } from '@/services/dawarichApi'
import PhoneGpsSettings from '../PhoneGpsSettings.vue'

vi.mock('@/services/dawarichApi', () => ({
  dawarichApi: {
    status: vi.fn<typeof dawarichApi.status>(),
    trackers: vi.fn<typeof dawarichApi.trackers>(),
    save: vi.fn<typeof dawarichApi.save>(),
    backfill: vi.fn<typeof dawarichApi.backfill>(),
  },
}))
const vin = 'FAKEVN00000000001'
const linked = [{ trackerId: 'fake-phone', createdAt: '2026-01-01T00:00:00Z' }]

async function render() {
  const wrapper = mount(PhoneGpsSettings, {
    props: { vin },
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en } })],
      stubs: { FontAwesomeIcon: true },
    },
  })
  await flushPromises()
  return wrapper
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.resetAllMocks()
  vi.mocked(dawarichApi.status).mockResolvedValue({ configured: true, status: 'ok' })
  vi.mocked(dawarichApi.trackers).mockResolvedValue({
    linked,
    available: [{ trackerId: 'fake-phone', lastSeenAt: '2026-01-02T10:00:00Z' }],
    error: null,
  })
  vi.mocked(dawarichApi.save).mockResolvedValue([])
  vi.mocked(dawarichApi.backfill).mockResolvedValue()
})

describe('PhoneGpsSettings', () => {
  it('is hidden when the server has not configured Dawarich', async () => {
    vi.mocked(dawarichApi.status).mockResolvedValue({ configured: false, status: 'disabled' })
    const wrapper = await render()
    expect(wrapper.text()).toBe('')
    expect(dawarichApi.trackers).not.toHaveBeenCalled()
  })

  it('shows connection, linked tracker and privacy information', async () => {
    const wrapper = await render()
    expect(wrapper.text()).toContain(en.dawarich.status.ok)
    expect(wrapper.text()).toContain('fake-phone')
    expect(wrapper.text()).toContain(en.dawarich.privacy)
  })

  it('clears all links to disable fusion', async () => {
    const wrapper = await render()
    wrapper.findComponent({ name: 'Multiselect' }).vm.$emit('update:modelValue', [])
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(dawarichApi.save).toHaveBeenCalledWith(vin, [])
    expect(wrapper.text()).toContain(en.dawarich.saved)
    expect(wrapper.find('input[type=date]').exists()).toBe(false)
  })

  it('queues an explicit date range, not on linking', async () => {
    const wrapper = await render()
    expect(dawarichApi.backfill).not.toHaveBeenCalled()
    await wrapper.find('input[type=date]').setValue('2026-01-01')
    await wrapper.findAll('form')[1]!.trigger('submit')
    await flushPromises()
    expect(dawarichApi.backfill).toHaveBeenCalledWith(vin, '2026-01-01T00:00:00Z')
    expect(wrapper.text()).toContain(en.dawarich.queued)
  })

  it('preserves links and explains upstream failure', async () => {
    vi.mocked(dawarichApi.trackers).mockResolvedValue({ linked, available: [], error: 'badKey' })
    const wrapper = await render()
    expect(wrapper.text()).toContain(en.dawarich.status.badKey)
    expect(wrapper.text()).toContain('fake-phone')
  })

  it('shows request errors instead of hiding the panel', async () => {
    vi.mocked(dawarichApi.status).mockRejectedValue(new Error('offline'))
    const wrapper = await render()
    expect(wrapper.find('[role=alert]').exists()).toBe(true)
  })
})
