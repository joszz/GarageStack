import type { MaintenanceItem } from '@/services/maintenanceApi'

type Translate = (key: string, named?: Record<string, unknown>) => string

/** "Every 15,000 km or every 12 months", built from whichever intervals the item defines. */
export function formatIntervalSummary(
  item: Pick<MaintenanceItem, 'intervalKm' | 'intervalMonths'>,
  t: Translate,
): string {
  const parts: string[] = []
  if (item.intervalKm != null)
    parts.push(t('maintenance.everyKm', { km: item.intervalKm.toLocaleString() }))
  if (item.intervalMonths != null)
    parts.push(t('maintenance.everyMonths', { months: item.intervalMonths }))
  return parts.join(` ${t('maintenance.or')} `)
}
