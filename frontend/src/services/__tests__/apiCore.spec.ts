import { describe, it, expect, afterEach, vi } from 'vitest'
import { ApiError, request, requestJson, send } from '@/services/apiCore'

function makeResponse(status: number, body?: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  }
}

type FetchSpy = (...args: Parameters<typeof fetch>) => Promise<ReturnType<typeof makeResponse>>

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
