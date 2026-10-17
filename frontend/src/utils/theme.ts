import type { Theme } from '@/stores/settingsShared'

/**
 * The browser chrome's colour per theme: the page background (--color-bg in main.css), so the
 * address bar and an installed app's title bar run on from the page.
 */
export const THEME_COLORS: Record<Theme, string> = {
  dark: '#0f1117',
  light: '#f0f4f8',
}

/**
 * Puts a theme on the document: `data-theme`, which the stylesheet's tokens follow, and the
 * theme-color meta tags. index.html ships one tag per OS preference; once a theme is chosen,
 * both name the chosen one, so a light page on a dark phone no longer gets a dark address bar.
 * public/theme-init.js does the same before the app loads; keep the two in step.
 */
export function applyTheme(theme: Theme, doc: Document = document): void {
  doc.documentElement.dataset.theme = theme
  for (const meta of doc.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    meta.removeAttribute('media')
    meta.content = THEME_COLORS[theme]
  }
}
