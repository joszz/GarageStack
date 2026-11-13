import { fileURLToPath } from 'node:url'
import { mergeConfig, defineConfig, configDefaults } from 'vitest/config'
import viteConfig from './vite.config.ts'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      // Each spec file still gets its own context, but shares the worker's jsdom setup instead of
      // building one per file: the suite ran 21 s, 80% of it creating environments, and now runs
      // in about 4. A spec stubs a browser global with vi.stubGlobal (test-setup.ts keeps
      // matchMedia configurable for it), never by replacing `window` or `location`.
      pool: 'vmThreads',
      exclude: [...configDefaults.exclude, 'e2e/**'],
      root: fileURLToPath(new URL('./', import.meta.url)),
      setupFiles: ['./src/test-setup.ts'],
      // pnpm test:coverage. The app's own source only: not the specs and their fixtures, not
      // type declarations, and not the generated time zone table.
      coverage: {
        provider: 'v8',
        include: ['src/**/*.{ts,vue}'],
        exclude: [
          'src/**/__tests__/**',
          'src/**/*.d.ts',
          'src/test-setup.ts',
          'src/utils/timeZoneRegions.ts',
        ],
        reporter: ['text-summary', 'json-summary', 'html'],
        // A floor a point or two under where coverage stood on 2026-11-13 (84.7% statements, 75.1%
        // branches, 78.9% functions, 86.3% lines), so it cannot slide without CI saying so. Raise
        // it as coverage grows; lowering it is a decision, not a fix.
        thresholds: {
          statements: 83,
          branches: 73,
          functions: 77,
          lines: 85,
        },
      },
    },
  }),
)
