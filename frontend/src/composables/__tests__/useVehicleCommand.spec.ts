import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { vehicleApi } from '@/services/vehicleApi'
import type { TelemetrySnapshot } from '@/services/vehicleApi'
import { useVehicleCommand } from '@/composables/useVehicleCommand'
import { useVehicleStore } from '@/stores/vehicle'

vi.mock('@/services/vehicleApi', () => ({
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
    electricRangeKm: null,
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
    expect(lastResult.value).toEqual({ key: 'climate', ok: true, detail: null })
  })

  it('sets lastResult ok:false after a failed command', async () => {
    sendCommandMock.mockRejectedValue(new Error('API error'))
    const { send, lastResult } = useVehicleCommand()
    await send('VIN1', 'climate', 'start')
    expect(lastResult.value).toEqual({ key: 'climate', ok: false, detail: null })
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

  it('clears pending state after the 45s timeout', async () => {
    sendCommandMock.mockResolvedValue(undefined)
    const { send, isPending } = useVehicleCommand()
    await send('VIN1', 'lock', 'True')
    expect(isPending('lock')).toBe(true)
    vi.advanceTimersByTime(45_000)
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

      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true })
      expect(isPending('lock')).toBe(true)

      store.applyLiveStatus(makeSnapshot({ isLocked: true }))
      await nextTick()

      expect(isPending('lock')).toBe(false)
    })

    it('does not clear pending when isConfirmed returns false', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send, isPending } = useVehicleCommand()

      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true })
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

      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true, onConfirmed })

      store.applyLiveStatus(makeSnapshot({ isLocked: true }))
      await nextTick()

      expect(onConfirmed).toHaveBeenCalledTimes(1)
    })

    it('does not call onConfirmed when isConfirmed returns false', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const store = useVehicleStore()
      const { send } = useVehicleCommand()
      const onConfirmed = vi.fn<() => void>()

      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true, onConfirmed })

      store.applyLiveStatus(makeSnapshot({ isLocked: false }))
      await nextTick()

      expect(onConfirmed).not.toHaveBeenCalled()
    })

    it('still clears pending via timeout when isConfirmed is provided', async () => {
      sendCommandMock.mockResolvedValue(undefined)
      const { send, isPending } = useVehicleCommand()

      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true })
      expect(isPending('lock')).toBe(true)

      vi.advanceTimersByTime(45_000)
      expect(isPending('lock')).toBe(false)
    })
  })

  // The API pushes the gateway's answer through the vehicle store, as App.vue does for SignalR.
  describe('gateway answers', () => {
    function answer(command: string, success: boolean, detail: string | null = null) {
      useVehicleStore().applyCommandResult({ command, success, detail })
    }

    beforeEach(() => {
      sendCommandMock.mockResolvedValue(undefined)
    })

    it('clears pending and reports the reason when the car refuses', async () => {
      const onRejected = vi.fn<() => void>()
      const { send, isPending, lastResult } = useVehicleCommand()
      await send('VIN1', 'lock', 'True', { onRejected })

      answer('lock', false, 'vehicle is not online')

      expect(isPending('lock')).toBe(false)
      expect(lastResult.value).toEqual({ key: 'lock', ok: false, detail: 'vehicle is not online' })
      expect(onRejected).toHaveBeenCalledTimes(1)
    })

    it('settles a command without a telemetry check as soon as the car carried it out', async () => {
      const onConfirmed = vi.fn<() => void>()
      const { send, isPending, lastResult } = useVehicleCommand()
      await send('VIN1', 'find-my-car', 'activate', { onConfirmed })

      answer('find-my-car', true)

      expect(isPending('find-my-car')).toBe(false)
      expect(lastResult.value?.ok).toBe(true)
      expect(onConfirmed).toHaveBeenCalledTimes(1)
    })

    // Success means the car did it, but the card shows telemetry, which catches up a little later.
    it('keeps a command with a telemetry check pending until telemetry shows it', async () => {
      const store = useVehicleStore()
      const onConfirmed = vi.fn<() => void>()
      const { send, isPending } = useVehicleCommand()
      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true, onConfirmed })

      answer('lock', true)
      expect(isPending('lock')).toBe(true)
      expect(onConfirmed).not.toHaveBeenCalled()

      store.applyLiveStatus(makeSnapshot({ isLocked: true }))
      await nextTick()
      expect(isPending('lock')).toBe(false)
      expect(onConfirmed).toHaveBeenCalledTimes(1)
    })

    // The gateway is free as soon as it answers, so a batch need not wait for telemetry.
    it('lets waitUntilSettled continue once the car carried the command out', async () => {
      const { send, waitUntilSettled } = useVehicleCommand()
      await send('VIN1', 'climate', 'on', { isConfirmed: (s) => s.climateOn === true })
      const settled = vi.fn<() => void>()
      void waitUntilSettled('climate').then(settled)

      await nextTick()
      expect(settled).not.toHaveBeenCalled()

      answer('climate', true)
      await vi.waitFor(() => expect(settled).toHaveBeenCalled())
    })

    it('lets waitUntilSettled continue once the car refused the command', async () => {
      const { send, waitUntilSettled } = useVehicleCommand()
      await send('VIN1', 'climate', 'on', { isConfirmed: (s) => s.climateOn === true })
      const settled = vi.fn<() => void>()
      void waitUntilSettled('climate').then(settled)

      answer('climate', false, 'vehicle is not online')
      await vi.waitFor(() => expect(settled).toHaveBeenCalled())
    })

    // An answer names a command, not a request: another tab's lock, say, is not this one's.
    it('ignores an answer for a command it is not waiting on', async () => {
      const { send, isPending, lastResult } = useVehicleCommand()
      await send('VIN1', 'lock', 'True')

      answer('climate', false, 'vehicle is not online')

      expect(isPending('lock')).toBe(true)
      expect(lastResult.value).toEqual({ key: 'lock', ok: true, detail: null })
    })

    it('ignores an answer arriving after the command was given up on', async () => {
      const onRejected = vi.fn<() => void>()
      const { send, lastResult } = useVehicleCommand()
      await send('VIN1', 'lock', 'True', { onRejected })
      vi.advanceTimersByTime(45_000)

      answer('lock', false, 'too late')

      expect(onRejected).not.toHaveBeenCalled()
      expect(lastResult.value?.ok).toBe(true)
    })

    it('ignores a second answer for a command already answered', async () => {
      const { send, lastResult } = useVehicleCommand()
      await send('VIN1', 'lock', 'True', { isConfirmed: (s) => s.isLocked === true })

      answer('lock', true)
      answer('lock', false, 'stale')

      expect(lastResult.value?.ok).toBe(true)
    })

    // SignalR can deliver several messages in one frame, so two answers can land in one tick.
    it('applies every answer arriving in the same tick', async () => {
      const { send, isPending } = useVehicleCommand()
      await send('VIN1', 'lock', 'True')
      await send('VIN1', 'find-my-car', 'activate')

      answer('lock', true)
      answer('find-my-car', true)

      expect(isPending('lock')).toBe(false)
      expect(isPending('find-my-car')).toBe(false)
    })
  })
})
