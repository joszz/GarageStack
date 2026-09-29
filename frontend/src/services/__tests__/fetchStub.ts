/**
 * A stand-in for the fetch Response, with only what apiCore reads, shared by the API client
 * specs. Not a spec itself, so importing it does not run anything twice.
 */
export function makeResponse(status: number, body?: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  }
}

export type FetchSpy = (
  ...args: Parameters<typeof fetch>
) => Promise<ReturnType<typeof makeResponse>>
