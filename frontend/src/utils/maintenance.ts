import type { MaintenanceItem } from '@/services/maintenanceApi'
import { ODOMETER_FORMAT, type UnitFormatter } from '@/utils/units'

type Translate = (key: string, named?: Record<string, unknown>) => string

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
