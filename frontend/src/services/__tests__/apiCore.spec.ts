import { describe, it, expect, afterEach, vi } from 'vitest'
import { requestJson, send } from '@/services/apiCore'

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
