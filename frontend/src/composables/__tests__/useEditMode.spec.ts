import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, watch, type Component } from 'vue'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { EditableCardGrid, useEditMode } from '../useEditMode'

// The editor's code arrives when the test lets it, as it would over a slow connection.
const editorCode = vi.hoisted(() => {
  let arrive = () => {}
  const arrived = new Promise<void>((resolve) => (arrive = resolve))
  return { arrived, arrive: () => arrive() }
})

vi.mock('@/components/EditableCardGrid.vue', async () => {
  await editorCode.arrived
  return { default: { template: '<div class="card-grid" />' } }
})

enableAutoUnmount(afterEach)

// The grid without its cards, which these tests do not need.
const grid = EditableCardGrid as Component

// A page with its cards, or the editor in their place, and what it showed each time edit mode
// changed.
function page() {
  const shown: string[] = []
  let edit!: ReturnType<typeof useEditMode>
  const wrapper = mount(
    defineComponent({
      setup() {
        edit = useEditMode()
        return () => (edit.editMode.value ? h(grid) : h('div', { class: 'cards' }, 'cards'))
      },
    }),
  )
  watch(edit.editMode, () => shown.push(wrapper.html()), { flush: 'post' })
  return { wrapper, edit, shown }
}

describe('useEditMode', () => {
  // These run in order: the editor's code is loaded once per page load, and so once per file.

  it('keeps the cards until the editor can take over, and stays closed when closed meanwhile', async () => {
    const { wrapper, edit } = page()

    edit.editMode.value = true
    await flushPromises()
    expect(edit.editMode.value).toBe(false)
    expect(wrapper.find('.cards').exists()).toBe(true)

    edit.editMode.value = false
    editorCode.arrive()
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(edit.editMode.value).toBe(false)
    expect(wrapper.find('.cards').exists()).toBe(true)
  })

  it('shows the editor in the same moment it opens, never an empty page that scrolls back up', async () => {
    const { edit, shown } = page()

    edit.editMode.value = true
    await vi.dynamicImportSettled()
    await flushPromises()

    expect(edit.editMode.value).toBe(true)
    expect(shown).toEqual(['<div class="card-grid"></div>'])
  })

  it('starts loading the editor as a finger goes down on a card', async () => {
    vi.resetModules()
    const imported = vi.fn<() => void>()
    vi.doMock('@/components/EditableCardGrid.vue', () => {
      imported()
      return { default: { template: '<div class="card-grid" />' } }
    })
    const { useEditMode: freshEditMode } = await import('../useEditMode')
    const wrapper = mount(
      defineComponent({
        setup() {
          const { cardLongPress } = freshEditMode()
          return () => h('div', cardLongPress, [h('div', { class: 'card' })])
        },
      }),
    )

    wrapper
      .find('.card')
      .element.dispatchEvent(
        new PointerEvent('pointerdown', { bubbles: true, isPrimary: true, pointerType: 'touch' }),
      )
    await vi.dynamicImportSettled()

    expect(imported).toHaveBeenCalledOnce()
  })
})
