import type { VehicleType } from '@/services/vehicleApi'

export const NOTIFICATION_CATEGORY_IDS = [
  'low-tyre',
  'high-tyre',
  'low-ev',
  'charging-complete',
  'engine-start',
  'unlocked-parked',
  'doors-open-parked',
  'windows-open-parked',
  'vehicle-message',
  'maintenance',
] as const

export type NotificationCategoryId = (typeof NOTIFICATION_CATEGORY_IDS)[number]

// Categories the Worker only emits for a car that charges from an external charger: both
// CheckEvSoc and CheckChargingComplete return early unless VehicleTypeHelper.CanCharge, so on a
// plain hybrid these are checklist rows that can never match a notification.
const PLUG_IN_ONLY_CATEGORY_IDS: readonly NotificationCategoryId[] = ['low-ev', 'charging-complete']

/**
 * The categories worth offering for a drivetrain. 'unknown' keeps the full list: while detection
 * is still pending, hiding a type the car does support is worse than offering one it doesn't.
 */
export function notificationCategoryIdsFor(
  vehicleType: VehicleType,
): readonly NotificationCategoryId[] {
  if (vehicleType !== 'hev') return NOTIFICATION_CATEGORY_IDS
  return NOTIFICATION_CATEGORY_IDS.filter((id) => !PLUG_IN_ONLY_CATEGORY_IDS.includes(id))
}

// Maintenance notifications carry a dynamic per-item suffix (maintenance-<id>) rather than a
// single fixed string, so they're grouped under one "maintenance" id here.
export function notificationCategoryId(category: string | null): NotificationCategoryId | null {
  if (!category) return null
  if (category.startsWith('maintenance-')) return 'maintenance'
  return (NOTIFICATION_CATEGORY_IDS as readonly string[]).includes(category)
    ? (category as NotificationCategoryId)
    : null
}
