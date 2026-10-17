import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { CONFIRM_WINDOW_MS, useTwoTapConfirm } from '../useTwoTapConfirm'

function withConfirm() {
  const result: { confirm: ReturnType<typeof useTwoTapConfirm<number>> | null } = { confirm: null }
  const wrapper = mount(
    defineComponent({
      setup() {
        result.confirm = useTwoTapConfirm<number>()
        return () => h('div')
      },
    }),
  )
  return { confirm: result.confirm!, wrapper }
}

describe('useTwoTapConfirm', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('arms on the first tap and goes ahead on the second', () => {
    const { confirm } = withConfirm()

    expect(confirm.tap(7)).toBe(false)
    expect(confirm.armed.value).toBe(7)
    expect(confirm.tap(7)).toBe(true)
    expect(confirm.armed.value).toBeNull()
  })

  it('lapses when the second tap does not come in time', () => {
    const { confirm } = withConfirm()

    confirm.tap(7)
    vi.advanceTimersByTime(CONFIRM_WINDOW_MS)

    expect(confirm.armed.value).toBeNull()
    expect(confirm.tap(7)).toBe(false)
  })

  it('arming another item disarms the first', () => {
    const { confirm } = withConfirm()

    confirm.tap(7)
    expect(confirm.tap(8)).toBe(false)

    expect(confirm.armed.value).toBe(8)
    expect(confirm.tap(7)).toBe(false)
  })

  it('stops its timer with the component', () => {
    const { confirm, wrapper } = withConfirm()
    confirm.tap(7)

    wrapper.unmount()

    expect(vi.getTimerCount()).toBe(0)
  })
})
