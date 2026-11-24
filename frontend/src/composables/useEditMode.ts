import { computed, defineComponent, h, ref, type Component } from 'vue'
import type EditableCardGridComponent from '@/components/EditableCardGrid.vue'
import { useLongPress } from './useLongPress'

type Grid = typeof EditableCardGridComponent

// Only edit mode drags cards around, and the drag-and-drop library is the size of every card on a
// page together, so it loads when the layout editor is about to open rather than with the page.
let loadedGrid: Grid | undefined
let loadingGrid: Promise<void> | undefined

/** Loads the editor's grid once; after a failed load the next call tries again. */
function loadGrid(): Promise<void> {
  loadingGrid ??= import('@/components/EditableCardGrid.vue').then(
    (module) => {
      loadedGrid = module.default
    },
    (error: unknown) => {
      loadingGrid = undefined
      throw error
    },
  )
  return loadingGrid
}

/**
 * The drag-and-drop grid of the layout editor, which useEditMode loads before it opens. Not
 * defineAsyncComponent: that renders nothing on its first mount, even with the code already
 * loaded, and the page, shorter for that moment, scrolls back to the top. The cast restores the
 * grid's generic item type, which a wrapper does not carry over.
 */
export const EditableCardGrid = defineComponent({
  name: 'LazyEditableCardGrid',
  inheritAttrs: false,
  setup(_, { attrs, slots }) {
    return () => (loadedGrid ? h(loadedGrid as Component, attrs, slots) : null)
  },
}) as unknown as Grid

/**
 * The layout editor of a page of cards (dashboard, statistics): whether it is open, for the
 * header's EditLayoutButton (`v-model`), and the bindings that open it when a finger rests on any
 * card, as on a phone. Bind `cardLongPress` (`v-bind`) to each grid of cards outside edit mode,
 * together with the `long-press-cards` class, and render the editor with {@link EditableCardGrid}.
 * Opening waits for the editor's code, so the cards stay in place until it can take over from them.
 */
export function useEditMode() {
  const open = ref(false)
  // What was asked last: closing while the editor still loads keeps it closed.
  let wanted = false

  const editMode = computed({
    get: () => open.value,
    set(value: boolean) {
      wanted = value
      if (!value) open.value = false
      else void loadGrid().then(() => (open.value = wanted))
    },
  })

  const cardLongPress = useLongPress(() => (editMode.value = true), {
    // The finger rests long enough to load the editor before the press counts.
    onPressStart: () => void loadGrid().catch(() => {}),
  })
  return { editMode, cardLongPress }
}
