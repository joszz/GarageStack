import { onUnmounted, readonly, ref, type Ref } from 'vue'

/**
 * Whether the browser says it has a network connection, kept up to date as it comes and goes.
 * `onReconnect` runs each time it comes back, for a page to ask for what it missed. A browser can
 * claim a connection that never answers, so true only means the page may try; false is certain.
 */
export function useOnline(onReconnect?: () => void): Readonly<Ref<boolean>> {
  const online = ref(navigator.onLine !== false)

  const goOnline = () => {
    online.value = true
    onReconnect?.()
  }
  const goOffline = () => {
    online.value = false
  }
  window.addEventListener('online', goOnline)
  window.addEventListener('offline', goOffline)
  onUnmounted(() => {
    window.removeEventListener('online', goOnline)
    window.removeEventListener('offline', goOffline)
  })

  return readonly(online)
}
