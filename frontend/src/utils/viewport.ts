/**
 * The phone breakpoint the stylesheets use (`@media (width <= 767px)`), for the few choices that
 * have to be made in script rather than in CSS, such as what a panel starts out as.
 */
export const PHONE_MEDIA_QUERY = '(width <= 767px)'

/** True on a phone-width viewport. False where matchMedia is missing, which reads as a desktop. */
export function isPhoneViewport(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.(PHONE_MEDIA_QUERY).matches === true
}
