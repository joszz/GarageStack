import { request, send } from '@/services/apiCore'

export const pushApi = {
  getVapidPublicKey: () => request<{ publicKey: string }>('/api/push/vapid-public-key'),
  // Left out, either list keeps whatever the server holds for this subscription.
  subscribe: (
    endpoint: string,
    p256DhKey: string,
    authKey: string,
    excludedCategories?: readonly string[],
    includedCategories?: readonly string[],
  ) =>
    send('/api/push/subscribe', 'POST', {
      endpoint,
      p256DhKey,
      authKey,
      excludedCategories,
      includedCategories,
    }),
  unsubscribe: (endpoint: string) => send('/api/push/unsubscribe', 'POST', { endpoint }),
}
