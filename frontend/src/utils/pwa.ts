/** Matches while the page runs as the installed app, in its own window rather than a browser tab. */
export const INSTALLED_APP_QUERY = '(display-mode: standalone)'

/** Safari on iOS says so on the navigator instead, for an app added to the home screen. */
export function isIosHomeScreenApp(): boolean {
  return (
    typeof navigator !== 'undefined' &&
    'standalone' in navigator &&
    (navigator as Navigator & { standalone: boolean }).standalone === true
  )
}

/** Whether the page runs as the installed app right now. */
export function isInstalledApp(): boolean {
  return window.matchMedia?.(INSTALLED_APP_QUERY).matches === true || isIosHomeScreenApp()
}
