import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { setConnectionListener, type ConnectionListener } from '@/services/apiCore'
import { resetConnection, watchConnection } from '../useConnection'
import { useReconnect } from '../useReconnect'

vi.mock('@/services/apiCore', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/apiCore')>()),
  setConnectionListener: vi.fn<(listener: ConnectionListener | null) => void>(),
}))

function mountWith(reload: () => void) {
  return mount(
    defineComponent({
      setup() {
        useReconnect(reload)
        return () => h('div')
      },
    }),
  )
}

describe('useReconnect', () => {
  let api: ConnectionListener

  beforeEach(() => {
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)
    resetConnection()
    watchConnection()
    api = vi.mocked(setConnectionListener).mock.lastCall![0]!
  })

  afterEach(() => {
    resetConnection()
    vi.restoreAllMocks()
  })

  it('reloads each time the server can be reached again', async () => {
    const reload = vi.fn<() => void>()
    mountWith(reload)

    api.unreachable()
    await nextTick()
    expect(reload).not.toHaveBeenCalled()

    api.answered()
    await nextTick()
    expect(reload).toHaveBeenCalledOnce()
  })

  it('does nothing while the server keeps answering', async () => {
    const reload = vi.fn<() => void>()
    mountWith(reload)

    api.answered()
    await nextTick()

    expect(reload).not.toHaveBeenCalled()
  })

  it('stops with the component', async () => {
    const reload = vi.fn<() => void>()
    const wrapper = mountWith(reload)
    api.unreachable()
    await nextTick()

    wrapper.unmount()
    api.answered()
    await nextTick()

    expect(reload).not.toHaveBeenCalled()
  })
})
