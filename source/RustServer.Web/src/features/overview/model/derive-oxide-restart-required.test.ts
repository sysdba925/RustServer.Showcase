import { describe, expect, it } from 'vitest'

import { deriveOxideRestartRequired } from '../../control-plane/model'

describe('deriveOxideRestartRequired', () => {
  const baseOxide = {
    installedVersion: '2.0.7726',
    availableVersion: '2.0.7726',
    updateAvailable: false,
  }

  it('is false when no Oxide version is running', () => {
    expect(
      deriveOxideRestartRequired({ ...baseOxide, runningVersion: null }),
    ).toBe(false)
  })

  it('is false when the optional running version is absent', () => {
    expect(deriveOxideRestartRequired(baseOxide)).toBe(false)
  })

  it('is false when the running and installed versions match', () => {
    expect(
      deriveOxideRestartRequired({
        ...baseOxide,
        runningVersion: '2.0.7726',
      }),
    ).toBe(false)
  })

  it('is true when the running and installed versions differ', () => {
    expect(
      deriveOxideRestartRequired({
        ...baseOxide,
        runningVersion: '2.0.7725',
      }),
    ).toBe(true)
  })
})
