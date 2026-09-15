import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import AppFooter from '../AppFooter.vue'

const appVersion = vi.hoisted(() => ({ value: null as string | null }))

vi.mock('@/utils/appVersion', () => ({
  get APP_VERSION() {
    return appVersion.value
  },
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    vehicles: [],
    loading: false,
    detectedVehicleType: 'unknown',
    vehicleConfig: {},
  }),
}))

vi.mock('@/stores/settingsUi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/stores/settingsUi')>()),
  useUiSettingsStore: () => ({ theme: 'dark', locale: 'en', notificationTypeExclusions: [] }),
}))

vi.mock('@/composables/useVehicleCommand', () => ({
  useVehicleCommand: () => ({ sending: ref(null), send: vi.fn<() => void>() }),
}))

vi.mock('@/composables/useNotificationPushSync', () => ({
  useNotificationPushSync: () => ({ showPermissionDeniedNotice: ref(false) }),
}))

const i18n = createI18n({ legacy: false, locale: 'en', missingWarn: false, fallbackWarn: false })

function mountFooter() {
  return shallowMount(AppFooter, {
    global: { plugins: [i18n], stubs: { FontAwesomeIcon: true } },
  })
}

describe('AppFooter', () => {
  beforeEach(() => {
    appVersion.value = null
  })

  it('separates the project name and version with a space', () => {
    appVersion.value = 'v0.5.0'
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack v0.5.0')
  })

  it('shows only the project name when no version was injected', () => {
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack')
  })
})
