// Applied before every test file via vitest.config.ts setupFiles.
// Defines browser APIs that jsdom does not implement.
import { vi } from 'vitest'

// The region comes from the machine's time zone, which differs between a laptop and CI, so every
// test runs in the Netherlands. region.spec imports the real detection to test it.
vi.mock('@/utils/region', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/region')>()),
  detectRegion: () => 'NL',
}))

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})
