import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { useKeepScreenOnOffered, useScreenWakeLock } from '../useScreenWakeLock'

type ReleaseListener = () => void

class FakeSentinel {
  released = false
  private listeners: ReleaseListener[] = []
  readonly release = vi.fn<() => Promise<void>>(async () => this.drop())
  addEventListener(_type: 'release', listener: ReleaseListener) {
    this.listeners.push(listener)
  }
  /** What the browser does when the page leaves the screen. */
  drop() {
    if (this.released) return
    this.released = true
    this.listeners.forEach((listener) => listener())
  }
}

let sentinels: FakeSentinel[]
const request = vi.fn<(type: 'screen') => Promise<FakeSentinel>>()
let visibility: DocumentVisibilityState

function installedApp(installed: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: installed && query === '(display-mode: standalone)',
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

function setVisibility(state: DocumentVisibilityState) {
  visibility = state
  document.dispatchEvent(new Event('visibilitychange'))
}

function mountLock() {
  return mount(
    defineComponent({
      setup() {
        useScreenWakeLock()
        return () => h('div')
      },
    }),
  )
}

beforeEach(() => {
  setActivePinia(createPinia())
  localStorage.clear()
  sentinels = []
  request.mockReset()
  request.mockImplementation(async () => {
    const sentinel = new FakeSentinel()
    sentinels.push(sentinel)
    return sentinel
  })
  Object.defineProperty(navigator, 'wakeLock', { configurable: true, value: { request } })
  visibility = 'visible'
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
  installedApp(true)
})

afterEach(() => {
  Reflect.deleteProperty(navigator, 'wakeLock')
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useKeepScreenOnOffered', () => {
  function offered() {
    let result = false
    mount(
      defineComponent({
        setup() {
          result = useKeepScreenOnOffered().value
          return () => h('div')
        },
      }),
    )
    return result
  }

  it('is offered in the installed app where the browser can hold the screen on', () => {
    expect(offered()).toBe(true)
  })

  it('is not offered in a browser tab', () => {
    installedApp(false)
    expect(offered()).toBe(false)
  })

  it('is not offered where the browser has no wake lock', () => {
    Reflect.deleteProperty(navigator, 'wakeLock')
    expect(offered()).toBe(false)
  })
})

describe('useScreenWakeLock', () => {
  it('asks for nothing while the setting is off', async () => {
    mountLock()
    await flushPromises()

    expect(request).not.toHaveBeenCalled()
  })

  it('holds the screen on once the setting is on, and lets go when it is turned off', async () => {
    const settings = useUiSettingsStore()
    mountLock()

    settings.keepScreenOn = true
    await flushPromises()
    expect(request).toHaveBeenCalledExactlyOnceWith('screen')

    settings.keepScreenOn = false
    await flushPromises()
    expect(sentinels[0]!.release).toHaveBeenCalledOnce()
  })

  it('asks nothing in a browser tab, even with the setting on', async () => {
    installedApp(false)
    useUiSettingsStore().keepScreenOn = true
    mountLock()
    await flushPromises()

    expect(request).not.toHaveBeenCalled()
  })

  it('asks again when the app comes back into view, since the browser let go when it left', async () => {
    useUiSettingsStore().keepScreenOn = true
    mountLock()
    await flushPromises()

    sentinels[0]!.drop()
    setVisibility('hidden')
    await flushPromises()
    expect(request).toHaveBeenCalledOnce()

    setVisibility('visible')
    await flushPromises()
    expect(request).toHaveBeenCalledTimes(2)
  })

  it('does not ask twice while the screen is already held', async () => {
    useUiSettingsStore().keepScreenOn = true
    mountLock()
    await flushPromises()

    setVisibility('visible')
    await flushPromises()

    expect(request).toHaveBeenCalledOnce()
  })

  it('lets go of a lock that arrives after the setting was turned off', async () => {
    let resolve: (sentinel: FakeSentinel) => void = () => {}
    request.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    const settings = useUiSettingsStore()
    settings.keepScreenOn = true
    mountLock()
    await nextTick()

    settings.keepScreenOn = false
    await nextTick()
    const late = new FakeSentinel()
    resolve(late)
    await flushPromises()

    expect(late.release).toHaveBeenCalledOnce()
  })

  it('carries on when the browser refuses, and asks again next time the app is in view', async () => {
    request.mockRejectedValueOnce(new DOMException('Battery saver', 'NotAllowedError'))
    useUiSettingsStore().keepScreenOn = true
    mountLock()
    await flushPromises()

    setVisibility('visible')
    await flushPromises()

    expect(request).toHaveBeenCalledTimes(2)
  })

  it('lets go when the app is torn down', async () => {
    useUiSettingsStore().keepScreenOn = true
    const wrapper = mountLock()
    await flushPromises()

    wrapper.unmount()

    expect(sentinels[0]!.release).toHaveBeenCalledOnce()
  })
})
