import { describe, it, expect, vi, beforeEach } from 'vitest'
import { reactive, ref } from 'vue'
import { shallowMount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import NotificationTypeSettings from '../NotificationTypeSettings.vue'
import type { Vehicle } from '@/services/vehicleApi'

const vehicleType = vi.hoisted(() => ({ value: 'unknown' as string }))

const cars = vi.hoisted(() => ({ value: [] as Vehicle[] }))

vi.mock('@/stores/vehicle', () => ({
  useVehicleStore: () => ({
    effectiveVehicleType: vehicleType.value,
    vehicles: cars.value,
    hasMultipleVehicles: cars.value.length > 1,
  }),
}))

const rdwAvailable = vi.hoisted(() => ({ value: null as boolean | null }))

vi.mock('@/stores/rdw', () => ({
  useRdwStore: () => ({ available: rdwAvailable.value }),
}))

// Reactive, so a row follows the choice made in it as it does in the app.
vi.mock('@/stores/settingsUi', () => ({
  useUiSettingsStore: () => reactive(settingsStub),
}))

const settingsStub = vi.hoisted(() => ({
  notificationTypeExclusions: [] as string[],
  notificationTypeInclusions: [] as string[],
  notificationTypeExclusionsByVehicle: {} as Record<string, string[]>,
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
    global: { plugins: [i18n], stubs: { SettingsToggle: false, SettingsSelect: false } },
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
    settingsStub.notificationTypeExclusionsByVehicle = {}
    cars.value = []
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

describe('NotificationTypeSettings with more than one car', () => {
  const MG4 = { id: 1, vin: 'FAKEVN00000000001', model: 'MG4', vehicleType: 'bev' } as Vehicle
  const HS = { id: 2, vin: 'FAKEVN00000000002', model: 'MG HS', vehicleType: 'hev' } as Vehicle

  beforeEach(() => {
    vehicleType.value = 'unknown'
    pushSupported.value = true
    rdwAvailable.value = null
    settingsStub.notificationTypeExclusions = []
    settingsStub.notificationTypeInclusions = []
    settingsStub.notificationTypeExclusionsByVehicle = {}
    cars.value = [MG4, HS]
  })

  function row(wrapper: ReturnType<typeof mountSettings>, key: string) {
    return wrapper
      .findAll('.notif-type-checklist__item')
      .find((item) => item.text().startsWith(`notifications.categories.${key}`))!
  }

  function checked(wrapper: ReturnType<typeof mountSettings>, key: string) {
    return row(wrapper, key).get<HTMLInputElement>('input').element.checked
  }

  function disabled(wrapper: ReturnType<typeof mountSettings>, key: string) {
    return row(wrapper, key).get<HTMLInputElement>('input').element.disabled
  }

  async function pickScope(wrapper: ReturnType<typeof mountSettings>, value: string) {
    await wrapper.get<HTMLSelectElement>('#settings-notification-scope').setValue(value)
  }

  it('offers no car picker with a single car', () => {
    cars.value = [MG4]

    expect(mountSettings().find('#settings-notification-scope').exists()).toBe(false)
  })

  it('offers all cars and then each car, named as the app names them', () => {
    const options = mountSettings()
      .findAll('#settings-notification-scope option')
      .map((option) => [option.attributes('value'), option.text()])

    expect(options).toEqual([
      ['all', 'notifications.scopeAll'],
      ['1', 'MG4'],
      ['2', 'MG HS'],
    ])
  })

  it('keeps what a car switches off to that car, and leaves the device list alone', async () => {
    const wrapper = mountSettings()
    await pickScope(wrapper, '2')

    await row(wrapper, 'doorsOpenParked').get('input').setValue(false)

    expect(settingsStub.notificationTypeExclusionsByVehicle).toEqual({ '2': ['doors-open-parked'] })
    expect(settingsStub.notificationTypeExclusions).toEqual([])
    await pickScope(wrapper, '1')
    expect(checked(wrapper, 'doorsOpenParked')).toBe(true)
    await pickScope(wrapper, '2')
    expect(checked(wrapper, 'doorsOpenParked')).toBe(false)
  })

  // A car with nothing off is the same as one the device never chose for.
  it('drops the car from the choice once nothing is off for it', async () => {
    settingsStub.notificationTypeExclusionsByVehicle = { '1': ['low-tyre'], '2': ['engine-start'] }
    const wrapper = mountSettings()
    await pickScope(wrapper, '2')

    await row(wrapper, 'engineStart').get('input').setValue(true)

    expect(settingsStub.notificationTypeExclusionsByVehicle).toEqual({ '1': ['low-tyre'] })
  })

  // Off for all cars wins, so a car cannot switch it back on: the row says why instead.
  it('shows a type switched off for all cars as off and locked under a car', async () => {
    settingsStub.notificationTypeExclusions = ['engine-start']
    const wrapper = mountSettings()
    await pickScope(wrapper, '1')

    expect(checked(wrapper, 'engineStart')).toBe(false)
    expect(disabled(wrapper, 'engineStart')).toBe(true)
    expect(row(wrapper, 'engineStart').text()).toContain('notifications.offForAllCars')
    expect(row(wrapper, 'lowTyre').text()).not.toContain('notifications.offForAllCars')
  })

  it('does not lock anything for all cars', () => {
    settingsStub.notificationTypeExclusions = ['engine-start']
    const wrapper = mountSettings()

    expect(disabled(wrapper, 'engineStart')).toBe(false)
    expect(wrapper.text()).not.toContain('notifications.offForAllCars')
  })

  it('applies "deselect all" and "select all" to the picked car alone', async () => {
    settingsStub.notificationTypeExclusions = ['low-tyre']
    settingsStub.notificationTypeExclusionsByVehicle = { '1': ['maintenance'] }
    const wrapper = mountSettings()
    await pickScope(wrapper, '2')
    const [allOn, allOff] = wrapper.findAll('.notif-type-filter__actions button')

    await allOff!.trigger('click')
    expect(settingsStub.notificationTypeExclusionsByVehicle['1']).toEqual(['maintenance'])
    expect(settingsStub.notificationTypeExclusionsByVehicle['2']).toContain('engine-start')
    expect(settingsStub.notificationTypeExclusionsByVehicle['2']).not.toContain('weekly-summary')
    expect(settingsStub.notificationTypeExclusions).toEqual(['low-tyre'])

    await allOn!.trigger('click')
    expect(settingsStub.notificationTypeExclusionsByVehicle).toEqual({ '1': ['maintenance'] })
    expect(settingsStub.notificationTypeExclusions).toEqual(['low-tyre'])
  })

  // The summaries are asked for once, for the device; a car can only switch one off for itself.
  it('switches a summary off for one car without turning it off for the device', async () => {
    settingsStub.notificationTypeInclusions = ['weekly-summary']
    const wrapper = mountSettings()
    await pickScope(wrapper, '1')

    expect(checked(wrapper, 'weeklySummary')).toBe(true)
    await row(wrapper, 'weeklySummary').get('input').setValue(false)

    expect(settingsStub.notificationTypeExclusionsByVehicle).toEqual({ '1': ['weekly-summary'] })
    expect(settingsStub.notificationTypeInclusions).toEqual(['weekly-summary'])
  })

  it('shows a summary the device has not asked for as locked under a car', async () => {
    const wrapper = mountSettings()
    await pickScope(wrapper, '1')

    expect(disabled(wrapper, 'weeklySummary')).toBe(true)
  })

  // The MG sign-in failing is about the account and names no car, so a car has nothing to switch off.
  it("leaves the MG sign-in alert out of a car's own list, and keeps it for all cars", async () => {
    const wrapper = mountSettings()
    const names = () => wrapper.findAll('.notif-type-checklist__label').map((el) => el.text())
    expect(names()).toContain('notifications.categories.gatewayLogin')

    await pickScope(wrapper, '1')
    expect(names()).not.toContain('notifications.categories.gatewayLogin')

    await pickScope(wrapper, 'all')
    expect(names()).toContain('notifications.categories.gatewayLogin')
  })

  // A BEV never burns fuel and a plain hybrid never charges: one car's list offers what it can do,
  // and the list for all cars what either can.
  it('offers each car the types its drivetrain can produce, and all cars what either can', async () => {
    const wrapper = mountSettings()
    const names = () => wrapper.findAll('.notif-type-checklist__label').map((el) => el.text())
    expect(names()).toContain('notifications.categories.lowEv')
    expect(names()).toContain('notifications.categories.lowFuel')

    await pickScope(wrapper, '1')
    expect(names()).toContain('notifications.categories.lowEv')
    expect(names()).not.toContain('notifications.categories.lowFuel')

    await pickScope(wrapper, '2')
    expect(names()).toContain('notifications.categories.lowFuel')
    expect(names()).not.toContain('notifications.categories.lowEv')
  })
})
