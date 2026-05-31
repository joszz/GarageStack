import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { authApi } from '@/services/api'

const AUTH_USERNAME_KEY = 'garagestack-auth-username'
const AUTH_EXPIRES_KEY = 'garagestack-auth-expires'

export const useAuthStore = defineStore('auth', () => {
  const username = ref<string>(localStorage.getItem(AUTH_USERNAME_KEY) ?? '')
  const expiresAtUtc = ref<string>(localStorage.getItem(AUTH_EXPIRES_KEY) ?? '')

  const isAuthenticated = computed(
    () => !!username.value && !!expiresAtUtc.value && new Date(expiresAtUtc.value) > new Date(),
  )

  async function login(usernameInput: string, password: string, rememberMe = false) {
    const result = await authApi.login(usernameInput, password, rememberMe)
    username.value = result.username
    expiresAtUtc.value = result.expiresAtUtc
    localStorage.setItem(AUTH_USERNAME_KEY, result.username)
    localStorage.setItem(AUTH_EXPIRES_KEY, result.expiresAtUtc)
  }

  async function logout() {
    username.value = ''
    expiresAtUtc.value = ''
    localStorage.removeItem(AUTH_USERNAME_KEY)
    localStorage.removeItem(AUTH_EXPIRES_KEY)
    await authApi.logout()
  }

  async function verifySession(): Promise<void> {
    try {
      const result = await authApi.me()
      username.value = result.username
    } catch {
      username.value = ''
      expiresAtUtc.value = ''
      localStorage.removeItem(AUTH_USERNAME_KEY)
      localStorage.removeItem(AUTH_EXPIRES_KEY)
    }
  }

  return { username, isAuthenticated, login, logout, verifySession }
})
