import { watch } from 'vue'
import { useConnection } from '@/composables/useConnection'

/**
 * Runs `reload` each time the server can be reached again, for a page to replace what the
 * service worker saved with the server's own answers. Stops with the component.
 */
export function useReconnect(reload: () => unknown): void {
  const connection = useConnection()
  watch(
    () => connection.reachable,
    (reachable, wasReachable) => {
      if (reachable && !wasReachable) void reload()
    },
  )
}
