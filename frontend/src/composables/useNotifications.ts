import { ref, computed, watch, onUnmounted, getCurrentInstance } from 'vue'
import { notificationsApi, type AppNotification } from '@/services/notificationsApi'
import { useAuthStore } from '@/stores/auth'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { notificationCategoryId } from '@/utils/notificationCategories'

const notifications = ref<AppNotification[]>([])

type BadgingNavigator = Navigator & {
  setAppBadge?: (count?: number) => Promise<void>
  clearAppBadge?: () => Promise<void>
}

function syncBadge(count: number) {
  const nav = navigator as BadgingNavigator
  if (count > 0) {
    nav.setAppBadge?.(count)
  } else {
    nav.clearAppBadge?.()
  }
}

export function prependNotification(notification: AppNotification) {
  notifications.value = [notification, ...notifications.value]
}
const panelOpen = ref(false)
const loading = ref(false)
const fetchError = ref<string | null>(null)
const actionError = ref<string | null>(null)

export function useNotifications() {
  const auth = useAuthStore()
  const uiSettings = useUiSettingsStore()

  // Empty exclusion list means "no filter" (show every type). A notification is hidden only
  // when its category resolves to a known id AND that id is excluded - a null/unrecognized
  // category (never selectable in the checklist) is always shown, even while a filter is active.
  const visibleNotifications = computed(() => {
    const excluded = uiSettings.notificationTypeExclusions
    if (excluded.length === 0) return notifications.value
    return notifications.value.filter((n) => {
      const categoryId = notificationCategoryId(n.category)
      return categoryId === null || !excluded.includes(categoryId)
    })
  })

  const unreadCount = computed(() => visibleNotifications.value.filter((n) => !n.isArchived).length)

  async function fetchNotifications() {
    loading.value = true
    fetchError.value = null
    try {
      notifications.value = await notificationsApi.list()
      syncBadge(unreadCount.value)
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
    actionError.value = null
    try {
      await notificationsApi.archive(id)
      const n = notifications.value.find((n) => n.id === id)
      if (n) n.isArchived = true
    } catch (err) {
      console.error('Failed to archive notification', err)
      actionError.value = err instanceof Error ? err.message : 'Failed to archive notification'
    }
  }

  async function archiveAllNotifications() {
    actionError.value = null
    try {
      await notificationsApi.archiveAll()
      notifications.value.forEach((n) => (n.isArchived = true))
      syncBadge(0)
    } catch (err) {
      console.error('Failed to archive all notifications', err)
      actionError.value = err instanceof Error ? err.message : 'Failed to archive all notifications'
    }
  }

  async function deleteNotification(id: number) {
    actionError.value = null
    try {
      await notificationsApi.delete(id)
      notifications.value = notifications.value.filter((n) => n.id !== id)
      syncBadge(unreadCount.value)
    } catch (err) {
      console.error('Failed to delete notification', err)
      actionError.value = err instanceof Error ? err.message : 'Failed to delete notification'
    }
  }

  async function deleteAllNotifications() {
    actionError.value = null
    try {
      await notificationsApi.deleteAll()
      notifications.value = []
      syncBadge(0)
    } catch (err) {
      console.error('Failed to delete all notifications', err)
      actionError.value = err instanceof Error ? err.message : 'Failed to delete all notifications'
    }
  }

  function togglePanel() {
    panelOpen.value = !panelOpen.value
    if (panelOpen.value) fetchNotifications()
  }

  function closePanel() {
    panelOpen.value = false
  }

  return {
    notifications: visibleNotifications,
    unreadCount,
    panelOpen,
    loading,
    fetchError,
    actionError,
    fetchNotifications,
    archiveNotification,
    archiveAllNotifications,
    deleteNotification,
    deleteAllNotifications,
    togglePanel,
    closePanel,
  }
}
