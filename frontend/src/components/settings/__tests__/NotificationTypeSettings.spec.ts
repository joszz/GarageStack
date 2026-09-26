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

vi.mock('@/composables/useNotificationPushSync', () => ({
  useNotificationPushSync: () => ({
    showPermissionDeniedNotice: ref(false),
    showSubscribeFailedNotice: ref(false),
  }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })

function labels() {
  return shallowMount(NotificationTypeSettings, { global: { plugins: [i18n] } })
    .findAll('.notif-type-checklist__label')
    .map((el) => el.text())
}

describe('NotificationTypeSettings', () => {
  beforeEach(() => {
    vehicleType.value = 'unknown'
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
})
