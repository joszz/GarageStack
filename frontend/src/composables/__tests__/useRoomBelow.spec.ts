import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { ROOM_BELOW_PROPERTY, useRoomBelow } from '../useRoomBelow'

enableAutoUnmount(afterEach)

// A column that scrolls, as the app's main content does, with the panel inside it.
function setUp() {
  const scroller = document.createElement('div')
  scroller.style.overflowY = 'auto'
  const content = document.createElement('div')
  const panel = document.createElement('aside')
  content.append(panel)
  scroller.append(content)
  document.body.append(scroller)

  let panelTop = 400
  vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue({ bottom: 800 } as DOMRect)
  vi.spyOn(panel, 'getBoundingClientRect').mockImplementation(() => ({ top: panelTop }) as DOMRect)

  const element = ref<HTMLElement | null>(panel)
  const wrapper = mount(
    defineComponent({
      setup() {
        useRoomBelow(element)
        return () => h('div')
      },
    }),
  )
  return {
    scroller,
    panel,
    element,
    wrapper,
    moveTo: (top: number) => (panelTop = top),
  }
}

const room = (el: HTMLElement) => el.style.getPropertyValue(ROOM_BELOW_PROPERTY)

describe('useRoomBelow', () => {
  beforeEach(() => {
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      callback(0)
      return 1
    })
    vi.stubGlobal('cancelAnimationFrame', () => {})
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    document.body.innerHTML = ''
  })

  it('sets the room from the top of the element to the bottom of the column it scrolls in', async () => {
    const { panel } = setUp()
    await nextTick()

    expect(room(panel)).toBe('400px')
  })

  it('measures again as the column scrolls', async () => {
    const { panel, scroller, moveTo } = setUp()
    await nextTick()

    moveTo(16)
    scroller.dispatchEvent(new Event('scroll'))

    expect(room(panel)).toBe('784px')
  })

  it('measures again as the window is resized', async () => {
    const { panel, moveTo } = setUp()
    await nextTick()

    moveTo(300)
    window.dispatchEvent(new Event('resize'))

    expect(room(panel)).toBe('500px')
  })

  it('never sets a negative room for an element below the column', async () => {
    const { panel, moveTo, scroller } = setUp()
    await nextTick()

    moveTo(900)
    scroller.dispatchEvent(new Event('scroll'))

    expect(room(panel)).toBe('0px')
  })

  it('stops listening once the element is gone, and once unmounted', async () => {
    const { panel, scroller, element, wrapper, moveTo } = setUp()
    await nextTick()

    element.value = null
    await nextTick()
    moveTo(100)
    scroller.dispatchEvent(new Event('scroll'))
    expect(room(panel)).toBe('400px')

    element.value = panel
    await nextTick()
    expect(room(panel)).toBe('700px')

    wrapper.unmount()
    moveTo(200)
    scroller.dispatchEvent(new Event('scroll'))
    window.dispatchEvent(new Event('resize'))
    expect(room(panel)).toBe('700px')
  })
})
