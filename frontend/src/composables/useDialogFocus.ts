import { onMounted, onUnmounted, watch, type Ref } from 'vue'

const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

// The dialogs open right now, innermost last: only that one keeps Tab, so a dialog opened from
// another one is not fought over by both.
const openDialogs: Ref<HTMLElement | null>[] = []

function focusablesIn(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))
}

/**
 * Keyboard focus for a modal dialog: on opening, focus moves to `dialog` (which needs
 * tabindex="-1"), Tab and Shift+Tab cycle through its controls instead of the page behind it,
 * and on closing focus returns to whatever had it before, usually the button that opened it.
 */
export function useDialogFocus(dialog: Ref<HTMLElement | null>, isOpen: () => boolean): void {
  let opener: HTMLElement | null = null

  function release() {
    const index = openDialogs.indexOf(dialog)
    if (index === -1) return
    openDialogs.splice(index, 1)
    if (opener?.isConnected) opener.focus()
    opener = null
  }

  function hold() {
    opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
    openDialogs.push(dialog)
    dialog.value?.focus()
  }

  // Post flush, so the dialog is in the DOM by the time it is focused.
  watch(isOpen, (open) => (open ? hold() : release()), { flush: 'post' })

  function onKey(event: KeyboardEvent) {
    const container = dialog.value
    if (event.key !== 'Tab' || !container || openDialogs[openDialogs.length - 1] !== dialog) return
    const items = focusablesIn(container)
    const first = items[0]
    const last = items[items.length - 1]
    const active = document.activeElement
    if (!first || !last) {
      event.preventDefault()
      return
    }
    if (event.shiftKey && (active === first || active === container)) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && active === last) {
      event.preventDefault()
      first.focus()
    } else if (!container.contains(active)) {
      event.preventDefault()
      first.focus()
    }
  }

  onMounted(() => {
    document.addEventListener('keydown', onKey)
    if (isOpen()) hold()
  })
  onUnmounted(() => {
    document.removeEventListener('keydown', onKey)
    release()
  })
}
