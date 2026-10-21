import { describe, expect, it } from 'vitest'

import { deriveServerState } from '../../control-plane/model'

describe('deriveServerState', () => {
  it.each([
    [{ isRunning: false, isReady: false }, 'stopped'],
    [{ isRunning: true, isReady: false }, 'starting'],
    [{ isRunning: true, isReady: true }, 'ready'],
    [{ isRunning: false, isReady: true }, 'inconsistent'],
  ] as const)('derives %o as %s', (status, expectedState) => {
    expect(deriveServerState(status)).toBe(expectedState)
  })
})
