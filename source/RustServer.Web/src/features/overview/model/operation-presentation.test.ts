import { describe, expect, it } from 'vitest'

import {
  classifyOperationStatus,
  formatOperationStatus,
  formatOperationType,
  isKnownActiveOperation,
  isKnownActiveUpdate,
} from '../../control-plane/model'

describe('operation presentation rules', () => {
  it.each([
    ['Pending', 'active'],
    ['Running', 'active'],
    ['Succeeded', 'terminal'],
    ['Failed', 'terminal'],
    ['WaitingForApproval', 'unknown'],
  ] as const)('classifies %s as %s', (status, expectedKind) => {
    expect(classifyOperationStatus(status)).toBe(expectedKind)
  })

  it('does not label an unknown operation type as an Oxide update', () => {
    expect(formatOperationType('PluginUpdate')).toBe('Unknown operation')
  })

  it('makes an unknown status explicit', () => {
    expect(formatOperationStatus('WaitingForApproval')).toBe(
      'Unknown status (WaitingForApproval)',
    )
  })

  it('requires a documented type and active status for maintenance', () => {
    const operation = {
      id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
      type: 'PluginUpdate',
      status: 'Running',
      createdAt: '2026-09-29T08:30:00Z',
      startedAt: null,
      finishedAt: null,
      exitCode: null,
      error: null,
    }

    expect(isKnownActiveUpdate(operation)).toBe(false)
    expect(isKnownActiveOperation(operation)).toBe(false)
  })

  it('recognizes an active wipe as maintenance', () => {
    expect(
      isKnownActiveOperation({
        id: 'f1ccbe0c-a923-4a38-8f3d-9c0f58b4c0d4',
        type: 'Wipe',
        status: 'Running',
        createdAt: '2026-09-30T08:30:00Z',
      }),
    ).toBe(true)
  })
})
