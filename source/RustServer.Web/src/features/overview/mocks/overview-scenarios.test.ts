import { describe, expect, it } from 'vitest'

import { overviewScenarios } from './overview-scenarios'

describe('overviewScenarios', () => {
  it('defines every required deterministic Overview input', () => {
    expect(Object.keys(overviewScenarios).sort()).toEqual([
      'inconsistent-status',
      'maintenance',
      'oxide-update-available',
      'partial-unavailable',
      'ready',
      'rust-update-available',
      'starting',
      'stopped',
      'update-failed',
    ])
  })
})
