import { onMounted, onUnmounted, watch } from 'vue'

// When each open layer opened, so Escape closes only the newest: the dialog opened from the
// mobile menu, then the menu on a second press, rather than both at once.
const openedAt = new Map<object, number>()
let sequence = 0

function isNewestOpen(layer: object): boolean {
  let newest: object | null = null
  let latest = -1
  for (const [candidate, at] of openedAt)
    if (at > latest) {
      latest = at
      newest = candidate
    }
  return newest === layer
}

/**
 * Calls `onEscape` when Escape is pressed while `isOpen()` is true and nothing opened after this
 * one is still open: the keyboard way out of a dialog or panel, which every one of them owes a
 * keyboard user, one layer per press.
 */
export function useEscapeKey(isOpen: () => boolean, onEscape: () => void): void {
  const layer = {}
  watch(
    isOpen,
    (open) => {
      if (open) openedAt.set(layer, ++sequence)
      else openedAt.delete(layer)
    },
    { immediate: true },
  )

  // Every open layer hears the same key press. The newest closes; by the time the others are
  // asked, it still counts as open (its watcher runs later), so they leave it be.
  function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape' && isOpen() && isNewestOpen(layer)) onEscape()
  }
  onMounted(() => document.addEventListener('keydown', onKey))
  onUnmounted(() => {
    document.removeEventListener('keydown', onKey)
    openedAt.delete(layer)
  })
}
