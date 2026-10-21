import { environment } from '../config/environment'

import { deleteNoContent } from './delete-no-content'
import { getJson } from './get-json'
import { postJson, postJsonWithBody } from './post-json'
import type {
  CreateSnapshotRequest,
  SnapshotOperationAcceptedResponse,
  SnapshotOperationResponse,
  SnapshotResponse,
  SnapshotSummaryResponse,
  SnapshotType,
} from './snapshots-types'

const snapshotsPath = '/api/snapshots'
const operationsPath = '/api/operations'

function createSnapshotsUrl(path: string): string {
  return `${environment.snapshotsApiBaseUrl.replace(/\/+$/, '')}${path}`
}

/** Lists snapshot summaries in the API's newest-first order. */
export function getSnapshots(
  signal?: AbortSignal,
): Promise<SnapshotSummaryResponse[]> {
  return getJson<SnapshotSummaryResponse[]>(createSnapshotsUrl(snapshotsPath), signal)
}

/** Gets one snapshot by its opaque stable ID. */
export function getSnapshot(
  id: string,
  signal?: AbortSignal,
): Promise<SnapshotResponse> {
  return getJson<SnapshotResponse>(
    createSnapshotsUrl(`${snapshotsPath}/${encodeURIComponent(id)}`),
    signal,
  )
}

/** Queues creation of a Fast or Consistent snapshot. */
export function createSnapshot(
  type: SnapshotType,
  signal?: AbortSignal,
): Promise<SnapshotOperationAcceptedResponse> {
  const request: CreateSnapshotRequest = { type }

  return postJsonWithBody<
    SnapshotOperationAcceptedResponse,
    CreateSnapshotRequest
  >(createSnapshotsUrl(snapshotsPath), request, signal)
}

/** Queues destructive restoration of a snapshot by its opaque stable ID. */
export function restoreSnapshot(
  id: string,
  signal?: AbortSignal,
): Promise<SnapshotOperationAcceptedResponse> {
  return postJson<SnapshotOperationAcceptedResponse>(
    createSnapshotsUrl(`${snapshotsPath}/${encodeURIComponent(id)}/restore`),
    signal,
  )
}

/** Deletes a snapshot archive and its metadata. */
export function deleteSnapshot(id: string, signal?: AbortSignal): Promise<void> {
  return deleteNoContent(
    createSnapshotsUrl(`${snapshotsPath}/${encodeURIComponent(id)}`),
    signal,
  )
}

/** Gets the current state of a previously accepted snapshot operation. */
export function getSnapshotOperation(
  operationId: string,
  signal?: AbortSignal,
): Promise<SnapshotOperationResponse> {
  return getJson<SnapshotOperationResponse>(
    createSnapshotsUrl(`${operationsPath}/${encodeURIComponent(operationId)}`),
    signal,
  )
}
