import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Vitest doesn't unmount React trees between tests on its own; without this
// every render() leaks into the next test's document.
afterEach(() => {
  cleanup()
})
