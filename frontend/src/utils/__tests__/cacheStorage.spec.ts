import { afterEach, describe, expect, it, vi } from 'vitest'
import { forgetCaches } from '../cacheStorage'

describe('forgetCaches', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('deletes every cache it names', async () => {
    const remove = vi.fn<(name: string) => Promise<boolean>>().mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: remove })

    await forgetCaches('garagestack-map-tiles', 'garagestack-api')

    expect(remove.mock.calls).toEqual([['garagestack-map-tiles'], ['garagestack-api']])
  })

  // Cache Storage only exists on a secure page; plain HTTP on a LAN address has none.
  it('does nothing without Cache Storage', async () => {
    vi.stubGlobal('caches', undefined)

    await expect(forgetCaches('garagestack-api')).resolves.toBeUndefined()
  })

  it('carries on with the others when the browser refuses one', async () => {
    const remove = vi
      .fn<(name: string) => Promise<boolean>>()
      .mockRejectedValueOnce(new Error('denied'))
      .mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: remove })

    await expect(forgetCaches('a', 'b')).resolves.toBeUndefined()
    expect(remove).toHaveBeenCalledTimes(2)
  })
})
