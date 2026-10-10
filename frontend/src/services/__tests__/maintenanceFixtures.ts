import type { MaintenanceItem } from '@/services/maintenanceApi'

/**
 * An oil change due every 10,000 km and nothing logged yet, as the API returns it, shared by the
 * maintenance specs. Not a spec itself, so importing it does not run anything twice.
 */
export function maintenanceItem(overrides: Partial<MaintenanceItem> = {}): MaintenanceItem {
  return {
    id: 1,
    vehicleId: 1,
    name: 'Oil change',
    notes: null,
    intervalKm: 10000,
    intervalMonths: null,
    lastServiceDate: null,
    lastServiceOdometerKm: null,
    dueStatus: 'ok',
    nextDueDate: null,
    nextDueOdometerKm: null,
    daysRemaining: null,
    kmRemaining: null,
    createdAt: '2026-01-01',
    ...overrides,
  }
}
