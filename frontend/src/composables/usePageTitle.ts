import { watchEffect } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'

export const APP_NAME = 'GarageStack'

/**
 * Names the browser tab after the view, so tabs, history and bookmarks can be told apart and a
 * screen reader announces where a navigation went (WCAG 2.4.2). Follows a language switch too.
 */
export function usePageTitle() {
  const route = useRoute()
  const { t } = useI18n()

  watchEffect(() => {
    const key = route.meta.titleKey
    document.title = key ? `${t(key)} - ${APP_NAME}` : APP_NAME
  })
}
