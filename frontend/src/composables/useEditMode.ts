import { ref } from 'vue'
import { useLongPress } from './useLongPress'

/**
 * The layout editor of a page of cards (dashboard, statistics): whether it is open, for the
 * header's EditLayoutButton (`v-model`), and the bindings that open it when a finger rests on any
 * card, as on a phone. Bind `cardLongPress` (`v-bind`) to each grid of cards outside edit mode,
 * together with the `long-press-cards` class.
 */
export function useEditMode() {
  const editMode = ref(false)
  const cardLongPress = useLongPress(() => (editMode.value = true))
  return { editMode, cardLongPress }
}
