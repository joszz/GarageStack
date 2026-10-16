import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp } from 'vue'
import { installErrorReporting, reportError, resetErrorReporting } from '../errorReporting'

const fetchMock = vi.fn<typeof fetch>()

function sentReports() {
  return fetchMock.mock.calls.map(([, init]) => JSON.parse(String(init?.body)))
}

describe('errorReporting', () => {
  beforeEach(() => {
    resetErrorReporting()
    fetchMock.mockReset().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('sends the message, stack, page and source to the API', () => {
    const error = new TypeError('x is undefined')

    reportError(error, 'Vue: render function')

    expect(fetchMock).toHaveBeenCalledOnce()
    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('/api/client-errors')
    expect(init?.method).toBe('POST')
    expect(sentReports()[0]).toEqual({
      message: 'TypeError: x is undefined',
      stack: error.stack,
      page: window.location.pathname,
      source: 'Vue: render function',
    })
  })

  it('reports each distinct message once per page load', () => {
    for (let tick = 0; tick < 5; tick++) reportError(new Error('loop'))
    reportError(new Error('another'))

    expect(sentReports().map((r) => r.message)).toEqual(['Error: loop', 'Error: another'])
  })

  it('stops after ten reports', () => {
    for (let n = 0; n < 25; n++) reportError(new Error(`error ${n}`))

    expect(fetchMock).toHaveBeenCalledTimes(10)
  })

  it('leaves out browser noise nobody can act on', () => {
    reportError('ResizeObserver loop completed with undelivered notifications.')

    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('never throws when the report cannot be delivered', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    expect(() => reportError(new Error('offline'))).not.toThrow()
    await Promise.resolve()
  })

  it('reports what Vue catches while signed in, and still logs it to the console', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    const app = createApp({})
    let signedIn = false
    installErrorReporting(app, () => signedIn)
    const handler = app.config.errorHandler!

    handler(new Error('before sign-in'), null, 'setup function')
    signedIn = true
    handler(new Error('after sign-in'), null, 'setup function')

    expect(sentReports().map((r) => r.message)).toEqual(['Error: after sign-in'])
    expect(sentReports()[0].source).toBe('Vue: setup function')
    expect(consoleError).toHaveBeenCalledTimes(2)
    consoleError.mockRestore()
  })
})
