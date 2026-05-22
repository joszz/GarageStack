import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

const BASE_URL = import.meta.env.VITE_API_URL ?? ''

export interface AuthUser {
  userId: number
  email: string
}

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(null)
  const userId = ref<number | null>(null)
  const email = ref<string | null>(null)
  const restoring = ref(true)

  const isAuthenticated = computed(() => accessToken.value !== null)

  async function login(userEmail: string, password: string, rememberMe: boolean): Promise<boolean> {
    try {
      const res = await fetch(`${BASE_URL}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ email: userEmail, password, rememberMe }),
      })
      if (!res.ok) return false
      const data = await res.json()
      accessToken.value = data.accessToken
      userId.value = data.userId
      email.value = data.email
      return true
    } catch {
      return false
    }
  }

  async function refresh(): Promise<boolean> {
    try {
      const res = await fetch(`${BASE_URL}/api/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
      })
      if (!res.ok) {
        accessToken.value = null
        userId.value = null
        email.value = null
        return false
      }
      const data = await res.json()
      accessToken.value = data.accessToken
      userId.value = data.userId
      email.value = data.email
      return true
    } catch {
      accessToken.value = null
      userId.value = null
      email.value = null
      return false
    }
  }

  async function logout(): Promise<void> {
    try {
      await fetch(`${BASE_URL}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include',
      })
    } catch {
      // ignore errors on logout
    }
    accessToken.value = null
    userId.value = null
    email.value = null
  }

  async function restoreSession(): Promise<void> {
    restoring.value = true
    await refresh()
    restoring.value = false
  }

  return { accessToken, userId, email, isAuthenticated, restoring, login, logout, refresh, restoreSession }
})
