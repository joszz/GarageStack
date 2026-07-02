import { computed, onScopeDispose, ref } from 'vue'
import { defineStore } from 'pinia'
import { authApi } from '@/services/authApi'

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
  }

  async function verifySession(): Promise<void> {
    const generation = ++_generation
    try {
      const result = await authApi.me()
      if (generation !== _generation) return
      // Only treat the session as authenticated once we have an expiry to go with the
      // username, otherwise isAuthenticated would stay false while username looked set.
      if (result.expiresAtUtc) {
        persistSession(result.username, result.expiresAtUtc)
      }
    } catch {
      if (generation !== _generation) return
      clearSession()
    }
  }

  function ensureVerified(): Promise<void> {
    _verifyPromise ??= verifySession()
    return _verifyPromise
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

  return { username, isAuthenticated, login, logout, verifySession, ensureVerified }
})
