/**
 * The phone breakpoint the stylesheets use (`@media (width <= 767px)`), for the few choices that
 * have to be made in script rather than in CSS, such as what a panel starts out as.
 */
export const PHONE_MEDIA_QUERY = '(width <= 767px)'

/** True on a phone-width viewport. False where matchMedia is missing, which reads as a desktop. */
export function isPhoneViewport(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.(PHONE_MEDIA_QUERY).matches === true
}

/**
 * The nearest ancestor that scrolls, for an IntersectionObserver whose margin has to reach past
 * what is in view: the page's main column rather than the window. Null when nothing does.
 */
export function scrollParent(element: HTMLElement | null): HTMLElement | null {
  for (let parent = element?.parentElement ?? null; parent; parent = parent.parentElement) {
    const { overflowY } = getComputedStyle(parent)
    if (overflowY === 'auto' || overflowY === 'scroll') return parent
  }
  return null
}
