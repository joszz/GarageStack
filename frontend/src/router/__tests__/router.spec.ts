import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import type { RouteLocationNormalized } from 'vue-router'
import { authApi, type MeResponse } from '@/services/authApi'
import { settingsApi, type AccountSettings } from '@/services/settingsApi'
import router, { preloadRouteComponents } from '@/router'

vi.mock('@/services/authApi', () => ({
  authApi: { me: vi.fn<() => Promise<MeResponse>>() },
}))

vi.mock('@/services/settingsApi', () => ({
  settingsApi: {
    load: vi.fn<() => Promise<AccountSettings>>(),
    save: vi.fn<() => Promise<void>>(),
  },
}))

const AUTH_USERNAME_KEY = 'garagestack-auth-username'
const AUTH_EXPIRES_KEY = 'garagestack-auth-expires'

const View = defineComponent({ render: () => null })
type ViewLoader = () => Promise<{ default: typeof View }>

function storeSession() {
  localStorage.setItem(AUTH_USERNAME_KEY, 'demo')
  localStorage.setItem(AUTH_EXPIRES_KEY, new Date(Date.now() + 3_600_000).toISOString())
}

/** A session check that stays pending until the test answers it. */
function holdSessionCheck() {
  let answer!: (me: MeResponse) => void
  vi.mocked(authApi.me).mockReturnValue(new Promise<MeResponse>((r) => (answer = r)))
  return () =>
    answer({
      username: 'demo',
      expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
    } as MeResponse)
}

function locationWith(components: Record<string, unknown>): RouteLocationNormalized {
  return { matched: [{ components }] } as unknown as RouteLocationNormalized
}

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  vi.mocked(authApi.me).mockReset()
  vi.mocked(settingsApi.load).mockReset().mockResolvedValue({})
})

afterEach(() => {
  localStorage.clear()
})

describe('preloadRouteComponents', () => {
  it('starts every lazy view of the matched records', () => {
    const load = vi.fn<ViewLoader>(() => Promise.resolve({ default: View }))

    preloadRouteComponents(locationWith({ default: load, aside: load }))

    expect(load).toHaveBeenCalledTimes(2)
  })

  it('leaves components that are already resolved alone', () => {
    expect(() => preloadRouteComponents(locationWith({ default: View }))).not.toThrow()
  })

  it('swallows a failed download instead of raising an unhandled rejection', async () => {
    const load = vi.fn<ViewLoader>(() => Promise.reject(new Error('chunk 404')))

    preloadRouteComponents(locationWith({ default: load }))
    await Promise.resolve()

    expect(load).toHaveBeenCalledOnce()
  })
})

describe('router guard', () => {
  it('downloads the view while the session check is still in flight', async () => {
    storeSession()
    const answerSession = holdSessionCheck()
    const load = vi.fn<ViewLoader>(() => Promise.resolve({ default: View }))
    router.addRoute({ path: '/preload-probe', component: load })

    const navigation = router.push('/preload-probe')

    // The session has not been answered yet, and the view is already on its way.
    await vi.waitFor(() => expect(load).toHaveBeenCalled())
    expect(authApi.me).toHaveBeenCalledOnce()
    answerSession()
    await navigation
    expect(router.currentRoute.value.path).toBe('/preload-probe')
  })

  it("asks for the account's settings alongside the session check, and enters once they are in", async () => {
    storeSession()
    const answerSession = holdSessionCheck()
    let answerSettings!: (settings: AccountSettings) => void
    vi.mocked(settingsApi.load).mockReturnValue(
      new Promise<AccountSettings>((resolve) => (answerSettings = resolve)),
    )
    router.addRoute({ path: '/settings-probe', component: View })

    const navigation = router.push('/settings-probe')

    // Both on their way at once, rather than one after the other.
    await vi.waitFor(() => expect(settingsApi.load).toHaveBeenCalledOnce())
    expect(authApi.me).toHaveBeenCalledOnce()

    answerSession()
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(router.currentRoute.value.path).not.toBe('/settings-probe')

    answerSettings({})
    await navigation
    expect(router.currentRoute.value.path).toBe('/settings-probe')
  })

  it('does not download a protected view for a visitor without a session', async () => {
    vi.mocked(authApi.me).mockRejectedValue(new Error('401'))
    const load = vi.fn<ViewLoader>(() => Promise.resolve({ default: View }))
    router.addRoute({ path: '/protected-probe', component: load })

    await router.push('/protected-probe')

    expect(load).not.toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('login')
  })
})
