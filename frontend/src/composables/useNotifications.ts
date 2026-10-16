import { ref, computed, watch, onUnmounted, getCurrentInstance } from 'vue'
import { notificationsApi, type AppNotification } from '@/services/notificationsApi'
import { useAuthStore } from '@/stores/auth'
import { useUiSettingsStore } from '@/stores/settingsUi'
import { notificationCategoryId } from '@/utils/notificationCategories'
import { asError } from '@/utils/errors'

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
  // A live notification is new, so it is never archived. Defaulting the flag keeps a payload
  // from an older API version out of the panel's "neither tab" gap, where a missing value
  // matches neither the active nor the archived filter.
  notifications.value = [
    { ...notification, isArchived: notification.isArchived ?? false },
    ...notifications.value,
  ]
}
const panelOpen = ref(false)
const loading = ref(false)
// Kept as the failures themselves; the panel translates them (useErrorMessage).
const fetchError = ref<Error | null>(null)
const actionError = ref<Error | null>(null)

/**
 * Owns the in-app notification list: fetching, the user's per-category exclusion filter,
 * unread badge sync (both the PWA app badge and the bell icon), service-worker push wiring,
 * and the archive/delete actions. The underlying state (`notifications`, `panelOpen`, etc.) is
 * module-level, not per-call - every component calling this composable shares one list, so
 * e.g. archiving a notification in one place updates it everywhere. Fetches automatically
 * whenever auth state changes (via a watcher, not onMounted, since App.vue stays mounted
 * across login rather than remounting).
 */
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
      fetchError.value = asError(err)
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

  // Every panel action reports its failure the same way: logged, and shown above the list.
  async function act(what: string, action: () => Promise<void>) {
    actionError.value = null
    try {
      await action()
    } catch (err) {
      console.error(`Failed to ${what}`, err)
      actionError.value = asError(err)
    }
  }

  const archiveNotification = (id: number) =>
    act('archive notification', async () => {
      await notificationsApi.archive(id)
      const n = notifications.value.find((n) => n.id === id)
      if (n) n.isArchived = true
    })

  const archiveAllNotifications = () =>
    act('archive all notifications', async () => {
      await notificationsApi.archiveAll()
      notifications.value.forEach((n) => (n.isArchived = true))
      syncBadge(0)
    })

  const deleteNotification = (id: number) =>
    act('delete notification', async () => {
      await notificationsApi.delete(id)
      notifications.value = notifications.value.filter((n) => n.id !== id)
      syncBadge(unreadCount.value)
    })

  const deleteAllNotifications = () =>
    act('delete all notifications', async () => {
      await notificationsApi.deleteAll()
      notifications.value = []
      syncBadge(0)
    })

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
