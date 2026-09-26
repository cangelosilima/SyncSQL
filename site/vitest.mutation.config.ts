import { defineConfig, mergeConfig } from 'vitest/config'
import config from './vitest.config'

// Packaging tests launch subprocesses and change the working directory. Stryker's
// Vitest runner uses worker threads, so mutation runs use application tests only.
export default mergeConfig(config, defineConfig({ test: { include: ['src/**/*.test.{ts,tsx}'] } }))
