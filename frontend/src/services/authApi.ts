import { request, send } from '@/services/apiCore'

export interface LoginResponse {
  username: string
  expiresAtUtc: string
}

export interface MeResponse {
  username: string
  expiresAtUtc: string | null
}

export const authApi = {
  login: (username: string, password: string, rememberMe = false) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, rememberMe }),
    }),
  logout: () => send('/api/auth/logout', 'POST'),
  me: () => request<MeResponse>('/api/auth/me'),
}
