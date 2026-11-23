import type {
  MaintenanceCost,
  MaintenanceDueStatus,
  MaintenanceItem,
} from '@/services/maintenanceApi'
import { ODOMETER_FORMAT, type UnitFormatter } from '@/utils/units'

type Translate = (key: string, named?: Record<string, unknown>) => string

/**
 * The colour a due status is shown in, on the maintenance page, its detail dialog and the
 * dashboard card alike. An item with no service on record yet is information, not a warning.
 */
export function dueStatusVariant(
  status: MaintenanceDueStatus,
): 'success' | 'warning' | 'danger' | 'info' {
  if (status === 'overdue') return 'danger'
  if (status === 'dueSoon') return 'warning'
  if (status === 'ok') return 'success'
  return 'info'
}

/** "Every 15,000 km or every 12 months", built from whichever intervals the item defines. */
export function formatIntervalSummary(
  item: Pick<MaintenanceItem, 'intervalKm' | 'intervalMonths'>,
  t: Translate,
  units: UnitFormatter,
): string {
  const parts: string[] = []
  if (item.intervalKm != null)
    parts.push(
      t('maintenance.everyDistance', {
        distance: units.format('distance', item.intervalKm, ODOMETER_FORMAT),
      }),
    )
  if (item.intervalMonths != null)
    parts.push(t('maintenance.everyMonths', { months: item.intervalMonths }))
  return parts.join(` ${t('maintenance.or')} `)
}

/** What the services cost: in the calendar year so far, the year before, and over all of them. */
export interface CostTotals {
  thisYear: number
  lastYear: number
  allTime: number
}

/**
 * The costs added up by the local calendar year each service fell in. Null without any cost, so
 * a car whose history holds none shows no totals at all rather than three zeroes.
 */
export function costTotals(
  costs: readonly Pick<MaintenanceCost, 'performedAt' | 'cost'>[],
  today: Date = new Date(),
): CostTotals | null {
  if (costs.length === 0) return null
  const year = today.getFullYear()
  const totals: CostTotals = { thisYear: 0, lastYear: 0, allTime: 0 }
  for (const { performedAt, cost } of costs) {
    const performed = new Date(performedAt).getFullYear()
    if (performed === year) totals.thisYear += cost
    else if (performed === year - 1) totals.lastYear += cost
    totals.allTime += cost
  }
  return totals
}
