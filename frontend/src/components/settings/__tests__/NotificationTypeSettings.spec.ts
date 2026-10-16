import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import NotificationTypeSettings from '../NotificationTypeSettings.vue'

const vehicleType = vi.hoisted(() => ({ value: 'unknown' as string }))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({ effectiveVehicleType: vehicleType.value }),
}))

vi.mock('@/stores/settingsUi', () => ({
  useUiSettingsStore: () => ({ notificationTypeExclusions: [] }),
}))

const pushSupported = vi.hoisted(() => ({ value: true }))

vi.mock('@/composables/useNotificationPushSync', () => ({
  useNotificationPushSync: () => ({
    pushSupported: pushSupported.value,
    pushEnabled: ref(false),
    pushSwitchDisabled: ref(false),
    showPermissionDeniedNotice: ref(false),
    showSubscribeFailedNotice: ref(false),
  }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })

function mountSettings() {
  return shallowMount(NotificationTypeSettings, { global: { plugins: [i18n] } })
}

function labels() {
  return mountSettings()
    .findAll('.notif-type-checklist__label')
    .map((el) => el.text())
}

describe('NotificationTypeSettings', () => {
  beforeEach(() => {
    vehicleType.value = 'unknown'
    pushSupported.value = true
  })

  it('hides the charging notification types for a hybrid', () => {
    vehicleType.value = 'hev'

    expect(labels()).not.toContain('notifications.categories.chargingComplete')
    expect(labels()).not.toContain('notifications.categories.lowEv')
    expect(labels()).toContain('notifications.categories.engineStart')
  })

  it('offers the charging notification types for a plug-in hybrid', () => {
    vehicleType.value = 'phev'

    expect(labels()).toContain('notifications.categories.chargingComplete')
    expect(labels()).toContain('notifications.categories.lowEv')
  })

  it('hides low fuel for a BEV', () => {
    vehicleType.value = 'bev'

    expect(labels()).not.toContain('notifications.categories.lowFuel')
    expect(labels()).toContain('notifications.categories.low12v')
  })

  it('offers the push switch where the browser supports push', () => {
    expect(mountSettings().findComponent({ name: 'SettingsToggle' }).exists()).toBe(true)
  })

  it('leaves the push switch out where the browser has no push', () => {
    pushSupported.value = false

    expect(mountSettings().findComponent({ name: 'SettingsToggle' }).exists()).toBe(false)
  })

  it('explains that push needs https on a plain-HTTP address', () => {
    pushSupported.value = false
    vi.stubGlobal('isSecureContext', false)

    const wrapper = mountSettings()

    expect(wrapper.text()).toContain('push.needsHttps')
    expect(wrapper.find('#settings-toggle-push').exists()).toBe(false)
    vi.unstubAllGlobals()
  })

  it('says nothing about https where push is simply unsupported on a secure page', () => {
    pushSupported.value = false
    vi.stubGlobal('isSecureContext', true)

    expect(mountSettings().text()).not.toContain('push.needsHttps')
    vi.unstubAllGlobals()
  })
})
