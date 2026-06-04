import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { authApi } from '@/services/api'
import type { MeResponse, LoginResponse } from '@/services/api'

vi.mock('@/services/api', () => ({
  authApi: {
    me: vi.fn<() => Promise<MeResponse>>(),
    login: vi.fn<() => Promise<LoginResponse>>(),
    logout: vi.fn<() => Promise<void>>(),
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
  })

  afterEach(() => {
    localStorage.clear()
  })

  // ── isAuthenticated ───────────────────────────────────────────────────────

  it('isAuthenticated is false when username is empty', async () => {
    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  it('isAuthenticated is false when expiry is in the past', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, pastExpiry())

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  it('isAuthenticated is true when username is set and expiry is in the future', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(true)
  })

  it('isAuthenticated is false when username is set but expiry is empty', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
  })

  // ── login() ───────────────────────────────────────────────────────────────

  it('login() sets username and persists to localStorage', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'alice', expiresAtUtc: expiry })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.login('alice', 'secret')

    expect(store.username).toBe('alice')
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBe('alice')
  })

  it('login() makes isAuthenticated true after success', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'alice', expiresAtUtc: expiry })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.login('alice', 'secret')

    expect(store.isAuthenticated).toBe(true)
  })

  it('login() passes credentials to the API', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.login).mockResolvedValue({ username: 'bob', expiresAtUtc: expiry })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.login('bob', 'password', true)

    expect(vi.mocked(authApi.login)).toHaveBeenCalledWith('bob', 'password', true)
  })

  // ── logout() ──────────────────────────────────────────────────────────────

  it('logout() clears username and isAuthenticated', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const { useAuthStore } = await import('@/stores/auth')
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

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.logout()

    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBeNull()
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  it('logout() calls the API', async () => {
    vi.mocked(authApi.logout).mockResolvedValue(undefined)

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.logout()

    expect(vi.mocked(authApi.logout)).toHaveBeenCalledOnce()
  })

  // ── verifySession() ───────────────────────────────────────────────────────

  it('verifySession() on success updates username and expiry in localStorage', async () => {
    const expiry = futureExpiry()
    vi.mocked(authApi.me).mockResolvedValue({ username: 'charlie', expiresAtUtc: expiry })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.verifySession()

    expect(store.username).toBe('charlie')
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBe('charlie')
  })

  it('verifySession() on API failure clears username', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.me).mockRejectedValue(new Error('Unauthorized'))

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.verifySession()

    expect(store.username).toBe('')
    expect(store.isAuthenticated).toBe(false)
    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBeNull()
  })

  it('verifySession() on API failure clears localStorage', async () => {
    localStorage.setItem(AUTH_USERNAME_KEY, 'testuser')
    localStorage.setItem(AUTH_EXPIRES_KEY, futureExpiry())
    vi.mocked(authApi.me).mockRejectedValue(new Error('Unauthorized'))

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.verifySession()

    expect(localStorage.getItem(AUTH_USERNAME_KEY)).toBeNull()
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  it('verifySession() does not update expiry when API returns null expiresAtUtc', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ username: 'dave', expiresAtUtc: null })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await store.verifySession()

    // username is set but expiresAtUtc stays empty
    expect(store.username).toBe('dave')
    expect(localStorage.getItem(AUTH_EXPIRES_KEY)).toBeNull()
  })

  // ── ensureVerified() ──────────────────────────────────────────────────────

  it('ensureVerified() calls verifySession only once across multiple calls', async () => {
    vi.mocked(authApi.me).mockResolvedValue({ username: 'eve', expiresAtUtc: futureExpiry() })

    const { useAuthStore } = await import('@/stores/auth')
    const store = useAuthStore()
    await Promise.all([store.ensureVerified(), store.ensureVerified(), store.ensureVerified()])

    expect(vi.mocked(authApi.me)).toHaveBeenCalledOnce()
  })
})
