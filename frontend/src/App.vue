<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '@/stores/auth'
import { useSettingsStore } from '@/stores/settings'
import AppFooter from '@/components/AppFooter.vue'
import DetailModal from '@/components/DetailModal.vue'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const settings = useSettingsStore()
const menuOpen = ref(false)
const logoutConfirmOpen = ref(false)

watch(() => route.path, () => { menuOpen.value = false })
watch(() => settings.locale, (val) => { locale.value = val })

function promptLogout() {
  menuOpen.value = false
  logoutConfirmOpen.value = true
}

async function confirmLogout() {
  logoutConfirmOpen.value = false
  settings.resetToGuest()
  await auth.logout()
  router.replace({ name: 'login' })
}
</script>

<template>
  <!-- Full-page login layout (no sidebar) -->
  <RouterView v-if="route.name === 'login'" />

  <!-- Main app layout (only reached when authenticated) -->
  <div v-else class="app-layout">
    <!-- Mobile topbar -->
    <header class="mobile-topbar">
      <button class="hamburger" :aria-expanded="menuOpen" aria-label="Menu" @click="menuOpen = !menuOpen">
        <font-awesome-icon :icon="menuOpen ? 'xmark' : 'bars'" />
      </button>
      <span class="mobile-brand">
        <font-awesome-icon icon="car" />
        GarageStack
      </span>
    </header>

    <!-- Backdrop (mobile only) -->
    <div v-if="menuOpen" class="sidebar-backdrop" @click="menuOpen = false" />

    <!-- Sidebar + main body (fills space between topbar and footer) -->
    <div class="app-body">
      <!-- Sidebar / drawer -->
      <nav class="sidebar" :class="{ 'sidebar--open': menuOpen }">
        <div class="sidebar-brand">
          <font-awesome-icon icon="car" />
          <span>GarageStack</span>
        </div>
        <ul class="sidebar-nav">
          <li>
            <RouterLink to="/" active-class="active">
              <font-awesome-icon icon="gauge-high" />
              <span>{{ t('nav.dashboard') }}</span>
            </RouterLink>
          </li>
          <li>
            <RouterLink to="/statistics" active-class="active">
              <font-awesome-icon icon="chart-line" />
              <span>{{ t('nav.statistics') }}</span>
            </RouterLink>
          </li>
          <li>
            <RouterLink to="/map" active-class="active">
              <font-awesome-icon icon="map" />
              <span>{{ t('nav.map') }}</span>
            </RouterLink>
          </li>
        </ul>
        <div class="sidebar-footer">
          <span v-if="auth.email" class="sidebar-user">
            <font-awesome-icon icon="user" />
            <span class="sidebar-user__email">{{ auth.email }}</span>
          </span>
          <button class="sidebar-logout-btn" @click="promptLogout">
            <font-awesome-icon icon="arrow-right-from-bracket" />
            <span>{{ t('auth.logout') }}</span>
          </button>
        </div>
      </nav>

      <main class="main-content">
        <RouterView />
      </main>
    </div>

    <AppFooter />
  </div>

  <DetailModal
    :open="logoutConfirmOpen"
    :title="t('auth.logoutConfirmTitle')"
    @close="logoutConfirmOpen = false"
  >
    <p class="logout-confirm__message">{{ t('auth.logoutConfirmMessage') }}</p>
    <div class="logout-confirm__actions">
      <button class="btn btn-outline-secondary" @click="logoutConfirmOpen = false">
        {{ t('common.cancel') }}
      </button>
      <button class="btn btn-danger" @click="confirmLogout">
        <font-awesome-icon icon="arrow-right-from-bracket" />
        {{ t('auth.logout') }}
      </button>
    </div>
  </DetailModal>
</template>
