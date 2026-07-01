import { request, send } from '@/services/apiCore'

export interface AppNotification {
  id: number
  title: string
  body: string
  createdAt: string
  isArchived: boolean
  category: string | null
}

export const notificationsApi = {
  list: () => request<AppNotification[]>('/api/notifications'),
  archive: (id: number) => send(`/api/notifications/${id}/archive`, 'PATCH'),
  archiveAll: () => send('/api/notifications/archive-all', 'PATCH'),
  delete: (id: number) => send(`/api/notifications/${id}`, 'DELETE'),
  deleteAll: () => send('/api/notifications', 'DELETE'),
}
