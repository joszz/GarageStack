<script setup lang="ts">
import '@/assets/login.css'
import { computed, onMounted, ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ApiError } from '@/services/apiCore'
import { oidcLoginUrl } from '@/services/authApi'
import { redirectTo } from '@/utils/navigation'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const username = ref('')
const password = ref('')
const rememberMe = ref(false)
const submitting = ref(false)
const errorText = ref<string | null>(null)
const loadingConfig = ref(true)
const redirecting = ref(false)

const config = computed(() => auth.config)
const providerName = computed(() => config.value?.oidcProviderName ?? 'SSO')
const noSignInMethod = computed(
  () => !loadingConfig.value && !config.value?.oidcEnabled && !config.value?.passwordLoginEnabled,
)

// Where to land after signing in. Only same-origin paths are accepted here, and the API applies
// the same rule again to the value that comes back from the identity provider.
const redirectTarget = computed(() => {
  const target = route.query.redirect
  return typeof target === 'string' && target.startsWith('/') && !target.startsWith('//')
    ? target
    : '/'
})

// Set by the API when it sends a failed sign-in back to this page.
const signInError = computed(() =>
  typeof route.query.error === 'string' ? route.query.error : null,
)

// After an explicit logout the page must stay put: auto-login would otherwise sign the user
// straight back in through the provider's still-valid session.
const justLoggedOut = computed(() => route.query.loggedOut === '1')

const oidcUrl = computed(() => oidcLoginUrl(redirectTarget.value))

function startOidcLogin() {
  redirecting.value = true
  redirectTo(oidcUrl.value)
}

onMounted(async () => {
  const cfg = await auth.ensureConfig()
  loadingConfig.value = false

  if (signInError.value) {
    errorText.value =
      signInError.value === 'access_denied' ? t('auth.accessDenied') : t('auth.ssoFailed')
    return
  }

  if (cfg?.oidcEnabled && cfg.oidcAutoLogin && !justLoggedOut.value) {
    startOidcLogin()
  }
})

async function submitLogin() {
  if (submitting.value) return

  submitting.value = true
  errorText.value = null

  try {
    await auth.login(username.value, password.value, rememberMe.value)
    await router.replace(redirectTarget.value)
  } catch (err) {
    if (err instanceof ApiError && err.status === 401) {
      errorText.value = t('auth.invalidCredentials')
    } else {
      errorText.value = t('auth.loginFailed')
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <section class="login-card" :aria-label="t('auth.formLabel')">
      <div class="login-card__brand">
        <div class="login-card__brand-icon">
          <font-awesome-icon icon="car" />
        </div>
        <div>
          <div class="login-card__title">GarageStack</div>
          <div class="login-card__subtitle">{{ t('auth.subtitle') }}</div>
        </div>
      </div>

      <div v-if="loadingConfig" class="login-status" role="status">
        <font-awesome-icon icon="spinner" spin />
        <span>{{ t('common.loading') }}</span>
      </div>

      <template v-else>
        <div v-if="errorText" class="login-error" role="alert">
          <font-awesome-icon icon="triangle-exclamation" />
          <span>{{ errorText }}</span>
        </div>
        <div v-else-if="justLoggedOut" class="login-note" role="status">
          <font-awesome-icon icon="circle-info" />
          <span>{{ t('auth.loggedOut') }}</span>
        </div>

        <a
          v-if="config?.oidcEnabled"
          class="btn btn-primary login-submit login-sso"
          :href="oidcUrl"
          data-testid="oidc-login"
          @click="redirecting = true"
        >
          <font-awesome-icon
            :icon="redirecting ? 'spinner' : 'arrow-right-to-bracket'"
            :spin="redirecting"
          />
          <span>{{
            redirecting
              ? t('auth.redirecting', { provider: providerName })
              : t('auth.signInWith', { provider: providerName })
          }}</span>
        </a>

        <div v-if="config?.oidcEnabled && config?.passwordLoginEnabled" class="login-divider">
          <span>{{ t('auth.or') }}</span>
        </div>

        <form
          v-if="config?.passwordLoginEnabled"
          class="login-card__form"
          @submit.prevent="submitLogin"
        >
          <div class="login-field">
            <div class="login-field__input-wrap">
              <span class="login-field__icon"><font-awesome-icon icon="user" /></span>
              <input
                id="username"
                v-model="username"
                class="login-field__input"
                type="text"
                :aria-label="t('auth.username')"
                :placeholder="t('auth.username')"
                autocomplete="username"
                autofocus
                required
              />
            </div>
          </div>

          <div class="login-field">
            <div class="login-field__input-wrap">
              <span class="login-field__icon"><font-awesome-icon icon="lock" /></span>
              <input
                id="password"
                v-model="password"
                class="login-field__input"
                type="password"
                :aria-label="t('auth.password')"
                :placeholder="t('auth.password')"
                autocomplete="current-password"
                required
              />
            </div>
          </div>

          <div class="settings-toggle login-remember">
            <div class="settings-toggle__info">
              <span class="settings-toggle__label">{{ t('auth.rememberMe') }}</span>
              <span class="settings-toggle__desc">{{ t('auth.rememberMeDesc') }}</span>
            </div>
            <div class="settings-toggle__control form-check form-switch">
              <input
                id="remember-me"
                v-model="rememberMe"
                type="checkbox"
                class="form-check-input"
              />
            </div>
          </div>

          <button class="btn btn-primary login-submit" type="submit" :disabled="submitting">
            <font-awesome-icon v-if="submitting" icon="spinner" spin />
            <span>{{ submitting ? t('auth.loggingIn') : t('auth.login') }}</span>
          </button>
        </form>

        <div v-if="noSignInMethod" class="login-note login-note--warning" role="alert">
          <font-awesome-icon icon="triangle-exclamation" />
          <span>{{ t('auth.noSignInMethod') }}</span>
        </div>
      </template>
    </section>
  </div>
</template>
