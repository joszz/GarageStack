import { request, send } from '@/services/apiCore'
import type { VehicleExclusions } from '@/utils/notificationCategories'

export const pushApi = {
  getVapidPublicKey: () => request<{ publicKey: string }>('/api/push/vapid-public-key'),
  // Left out, any of the lists keeps whatever the server holds for this subscription.
  // excludedCategoriesByVehicle holds the types switched off for one car only, by vehicle id.
  subscribe: (
    endpoint: string,
    p256DhKey: string,
    authKey: string,
    excludedCategories?: readonly string[],
    includedCategories?: readonly string[],
    excludedCategoriesByVehicle?: VehicleExclusions,
  ) =>
    send('/api/push/subscribe', 'POST', {
      endpoint,
      p256DhKey,
      authKey,
      excludedCategories,
      includedCategories,
      excludedCategoriesByVehicle,
    }),
  unsubscribe: (endpoint: string) => send('/api/push/unsubscribe', 'POST', { endpoint }),
}
