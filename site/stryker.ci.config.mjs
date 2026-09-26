import { readFileSync } from 'node:fs'

const config = JSON.parse(readFileSync(new URL('./stryker.config.json', import.meta.url), 'utf8'))

export default {
  ...config,
  mutate: ['src/lib/reachability.ts'],
  thresholds: { high: 100, low: 100, break: 100 },
}
