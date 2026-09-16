import { test as base, expect, type Page } from '@playwright/test'

export interface CspViolation {
  page: string
  directive: string
  blockedUri: string
  source: string
}

/**
 * Records every Content-Security-Policy violation the browser reports during a test.
 *
 * This is the reason these tests exist: the policy only applies to a real deployment, so a
 * change that the dev server, the unit tests and the type checker all accept can still break
 * the shipped app. It happened once already, when the policy started covering index.html and
 * blocked both FontAwesome's injected stylesheet and the map marker's inline style.
 */
async function collectCspViolations(page: Page, sink: CspViolation[]) {
  await page.exposeFunction('__reportCspViolation', (v: Omit<CspViolation, 'page'>) => {
    sink.push({ ...v, page: new URL(page.url()).pathname })
  })
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (e) => {
      const report = window as unknown as {
        __reportCspViolation?: (v: Record<string, string>) => void
      }
      report.__reportCspViolation?.({
        directive: e.effectiveDirective,
        blockedUri: e.blockedURI,
        source: `${e.sourceFile ?? ''}:${e.lineNumber ?? ''}`,
      })
    })
  })
}

export const test = base.extend<{ cspViolations: CspViolation[] }>({
  cspViolations: async ({ page }, use) => {
    const violations: CspViolation[] = []
    await collectCspViolations(page, violations)
    await use(violations)
  },
})

/**
 * Opens a page with the session from the setup project and waits until the view has settled,
 * so assertions do not race the render that follows the first data load.
 */
export async function open(page: Page, path: string) {
  await page.goto(path)
  await page.waitForLoadState('networkidle')
}

export { expect }
