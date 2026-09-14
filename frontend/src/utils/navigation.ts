/**
 * Full-page navigation, for the handful of places that have to leave the SPA entirely (the
 * OpenID Connect redirect). Kept as its own module so components can be tested without the
 * test environment trying to follow the navigation.
 */
export function redirectTo(url: string): void {
  window.location.assign(url)
}
