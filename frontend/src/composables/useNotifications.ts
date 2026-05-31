import { ref, computed } from 'vue'
import { notificationsApi, type AppNotification } from '@/services/api'

const notifications = ref<AppNotification[]>([])
const panelOpen = ref(false)
const loading = ref(false)

export function useNotifications() {
  const unreadCount = computed(() => notifications.value.filter((n) => !n.isArchived).length)

  async function fetchNotifications() {
    loading.value = true
    try {
      notifications.value = await notificationsApi.list()
    } finally {
      loading.value = false
    }
  }

  async function archiveNotification(id: number) {
    await notificationsApi.archive(id)
    const n = notifications.value.find((n) => n.id === id)
    if (n) n.isArchived = true
  }

  async function deleteNotification(id: number) {
    await notificationsApi.delete(id)
    notifications.value = notifications.value.filter((n) => n.id !== id)
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
    fetchNotifications,
    archiveNotification,
    deleteNotification,
    togglePanel,
    closePanel,
  }
}
