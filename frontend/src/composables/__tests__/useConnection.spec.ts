import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { setConnectionListener, type ConnectionListener } from '@/services/apiCore'
import {
  connectionPageChanged,
  resetConnection,
  SERVER_RETRY_MS,
  useConnection,
  watchConnection,
} from '../useConnection'

vi.mock('@/services/apiCore', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/apiCore')>()),
  setConnectionListener: vi.fn<(listener: ConnectionListener | null) => void>(),
}))

/** What the API layer tells the connection, once watching has started. */
function requests(): ConnectionListener {
  watchConnection()
  return vi.mocked(setConnectionListener).mock.lastCall![0]!
}

describe('useConnection', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)
    resetConnection()
  })

  afterEach(() => {
    resetConnection()
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('starts reachable when the browser has a network', () => {
    expect(useConnection().reachable).toBe(true)
  })

  it('follows the browser going offline and back', async () => {
    requests()
    const connection = useConnection()

    window.dispatchEvent(new Event('offline'))
    await nextTick()
    expect(connection.reachable).toBe(false)

    window.dispatchEvent(new Event('online'))
    await nextTick()
    expect(connection.reachable).toBe(true)
  })

  it('is unreachable once a request finds no server, and reachable once one answers', () => {
    const api = requests()
    const connection = useConnection()

    api.unreachable()
    expect(connection.reachable).toBe(false)

    api.answered()
    expect(connection.reachable).toBe(true)
  })

  it('keeps the oldest saved answer the page shows, until the page changes', () => {
    const api = requests()
    const connection = useConnection()

    api.answeredFromSave('2026-11-17T08:00:00Z')
    api.answeredFromSave('2026-11-15T08:00:00Z')
    api.answeredFromSave('2026-11-16T08:00:00Z')
    expect(connection.reachable).toBe(false)
    expect(connection.savedAt).toBe('2026-11-15T08:00:00Z')

    connectionPageChanged()
    expect(connection.savedAt).toBeNull()
  })

  it('asks the server again on its own while it is away, and stops once it answers', async () => {
    const fetchSpy = vi
      .fn<typeof fetch>()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValue(new Response(null, { status: 401 }))
    vi.stubGlobal('fetch', fetchSpy)
    requests().unreachable()
    const connection = useConnection()

    await vi.advanceTimersByTimeAsync(SERVER_RETRY_MS)
    expect(connection.reachable).toBe(false)

    // Nobody signed in is still an answer from the server.
    await vi.advanceTimersByTimeAsync(SERVER_RETRY_MS)
    expect(connection.reachable).toBe(true)

    await vi.advanceTimersByTimeAsync(SERVER_RETRY_MS * 3)
    expect(fetchSpy).toHaveBeenCalledTimes(2)
    expect(String(fetchSpy.mock.calls[0]![0])).toBe('/api/auth/me')
  })

  it('takes a proxy saying the server is down as still away', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 502 })),
    )
    requests().unreachable()

    await vi.advanceTimersByTimeAsync(SERVER_RETRY_MS)

    expect(useConnection().reachable).toBe(false)
  })
})
