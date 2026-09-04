/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Relative base so the build works when served from a GitLab Pages
// project subpath (https://<group>.gitlab.io/<project>/) without having
// to know that path at build time.
export default defineConfig({
  plugins: [react()],
  base: './',
  test: {
    // `globals` lets describe/it/expect be used without importing them, which is
    // also what VS Code's Vitest extension needs to enumerate tests statically -
    // that enumeration is what populates Test Explorer.
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    css: false,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'lcov'],
      include: ['src/lib/**/*.{ts,tsx}'],
    },
  },
})
