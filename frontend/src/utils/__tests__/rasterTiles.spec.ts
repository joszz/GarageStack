import { afterEach, describe, expect, it, vi } from 'vitest'
import { forgetMapTiles, MAP_TILE_CACHE } from '../rasterTiles'

describe('forgetMapTiles', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('deletes the tile cache', async () => {
    const remove = vi.fn<(name: string) => Promise<boolean>>().mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: remove })

    await forgetMapTiles()

    expect(remove).toHaveBeenCalledExactlyOnceWith(MAP_TILE_CACHE)
  })

  // Cache Storage only exists on a secure page; plain HTTP on a LAN address has none.
  it('does nothing without Cache Storage', async () => {
    vi.stubGlobal('caches', undefined)

    await expect(forgetMapTiles()).resolves.toBeUndefined()
  })

  it('carries on when the browser refuses', async () => {
    vi.stubGlobal('caches', {
      delete: vi.fn<(name: string) => Promise<boolean>>().mockRejectedValue(new Error('denied')),
    })

    await expect(forgetMapTiles()).resolves.toBeUndefined()
  })
})
