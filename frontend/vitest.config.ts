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
      },
    },
  }),
)
