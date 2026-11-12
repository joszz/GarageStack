import { onMounted, onUnmounted, type Ref } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'

/**
 * Asks before unsaved work is dropped: on leaving the view (a link, the menu, the back button),
 * on the same view opening something else, and on a reload or a closed tab. The browser shows its
 * own wording for the last one; it allows no text of ours there.
 */
export function useLeaveGuard(dirty: Ref<boolean>, message: () => string) {
  const confirmLeave = () => !dirty.value || window.confirm(message())

  onBeforeRouteLeave(confirmLeave)
  onBeforeRouteUpdate(confirmLeave)

  function onBeforeUnload(event: BeforeUnloadEvent) {
    if (dirty.value) event.preventDefault()
  }

  onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
  onUnmounted(() => window.removeEventListener('beforeunload', onBeforeUnload))
}
