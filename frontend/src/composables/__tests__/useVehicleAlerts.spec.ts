import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ref, nextTick } from 'vue'
import { getOpenItems, getTyrePressureAlerts, useVehicleAlerts } from '../useVehicleAlerts'
import type { TelemetrySnapshot } from '@/services/api'

function makeSnapshot(overrides: Partial<TelemetrySnapshot> = {}): TelemetrySnapshot {
  return {
    id: 1,
    vehicleId: 1,
    recordedAt: new Date().toISOString(),
    fuelLevelPercent: null,
    fuelRangeKm: null,
    odometerKm: null,
    isLocked: null,
    engineRunning: false,
    climateOn: null,
    driverDoorOpen: null,
    passengerDoorOpen: null,
    rearLeftDoorOpen: null,
    rearRightDoorOpen: null,
    trunkOpen: null,
    bonnetOpen: null,
    driverWindowOpen: null,
    passengerWindowOpen: null,
    rearLeftWindowOpen: null,
    rearRightWindowOpen: null,
    latitude: null,
    longitude: null,
    speed: null,
    heading: null,
    batteryVoltage: null,
    interiorTemperature: null,
    exteriorTemperature: null,
    evSocPercent: null,
    isCharging: null,
    sunRoofOpen: null,
    tyrePressureFrontLeft: null,
    tyrePressureFrontRight: null,
    tyrePressureRearLeft: null,
    tyrePressureRearRight: null,
    mileageOfTheDay: null,
    powerUsageOfDay: null,
    mileageSinceLastCharge: null,
    hvVoltage: null,
    hvCurrent: null,
    hvPower: null,
    hvSocKwh: null,
    hvTotalCapacityKwh: null,
    powerUsageSinceLastCharge: null,
    chargerConnected: null,
    hvBatteryActive: null,
    lightsMainBeam: null,
    lightsDippedBeam: null,
    lightsSide: null,
    remoteTemperature: null,
    heatedSeatFrontLeft: null,
    heatedSeatFrontRight: null,
    rearWindowDefroster: null,
    isAvailable: null,
    lastVehicleStateAt: null,
    lastChargeStateAt: null,
    currentJourneyDistance: null,
    chargingType: null,
    chargingCableLock: null,
    remainingChargingTime: null,
    obcCurrent: null,
    obcVoltage: null,
    obcPowerSinglePhase: null,
    obcPowerThreePhase: null,
    batteryHeating: null,
    batteryHeatingScheduleMode: null,
    batteryHeatingScheduleStartTime: null,
    elevation: null,
    ...overrides,
  }
}

describe('getOpenItems', () => {
  it('returns empty array when all values are null', () => {
    expect(getOpenItems(makeSnapshot())).toEqual([])
  })

  it('returns empty array when all items are closed', () => {
    expect(
      getOpenItems(
        makeSnapshot({
          driverDoorOpen: false,
          passengerDoorOpen: false,
          sunRoofOpen: false,
        }),
      ),
    ).toEqual([])
  })

  it('detects open driver door', () => {
    expect(getOpenItems(makeSnapshot({ driverDoorOpen: true }))).toContain('driver door')
  })

  it('detects open passenger door', () => {
    expect(getOpenItems(makeSnapshot({ passengerDoorOpen: true }))).toContain('passenger door')
  })

  it('detects open rear doors', () => {
    const items = getOpenItems(makeSnapshot({ rearLeftDoorOpen: true, rearRightDoorOpen: true }))
    expect(items).toContain('rear left door')
    expect(items).toContain('rear right door')
  })

  it('detects open boot and bonnet', () => {
    const items = getOpenItems(makeSnapshot({ trunkOpen: true, bonnetOpen: true }))
    expect(items).toContain('boot')
    expect(items).toContain('bonnet')
  })

  it('detects open windows and sunroof', () => {
    const items = getOpenItems(
      makeSnapshot({
        driverWindowOpen: true,
        passengerWindowOpen: true,
        rearLeftWindowOpen: true,
        rearRightWindowOpen: true,
        sunRoofOpen: true,
      }),
    )
    expect(items).toContain('driver window')
    expect(items).toContain('passenger window')
    expect(items).toContain('rear left window')
    expect(items).toContain('rear right window')
    expect(items).not.toContain('sunroof')
  })

  it('returns only open items when mixed', () => {
    const items = getOpenItems(makeSnapshot({ driverDoorOpen: true, passengerDoorOpen: false }))
    expect(items).toHaveLength(1)
    expect(items).toContain('driver door')
  })
})

describe('getTyrePressureAlerts', () => {
  it('returns empty array for all null pressures', () => {
    expect(getTyrePressureAlerts(makeSnapshot())).toEqual([])
  })

  it('returns empty array for normal pressures', () => {
    expect(
      getTyrePressureAlerts(
        makeSnapshot({
          tyrePressureFrontLeft: 2.5,
          tyrePressureFrontRight: 2.5,
          tyrePressureRearLeft: 2.4,
          tyrePressureRearRight: 2.4,
        }),
      ),
    ).toEqual([])
  })

  it('flags FL tyre below minimum', () => {
    const alerts = getTyrePressureAlerts(makeSnapshot({ tyrePressureFrontLeft: 1.5 }))
    expect(alerts).toHaveLength(1)
    expect(alerts[0]).toContain('FL')
    expect(alerts[0]).toContain('1.50 bar')
  })

  it('flags FR tyre above maximum', () => {
    const alerts = getTyrePressureAlerts(makeSnapshot({ tyrePressureFrontRight: 3.5 }))
    expect(alerts).toHaveLength(1)
    expect(alerts[0]).toContain('FR')
    expect(alerts[0]).toContain('3.50 bar')
  })

  it('flags RL and RR tyres', () => {
    const alerts = getTyrePressureAlerts(
      makeSnapshot({
        tyrePressureRearLeft: 1.2,
        tyrePressureRearRight: 3.9,
      }),
    )
    expect(alerts).toHaveLength(2)
    expect(alerts[0]).toContain('RL')
    expect(alerts[1]).toContain('RR')
  })

  it('accepts pressures exactly at boundaries', () => {
    expect(
      getTyrePressureAlerts(
        makeSnapshot({
          tyrePressureFrontLeft: 1.8,
          tyrePressureFrontRight: 3.2,
        }),
      ),
    ).toEqual([])
  })
})

describe('useVehicleAlerts', () => {
  let notificationMock: ReturnType<
    typeof vi.fn<(title: string, options?: NotificationOptions) => void>
  >

  beforeEach(() => {
    notificationMock = vi.fn<(title: string, options?: NotificationOptions) => void>()
    Object.defineProperty(window, 'Notification', {
      value: notificationMock,
      writable: true,
      configurable: true,
    })
    Object.defineProperty(window.Notification, 'permission', {
      value: 'granted',
      writable: true,
      configurable: true,
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('does not fire when status is null', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    await nextTick()
    expect(notificationMock).not.toHaveBeenCalled()
  })

  it('fires open-while-parked notification when door is open and engine off', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    expect(notificationMock).toHaveBeenCalledWith(
      'GarageStack',
      expect.objectContaining({ body: expect.stringContaining('driver door') }),
    )
  })

  it('does not fire open notification when engine is running', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ engineRunning: true, driverDoorOpen: true })
    await nextTick()
    expect(notificationMock).not.toHaveBeenCalled()
  })

  it('does not repeat open notification on subsequent polls while still open', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    expect(notificationMock).toHaveBeenCalledTimes(1)
  })

  it('resets and re-fires open alert after door closes then reopens', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: false })
    await nextTick()
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    expect(notificationMock).toHaveBeenCalledTimes(2)
  })

  it('fires tyre pressure notification when pressure is low', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ tyrePressureFrontLeft: 1.5 })
    await nextTick()
    expect(notificationMock).toHaveBeenCalledWith(
      'GarageStack',
      expect.objectContaining({ body: expect.stringContaining('FL') }),
    )
  })

  it('does not repeat tyre alert on subsequent polls while still low', async () => {
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ tyrePressureFrontLeft: 1.5 })
    await nextTick()
    status.value = makeSnapshot({ tyrePressureFrontLeft: 1.5 })
    await nextTick()
    expect(notificationMock).toHaveBeenCalledTimes(1)
  })

  it('does not fire notification when permission is not granted', async () => {
    Object.defineProperty(window.Notification, 'permission', {
      value: 'default',
      writable: true,
      configurable: true,
    })
    const status = ref<TelemetrySnapshot | null>(null)
    useVehicleAlerts(status)
    status.value = makeSnapshot({ engineRunning: false, driverDoorOpen: true })
    await nextTick()
    expect(notificationMock).not.toHaveBeenCalled()
  })
})
