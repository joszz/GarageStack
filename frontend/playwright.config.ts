import { defineConfig, devices } from '@playwright/test'
import { AUTH_STATE_FILE } from './e2e/auth-state'

/**
 * Smoke tests run against a real deployment, not the Vite dev server: the point is to exercise
 * the production bundle behind the production nginx config, including the
 * Content-Security-Policy, which is where the dev server and the shipped image differ most.
 *
 * Start the stack first, from the repository root:
 *   cp .env.demo.example .env.demo
 *   docker compose -f docker-compose.demo.yml up -d --build
 *
 * Then, from frontend/:  pnpm test:e2e
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:8080'

// The same deployment under a name that is not localhost. Browsers treat localhost as a secure
// origin, so a policy that only breaks plain HTTP on a LAN address (upgrade-insecure-requests
// once blanked every such install) never shows up there.
const insecureHost = 'garagestack.test'
const insecureURL = new URL(baseURL)
const insecureTarget = insecureURL.hostname
insecureURL.hostname = insecureHost

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    {
      name: 'chromium',
      testIgnore: /plain-http\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], storageState: AUTH_STATE_FILE },
      dependencies: ['setup'],
    },
    {
      name: 'plain-http',
      testMatch: /plain-http\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        baseURL: insecureURL.origin,
        launchOptions: {
          args: [`--host-resolver-rules=MAP ${insecureHost} ${insecureTarget}`],
        },
      },
    },
  ],
})
