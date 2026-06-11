import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { vehicleApi } from '@/services/api'
import type { TelemetrySnapshot } from '@/services/api'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useVehicleStore } from '@/stores/vehicle'

vi.mock('@/services/api', () => ({
  vehicleApi: {
    sendCommand: vi.fn<(vin: string, command: string, value: string) => Promise<void>>(),
  },
}))

// Typed reference to the mock after hoisting is resolved
const sendCommandMock = vi.mocked(vehicleApi.sendCommand)

function makeSnapshot(overrides: Partial<TelemetrySnapshot> = {}): TelemetrySnapshot {
  return {
    id: 1,
    vehicleId: 1,
    recordedAt: new Date().toISOString(),
    fuelLevelPercent: null,
    fuelRangeKm: null,
    odometerKm: null,
    isLocked: null,
    engineRunning: null,
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
    bmsChargeStatus: null,
    lastChargeEndingPower: null,
    chargingLastEndAt: null,
    chargingScheduleMode: null,
    chargingScheduleStartTime: null,
    chargingScheduleEndTime: null,
    onboardChargerPlugStatus: null,
    offboardChargerPlugStatus: null,
    ...overrides,
  }
}

describe('useVehicleCommand', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sendCommandMock.mockReset()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('does nothing when vin is null', async () => {
    const { send, sending, lastResult } = useVehicleCommand()
    await send(null, 'lock', 'lock')
    expect(sending.value).toBeNull()
    expect(lastResult.value).toBeNull()
    expect(sendCommandMock).not.toHaveBeenCalled()
  })

  it('returns false when vin is null', async () => {
    const { send } = useVehicleCommand()
    expect(await send(null, 'lock', 'lock')).toBe(false)
  })

  it('returns true after a successful command', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send } = useVehicleCommand()
    expect(await send('VIN1', 'lock', 'True')).toBe(true)
  })

  it('returns false after a failed command', async () => {
    sendCommandMock.mockRejectedValue(new Error('API error'))
    const { send } = useVehicleCommand()
    expect(await send('VIN1', 'lock', 'True')).toBe(false)
  })

  it('does nothing when vin is undefined', async () => {
    const { send, sending } = useVehicleCommand()
    await send(undefined, 'lock', 'lock')
    expect(sending.value).toBeNull()
  })

  it('sets sending to the command name while the request is in flight', async () => {
    const { send, sending } = useVehicleCommand()
    let sendingDuringCall: string | null = null
    sendCommandMock.mockImplementation(async () => {
      sendingDuringCall = sending.value
    })
    await send('VIN1', 'climate', 'start')
    expect(sendingDuringCall).toBe('climate')
  })

  it('resets sending to null after a successful command', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, sending } = useVehicleCommand()
    await send('VIN1', 'lock', 'lock')
    expect(sending.value).toBeNull()
  })

  it('resets sending to null after a failed command', async () => {
    sendCommandMock.mockRejectedValue(new Error('API error'))
    const { send, sending } = useVehicleCommand()
    await send('VIN1', 'lock', 'lock')
    expect(sending.value).toBeNull()
  })

  it('sets lastResult ok:true after a successful command', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, lastResult } = useVehicleCommand()
    await send('VIN1', 'climate', 'start')
    expect(lastResult.value).toEqual({ key: 'climate', ok: true })
  })

  it('sets lastResult ok:false after a failed command', async () => {
    sendCommandMock.mockRejectedValue(new Error('API error'))
    const { send, lastResult } = useVehicleCommand()
    await send('VIN1', 'climate', 'start')
    expect(lastResult.value).toEqual({ key: 'climate', ok: false })
  })

  it('clears lastResult when a new command starts', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, lastResult } = useVehicleCommand()
    await send('VIN1', 'lock', 'lock')
    expect(lastResult.value?.ok).toBe(true)

    let resultDuringSecondCall: typeof lastResult.value = undefined as never
    sendCommandMock.mockImplementation(async () => {
      resultDuringSecondCall = lastResult.value
    })
    await send('VIN1', 'unlock', 'unlock')
    expect(resultDuringSecondCall).toBeNull()
  })

  it('passes vin, command and value through to the API', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send } = useVehicleCommand()
    await send('MYVIN', 'charge-limit', '80')
    expect(sendCommandMock).toHaveBeenCalledWith('MYVIN', 'charge-limit', '80')
  })

  it('marks a command as pending after success', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, isPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    expect(isPending('lock')).toBe(true)
  })

  it('does not mark a command as pending after failure', async () => {
    sendCommandMock.mockRejectedValue(new Error('API error'))
    const { send, isPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    expect(isPending('lock')).toBe(false)
  })

  it('blocks re-sending a command while it is pending', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    await send('VIN1', 'lock', 'False')
    expect(sendCommandMock).toHaveBeenCalledTimes(1)
  })

  it('clears pending state after the 30s timeout', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, isPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    expect(isPending('lock')).toBe(true)
    vi.advanceTimersByTime(30_000)
    expect(isPending('lock')).toBe(false)
  })

  it('clears pending state manually via clearPending', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, isPending, clearPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    expect(isPending('lock')).toBe(true)
    clearPending('lock')
    expect(isPending('lock')).toBe(false)
  })

  it('allows resending after pending is cleared', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, clearPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    clearPending('lock')
    await send('VIN1', 'lock', 'False')
    expect(sendCommandMock).toHaveBeenCalledTimes(2)
  })

  describe('isConfirmed', () => {
    it('clears pending when isConfirmed returns true on telemetry update', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send, isPending } = useVehicleCommand()

      await send('VIN1', 'lock', 'True', (s) => s.isLocked === true)
      expect(isPending('lock')).toBe(true)

      store.applyLiveStatus(makeSnapshot({ isLocked: true }))
      await nextTick()

      expect(isPending('lock')).toBe(false)
    })

    it('does not clear pending when isConfirmed returns false', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send, isPending } = useVehicleCommand()

      await send('VIN1', 'lock', 'True', (s) => s.isLocked === true)
      expect(isPending('lock')).toBe(true)

      store.applyLiveStatus(makeSnapshot({ isLocked: false }))
      await nextTick()

      expect(isPending('lock')).toBe(true)
    })

    it('calls onConfirmed when isConfirmed returns true', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send } = useVehicleCommand()
      const onConfirmed = vi.fn<() => void>()

      await send('VIN1', 'lock', 'True', (s) => s.isLocked === true, onConfirmed)

      store.applyLiveStatus(makeSnapshot({ isLocked: true }))
      await nextTick()

      expect(onConfirmed).toHaveBeenCalledTimes(1)
    })

    it('does not call onConfirmed when isConfirmed returns false', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send } = useVehicleCommand()
      const onConfirmed = vi.fn<() => void>()

      await send('VIN1', 'lock', 'True', (s) => s.isLocked === true, onConfirmed)

      store.applyLiveStatus(makeSnapshot({ isLocked: false }))
      await nextTick()

      expect(onConfirmed).not.toHaveBeenCalled()
    })

    it('still clears pending via timeout when isConfirmed is provided', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const { send, isPending } = useVehicleCommand()

      await send('VIN1', 'lock', 'True', (s) => s.isLocked === true)
      expect(isPending('lock')).toBe(true)

      vi.advanceTimersByTime(30_000)
      expect(isPending('lock')).toBe(false)
    })
  })
})
