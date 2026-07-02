import { useI18n } from 'vue-i18n'

export function useRelativeTime() {
  const { t } = useI18n()

  function relativeTime(date: string | Date): string {
    const d = typeof date === 'string' ? new Date(date) : date
    const diffMs = Date.now() - d.getTime()
    const diffMin = Math.floor(diffMs / 60_000)
    if (diffMin < 1) return t('notifications.justNow')
    if (diffMin < 60) return t('notifications.minutesAgo', { n: diffMin })
    const diffHrs = Math.floor(diffMin / 60)
    if (diffHrs < 24) return t('notifications.hoursAgo', { n: diffHrs })
    return d.toLocaleDateString()
  }

  return { relativeTime }
}
