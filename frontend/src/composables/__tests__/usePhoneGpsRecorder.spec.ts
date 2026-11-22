import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiCore'
import { phonePositionsApi } from '@/services/phonePositionsApi'
import type { Vehicle } from '@/services/vehicleApi'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useVehicleStore } from '@/stores/vehicle'
import { queuedPositions } from '@/utils/phoneGps'
import { FLUSH_AT, usePhoneGpsRecorder, usePhoneGpsStatus } from '../usePhoneGpsRecorder'

vi.mock('@/services/phonePositionsApi', async (original) => ({
  ...(await original<typeof import('@/services/phonePositionsApi')>()),
  phonePositionsApi: { upload: vi.fn<typeof phonePositionsApi.upload>() },
}))

const VIN = 'FAKEVN00000000001'
const car: Vehicle = {
  id: 1,
  vin: VIN,
  model: 'Test car',
  series: null,
  createdAt: '2026-01-01T00:00:00Z',
  vehicleType: 'hev',
  vehicleTypeOverride: null,
  hvBatteryCapacityKwh: null,
  supportsTargetSoc: false,
  hasPowerTailgate: false,
}

type Success = (position: GeolocationPosition) => void
type Failure = (error: GeolocationPositionError) => void

let onFix: Success | null
let onError: Failure | null
const watchPosition = vi.fn<(success: Success, error: Failure) => number>()
const clearWatch = vi.fn<(id: number) => void>()
let visibility: DocumentVisibilityState
const upload = vi.mocked(phonePositionsApi.upload)

function fix(seconds: number, latitude = 52 + seconds * 0.0001, accuracy = 5): GeolocationPosition {
  return {
    timestamp: Date.UTC(2026, 0, 1, 10) + seconds * 1000,
    coords: { latitude, longitude: 5, accuracy, speed: 10 },
  } as GeolocationPosition
}

function setVisibility(state: DocumentVisibilityState) {
  visibility = state
  document.dispatchEvent(new Event('visibilitychange'))
}

function mountRecorder() {
  return mount(
    defineComponent({
      setup() {
        usePhoneGpsRecorder()
        return () => h('div')
      },
    }),
  )
}

/** Signed in, a car, the installed app, and both settings on. */
function readyToRecord() {
  localStorage.setItem('garagestack-auth-username', 'test-user')
  localStorage.setItem('garagestack-auth-expires', '2999-01-01T00:00:00Z')
  setActivePinia(createPinia())
  useVehicleStore().vehicles = [{ ...car }]
  const settings = useUiSettingsStore()
  settings.keepScreenOn = true
  settings.recordPhoneGps = true
  return settings
}

enableAutoUnmount(afterEach)

beforeEach(() => {
  localStorage.clear()
  onFix = null
  onError = null
  watchPosition.mockReset()
  watchPosition.mockImplementation((success, error) => {
    onFix = success
    onError = error
    return 7
  })
  clearWatch.mockReset()
  upload.mockReset()
  upload.mockResolvedValue({ stored: 1 })
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: { watchPosition, clearWatch },
  })
  Object.defineProperty(navigator, 'wakeLock', {
    configurable: true,
    value: { request: vi.fn<() => Promise<never>>() },
  })
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === '(display-mode: standalone)',
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
  visibility = 'visible'
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
})

afterEach(() => {
  Reflect.deleteProperty(navigator, 'geolocation')
  Reflect.deleteProperty(navigator, 'wakeLock')
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  vi.useRealTimers()
})

describe('usePhoneGpsRecorder', () => {
  it('follows the GPS once everything is on, and queues the fixes worth sending', async () => {
    readyToRecord()
    mountRecorder()
    await flushPromises()

    expect(watchPosition).toHaveBeenCalledOnce()
    expect(usePhoneGpsStatus().recording.value).toBe(true)
    onFix!(fix(0))
    onFix!(fix(2))
    onFix!(fix(10, 52.01, 80))
    onFix!(fix(10))

    expect(queuedPositions(VIN).map((p) => p.recordedAt)).toEqual([
      '2026-01-01T10:00:00.000Z',
      '2026-01-01T10:00:10.000Z',
    ])
  })

  it('records nothing while keeping the screen on is off', async () => {
    const settings = readyToRecord()
    settings.keepScreenOn = false
    mountRecorder()
    await flushPromises()

    expect(watchPosition).not.toHaveBeenCalled()
    expect(usePhoneGpsStatus().recording.value).toBe(false)
  })

  it('records nothing signed out', async () => {
    readyToRecord()
    localStorage.clear()
    setActivePinia(createPinia())
    useVehicleStore().vehicles = [{ ...car }]
    Object.assign(useUiSettingsStore(), { keepScreenOn: true, recordPhoneGps: true })
    mountRecorder()
    await flushPromises()
    expect(watchPosition).not.toHaveBeenCalled()
  })

  it('records nothing in a browser tab', async () => {
    readyToRecord()
    vi.stubGlobal('matchMedia', () => ({
      matches: false,
      addEventListener: () => {},
      removeEventListener: () => {},
    }))
    mountRecorder()
    await flushPromises()
    expect(watchPosition).not.toHaveBeenCalled()
  })

  it('stops while the app is out of view and carries on when it is back', async () => {
    readyToRecord()
    mountRecorder()
    await flushPromises()

    setVisibility('hidden')
    await nextTick()
    expect(clearWatch).toHaveBeenCalledWith(7)

    setVisibility('visible')
    await nextTick()
    expect(watchPosition).toHaveBeenCalledTimes(2)
  })

  it('sends the queue every half minute, and at once when it grows long', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    readyToRecord()
    mountRecorder()
    await flushPromises()
    onFix!(fix(0))

    await vi.advanceTimersByTimeAsync(30_000)
    expect(upload).toHaveBeenCalledExactlyOnceWith(VIN, expect.stringMatching(/^[0-9a-f]{32}$/), [
      expect.objectContaining({ recordedAt: '2026-01-01T10:00:00.000Z' }),
    ])
    expect(queuedPositions(VIN)).toEqual([])

    for (let i = 1; i <= FLUSH_AT; i++) onFix!(fix(i * 5))
    await flushPromises()
    expect(upload).toHaveBeenCalledTimes(2)
  })

  it('keeps the queue through a failed upload, and drops a batch the API refuses', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    readyToRecord()
    mountRecorder()
    await flushPromises()
    onFix!(fix(0))

    upload.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await vi.advanceTimersByTimeAsync(30_000)
    expect(queuedPositions(VIN)).toHaveLength(1)

    upload.mockRejectedValueOnce(new ApiError(503, '/api'))
    await vi.advanceTimersByTimeAsync(30_000)
    expect(queuedPositions(VIN)).toHaveLength(1)

    upload.mockRejectedValueOnce(new ApiError(400, '/api', { code: 'phone.invalidPositions' }))
    await vi.advanceTimersByTimeAsync(30_000)
    expect(queuedPositions(VIN)).toEqual([])
  })

  it('stops when the browser refuses, and asks again once switched back on', async () => {
    const settings = readyToRecord()
    mountRecorder()
    await flushPromises()

    onError!({ code: 1, PERMISSION_DENIED: 1 } as GeolocationPositionError)
    expect(usePhoneGpsStatus().denied.value).toBe(true)
    expect(clearWatch).toHaveBeenCalledOnce()

    settings.recordPhoneGps = false
    await nextTick()
    settings.recordPhoneGps = true
    await nextTick()

    expect(usePhoneGpsStatus().denied.value).toBe(false)
    expect(watchPosition).toHaveBeenCalledTimes(2)
  })

  it('lets go of the GPS when the app is torn down', async () => {
    readyToRecord()
    const wrapper = mountRecorder()
    await flushPromises()

    wrapper.unmount()

    expect(clearWatch).toHaveBeenCalledWith(7)
  })
})
