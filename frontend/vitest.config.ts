import { fileURLToPath } from 'node:url'
import { mergeConfig, defineConfig, configDefaults } from 'vitest/config'
import viteConfig from './vite.config'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
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
