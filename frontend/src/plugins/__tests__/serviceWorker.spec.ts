import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { watchServiceWorkerUpdates } from '@/plugins/serviceWorker'

// A stand-in ServiceWorkerContainer: an event target whose controller the test swaps, the way
// the browser does when a worker claims the page.
class FakeContainer extends EventTarget {
  controller: object | null
  constructor(controller: object | null) {
    super()
    this.controller = controller
  }
  getRegistration() {
    return Promise.resolve(undefined)
  }
  takeControl(worker: object) {
    this.controller = worker
    this.dispatchEvent(new Event('controllerchange'))
  }
}

let reload: ReturnType<typeof vi.fn<() => void>>

function installContainer(controller: object | null) {
  const container = new FakeContainer(controller)
  Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: container })
  return container
}

beforeEach(() => {
  reload = vi.fn<() => void>()
  vi.stubGlobal('location', { ...window.location, reload })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('watchServiceWorkerUpdates', () => {
  it('does not reload when the first worker claims an uncontrolled page', () => {
    // A first visit: the worker that just installed serves the build the page already runs.
    const container = installContainer(null)
    watchServiceWorkerUpdates()

    container.takeControl({ id: 'first' })

    expect(reload).not.toHaveBeenCalled()
  })

  it('reloads when a new worker replaces the one in control', () => {
    const container = installContainer({ id: 'old' })
    watchServiceWorkerUpdates()

    container.takeControl({ id: 'new' })

    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('reloads for an update that arrives after the first claim in the same visit', () => {
    const container = installContainer(null)
    watchServiceWorkerUpdates()

    container.takeControl({ id: 'first' })
    container.takeControl({ id: 'update' })

    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('reloads only once when control changes again while reloading', () => {
    const container = installContainer({ id: 'old' })
    watchServiceWorkerUpdates()

    container.takeControl({ id: 'new' })
    container.takeControl({ id: 'newer' })

    expect(reload).toHaveBeenCalledTimes(1)
  })
})
