import { request, send } from '@/services/apiCore'

export const pushApi = {
  getVapidPublicKey: () => request<{ publicKey: string }>('/api/push/vapid-public-key'),
  // Left out, excludedCategories keeps whatever the server holds for this subscription.
  subscribe: (
    endpoint: string,
    p256DhKey: string,
    authKey: string,
    excludedCategories?: readonly string[],
  ) => send('/api/push/subscribe', 'POST', { endpoint, p256DhKey, authKey, excludedCategories }),
  unsubscribe: (endpoint: string) => send('/api/push/unsubscribe', 'POST', { endpoint }),
}
