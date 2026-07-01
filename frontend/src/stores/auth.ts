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

  async function verifySession(): Promise<void> {
    try {
      const result = await authApi.me()
      username.value = result.username
      if (result.expiresAtUtc) {
        expiresAtUtc.value = result.expiresAtUtc
        localStorage.setItem(AUTH_USERNAME_KEY, result.username)
        localStorage.setItem(AUTH_EXPIRES_KEY, result.expiresAtUtc)
      }
    } catch {
      username.value = ''
      expiresAtUtc.value = ''
      localStorage.removeItem(AUTH_USERNAME_KEY)
      localStorage.removeItem(AUTH_EXPIRES_KEY)
    }
  }

  function ensureVerified(): Promise<void> {
    _verifyPromise ??= verifySession()
    return _verifyPromise
  }

  async function login(usernameInput: string, password: string, rememberMe = false) {
    const result = await authApi.login(usernameInput, password, rememberMe)
    username.value = result.username
    expiresAtUtc.value = result.expiresAtUtc
    localStorage.setItem(AUTH_USERNAME_KEY, result.username)
    localStorage.setItem(AUTH_EXPIRES_KEY, result.expiresAtUtc)
    // Fresh login: reset the verify cache so next guard check reflects the new session.
    _verifyPromise = Promise.resolve()
  }

  async function logout() {
    _verifyPromise = null
    username.value = ''
    expiresAtUtc.value = ''
    localStorage.removeItem(AUTH_USERNAME_KEY)
    localStorage.removeItem(AUTH_EXPIRES_KEY)
    await authApi.logout()
  }

  return { username, isAuthenticated, login, logout, verifySession, ensureVerified }
})
