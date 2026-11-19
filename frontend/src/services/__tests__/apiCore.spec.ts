import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import {
  ApiError,
  clearUnauthorizedState,
  request,
  requestJson,
  send,
  setConnectionListener,
  setUnauthorizedHandler,
  type ConnectionListener,
} from '@/services/apiCore'
import { SAVED_AT_HEADER } from '@/utils/savedApi'
import { makeResponse, type FetchSpy } from './fetchStub'

describe('apiCore JSON helpers', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('requestJson sends a JSON body with the content type and parses the reply', async () => {
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue(makeResponse(200, { ok: true }))
    vi.stubGlobal('fetch', fetchSpy)

    const result = await requestJson<{ ok: boolean }>('/api/x', 'PUT', { a: 1 })

    const [url, options] = fetchSpy.mock.calls[0]!
    expect(url).toBe('/api/x')
    expect(options!.method).toBe('PUT')
    expect(options!.credentials).toBe('include')
    expect((options!.headers as Record<string, string>)['Content-Type']).toBe('application/json')
    expect(JSON.parse(options!.body as string)).toEqual({ a: 1 })
    expect(result).toEqual({ ok: true })
  })

  it('send omits the body when none is given and never reads the response body', async () => {
    const json = vi.fn<() => Promise<unknown>>()
    const fetchSpy = vi.fn<FetchSpy>().mockResolvedValue({ ok: true, status: 200, json })
    vi.stubGlobal('fetch', fetchSpy)

    await send('/api/logout', 'POST')

    const [, options] = fetchSpy.mock.calls[0]!
    expect(options!.method).toBe('POST')
    expect(options!.body).toBeUndefined()
    expect(json).not.toHaveBeenCalled()
  })
})

describe('apiCore errors', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  async function failure(response: unknown): Promise<ApiError> {
    vi.stubGlobal('fetch', vi.fn<() => Promise<unknown>>().mockResolvedValue(response))
    const error = await request('/api/x').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    return error as ApiError
  }

  it('names the status in the message of a failed request', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(500)))

    await expect(request('/api/x')).rejects.toThrow('API error 500')
  })

  it('keeps the code and detail of a problem answer', async () => {
    const error = await failure(
      makeResponse(400, {
        status: 400,
        code: 'maintenance.nameRequired',
        detail: 'Name is required',
      }),
    )

    expect(error.status).toBe(400)
    expect(error.code).toBe('maintenance.nameRequired')
    expect(error.detail).toBe('Name is required')
  })

  it('has no code when the answer is not a problem body', async () => {
    const error = await failure({
      ok: false,
      status: 502,
      json: () => Promise.reject(new SyntaxError('Unexpected token <')),
    })

    expect(error.status).toBe(502)
    expect(error.code).toBeNull()
    expect(error.detail).toBeNull()
  })

  it('reads the problem on a send too', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<() => Promise<unknown>>()
        .mockResolvedValue(makeResponse(403, { code: 'csrf.originNotAllowed' })),
    )

    const error = await send('/api/x', 'POST').catch((e: unknown) => e)

    expect((error as ApiError).code).toBe('csrf.originNotAllowed')
  })
})

describe('apiCore unauthorized handler', () => {
  beforeEach(() => {
    clearUnauthorizedState()
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(401)))
  })

  afterEach(() => {
    setUnauthorizedHandler(null)
    clearUnauthorizedState()
    vi.unstubAllGlobals()
  })

  it('calls the handler once on a 401 response', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    await request('/api/x').catch(() => {})
    expect(handler).toHaveBeenCalledOnce()
  })

  it('does not call the handler a second time when already handling a 401', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    await Promise.all([request('/api/x').catch(() => {}), request('/api/x').catch(() => {})])
    expect(handler).toHaveBeenCalledOnce()
  })

  it('calls the handler again after clearUnauthorizedState', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    await request('/api/x').catch(() => {})
    clearUnauthorizedState()
    await request('/api/x').catch(() => {})
    expect(handler).toHaveBeenCalledTimes(2)
  })

  it('does not call a removed handler', async () => {
    const handler = vi.fn<() => void>()
    setUnauthorizedHandler(handler)
    setUnauthorizedHandler(null)
    await request('/api/x').catch(() => {})
    expect(handler).not.toHaveBeenCalled()
  })
})

function fakeListener() {
  return {
    answered: vi.fn<ConnectionListener['answered']>(),
    answeredFromSave: vi.fn<ConnectionListener['answeredFromSave']>(),
    unreachable: vi.fn<ConnectionListener['unreachable']>(),
  }
}

describe('apiCore connection listener', () => {
  let listener: ReturnType<typeof fakeListener>

  beforeEach(() => {
    listener = fakeListener()
    setConnectionListener(listener)
  })

  afterEach(() => {
    setConnectionListener(null)
    vi.unstubAllGlobals()
  })

  it('hears that the server answered, even with a refusal', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockResolvedValue(makeResponse(404)))

    await expect(request('/api/x')).rejects.toBeInstanceOf(ApiError)

    expect(listener.answered).toHaveBeenCalledOnce()
    expect(listener.unreachable).not.toHaveBeenCalled()
  })

  it("hears when the answer is the service worker's saved copy, and from when", async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<typeof fetch>()
        .mockResolvedValue(
          new Response('[]', { headers: { [SAVED_AT_HEADER]: '2026-11-17T08:00:00.000Z' } }),
        ),
    )

    await expect(request('/api/vehicles')).resolves.toEqual([])

    expect(listener.answeredFromSave).toHaveBeenCalledExactlyOnceWith('2026-11-17T08:00:00.000Z')
    expect(listener.answered).not.toHaveBeenCalled()
  })

  it('hears that there was no answer without a connection', async () => {
    vi.stubGlobal('fetch', vi.fn<FetchSpy>().mockRejectedValue(new TypeError('Failed to fetch')))

    await expect(send('/api/x', 'POST')).rejects.toBeInstanceOf(TypeError)

    expect(listener.unreachable).toHaveBeenCalledOnce()
  })

  it('takes a timeout or a proxy saying the server is down as no answer', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn<FetchSpy>()
        .mockRejectedValueOnce(new DOMException('timed out', 'TimeoutError'))
        .mockResolvedValueOnce(makeResponse(502)),
    )

    await expect(request('/api/x')).rejects.toThrow('timed out')
    await expect(request('/api/x')).rejects.toBeInstanceOf(ApiError)

    expect(listener.unreachable).toHaveBeenCalledTimes(2)
  })

  it('hears nothing of a request the page called off itself', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn<FetchSpy>().mockRejectedValue(new DOMException('aborted', 'AbortError')),
    )

    await expect(request('/api/x')).rejects.toThrow('aborted')

    expect(listener.unreachable).not.toHaveBeenCalled()
    expect(listener.answered).not.toHaveBeenCalled()
  })
})
