import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ref } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import NotificationTypeSettings from '../NotificationTypeSettings.vue'

const vehicleType = vi.hoisted(() => ({ value: 'unknown' as string }))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({ effectiveVehicleType: vehicleType.value }),
}))

const rdwAvailable = vi.hoisted(() => ({ value: null as boolean | null }))

vi.mock('@/stores/rdw', () => ({
  useRdwStore: () => ({ available: rdwAvailable.value }),
}))

vi.mock('@/stores/settingsUi', () => ({
  useUiSettingsStore: () => settingsStub,
}))

const settingsStub = vi.hoisted(() => ({
  notificationTypeExclusions: [] as string[],
  notificationTypeInclusions: [] as string[],
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
  // The rows render for real: the switch and the checklist are their slot content.
  return shallowMount(NotificationTypeSettings, {
    global: { plugins: [i18n], stubs: { SettingsToggle: false } },
  })
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
    rdwAvailable.value = null
    settingsStub.notificationTypeExclusions = []
    settingsStub.notificationTypeInclusions = []
  })

  function summarySwitch(wrapper: ReturnType<typeof mountSettings>, key: string) {
    return wrapper
      .findAll('.notif-type-checklist__item')
      .find((item) => item.text() === `notifications.categories.${key}`)!
      .get<HTMLInputElement>('input')
  }

  it('offers the summaries off until switched on, kept apart from the alerts', async () => {
    const wrapper = mountSettings()
    const weekly = summarySwitch(wrapper, 'weeklySummary')
    expect(weekly.element.checked).toBe(false)

    await weekly.setValue(true)
    expect(settingsStub.notificationTypeInclusions).toEqual(['weekly-summary'])
    expect(settingsStub.notificationTypeExclusions).toEqual([])

    await weekly.setValue(false)
    expect(settingsStub.notificationTypeInclusions).toEqual([])
  })

  // "All off" and "all on" are about the alerts: they must not start or stop a weekly push.
  it('leaves the summaries alone when every type is switched on or off', async () => {
    settingsStub.notificationTypeInclusions = ['monthly-summary']
    const wrapper = mountSettings()
    const [allOn, allOff] = wrapper.findAll('.notif-type-filter__actions button')

    await allOff!.trigger('click')
    expect(settingsStub.notificationTypeExclusions).not.toContain('weekly-summary')
    expect(settingsStub.notificationTypeExclusions).toContain('engine-start')
    await allOn!.trigger('click')

    expect(settingsStub.notificationTypeExclusions).toEqual([])
    expect(settingsStub.notificationTypeInclusions).toEqual(['monthly-summary'])
  })

  it('offers the RDW type only where the deployment looks plates up', () => {
    expect(labels()).not.toContain('notifications.categories.rdw')

    rdwAvailable.value = false
    expect(labels()).not.toContain('notifications.categories.rdw')

    rdwAvailable.value = true
    expect(labels()).toContain('notifications.categories.rdw')
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
    expect(mountSettings().find('#settings-toggle-push').exists()).toBe(true)
  })

  it('leaves the push switch out where the browser has no push', () => {
    pushSupported.value = false

    expect(mountSettings().find('#settings-toggle-push').exists()).toBe(false)
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
