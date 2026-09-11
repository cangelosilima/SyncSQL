import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'

const sqlStylePath = new URL('../config/sql-style.json', import.meta.url)

// Relative base so the build works when served from a GitLab Pages
// project subpath (https://<group>.gitlab.io/<project>/) without having
// to know that path at build time.
export default defineConfig({
  plugins: [react(), {
    name: 'publish-sql-style-config',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'config/sql-style.json', source: readFileSync(sqlStylePath, 'utf8') })
    },
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url?.split('?')[0] !== '/config/sql-style.json') return next()
        response.setHeader('Content-Type', 'application/json; charset=utf-8')
        response.end(readFileSync(sqlStylePath, 'utf8'))
      })
    },
  }],
  base: './',
  // The app intentionally ships the browser-side XLSX and graph runtimes as
  // self-contained chunks. Keep Vite's warning threshold aligned with that
  // deployment choice while retaining the useful dynamic split.
  build: {
    chunkSizeWarningLimit: 1000,
  },
})
