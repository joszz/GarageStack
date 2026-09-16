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
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'setup', testMatch: /auth\.setup\.ts/ },
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], storageState: AUTH_STATE_FILE },
      dependencies: ['setup'],
    },
  ],
})
