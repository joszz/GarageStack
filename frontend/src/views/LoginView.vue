<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useSettingsStore } from '@/stores/settings'

const { t } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const settings = useSettingsStore()

const email = ref('')
const password = ref('')
const rememberMe = ref(false)
const showPassword = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

async function handleSubmit() {
  if (!email.value || !password.value) {
    error.value = t('auth.errorRequired')
    return
  }

  loading.value = true
  error.value = null

  const ok = await auth.login(email.value, password.value, rememberMe.value)

  loading.value = false

  if (!ok) {
    error.value = t('auth.errorInvalid')
    return
  }

  if (auth.userId !== null) {
    settings.loadForUser(auth.userId)
  }

  router.replace('/')
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-card__brand">
        <div class="login-card__brand-icon">
          <font-awesome-icon icon="car" />
        </div>
        <div>
          <div class="login-card__title">GarageStack</div>
          <div class="login-card__subtitle">{{ t('auth.hint') }}</div>
        </div>
      </div>

      <form class="login-card__form" novalidate @submit.prevent="handleSubmit">
        <div class="login-field">
          <label class="login-field__label" for="login-email">{{ t('auth.email') }}</label>
          <div class="login-field__input-wrap">
            <font-awesome-icon icon="user" class="login-field__icon" />
            <input
              id="login-email"
              v-model="email"
              type="email"
              class="login-field__input"
              :placeholder="t('auth.emailPlaceholder')"
              autocomplete="email"
              required
            />
          </div>
        </div>

        <div class="login-field">
          <label class="login-field__label" for="login-password">{{ t('auth.password') }}</label>
          <div class="login-field__input-wrap">
            <font-awesome-icon icon="lock" class="login-field__icon" />
            <input
              id="login-password"
              v-model="password"
              :type="showPassword ? 'text' : 'password'"
              class="login-field__input"
              :placeholder="t('auth.passwordPlaceholder')"
              autocomplete="current-password"
              required
            />
            <button
              type="button"
              class="login-field__toggle"
              :aria-label="t('auth.togglePassword')"
              @click="showPassword = !showPassword"
            >
              <font-awesome-icon :icon="showPassword ? 'eye-slash' : 'eye'" />
            </button>
          </div>
        </div>

        <div class="settings-toggle login-remember">
          <div class="settings-toggle__info">
            <label class="settings-toggle__label" for="login-remember-me">{{ t('auth.rememberMe') }}</label>
          </div>
          <div class="settings-toggle__control">
            <div class="form-check form-switch mb-0">
              <input
                id="login-remember-me"
                v-model="rememberMe"
                class="form-check-input"
                type="checkbox"
                role="switch"
              />
            </div>
          </div>
        </div>

        <div v-if="error" class="login-error" role="alert">
          <font-awesome-icon icon="triangle-exclamation" />
          {{ error }}
        </div>

        <button type="submit" class="btn btn-primary login-submit" :disabled="loading">
          <font-awesome-icon v-if="loading" icon="spinner" spin />
          {{ loading ? t('common.loading') : t('auth.login') }}
        </button>
      </form>
    </div>
  </div>
</template>
