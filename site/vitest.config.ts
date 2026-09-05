import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}', 'scripts/*.mjs'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/vite-env.d.ts'],
      reporter: ['text', 'json', 'json-summary', 'lcov'],
      thresholds: {
        'scripts/package-ai-model.mjs': { lines: 100, statements: 100, functions: 100 },
      },
    },
  },
})
