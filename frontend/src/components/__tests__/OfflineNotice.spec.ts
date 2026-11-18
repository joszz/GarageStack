import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { formatDateTime } from '@/utils/format'
import OfflineNotice from '../OfflineNotice.vue'

enableAutoUnmount(afterEach)

const reading = {
  vehicleId: 1,
  recordedAt: '2026-11-18T09:30:00Z',
  lastVehicleStateAt: '2026-11-18T09:00:00Z',
} as TelemetrySnapshot

function setOnline(online: boolean) {
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(online)
}

function mountNotice() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(OfflineNotice, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

/** The store as it starts from the copy this browser saved. */
function showSaved(status: TelemetrySnapshot | null = reading) {
  const store = useVehicleStore()
  store.currentStatus = status
  store.showingSavedState = true
  return store
}

describe('OfflineNotice', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setOnline(true)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("says the car is the saved copy, with the car's own time, when the browser is offline", () => {
    setOnline(false)
    showSaved()

    const notice = mountNotice().get('[role="status"]')

    expect(notice.text()).toBe(
      en.offline.savedState.replace('{time}', formatDateTime(reading.lastVehicleStateAt!)),
    )
  })

  it('says so when the server could not be reached while the browser thinks it is online', () => {
    const store = showSaved()
    store.statusError = new TypeError('Failed to fetch')

    expect(mountNotice().find('[role="status"]').exists()).toBe(true)
  })

  it('stays quiet while the saved copy is only a stand-in for an answer on its way', () => {
    showSaved()

    expect(mountNotice().find('[role="status"]').exists()).toBe(false)
  })

  it('stays quiet once the server has answered, even offline afterwards', () => {
    setOnline(false)
    const store = showSaved()
    store.showingSavedState = false

    expect(mountNotice().find('[role="status"]').exists()).toBe(false)
  })

  it('stays quiet with no car to show', () => {
    setOnline(false)
    showSaved(null)

    expect(mountNotice().find('[role="status"]').exists()).toBe(false)
  })

  it('appears when the connection drops and asks for what was missed when it returns', async () => {
    showSaved()
    const wrapper = mountNotice()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)

    window.dispatchEvent(new Event('offline'))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="status"]').exists()).toBe(true)

    window.dispatchEvent(new Event('online'))
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('reconnect')).toHaveLength(1)
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })
})
