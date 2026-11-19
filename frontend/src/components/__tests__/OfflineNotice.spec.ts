import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleStore } from '@/stores/vehicle'
import { formatDateTime } from '@/utils/format'
import { resetConnection, watchConnection } from '@/composables/useConnection'
import { setConnectionListener, type ConnectionListener } from '@/services/apiCore'
import OfflineNotice from '../OfflineNotice.vue'

vi.mock('@/services/apiCore', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/apiCore')>()),
  setConnectionListener: vi.fn<(listener: ConnectionListener | null) => void>(),
}))

enableAutoUnmount(afterEach)

const reading = {
  vehicleId: 1,
  recordedAt: '2026-11-18T09:30:00Z',
  lastVehicleStateAt: '2026-11-18T09:00:00Z',
} as TelemetrySnapshot

function setOnline(online: boolean) {
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(online)
}

/** What the API layer tells the connection, as main.ts wires it. */
function requests(): ConnectionListener {
  watchConnection()
  return vi.mocked(setConnectionListener).mock.lastCall![0]!
}

function mountNotice() {
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
  return mount(OfflineNotice, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('OfflineNotice', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setOnline(true)
    resetConnection()
  })

  afterEach(() => {
    resetConnection()
    vi.restoreAllMocks()
  })

  it('stays quiet while the server answers', () => {
    requests().answered()

    expect(mountNotice().find('[role="status"]').exists()).toBe(false)
  })

  it('says how old the oldest saved answer on the page is', () => {
    const api = requests()
    api.answeredFromSave('2026-11-17T08:00:00Z')
    api.answeredFromSave('2026-11-16T08:00:00Z')

    expect(mountNotice().get('[role="status"]').text()).toBe(
      en.offline.savedPage.replace('{time}', formatDateTime('2026-11-16T08:00:00Z')),
    )
  })

  it("names the car's own time for the car this browser kept, with nothing else saved", () => {
    setOnline(false)
    resetConnection()
    const store = useVehicleStore()
    store.currentStatus = reading
    store.showingSavedState = true

    expect(mountNotice().get('[role="status"]').text()).toBe(
      en.offline.savedState.replace('{time}', formatDateTime(reading.lastVehicleStateAt!)),
    )
  })

  it('still says there is no connection when the page has nothing dated to show', () => {
    requests().unreachable()

    expect(mountNotice().get('[role="status"]').text()).toBe(en.offline.savedPageUnknown)
  })

  it('appears when the connection drops and goes when the server answers again', async () => {
    const api = requests()
    const wrapper = mountNotice()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)

    window.dispatchEvent(new Event('offline'))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="status"]').exists()).toBe(true)

    window.dispatchEvent(new Event('online'))
    api.answered()
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })
})
