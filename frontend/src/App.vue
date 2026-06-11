<script setup lang="ts">
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useSettingsStore } from '@/stores/settings'
import { useAuthStore } from '@/stores/auth'
import { useVehicleStore } from '@/stores/vehicle'
import AppFooter from '@/components/AppFooter.vue'
import NotificationPanel from '@/components/NotificationPanel.vue'
import DemoBanner from '@/components/DemoBanner.vue'
import DemoControlPanel from '@/components/DemoControlPanel.vue'
import PwaInstallModal from '@/components/PwaInstallModal.vue'
import { useNotifications, prependNotification } from '@/composables/useNotifications'
import { useSignalR } from '@/composables/useSignalR'

const isDemoMode = import.meta.env.VITE_DEMO_MODE === 'true'
const demoControlsOpen = ref(false)

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const settings = useSettingsStore()
const auth = useAuthStore()
const vehicleStore = useVehicleStore()
const vehicleStatus = computed(() => vehicleStore.currentStatus)

const onlineStatusText = computed(() => {
  const s = vehicleStatus.value
  if (!s || s.isAvailable === null) return null
  return s.isAvailable ? t('common.online') : t('common.offline')
})

const onlineStatusVariant = computed(() => {
  const s = vehicleStatus.value
  if (!s || s.isAvailable === null) return null
  return s.isAvailable ? 'online' : 'offline'
})

function relativeTime(iso: string | null | undefined): string | undefined {
  if (!iso) return undefined
  const diffMs = Date.now() - new Date(iso).getTime()
  const diffMin = Math.floor(diffMs / 60_000)
  if (diffMin < 1) return t('notifications.justNow')
  if (diffMin < 60) return t('notifications.minutesAgo', { n: diffMin })
  return t('notifications.hoursAgo', { n: Math.floor(diffMin / 60) })
}

const onlineStatusTime = computed(() => relativeTime(vehicleStatus.value?.lastVehicleStateAt))
const {
  notifications,
  unreadCount,
  panelOpen,
  loading,
  togglePanel,
  closePanel,
  archiveNotification,
  archiveAllNotifications,
  deleteNotification,
  deleteAllNotifications,
} = useNotifications()

const carModel = computed(() => vehicleStore.vehicles[0]?.model ?? null)
const vehicleId = computed(() => vehicleStore.vehicles[0]?.id ?? null)

const availabilityToast = ref<'online' | 'offline' | null>(null)
let toastTimer: ReturnType<typeof setTimeout> | null = null

watch(
  () => vehicleStore.currentStatus?.isAvailable,
  (now, prev) => {
    if (prev === undefined || prev === null || now === null || now === undefined) return
    if (now === prev) return
    if (toastTimer) clearTimeout(toastTimer)
    availabilityToast.value = now ? 'online' : 'offline'
    toastTimer = setTimeout(() => {
      availabilityToast.value = null
    }, 5000)
  },
)

onBeforeUnmount(() => {
  if (toastTimer) clearTimeout(toastTimer)
})

const { start: startSignalR, stop: stopSignalR } = useSignalR({
  onTelemetryUpdated: (snapshot) => vehicleStore.applyLiveStatus(snapshot),
  onNotificationReceived: (notification) => prependNotification(notification),
  onTripCompleted: () => vehicleStore.notifyTripCompleted(),
})

watch(vehicleId, (id) => {
  if (id) startSignalR(id)
})

const isInitialLoading = computed(() => vehicleStore.loading && !vehicleStore.currentStatus)

const lastFetched = computed(() => {
  const d = vehicleStore.lastUpdated
  if (!d) return null
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
})

const lastRecorded = computed(() => {
  const ts = vehicleStore.currentStatus?.recordedAt
  if (!ts) return null
  return new Date(ts).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
})

const menuOpen = ref(false)
const isLoginRoute = computed(() => route.name === 'login')

function toggleMenu() {
  menuOpen.value = !menuOpen.value
}

function closeMenu() {
  menuOpen.value = false
}

async function logout() {
  await stopSignalR()
  await auth.logout()
  await router.replace({ name: 'login' })
}

watch(
  () => route.path,
  () => {
    menuOpen.value = false
  },
)
watch(
  () => settings.locale,
  (val) => {
    locale.value = val
  },
)
</script>

<template>
  <RouterView v-if="isLoginRoute" />

  <div v-else class="app-layout" :class="{ 'has-demo-banner': isDemoMode }">
    <DemoBanner v-if="isDemoMode" />

    <!-- Mobile topbar -->
    <header class="mobile-topbar">
      <button class="hamburger" :aria-expanded="menuOpen" aria-label="Menu" @click="toggleMenu">
        <font-awesome-icon :icon="menuOpen ? 'xmark' : 'bars'" />
      </button>
      <RouterLink to="/" class="mobile-brand">
        <font-awesome-icon icon="car" />
        GarageStack
      </RouterLink>
      <button class="notif-bell" :aria-label="t('notifications.title')" @click="togglePanel">
        <font-awesome-icon icon="bell" />
        <span v-if="unreadCount > 0" class="notif-bell__badge">{{ unreadCount }}</span>
      </button>
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
          <div v-if="carModel" class="sidebar-car-model">
            <font-awesome-icon icon="car" />
            <span>{{ carModel }}</span>
          </div>
          <div
            v-if="isInitialLoading || onlineStatusText"
            class="sidebar-online-status"
            :class="
              !isInitialLoading && onlineStatusVariant
                ? `sidebar-online-status--${onlineStatusVariant}`
                : ''
            "
          >
            <template v-if="isInitialLoading">
              <span class="skeleton skeleton--icon" />
              <span class="skeleton skeleton--text skeleton--text-md" />
            </template>
            <template v-else>
              <font-awesome-icon icon="wifi" />
              <span class="sidebar-footer__text">{{ onlineStatusText }}</span>
              <span v-if="onlineStatusTime" class="sidebar-online-status__time">{{
                onlineStatusTime
              }}</span>
            </template>
          </div>
          <div v-if="isInitialLoading || lastFetched" class="sidebar-timestamp">
            <template v-if="isInitialLoading">
              <span class="skeleton skeleton--icon" />
              <span class="skeleton skeleton--text skeleton--text-sm" />
            </template>
            <template v-else>
              <font-awesome-icon icon="rotate" :spin="vehicleStore.loading" />
              <span class="sidebar-footer__text">{{ t('common.fetched') }} {{ lastFetched }}</span>
            </template>
          </div>
          <div v-if="isInitialLoading || lastRecorded" class="sidebar-timestamp">
            <template v-if="isInitialLoading">
              <span class="skeleton skeleton--icon" />
              <span class="skeleton skeleton--text skeleton--text-lg" />
            </template>
            <template v-else>
              <font-awesome-icon icon="clock" />
              <span class="sidebar-footer__text"
                >{{ t('common.recorded') }} {{ lastRecorded }}</span
              >
            </template>
          </div>
          <div class="sidebar-user">
            <font-awesome-icon icon="user" />
            <span class="sidebar-user__email">{{ auth.username }}</span>
          </div>
          <button
            v-if="isDemoMode"
            class="sidebar-demo-btn"
            :class="{ 'is-active': demoControlsOpen }"
            @click="demoControlsOpen = !demoControlsOpen"
          >
            <font-awesome-icon :icon="['fas', 'flask']" />
            <span>{{ t('demo.controlPanel') }}</span>
          </button>
          <button class="sidebar-notif-btn" @click="togglePanel">
            <font-awesome-icon icon="bell" />
            <span>{{ t('notifications.title') }}</span>
            <span v-if="unreadCount > 0" class="notif-bell__badge">{{ unreadCount }}</span>
          </button>

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

    <Transition name="availability-toast">
      <div
        v-if="availabilityToast"
        class="availability-toast"
        :class="`availability-toast--${availabilityToast}`"
      >
        <font-awesome-icon
          :icon="availabilityToast === 'online' ? 'wifi' : 'triangle-exclamation'"
        />
        {{ availabilityToast === 'online' ? t('vehicle.wentOnline') : t('vehicle.wentOffline') }}
      </div>
    </Transition>

    <AppFooter />

    <NotificationPanel
      :open="panelOpen"
      :notifications="notifications"
      :loading="loading"
      @close="closePanel"
      @archive="archiveNotification"
      @archive-all="archiveAllNotifications"
      @delete="deleteNotification"
      @delete-all="deleteAllNotifications"
    />

    <DemoControlPanel v-if="isDemoMode" :open="demoControlsOpen" />
    <PwaInstallModal />
  </div>
</template>
