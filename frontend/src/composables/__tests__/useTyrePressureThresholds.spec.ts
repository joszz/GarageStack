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
    const custom = { lowBar: 2.4, goodBar: 2.55, highBar: 2.7 }
    expect(pressureVariant(2.5, custom)).toBe('warning')
    expect(pressureVariant(2.55, custom)).toBe('ok')
  })
})

describe('useTyrePressureThresholds', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.resetModules()
  })

  it('starts at the default thresholds and updates once the fetch resolves', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: () => Promise.resolve({ lowBar: 2.4, goodBar: 2.55, highBar: 2.7 }),
      }),
    )

    const { useTyrePressureThresholds } = await import('../useTyrePressureThresholds')
    const thresholds = useTyrePressureThresholds()
    expect(thresholds.value).toEqual(DEFAULT_TYRE_PRESSURE_THRESHOLDS)

    await nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(thresholds.value).toEqual({ lowBar: 2.4, goodBar: 2.55, highBar: 2.7 })
  })

  it('keeps the default thresholds when the fetch fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({ ok: false, status: 500, json: () => Promise.resolve() }),
    )

    const { useTyrePressureThresholds } = await import('../useTyrePressureThresholds')
    const thresholds = useTyrePressureThresholds()

    await nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(thresholds.value).toEqual(DEFAULT_TYRE_PRESSURE_THRESHOLDS)
  })
})
