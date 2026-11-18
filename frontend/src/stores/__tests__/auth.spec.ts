import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { authApi } from '@/services/authApi'
import { ApiError } from '@/services/apiCore'
import { readLastKnownState, saveLastKnownState } from '@/utils/lastKnownState'
import { readLastVin, rememberVin } from '@/utils/lastVin'
import type { MeResponse, LoginResponse, AuthConfigResponse } from '@/services/authApi'
import { useAuthStore } from '@/stores/auth'

vi.mock('@/services/authApi', () => ({
  authApi: {
    me: vi.fn<() => Promise<MeResponse>>(),
    login: vi.fn<() => Promise<LoginResponse>>(),
    logout: vi.fn<() => Promise<void>>(),
    config: vi.fn<() => Promise<AuthConfigResponse>>(),
  },
}))

const AUTH_USERNAME_KEY = 'garagestack-auth-username'
const AUTH_EXPIRES_KEY = 'garagestack-auth-expires'

function futureExpiry(offsetMs = 60 * 60 * 1000) {
  return new Date(Date.now() + offsetMs).toISOString()
}

function pastExpiry() {
  return new Date(Date.now() - 1000).toISOString()
}

describe('useAuthStore', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.mocked(authApi.me).mockReset()
    vi.mocked(authApi.login).mockReset()
    vi.mocked(authApi.logout).mockReset()
    vi.mocked(authApi.config).mockReset()
  })

  afterEach(() => {
    localStorage.clear()
  })

  // ── isAuthenticated ───────────────────────────────────────────────────────

  it('isAuthenticated is false when username is empty', () => {
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  it('isAuthenticated is false when expiry is in the past', () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, pastExpiry())

    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  it('isAuthenticated is true when username is set and expiry is in the future', () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())

    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(true)
  })

  it('isAuthenticated is false when username is set but expiry is empty', () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')

    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  // ── login() ───────────────────────────────────────────────────────────────

  it('login() sets username and persists to localStorage', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'alice', expiresAtUtc: expiry })

    const store = useAuthStore()
    await store.login('alice', 'secret')

    expect(store.username).toBe('alice')
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBe('alice')
  })

  it('login() makes isAuthenticated true after success', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'alice', expiresAtUtc: expiry })

    const store = useAuthStore()
    await store.login('alice', 'secret')

    expect(store.isAuthenticated).toBe(true)
  })

  it('login() passes credentials to the API', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'bob', expiresAtUtc: expiry })

    const store = useAuthStore()
    await store.login('bob', 'password', true)

    expect(vi.mocked(authApi.login)).toHaveBeenCalledWith('bob', 'password', true)
  })

  // ── logout() ──────────────────────────────────────────────────────────────

  it('logout() clears username and isAuthenticated', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(true)

    await store.logout()

    expect(store.username).toBe('')
    expect(store.isAuthenticated).toBe(false)
  })

  it('logout() removes entries from localStorage', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const store = useAuthStore()
    await store.logout()

    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBeNull()
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  it("logout() forgets the car's VIN, so the next account does not start by asking for it", async () => {
    rememberVin('FAKEVN00000000001')
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    await useAuthStore().logout()

    expect(readLastVin()).toBeNull()
  })

  it('logout() calls the API', async () => {
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const store = useAuthStore()
    await store.logout()

    expect(vi.mocked(authApi.logout)).toHaveBeenCalledOnce()
  })

  // ── verifySession() ───────────────────────────────────────────────────────

  it('verifySession() on success updates username and expiry in localStorage', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.me).mockResolvedValue({ username: 'charlie', expiresAtUtc: expiry })

    const store = useAuthStore()
    await store.verifySession()

    expect(store.username).toBe('charlie')
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBe('charlie')
  })

  it('verifySession() on a refused session clears it from the store and localStorage', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.me).mockRejectedValue(new ApiError(401, '/api/auth/me'))

    const store = useAuthStore()
    await store.verifySession()

    expect(store.username).toBe('')
    expect(store.isAuthenticated).toBe(false)
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBeNull()
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  it('forgets the map tiles kept for offline use when the session ends', async () => {
    const deleteCache = vi.fn<(name: string) => Promise<boolean>>().mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: deleteCache })
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.logout).mockResolvedValue()

    await useAuthStore().logout()

    expect(deleteCache).toHaveBeenCalledWith('garagestack-map-tiles')
    vi.unstubAllGlobals()
  })

  it('verifySession() forgets the car saved for offline use when the session is refused', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    saveLastKnownState({ vehicles: [], status: null, config: {}, latestTrip: null })
    vi.mocked(authApi.me).mockRejectedValue(new ApiError(401, '/api/auth/me'))

    await useAuthStore().verifySession()

    expect(readLastKnownState()).toBeNull()
  })

  it.each([
    ['without a connection', new TypeError('Failed to fetch')],
    ['when the check times out', new DOMException('signal timed out', 'TimeoutError')],
    ['when the server is down', new ApiError(502, '/api/auth/me')],
  ])('verifySession() keeps the stored session %s', async (_, failure) => {
    const expiry = futureExpiry()
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, expiry)
    saveLastKnownState({ vehicles: [], status: null, config: {}, latestTrip: null })
    vi.mocked(authApi.me).mockRejectedValue(failure)

    const store = useAuthStore()
    await store.verifySession()

    expect(store.isAuthenticated).toBe(true)
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBe(expiry)
    expect(readLastKnownState()).not.toBeNull()
  })

  it('ensureVerified() asks again on the next navigation after the server could not be reached', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.me)
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce({ username: 'testuser', expiresAtUtc: futureExpiry() })

    const store = useAuthStore()
    await store.ensureVerified()
    await store.ensureVerified()

    expect(authApi.me).toHaveBeenCalledTimes(2)
  })

  it('verifySession() gives up on a stored session check that never answers', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.me).mockResolvedValue({ username: 'testuser', expiresAtUtc: futureExpiry() })

    await useAuthStore().verifySession()

    expect(vi.mocked(authApi.me).mock.calls[0]![0]).toBeInstanceOf(AbortSignal)
  })

  it('verifySession() waits as long as it takes for a visitor without a session', async () => {
    vi.mocked(authApi.me).mockRejectedValue(new ApiError(401, '/api/auth/me'))

    await useAuthStore().verifySession()

    expect(vi.mocked(authApi.me).mock.calls[0]![0]).toBeUndefined()
  })

  it('verifySession() does not authenticate when API returns null expiresAtUtc', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ username: 'dave', expiresAtUtc: null })

    const store = useAuthStore()
    await store.verifySession()

    // Without an expiry there's no valid session, so username must not be set either -
    // otherwise the store would be in a half-authenticated state (username set, not authenticated).
    expect(store.username).toBe('')
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  // ── ensureVerified() ──────────────────────────────────────────────────────

  it('ensureVerified() calls verifySession only once across multiple calls', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ username: 'eve', expiresAtUtc: futureExpiry() })

    const store = useAuthStore()
    await Promise.all([store.ensureVerified(), store.ensureVerified(), store.ensureVerified()])

    expect(vi.mocked(authApi.me)).toHaveBeenCalledOnce()
  })

  // ── ensureConfig() ────────────────────────────────────────────────────────

  it('ensureConfig() exposes the sign-in methods the server reports', async () => {
    const serverConfig = {
      passwordLoginEnabled: false,
      oidcEnabled: true,
      oidcProviderName: 'Authentik',
      oidcAutoLogin: true,
    }
    vi.mocked(authApi.config).mockResolvedValue(serverConfig)

    const store = useAuthStore()
    const result = await store.ensureConfig()

    expect(result).toEqual(serverConfig)
    expect(store.config).toEqual(serverConfig)
  })

  it('ensureConfig() fetches only once across multiple calls', async () => {
    vi.mocked(authApi.config).mockResolvedValue({
      passwordLoginEnabled: true,
      oidcEnabled: false,
      oidcProviderName: null,
      oidcAutoLogin: false,
    })

    const store = useAuthStore()
    await Promise.all([store.ensureConfig(), store.ensureConfig()])

    expect(vi.mocked(authApi.config)).toHaveBeenCalledOnce()
  })

  it('ensureConfig() returns null when the server cannot be reached', async () => {
    vi.mocked(authApi.config).mockRejectedValue(new Error('offline'))

    const store = useAuthStore()

    expect(await store.ensureConfig()).toBeNull()
    expect(store.config).toBeNull()
  })
})
