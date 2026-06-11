import { ref, computed, watch, onUnmounted, getCurrentInstance } from 'vue'
import { notificationsApi, type AppNotification } from '@/services/api'
import { useAuthStore } from '@/stores/auth'

const notifications = ref<AppNotification[]>([])

export function prependNotification(notification: AppNotification) {
  notifications.value = [notification, ...notifications.value]
}
const panelOpen = ref(false)
const loading = ref(false)
const fetchError = ref<string | null>(null)

export function useNotifications() {
  const auth = useAuthStore()
  const unreadCount = computed(() => notifications.value.filter((n) => !n.isArchived).length)

  async function fetchNotifications() {
    loading.value = true
    fetchError.value = null
    try {
      notifications.value = await notificationsApi.list()
    } catch (err) {
      fetchError.value = err instanceof Error ? err.message : 'Failed to load notifications'
    } finally {
      loading.value = false
    }
  }

  function onSwMessage(event: MessageEvent) {
    if (event.data?.type === 'NOTIFICATION_RECEIVED') {
      fetchNotifications()
    }
  }

  // Use a watch instead of onMounted so initialization also fires when the user
  // logs in from the login page (App.vue stays mounted throughout; onMounted would
  // only run once, before auth is established on a cold start on /login).
  watch(
    () => auth.isAuthenticated,
    (authenticated) => {
      if (authenticated) {
        fetchNotifications()
        navigator.serviceWorker?.addEventListener('message', onSwMessage)
      } else {
        navigator.serviceWorker?.removeEventListener('message', onSwMessage)
        notifications.value = []
      }
    },
    { immediate: true },
  )

  if (getCurrentInstance()) {
    onUnmounted(() => {
      navigator.serviceWorker?.removeEventListener('message', onSwMessage)
    })
  }

  async function archiveNotification(id: number) {
    await notificationsApi.archive(id)
    const n = notifications.value.find((n) => n.id === id)
    if (n) n.isArchived = true
  }

  async function archiveAllNotifications() {
    await notificationsApi.archiveAll()
    notifications.value.forEach((n) => (n.isArchived = true))
  }

  async function deleteNotification(id: number) {
    await notificationsApi.delete(id)
    notifications.value = notifications.value.filter((n) => n.id !== id)
  }

  async function deleteAllNotifications() {
    await notificationsApi.deleteAll()
    notifications.value = []
  }

  function togglePanel() {
    panelOpen.value = !panelOpen.value
    if (panelOpen.value) fetchNotifications()
  }

  function closePanel() {
    panelOpen.value = false
  }

  return {
    notifications,
    unreadCount,
    panelOpen,
    loading,
    fetchError,
    fetchNotifications,
    archiveNotification,
    archiveAllNotifications,
    deleteNotification,
    deleteAllNotifications,
    togglePanel,
    closePanel,
  }
}
