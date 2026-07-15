export const NOTIFICATION_CATEGORY_IDS = [
  'low-tyre',
  'high-tyre',
  'low-ev',
  'charging-complete',
  'engine-start',
  'unlocked-parked',
  'doors-open-parked',
  'windows-open-parked',
  'maintenance',
] as const

export type NotificationCategoryId = (typeof NOTIFICATION_CATEGORY_IDS)[number]

// Maintenance notifications carry a dynamic per-item suffix (maintenance-<id>) rather than a
// single fixed string, so they're grouped under one "maintenance" id here.
export function notificationCategoryId(category: string | null): NotificationCategoryId | null {
  if (!category) return null
  if (category.startsWith('maintenance-')) return 'maintenance'
  return (NOTIFICATION_CATEGORY_IDS as readonly string[]).includes(category)
    ? (category as NotificationCategoryId)
    : null
}
