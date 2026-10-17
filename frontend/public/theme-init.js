// Puts the saved theme on the page before the app's script and stylesheet arrive, so a light
// theme never paints dark first. A plain script loaded from index.html's head: it mirrors
// applyTheme in src/utils/theme.ts and the settings store's default (the OS preference), which
// take over once the app runs. Keep the three in step.
;(() => {
  const colors = { dark: '#0f1117', light: '#f0f4f8' }
  let theme = 'dark'
  try {
    const saved = JSON.parse(localStorage.getItem('garagestack-settings-ui') ?? '{}').theme
    if (saved === 'dark' || saved === 'light') theme = saved
    else if (matchMedia('(prefers-color-scheme: light)').matches) theme = 'light'
  } catch {
    // Storage blocked or unreadable: the OS preference still decides.
    if (matchMedia('(prefers-color-scheme: light)').matches) theme = 'light'
  }
  document.documentElement.dataset.theme = theme
  for (const meta of document.querySelectorAll('meta[name="theme-color"]')) {
    meta.removeAttribute('media')
    meta.content = colors[theme]
  }
})()
