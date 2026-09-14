import { apiUrl, request, send } from '@/services/apiCore'

export interface LoginResponse {
  username: string
  expiresAtUtc: string
}

export interface MeResponse {
  username: string
  expiresAtUtc: string | null
}

export interface AuthConfigResponse {
  passwordLoginEnabled: boolean
  oidcEnabled: boolean
  oidcProviderName: string | null
  oidcAutoLogin: boolean
}

export const authApi = {
  config: () => request<AuthConfigResponse>('/api/auth/config'),
  login: (username: string, password: string, rememberMe = false) =>
    request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password, rememberMe }),
    }),
  logout: () => send('/api/auth/logout', 'POST'),
  me: () => request<MeResponse>('/api/auth/me'),
}

/**
 * URL that starts the OpenID Connect sign-in. The browser has to navigate here rather than
 * fetch it: the endpoint answers with a redirect to the identity provider, which takes over the
 * page and eventually redirects back to `returnUrl`.
 */
export function oidcLoginUrl(returnUrl: string): string {
  return apiUrl(`/api/auth/oidc/login?returnUrl=${encodeURIComponent(returnUrl)}`)
}
