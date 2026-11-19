import { computed, onScopeDispose, ref } from 'vue'
import { defineStore } from 'pinia'
import { authApi, type AuthConfigResponse } from '@/services/authApi'
import { ApiError, STARTUP_REQUEST_TIMEOUT_MS } from '@/services/apiCore'
import { forgetLastVin } from '@/utils/lastVin'
import { forgetLastKnownState } from '@/utils/lastKnownState'
import { forgetCaches } from '@/utils/cacheStorage'
import { MAP_TILE_CACHE } from '@/utils/rasterTiles'
import { BASEMAP_CACHE } from '@/utils/vectorTiles'
import { SAVED_API_CACHE } from '@/utils/savedApi'

const AUTH_USERNAME_KEY = 'garagestack-auth-username'
const AUTH_EXPIRES_KEY = 'garagestack-auth-expires'

export const useAuthStore = defineStore('auth', () => {
  const username = ref<string>(localStorage.getItem(AUTH_USERNAME_KEY) ?? '')
  const expiresAtUtc = ref<string>(localStorage.getItem(AUTH_EXPIRES_KEY) ?? '')

  const now = ref(Date.now())
  const clockInterval = setInterval(() => {
    now.value = Date.now()
  }, 60_000)
  onScopeDispose(() => clearInterval(clockInterval))

  const isAuthenticated = computed(
    () =>
      !!username.value &&
      !!expiresAtUtc.value &&
      new Date(expiresAtUtc.value).getTime() > now.value,
  )

  // Which sign-in methods the server offers. Only the server knows: whether OIDC is configured
  // comes from the container's environment, not from the build.
  const config = ref<AuthConfigResponse | null>(null)
  let _configPromise: Promise<AuthConfigResponse | null> | null = null

  // Cached promise so the server round-trip happens exactly once per page load.
  // The router guard awaits this before deciding whether to allow or redirect.
  let _verifyPromise: Promise<void> | null = null

  // Bumped at the start of login()/verifySession(). Each call captures its own value
  // and only applies its result if it's still the latest call by the time it settles,
  // so a stale verifySession() can't clobber state set by a newer login() (or vice versa).
  let _generation = 0

  function persistSession(user: string, expires: string) {
    username.value = user
    expiresAtUtc.value = expires
    localStorage.setItem(AUTH_USERNAME_KEY, user)
    localStorage.setItem(AUTH_EXPIRES_KEY, expires)
  }

  function clearSession() {
    username.value = ''
    expiresAtUtc.value = ''
    localStorage.removeItem(AUTH_USERNAME_KEY)
    localStorage.removeItem(AUTH_EXPIRES_KEY)
    forgetLastVin()
    forgetLastKnownState()
    void forgetCaches(MAP_TILE_CACHE, BASEMAP_CACHE, SAVED_API_CACHE)
  }

  async function verifySession(): Promise<void> {
    const generation = ++_generation
    try {
      // A session held in storage is not kept waiting on a connection that never answers (one
      // bar of signal in a car park): past the limit it counts as no connection, below.
      const result = await authApi.me(
        isAuthenticated.value ? AbortSignal.timeout(STARTUP_REQUEST_TIMEOUT_MS) : undefined,
      )
      if (generation !== _generation) return
      // Only treat the session as authenticated once we have an expiry to go with the
      // username, otherwise isAuthenticated would stay false while username looked set.
      if (result.expiresAtUtc) {
        persistSession(result.username, result.expiresAtUtc)
      }
    } catch (error) {
      if (generation !== _generation) return
      // Only the server refusing the session ends it. Without a connection, or with the server
      // down, the session held in storage stands, so the car can still be shown as last loaded;
      // the next request the server refuses signs out as before. Asked again on the next
      // navigation.
      if (error instanceof ApiError && error.status < 500) clearSession()
      else _verifyPromise = null
    }
  }

  function ensureVerified(): Promise<void> {
    _verifyPromise ??= verifySession()
    return _verifyPromise
  }

  async function loadConfig(): Promise<AuthConfigResponse | null> {
    try {
      config.value = await authApi.config()
    } catch {
      // The API may simply not be up yet (a container restart, a cold stack). Drop the cached
      // attempt so the next caller asks again, instead of holding a failure for the life of the
      // page and leaving the login form with nothing to show.
      config.value = null
      _configPromise = null
    }
    return config.value
  }

  function ensureConfig(): Promise<AuthConfigResponse | null> {
    _configPromise ??= loadConfig()
    return _configPromise
  }

  async function login(usernameInput: string, password: string, rememberMe = false) {
    const generation = ++_generation
    const result = await authApi.login(usernameInput, password, rememberMe)
    if (generation !== _generation) return
    persistSession(result.username, result.expiresAtUtc)
    // Fresh login: reset the verify cache so next guard check reflects the new session.
    _verifyPromise = Promise.resolve()
  }

  async function logout() {
    _verifyPromise = null
    clearSession()
    await authApi.logout()
  }

  return {
    username,
    config,
    isAuthenticated,
    login,
    logout,
    verifySession,
    ensureVerified,
    ensureConfig,
  }
})
