import { describe, it, expect, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { pressureVariant, DEFAULT_TYRE_PRESSURE_THRESHOLDS } from '../useTyrePressureThresholds'

describe('pressureVariant', () => {
  it('returns unknown for null', () => {
    expect(pressureVariant(null, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('unknown')
  })

  it('returns danger below the low threshold', () => {
    expect(pressureVariant(2.1, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('danger')
  })

  it('returns warning between low and good', () => {
    expect(pressureVariant(2.4, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('warning')
  })

  it('returns ok at and above good, up to high', () => {
    expect(pressureVariant(2.6, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('ok')
    expect(pressureVariant(3.2, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('ok')
  })

  it('returns danger above the high threshold', () => {
    expect(pressureVariant(3.3, DEFAULT_TYRE_PRESSURE_THRESHOLDS)).toBe('danger')
  })

  it('honours custom thresholds instead of the defaults', () => {
    const custom = { lowBar: 2.4, goodBar: 2.55, highBar: 2.7, dropBar: 0.2 }
    expect(pressureVariant(2.5, custom)).toBe('warning')
    expect(pressureVariant(2.55, custom)).toBe('ok')
  })
})

describe('useTyrePressureThresholds', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  // Imported afresh with the module, so the store the composable reads is this Pinia's.
  async function freshComposable() {
    const { createPinia, setActivePinia } = await import('pinia')
    setActivePinia(createPinia())
    return import('../useTyrePressureThresholds')
  }

  function answering(body: unknown) {
    return vi
      .fn<(url: string) => Promise<unknown>>()
      .mockResolvedValue({ ok: true, status: 200, json: () => Promise.resolve(body) })
  }

  it("asks for the car on screen's own thresholds, and again after its settings change", async () => {
    const custom = { lowBar: 2.4, goodBar: 2.55, highBar: 2.7, dropBar: 0.2 }
    const fetchMock = answering(custom)
    vi.stubGlobal('fetch', fetchMock)
    const { useTyrePressureThresholds, reloadTyrePressureThresholds } = await freshComposable()
    const { useVehicleStore } = await import('@/stores/vehicle')
    useVehicleStore().vehicles = [
      {
        id: 1,
        vin: 'FAKEVN00000000001',
        model: null,
        series: null,
        createdAt: '2026-01-01T00:00:00Z',
        vehicleType: 'bev',
        vehicleTypeOverride: null,
        hvBatteryCapacityKwh: null,
        supportsTargetSoc: false,
        hasPowerTailgate: false,
      },
    ]

    const thresholds = useTyrePressureThresholds()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(String(fetchMock.mock.calls[0]![0])).toContain(
      'tyre-pressure-thresholds?vin=FAKEVN00000000001',
    )
    expect(thresholds.value).toEqual(custom)

    fetchMock.mockClear()
    await reloadTyrePressureThresholds('FAKEVN00000000001')
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('starts at the default thresholds and updates once the fetch resolves', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ lowBar: 2.4, goodBar: 2.55, highBar: 2.7, dropBar: 0.2 }),
      }),
    )

    const { useTyrePressureThresholds } = await freshComposable()
    const thresholds = useTyrePressureThresholds()
    expect(thresholds.value).toEqual(DEFAULT_TYRE_PRESSURE_THRESHOLDS)

    await nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(thresholds.value).toEqual({ lowBar: 2.4, goodBar: 2.55, highBar: 2.7, dropBar: 0.2 })
  })

  it('keeps the default thresholds when the fetch fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 500, json: () => Promise.resolve() }),
    )

    const { useTyrePressureThresholds } = await freshComposable()
    const thresholds = useTyrePressureThresholds()

    await nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(thresholds.value).toEqual(DEFAULT_TYRE_PRESSURE_THRESHOLDS)
  })
})
