/**
 * Deletes the service worker's caches with these names, on sign-out: the map tiles and the saved
 * API answers show where the car has been and its whole history. A page without Cache Storage
 * (plain HTTP on a LAN address) has none to forget, and a cache the browser refuses to delete
 * expires on its own.
 */
export async function forgetCaches(...names: string[]): Promise<void> {
  await Promise.all(
    names.map(async (name) => {
      try {
        await globalThis.caches?.delete(name)
      } catch {
        // Nothing to do: the cache expires on its own.
      }
    }),
  )
}
