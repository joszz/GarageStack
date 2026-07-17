import { describe, it, expect } from 'vitest'
import { ref } from 'vue'
import { useBooleanStatusList } from '../useBooleanStatusList'

describe('useBooleanStatusList', () => {
  it('drops candidates whose open value is null', () => {
    const list = useBooleanStatusList(() => [
      { key: 'a', label: 'A', open: true },
      { key: 'b', label: 'B', open: null },
      { key: 'c', label: 'C', open: false },
    ])

    expect(list.value).toEqual([
      { key: 'a', label: 'A', open: true },
      { key: 'c', label: 'C', open: false },
    ])
  })

  it('returns an empty array when everything is null', () => {
    const list = useBooleanStatusList(() => [
      { key: 'a', label: 'A', open: null },
      { key: 'b', label: 'B', open: null },
    ])

    expect(list.value).toEqual([])
  })

  it('preserves extra fields beyond key/label/open', () => {
    const list = useBooleanStatusList(() => [
      { key: 'a', label: 'A', icon: 'door-open', open: true },
    ])

    expect(list.value).toEqual([{ key: 'a', label: 'A', icon: 'door-open', open: true }])
  })

  it('is reactive to changes in the source candidates', () => {
    // Mirrors real usage: candidates() closes over a Vue prop/ref, so the returned computed
    // re-evaluates whenever that reactive dependency changes.
    const open = ref<boolean | null>(null)
    const list = useBooleanStatusList(() => [{ key: 'a', label: 'A', open: open.value }])

    expect(list.value).toEqual([])

    open.value = true
    expect(list.value).toEqual([{ key: 'a', label: 'A', open: true }])
  })
})
