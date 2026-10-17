import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { applyTheme, THEME_COLORS } from '@/utils/theme'

// The script index.html runs before the app, read as the browser gets it.
// Vitest runs from the frontend folder.
const themeInit = readFileSync(resolve(process.cwd(), 'public/theme-init.js'), 'utf8')
const STORAGE_KEY = 'garagestack-settings-ui'

function runThemeInit() {
  new Function(themeInit)()
}

function osPrefers(light: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: light && query === '(prefers-color-scheme: light)',
    media: query,
  })) as typeof window.matchMedia
}

function themeColors() {
  return [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map((m) => ({
    content: m.content,
    media: m.getAttribute('media'),
  }))
}

describe('the theme on the document', () => {
  const originalMatchMedia = window.matchMedia

  beforeEach(() => {
    // As index.html ships them: one per OS preference.
    document.head.innerHTML =
      '<meta name="theme-color" content="#0f1117" media="(prefers-color-scheme: dark)" />' +
      '<meta name="theme-color" content="#f0f4f8" media="(prefers-color-scheme: light)" />'
    delete document.documentElement.dataset.theme
    localStorage.clear()
  })

  afterEach(() => {
    window.matchMedia = originalMatchMedia
    localStorage.clear()
  })

  it('applyTheme sets data-theme and points both theme-color tags at the chosen theme', () => {
    applyTheme('light')

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(themeColors()).toEqual([
      { content: THEME_COLORS.light, media: null },
      { content: THEME_COLORS.light, media: null },
    ])
  })

  it('the boot script puts the saved theme on the page before the app runs', () => {
    osPrefers(false)
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ theme: 'light', locale: 'en' }))

    runThemeInit()

    expect(document.documentElement.dataset.theme).toBe('light')
    expect(themeColors().every((m) => m.content === THEME_COLORS.light && m.media === null)).toBe(
      true,
    )
  })

  it('without a saved theme, the boot script follows the OS, like the settings default', () => {
    osPrefers(true)
    runThemeInit()
    expect(document.documentElement.dataset.theme).toBe('light')

    osPrefers(false)
    runThemeInit()
    expect(document.documentElement.dataset.theme).toBe('dark')
  })

  it('a saved value that is no theme, or storage that cannot be read, falls back to the OS', () => {
    osPrefers(true)
    localStorage.setItem(STORAGE_KEY, '{not json')
    runThemeInit()
    expect(document.documentElement.dataset.theme).toBe('light')

    localStorage.setItem(STORAGE_KEY, JSON.stringify({ theme: 'sepia' }))
    runThemeInit()
    expect(document.documentElement.dataset.theme).toBe('light')
  })

  it('the boot script and applyTheme agree on every theme colour', () => {
    for (const theme of ['dark', 'light'] as const) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify({ theme }))
      runThemeInit()
      const fromScript = themeColors()

      applyTheme(theme)

      expect(themeColors()).toEqual(fromScript)
    }
  })
})
