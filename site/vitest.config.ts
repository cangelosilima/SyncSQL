import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    coverage: {
      provider: 'v8',
      reportOnFailure: true,
      include: ['src/**/*.{ts,tsx}', 'scripts/*.mjs'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/test/**', 'src/vite-env.d.ts'],
      reporter: ['text', 'json', 'json-summary', 'lcov'],
      thresholds: {
        lines: 80,
        statements: 80,
        functions: 75,
        branches: 70,
        'scripts/package-ai-model.mjs': { lines: 100, statements: 100, functions: 100 },
      },
    },
  },
})
