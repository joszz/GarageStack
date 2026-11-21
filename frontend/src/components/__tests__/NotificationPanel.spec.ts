import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createPinia, setActivePinia } from 'pinia'
import { useVehicleStore } from '@/stores/vehicle'
import type { Vehicle } from '@/services/vehicleApi'
import en from '@/locales/en.json'
import NotificationPanel from '../NotificationPanel.vue'
import type { AppNotification } from '@/services/notificationsApi'
import { ApiError } from '@/services/apiCore'
import { resetConnection } from '@/composables/useConnection'
import { setBrowserOnline } from '@/composables/__tests__/connectionStub'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const notification: AppNotification = {
  id: 1,
  title: 'Low tyre pressure',
  body: 'Front left is at 1.9 bar',
  category: 'low-tyre',
  createdAt: '2026-10-15T08:00:00Z',
  isArchived: false,
}

function car(id: number, vin: string, model: string): Vehicle {
  return {
    id,
    vin,
    model,
    series: null,
    createdAt: '2026-01-01T00:00:00Z',
    vehicleType: 'bev',
    vehicleTypeOverride: null,
    hvBatteryCapacityKwh: null,
    supportsTargetSoc: false,
    hasPowerTailgate: false,
  }
}

function mountPanel(props: Partial<InstanceType<typeof NotificationPanel>['$props']> = {}) {
  return mount(NotificationPanel, {
    props: { open: true, notifications: [], loading: false, ...props },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, Teleport: true } },
    attachTo: document.body,
  })
}

describe('NotificationPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('names the car a notification is about when the account has more than one', () => {
    useVehicleStore().vehicles = [
      car(1, 'VIN00000000000001', 'MG HS'),
      car(2, 'VIN00000000000002', 'MG4'),
    ]
    const wrapper = mountPanel({ notifications: [{ ...notification, vehicleId: 2 }] })

    expect(wrapper.find('.notif-item__car').text()).toBe('MG4')
    wrapper.unmount()
  })

  it('leaves the car out with one car on the account', () => {
    useVehicleStore().vehicles = [car(1, 'VIN00000000000001', 'MG4')]
    const wrapper = mountPanel({ notifications: [{ ...notification, vehicleId: 1 }] })

    expect(wrapper.find('.notif-item__car').exists()).toBe(false)
    wrapper.unmount()
  })

  it('says the list could not be loaded instead of claiming there is nothing, and offers a retry', async () => {
    const wrapper = mountPanel({ fetchError: new TypeError('Failed to fetch') })

    expect(wrapper.text()).toContain(en.errors.offline)
    expect(wrapper.text()).not.toContain(en.notifications.empty)

    await wrapper
      .findAll('button')
      .find((b) => b.text() === en.common.retry)!
      .trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
    wrapper.unmount()
  })

  it('shows the empty state when the list loaded and is empty', () => {
    const wrapper = mountPanel()

    expect(wrapper.text()).toContain(en.notifications.empty)
    expect(wrapper.find('[role=alert]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('names why an action failed', () => {
    const wrapper = mountPanel({
      notifications: [notification],
      actionError: new ApiError(500, '/api/notifications/1/archive'),
    })

    expect(wrapper.find('[role=alert]').text()).toBe(en.errors.server)
    expect(wrapper.text()).toContain(notification.title)
    wrapper.unmount()
  })

  it('is a modal dialog that takes focus and closes on Escape', () => {
    const wrapper = mountPanel()
    const dialog = wrapper.find('[role=dialog]')

    expect(dialog.attributes('aria-modal')).toBe('true')
    expect(document.activeElement).toBe(dialog.element)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(wrapper.emitted('close')).toHaveLength(1)
    wrapper.unmount()
  })
})

describe('NotificationPanel without the server', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setBrowserOnline(false)
  })
  afterEach(() => {
    vi.restoreAllMocks()
    resetConnection()
  })

  it('shows the notifications without archiving or deleting', () => {
    const wrapper = mountPanel({ notifications: [notification] })

    expect(wrapper.text()).toContain(notification.title)
    expect(wrapper.find('.notif-item__actions').exists()).toBe(false)
    expect(wrapper.find('.notif-panel__footer').exists()).toBe(false)
    wrapper.unmount()
  })
})
