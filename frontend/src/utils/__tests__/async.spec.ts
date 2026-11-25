import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { PAGE_IDLE_TIMEOUT_MS, whenPageIdle } from '@/utils/async'

function setReadyState(state: DocumentReadyState) {
  Object.defineProperty(document, 'readyState', { configurable: true, get: () => state })
}

describe('whenPageIdle', () => {
  let idleCallbacks: { callback: () => void; timeout: number | undefined }[]

  beforeEach(() => {
    vi.useFakeTimers()
    idleCallbacks = []
    vi.stubGlobal('requestIdleCallback', (callback: () => void, options?: { timeout?: number }) =>
      idleCallbacks.push({ callback, timeout: options?.timeout }),
    )
    setReadyState('complete')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    // Back to jsdom's own getter.
    delete (document as { readyState?: DocumentReadyState }).readyState
  })

  it('waits for an idle moment, at most PAGE_IDLE_TIMEOUT_MS', async () => {
    let done = false
    void whenPageIdle().then(() => {
      done = true
    })
    await vi.waitFor(() => expect(idleCallbacks).toHaveLength(1))
    expect(idleCallbacks[0]!.timeout).toBe(PAGE_IDLE_TIMEOUT_MS)
    expect(done).toBe(false)

    idleCallbacks[0]!.callback()

    await vi.waitFor(() => expect(done).toBe(true))
  })

  it('waits for the page to finish loading first', async () => {
    setReadyState('interactive')
    void whenPageIdle()
    await Promise.resolve()
    expect(idleCallbacks).toHaveLength(0)

    window.dispatchEvent(new Event('load'))

    await vi.waitFor(() => expect(idleCallbacks).toHaveLength(1))
  })

  it('goes ahead right after the load in a browser without requestIdleCallback', async () => {
    vi.stubGlobal('requestIdleCallback', undefined)
    let done = false
    void whenPageIdle().then(() => {
      done = true
    })

    await vi.advanceTimersByTimeAsync(0)

    expect(done).toBe(true)
  })
})
