/** Source shapes published by the RustServer.Snapshots API. */

/** The exact snapshot consistency vocabulary published by the API. */
export type SnapshotType = 'fast' | 'consistent'

/** Mirrors the Snapshots API's `CreateSnapshotRequest` schema. */
export interface CreateSnapshotRequest {
  type: SnapshotType
}

/** Mirrors the Snapshots API's `OperationAcceptedResponse` schema. */
export interface SnapshotOperationAcceptedResponse {
  operationId: string
  status: SnapshotOperationStatus
}

/** The exact snapshot operation status vocabulary published by the API. */
export type SnapshotOperationStatus =
  | 'pending'
  | 'running'
  | 'succeeded'
  | 'failed'

/** The exact snapshot operation kind vocabulary published by the API. */
export type SnapshotOperationType = 'createSnapshot' | 'restoreSnapshot'

/** Mirrors the Snapshots API's `SnapshotOperationResponse` schema. */
export interface SnapshotOperationResponse {
  operationId: string
  type: SnapshotOperationType
  status: SnapshotOperationStatus
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
  error: string | null
}

/** Mirrors the Snapshots API's `SnapshotSummaryResponse` schema. */
export interface SnapshotSummaryResponse {
  id: string
  createdAt: string
  type: SnapshotType
  sizeBytes: number
}

/** Mirrors the Snapshots API's `SnapshotResponse` schema. */
export interface SnapshotResponse {
  id: string
  createdAt: string
  type: SnapshotType
  sizeBytes: number
}
