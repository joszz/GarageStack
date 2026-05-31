import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { vehicleApi } from '@/services/api'
import { useVehicleCommand } from '@/composables/useVehicleCommand'

vi.mock('@/services/api', () => ({
  vehicleApi: {
    sendCommand: vi.fn<(vin: string, command: string, value: string) => Promise<void>>(),
  },
}))

// Typed reference to the mock after hoisting is resolved
const sendCommandMock = vi.mocked(vehicleApi.sendCommand)

describe('useVehicleCommand', () => {
  beforeEach(() => {
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
})
