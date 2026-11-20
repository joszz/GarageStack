import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref, type EffectScope } from 'vue'
import { OFFLINE_GRACE_MS, useSettledAvailability } from '../useSettledAvailability'

describe('useSettledAvailability', () => {
  let scope: EffectScope
  const available = ref<boolean | null | undefined>(true)
  const car = ref(1)

  function settle() {
    return scope.run(() =>
      useSettledAvailability(
        () => available.value,
        () => car.value,
      ),
    )!
  }

  beforeEach(() => {
    vi.useFakeTimers()
    scope = effectScope()
    available.value = true
    car.value = 1
  })

  afterEach(() => {
    scope.stop()
    vi.useRealTimers()
  })

  it('starts from what the car says', () => {
    available.value = false

    expect(settle().value).toBe(false)
  })

  // The gateway flips to offline after three failed polls and back on the next good one.
  it('keeps a car that goes offline for a moment online', async () => {
    const shown = settle()

    available.value = false
    await nextTick()
    vi.advanceTimersByTime(OFFLINE_GRACE_MS - 1_000)
    available.value = true
    await nextTick()
    vi.advanceTimersByTime(OFFLINE_GRACE_MS)

    expect(shown.value).toBe(true)
  })

  it('shows the car offline once that has lasted the grace period', async () => {
    const shown = settle()

    available.value = false
    await nextTick()
    vi.advanceTimersByTime(OFFLINE_GRACE_MS - 1)
    expect(shown.value).toBe(true)

    vi.advanceTimersByTime(1)
    expect(shown.value).toBe(false)
  })

  it('shows a car coming back online at once', async () => {
    available.value = false
    const shown = settle()

    available.value = true
    await nextTick()

    expect(shown.value).toBe(true)
  })

  // Another car's state is news about that car, not a flip of this one.
  it('shows another car offline straight away', async () => {
    const shown = settle()

    car.value = 2
    available.value = false
    await nextTick()

    expect(shown.value).toBe(false)
  })

  it('forgets the state when there is no status', async () => {
    const shown = settle()

    available.value = undefined
    await nextTick()

    expect(shown.value).toBeNull()
  })
})
