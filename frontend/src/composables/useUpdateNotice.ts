import { onMounted, shallowRef } from 'vue'
import { updatesApi } from '@/services/updatesApi'

export interface UpdateNotice {
  version: string
  releaseUrl: string
}

/**
 * The newer release to point at, asked once when the footer mounts. The API checks GitHub once a
 * day, so asking again while the page stays open would only repeat its answer. Failing to ask
 * shows nothing: an update notice is never worth an error.
 */
export function useUpdateNotice() {
  const notice = shallowRef<UpdateNotice | null>(null)

  onMounted(async () => {
    try {
      const info = await updatesApi.get()
      // The API builds the link itself; anything not on GitHub is not shown regardless.
      if (
        info.updateAvailable &&
        info.latestVersion &&
        info.releaseUrl?.startsWith('https://github.com/')
      )
        notice.value = { version: info.latestVersion, releaseUrl: info.releaseUrl }
    } catch {
      // Offline, or an older API without the endpoint.
    }
  })

  return { notice }
}
