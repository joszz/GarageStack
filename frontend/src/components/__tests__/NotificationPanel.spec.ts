import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import NotificationPanel from '../NotificationPanel.vue'
import type { AppNotification } from '@/services/notificationsApi'
import { ApiError } from '@/services/apiCore'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

const notification: AppNotification = {
  id: 1,
  title: 'Low tyre pressure',
  body: 'Front left is at 1.9 bar',
  category: 'low-tyre',
  createdAt: '2026-10-15T08:00:00Z',
  isArchived: false,
}

function mountPanel(props: Partial<InstanceType<typeof NotificationPanel>['$props']> = {}) {
  return mount(NotificationPanel, {
    props: { open: true, notifications: [], loading: false, ...props },
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true, Teleport: true } },
    attachTo: document.body,
  })
}

describe('NotificationPanel', () => {
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
