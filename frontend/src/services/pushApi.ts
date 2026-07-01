import { request, send } from '@/services/apiCore'

export const pushApi = {
  getVapidPublicKey: () => request<{ publicKey: string }>('/api/push/vapid-public-key'),
  subscribe: (endpoint: string, p256DhKey: string, authKey: string) =>
    send('/api/push/subscribe', 'POST', { endpoint, p256DhKey, authKey }),
  unsubscribe: (endpoint: string) => send('/api/push/unsubscribe', 'POST', { endpoint }),
}
