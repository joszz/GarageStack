import { vi } from 'vitest'
import { resetConnection } from '../useConnection'

/**
 * Takes the browser off the network (or puts it back) as the app's connection state sees it, for
 * the specs of what hides without the server. Not a spec itself, so importing it runs nothing.
 * Undo it with vi.restoreAllMocks() and resetConnection().
 */
export function setBrowserOnline(online: boolean): void {
  vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(online)
  resetConnection()
}
