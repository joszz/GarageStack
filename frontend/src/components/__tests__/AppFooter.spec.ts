import { describe, it, expect, vi, beforeEach } from 'vitest'
import { nextTick, ref } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import Multiselect from '@vueform/multiselect'
import AppFooter from '../AppFooter.vue'

const appVersion = vi.hoisted(() => ({ value: null as string | null }))
const vehicleType = vi.hoisted(() => ({ value: 'unknown' as string }))

vi.mock('@/utils/appVersion', () => ({
  get APP_VERSION() {
    return appVersion.value
  },
}))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    vehicles: [],
    loading: false,
    detectedVehicleType: vehicleType.value,
    effectiveVehicleType: vehicleType.value,
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
    global: {
      plugins: [i18n],
      // DetailModal renders the settings body in its default slot, which an auto-stub drops
      stubs: { FontAwesomeIcon: true, DetailModal: { template: '<div><slot /></div>' } },
    },
  })
}

function notificationTypeLabels(wrapper: ReturnType<typeof mountFooter>) {
  return wrapper.findAll('.notif-type-checklist__label').map((el) => el.text())
}

describe('AppFooter', () => {
  beforeEach(() => {
    appVersion.value = null
    vehicleType.value = 'unknown'
  })

  it('separates the project name and version with a space', () => {
    appVersion.value = 'v0.5.0'
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack v0.5.0')
  })

  it('shows only the project name when no version was injected', () => {
    expect(mountFooter().find('.app-footer__copyright').text()).toBe('© 2026 GarageStack')
  })

  it('hides the charging notification types for a hybrid', () => {
    vehicleType.value = 'hev'
    const labels = notificationTypeLabels(mountFooter())

    expect(labels).not.toContain('notifications.categories.chargingComplete')
    expect(labels).not.toContain('notifications.categories.lowEv')
    expect(labels).toContain('notifications.categories.engineStart')
  })

  it('offers the charging notification types for a plug-in hybrid', () => {
    vehicleType.value = 'phev'
    const labels = notificationTypeLabels(mountFooter())

    expect(labels).toContain('notifications.categories.chargingComplete')
    expect(labels).toContain('notifications.categories.lowEv')
  })

  it('relabels the vehicle type options when the language changes', async () => {
    const translated = createI18n({
      legacy: false,
      locale: 'en',
      missingWarn: false,
      fallbackWarn: false,
      messages: {
        en: { settings: { vehicleType: { auto: 'Automatic' } } },
        nl: { settings: { vehicleType: { auto: 'Automatisch' } } },
      },
    })
    const wrapper = shallowMount(AppFooter, {
      global: {
        plugins: [translated],
        stubs: { FontAwesomeIcon: true, DetailModal: { template: '<div><slot /></div>' } },
      },
    })
    const autoLabel = () =>
      (
        wrapper.findComponent(Multiselect).props('options') as { value: string; label: string }[]
      ).find((option) => option.value === 'auto')?.label

    expect(autoLabel()).toBe('Automatic')

    translated.global.locale.value = 'nl'
    await nextTick()

    expect(autoLabel()).toBe('Automatisch')
  })
})
