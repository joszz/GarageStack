import { describe, it, expect, vi, afterEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useMediaQuery } from '../useMediaQuery'

afterEach(() => {
  vi.unstubAllGlobals()
})

type Listener = (event: { matches: boolean }) => void

function fakeMediaList(initial: boolean) {
  let listener: Listener | null = null
  const list = {
    matches: initial,
    addEventListener: vi.fn<(type: string, l: Listener) => void>((_, l) => {
      listener = l
    }),
    removeEventListener: vi.fn<(type: string, l: Listener) => void>(),
  }
  vi.stubGlobal('matchMedia', () => list)
  return { list, change: (matches: boolean) => listener?.({ matches }) }
}

function mountWith(query: string) {
  let matches: ReturnType<typeof useMediaQuery> | undefined
  const wrapper = mount(
    defineComponent({
      setup() {
        matches = useMediaQuery(query)
        return () => h('div')
      },
    }),
  )
  return { wrapper, matches: matches! }
}

describe('useMediaQuery', () => {
  it('starts as the query matches now, and follows it as it changes', async () => {
    const media = fakeMediaList(true)
    const { matches } = mountWith('(width >= 1280px)')

    expect(matches.value).toBe(true)
    media.change(false)
    await nextTick()

    expect(matches.value).toBe(false)
  })

  it('stops listening once the component is gone', () => {
    const media = fakeMediaList(false)
    const { wrapper } = mountWith('(width >= 1280px)')

    wrapper.unmount()

    expect(media.list.removeEventListener).toHaveBeenCalled()
  })
})
