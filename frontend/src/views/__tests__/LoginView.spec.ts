import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import LoginView from '@/views/LoginView.vue'
import { authApi } from '@/services/authApi'
import type { AuthConfigResponse, LoginResponse, MeResponse } from '@/services/authApi'
import { redirectTo } from '@/utils/navigation'

const mockRoute: { query: Record<string, string> } = { query: {} }
const routerReplace = vi.fn<(to: string) => Promise<void>>()

vi.mock('vue-router', () => ({
  useRouter: () => ({ replace: routerReplace }),
  useRoute: () => mockRoute,
}))

vi.mock('@/utils/navigation', () => ({ redirectTo: vi.fn<(url: string) => void>() }))

// Keep the real oidcLoginUrl builder: the URL it produces is part of what these tests assert.
vi.mock('@/services/authApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/services/authApi')>()),
  authApi: {
    config: vi.fn<() => Promise<AuthConfigResponse>>(),
    login: vi.fn<() => Promise<LoginResponse>>(),
    logout: vi.fn<() => Promise<void>>(),
    me: vi.fn<() => Promise<MeResponse>>(),
  },
}))

const FaStub = { template: '<i />', props: ['icon', 'spin'] }

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

const oidcConfig: AuthConfigResponse = {
  passwordLoginEnabled: false,
  oidcEnabled: true,
  oidcProviderName: 'Authentik',
  oidcAutoLogin: false,
}

const passwordConfig: AuthConfigResponse = {
  passwordLoginEnabled: true,
  oidcEnabled: false,
  oidcProviderName: null,
  oidcAutoLogin: false,
}

async function mountLogin(config: AuthConfigResponse | null) {
  if (config) {
    vi.mocked(authApi.config).mockResolvedValue(config)
  } else {
    vi.mocked(authApi.config).mockRejectedValue(new Error('unreachable'))
  }

  const wrapper = mount(LoginView, {
    global: {
      plugins: [createPinia(), i18n],
      stubs: { FontAwesomeIcon: FaStub },
    },
  })
  await flushPromises()
  return wrapper
}

describe('LoginView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockRoute.query = {}
    vi.mocked(authApi.config).mockReset()
    vi.mocked(redirectTo).mockClear()
    routerReplace.mockClear()
  })

  // ── Sign-in methods ───────────────────────────────────────────────────────

  it('shows the single sign-on button with the provider name when OIDC is enabled', async () => {
    const wrapper = await mountLogin(oidcConfig)

    const button = wrapper.find('[data-testid="oidc-login"]')
    expect(button.exists()).toBe(true)
    expect(button.text()).toContain('Sign in with Authentik')
  })

  it('hides the password form when only OIDC is enabled', async () => {
    const wrapper = await mountLogin(oidcConfig)

    expect(wrapper.find('form').exists()).toBe(false)
  })

  it('shows the password form and no single sign-on button when OIDC is not configured', async () => {
    const wrapper = await mountLogin(passwordConfig)

    expect(wrapper.find('form').exists()).toBe(true)
    expect(wrapper.find('[data-testid="oidc-login"]').exists()).toBe(false)
  })

  it('offers both methods when the password login is kept alongside OIDC', async () => {
    const wrapper = await mountLogin({ ...oidcConfig, passwordLoginEnabled: true })

    expect(wrapper.find('[data-testid="oidc-login"]').exists()).toBe(true)
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('reports that nothing is available when the server offers no sign-in method', async () => {
    const wrapper = await mountLogin(null)

    expect(wrapper.text()).toContain('No sign-in method is available')
  })

  // ── Return URL ────────────────────────────────────────────────────────────

  it('carries the requested page through the sign-in as the return URL', async () => {
    mockRoute.query = { redirect: '/map' }

    const wrapper = await mountLogin(oidcConfig)

    expect(wrapper.find('[data-testid="oidc-login"]').attributes('href')).toBe(
      '/api/auth/oidc/login?returnUrl=%2Fmap',
    )
  })

  it('ignores an off-site redirect query and falls back to the start page', async () => {
    mockRoute.query = { redirect: '//evil.example.com' }

    const wrapper = await mountLogin(oidcConfig)

    expect(wrapper.find('[data-testid="oidc-login"]').attributes('href')).toBe(
      '/api/auth/oidc/login?returnUrl=%2F',
    )
  })

  // ── Auto-login ────────────────────────────────────────────────────────────

  it('redirects to the provider straight away when auto-login is enabled', async () => {
    await mountLogin({ ...oidcConfig, oidcAutoLogin: true })

    expect(vi.mocked(redirectTo)).toHaveBeenCalledWith('/api/auth/oidc/login?returnUrl=%2F')
  })

  it('does not auto-login right after an explicit logout', async () => {
    mockRoute.query = { loggedOut: '1' }

    const wrapper = await mountLogin({ ...oidcConfig, oidcAutoLogin: true })

    expect(vi.mocked(redirectTo)).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('You have been signed out')
  })

  it('does not auto-login when the provider just rejected the sign-in', async () => {
    mockRoute.query = { error: 'access_denied' }

    await mountLogin({ ...oidcConfig, oidcAutoLogin: true })

    expect(vi.mocked(redirectTo)).not.toHaveBeenCalled()
  })

  it('does not redirect when auto-login is disabled', async () => {
    await mountLogin(oidcConfig)

    expect(vi.mocked(redirectTo)).not.toHaveBeenCalled()
  })

  // ── Errors reported by the API ────────────────────────────────────────────

  it('explains an access_denied bounce from the provider', async () => {
    mockRoute.query = { error: 'access_denied' }

    const wrapper = await mountLogin(oidcConfig)

    expect(wrapper.text()).toContain('This account is not allowed to use GarageStack')
  })

  it('explains a failed single sign-on attempt', async () => {
    mockRoute.query = { error: 'oidc_failed' }

    const wrapper = await mountLogin(oidcConfig)

    expect(wrapper.text()).toContain('Single sign-on failed')
  })
})
