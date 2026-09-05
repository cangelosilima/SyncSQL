import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Relative base so the build works when served from a GitLab Pages
// project subpath (https://<group>.gitlab.io/<project>/) without having
// to know that path at build time.
export default defineConfig({
  plugins: [react()],
  base: './',
  // The app intentionally ships the browser-side XLSX and graph runtimes as
  // self-contained chunks. Keep Vite's warning threshold aligned with that
  // deployment choice while retaining the useful dynamic split.
  build: {
    chunkSizeWarningLimit: 1000,
  },
})
