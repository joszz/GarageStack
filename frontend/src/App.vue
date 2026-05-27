<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useSettingsStore } from '@/stores/settings'
import { useAuthStore } from '@/stores/auth'
import AppFooter from '@/components/AppFooter.vue'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const settings = useSettingsStore()
const auth = useAuthStore()
const menuOpen = ref(false)
const isLoginRoute = computed(() => route.name === 'login')

function toggleMenu() {
  menuOpen.value = !menuOpen.value
}

function closeMenu() {
  menuOpen.value = false
}

async function logout() {
  await auth.logout()
  await router.replace({ name: 'login' })
}

watch(() => route.path, () => { menuOpen.value = false })
watch(() => settings.locale, (val) => { locale.value = val })
</script>

<template>
  <RouterView v-if="isLoginRoute" />

  <div v-else class="app-layout">
    <!-- Mobile topbar -->
    <header class="mobile-topbar">
      <button class="hamburger" :aria-expanded="menuOpen" aria-label="Menu" @click="toggleMenu">
        <font-awesome-icon :icon="menuOpen ? 'xmark' : 'bars'" />
      </button>
      <RouterLink to="/" class="mobile-brand">
        <font-awesome-icon icon="car" />
        GarageStack
      </RouterLink>
    </header>

    <!-- Backdrop (mobile only) -->
    <div v-if="menuOpen" class="sidebar-backdrop" @click="closeMenu" />

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
          <div class="sidebar-user">
            <font-awesome-icon icon="user" />
            <span class="sidebar-user__email">{{ auth.username }}</span>
          </div>
          <button class="sidebar-logout-btn" @click="logout">
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
</template>
