import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { WIDE_MEDIA_QUERY } from '@/utils/viewport'
import { useListDetail } from '../useListDetail'

interface Row {
  id: number
}

function stubWindow(wide: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: wide && query === WIDE_MEDIA_QUERY,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

function mountList(wide: boolean, initial: Row[] = [{ id: 1 }, { id: 2 }]) {
  stubWindow(wide)
  const rows = ref<Row[]>(initial)
  let detail: ReturnType<typeof useListDetail<Row, number>> | undefined
  mount(
    defineComponent({
      setup() {
        detail = useListDetail(
          (id: number) => rows.value.find((row) => row.id === id),
          () => rows.value[0],
        )
        return () => h('div')
      },
    }),
  )
  return { rows, detail: detail! }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useListDetail', () => {
  it('shows the first row beside the list on a desktop, until another is picked', () => {
    const { detail } = mountList(true)

    expect(detail.selected.value?.id).toBe(1)
    expect(detail.inDialog.value).toBe(false)

    detail.pick(2)
    expect(detail.selected.value?.id).toBe(2)

    detail.clear()
    expect(detail.selected.value?.id).toBe(1)
  })

  it('shows nothing on a narrower screen until a row is picked, then in a dialog', () => {
    const { detail } = mountList(false)

    expect(detail.selected.value).toBeNull()
    expect(detail.inDialog.value).toBe(false)

    detail.pick(2)
    expect(detail.selected.value?.id).toBe(2)
    expect(detail.inDialog.value).toBe(true)

    detail.clear()
    expect(detail.inDialog.value).toBe(false)
  })

  it('falls back to the first row once the picked one has gone', () => {
    const { rows, detail } = mountList(true)
    detail.pick(2)

    rows.value = [{ id: 3 }]

    expect(detail.selected.value?.id).toBe(3)
  })

  it('shows nothing on a desktop when the list is empty', () => {
    expect(mountList(true, []).detail.selected.value).toBeNull()
  })
})
