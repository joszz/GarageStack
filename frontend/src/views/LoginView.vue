<script setup lang="ts">
import { ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ApiError } from '@/services/api'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const auth = useAuthStore()

const username = ref('')
const password = ref('')
const submitting = ref(false)
const errorText = ref<string | null>(null)

async function submitLogin() {
  if (submitting.value) return

  submitting.value = true
  errorText.value = null

  try {
    await auth.login(username.value, password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
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
    <section class="login-card" aria-label="Login form">
      <div class="login-card__brand">
        <div class="login-card__brand-icon">
          <font-awesome-icon icon="car" />
        </div>
        <div>
          <div class="login-card__title">GarageStack</div>
          <div class="login-card__subtitle">{{ t('auth.subtitle') }}</div>
        </div>
      </div>

      <form class="login-card__form" @submit.prevent="submitLogin">
        <div class="login-field">
          <label for="username" class="login-field__label">{{ t('auth.username') }}</label>
          <div class="login-field__input-wrap">
            <span class="login-field__icon"><font-awesome-icon icon="user" /></span>
            <input
              id="username"
              v-model="username"
              class="login-field__input"
              type="text"
              autocomplete="username"
              required
            >
          </div>
        </div>

        <div class="login-field">
          <label for="password" class="login-field__label">{{ t('auth.password') }}</label>
          <div class="login-field__input-wrap">
            <span class="login-field__icon"><font-awesome-icon icon="lock" /></span>
            <input
              id="password"
              v-model="password"
              class="login-field__input"
              type="password"
              autocomplete="current-password"
              required
            >
          </div>
        </div>

        <div v-if="errorText" class="login-error" role="alert">
          <font-awesome-icon icon="triangle-exclamation" />
          <span>{{ errorText }}</span>
        </div>

        <button class="btn btn-primary login-submit" type="submit" :disabled="submitting">
          <font-awesome-icon v-if="submitting" icon="spinner" spin />
          <span>{{ submitting ? t('auth.loggingIn') : t('auth.login') }}</span>
        </button>
      </form>
    </section>
  </div>
</template>
