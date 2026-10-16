import { onMounted, onUnmounted } from 'vue'

/**
 * Calls `onEscape` when Escape is pressed while `isOpen()` is true: the keyboard way out of a
 * dialog or panel, which every one of them owes a keyboard user.
 */
export function useEscapeKey(isOpen: () => boolean, onEscape: () => void): void {
  function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape' && isOpen()) onEscape()
  }
  onMounted(() => document.addEventListener('keydown', onKey))
  onUnmounted(() => document.removeEventListener('keydown', onKey))
}
