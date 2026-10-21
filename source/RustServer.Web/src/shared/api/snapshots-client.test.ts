import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  createSnapshot,
  deleteSnapshot,
  getSnapshot,
  getSnapshotOperation,
  getSnapshots,
  restoreSnapshot,
} from './snapshots-client'
import type {
  SnapshotOperationResponse,
  SnapshotSummaryResponse,
} from './snapshots-types'

afterEach(() => {
  vi.unstubAllGlobals()
})

const snapshot: SnapshotSummaryResponse = {
  id: 'snapshot/1',
  createdAt: '2026-10-07T14:35:00Z',
  type: 'consistent',
  sizeBytes: 37_800_000,
}

const operation: SnapshotOperationResponse = {
  operationId: 'operation/1',
  type: 'createSnapshot',
  status: 'pending',
  createdAt: '2026-10-07T14:35:00Z',
  startedAt: null,
  finishedAt: null,
  error: null,
}

function successfulResponse(body?: unknown): Response {
  return {
    ok: true,
    json: vi.fn().mockResolvedValue(body),
  } as unknown as Response
}

describe('Snapshots API client', () => {
  it('uses the published list and detail paths', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse([snapshot]))
      .mockResolvedValueOnce(successfulResponse(snapshot))
    vi.stubGlobal('fetch', fetchMock)

    await expect(getSnapshots()).resolves.toEqual([snapshot])
    await expect(getSnapshot('snapshot/1')).resolves.toEqual(snapshot)

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/snapshots-api/api/snapshots', {
      method: 'GET',
      signal: undefined,
    })
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/snapshots-api/api/snapshots/snapshot%2F1',
      { method: 'GET', signal: undefined },
    )
  })

  it('uses the published create, restore, operation, and delete paths', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(successfulResponse({ operationId: operation.operationId, status: 'pending' }))
      .mockResolvedValueOnce(successfulResponse({ operationId: 'restore-operation', status: 'pending' }))
      .mockResolvedValueOnce(successfulResponse(operation))
      .mockResolvedValueOnce(successfulResponse())
    vi.stubGlobal('fetch', fetchMock)

    await createSnapshot('fast')
    await restoreSnapshot('snapshot/1')
    await getSnapshotOperation('operation/1')
    await deleteSnapshot('snapshot/1')

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/snapshots-api/api/snapshots', {
      method: 'POST',
      signal: undefined,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ type: 'fast' }),
    })
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/snapshots-api/api/snapshots/snapshot%2F1/restore',
      { method: 'POST', signal: undefined },
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      '/snapshots-api/api/operations/operation%2F1',
      { method: 'GET', signal: undefined },
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      '/snapshots-api/api/snapshots/snapshot%2F1',
      { method: 'DELETE', signal: undefined },
    )
  })
})
