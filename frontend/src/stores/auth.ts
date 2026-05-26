import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { authApi, getAuthToken, setAuthToken } from '@/services/api'

const AUTH_USERNAME_KEY = 'garagestack-auth-username'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(getAuthToken())
  const username = ref<string>(localStorage.getItem(AUTH_USERNAME_KEY) ?? '')

  const isAuthenticated = computed(() => Boolean(token.value))

  async function login(usernameInput: string, password: string) {
    const result = await authApi.login(usernameInput, password)
    token.value = result.token
    username.value = result.username
    setAuthToken(result.token)
    localStorage.setItem(AUTH_USERNAME_KEY, result.username)
  }

  function logout() {
    token.value = null
    username.value = ''
    setAuthToken(null)
    localStorage.removeItem(AUTH_USERNAME_KEY)
  }

  return { token, username, isAuthenticated, login, logout }
})
