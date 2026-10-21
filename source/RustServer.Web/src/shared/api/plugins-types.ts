/** Source shapes published by the RustServer.Plugins API. */

/** Mirrors the Plugins API's `GetEnvironmentResponse` schema. */
export interface GetEnvironmentResponse {
  environment: string
  systemState: string
}

/** Mirrors the Plugins API's `BaselineResponse` schema. */
export interface BaselineResponse {
  environment: string
  source: string
  release: string | null
  pluginsCommit: string
  configsCommit: string
  appliedAt: string
}

/** Mirrors the Plugins API's `GetBaselineResponse` schema. */
export interface GetBaselineResponse {
  status: string
  baseline: BaselineResponse | null
}

/** Mirrors the Plugins API's `GetGitRepositoryStatusResponse` schema. */
export interface GetGitRepositoryStatusResponse {
  configured: boolean
  localBranch: string | null
  localHead: string | null
  remoteBranch: string | null
  remoteHead: string | null
  workspace: string
  relation: string
  diagnostic: string | null
}

/** Mirrors the Plugins API's `GetGitStatusResponse` schema. */
export interface GetGitStatusResponse {
  gitStatus: string
  plugins: GetGitRepositoryStatusResponse
  configs: GetGitRepositoryStatusResponse
  lastFetchAt: string | null
}

/** Mirrors the Plugins API's `RuntimeDiffChangeResponse` schema. */
export interface RuntimeDiffChangeResponse {
  repository: string
  path: string
  change: string
}

/** Mirrors the Plugins API's `GetRuntimeDiffResponse` schema. */
export interface GetRuntimeDiffResponse {
  status: string
  changes: RuntimeDiffChangeResponse[]
}

/** Mirrors the Plugins API's `RuntimeDiffContentSideResponse` schema. */
export interface RuntimeDiffContentSideResponse {
  kind: string
  text: string | null
}

/** Mirrors the Plugins API's `RuntimeDiffContentResponse` schema. */
export interface RuntimeDiffContentResponse {
  repository: string
  path: string
  change: string
  baseline: RuntimeDiffContentSideResponse
  runtime: RuntimeDiffContentSideResponse
}

/** Mirrors the Plugins API's `GetRuntimeDiffContentResponse` schema. */
export interface GetRuntimeDiffContentResponse {
  status: string
  content: RuntimeDiffContentResponse
}

/** Mirrors the Plugins API's `ReleaseResponse` schema. */
export interface ReleaseResponse {
  version: string
  branch: string
  status: string
  current: boolean
  matchesBaseline: boolean | null
  pluginsHead: string | null
  configsHead: string | null
}

/** Mirrors the Plugins API's `GetReleasesResponse` schema. */
export interface GetReleasesResponse {
  releases: ReleaseResponse[]
}

/** Mirrors the Plugins API's `UpdateEnvironmentRequest` schema. */
export interface UpdateEnvironmentRequest {
  environment: string | null
}

/** Mirrors the Plugins API's `UpdateEnvironmentResponse` schema. */
export interface UpdateEnvironmentResponse {
  environment: string
}

/** Mirrors the Plugins API's `FetchGitResponse` schema. */
export interface FetchGitResponse {
  operationId: string
}

/** Mirrors the Plugins API's `GitSyncRequest` schema. */
export interface GitSyncRequest {
  repository: string | null
}

/** Mirrors the Plugins API's `GitSyncResponse` schema. */
export interface GitSyncResponse {
  operationId: string
}

/** Mirrors the Plugins API's `TargetSourceRequest` schema. */
export interface TargetSourceRequest {
  source: string | null
  release?: string | null
}

/** Mirrors the Plugins API's `SourceResponse` schema. */
export interface SourceResponse {
  type: string
  release: string | null
}

/** Mirrors the Plugins API's `CurrentSourceDiagnosticResponse` schema. */
export interface CurrentSourceDiagnosticResponse {
  pluginsBranch: string | null
  configsBranch: string | null
}

/** Mirrors the Plugins API's `SourcePreviewBaselineResponse` schema. */
export interface SourcePreviewBaselineResponse {
  environment: string
  source: string
  release: string | null
  pluginsCommit: string
  configsCommit: string
  appliedAt: string
}

/** Mirrors the Plugins API's `TargetGitResponse` schema. */
export interface TargetGitResponse {
  pluginsHead: string
  configsHead: string
}

/** Mirrors the Plugins API's `SourcePreviewCountsResponse` schema. */
export interface SourcePreviewCountsResponse {
  added: number
  modified: number
  deleted: number
}

/** Mirrors the Plugins API's `SourcePreviewChangeResponse` schema. */
export interface SourcePreviewChangeResponse {
  repository: string
  path: string
  change: string
}

/** Mirrors the Plugins API's `SourcePreviewResponse` schema. */
export interface SourcePreviewResponse {
  environment: string
  currentSource: SourceResponse | null
  currentSourceDiagnostic: CurrentSourceDiagnosticResponse | null
  baseline: SourcePreviewBaselineResponse | null
  targetSource: SourceResponse
  targetGit: TargetGitResponse
  counts: SourcePreviewCountsResponse
  changes: SourcePreviewChangeResponse[]
}

/** Mirrors the Plugins API's `ApplySourceResponse` schema. */
export interface ApplySourceResponse {
  operationId: string
}

/** Mirrors the Plugins API's `CommitRuntimeSelectionRequest` schema. */
export interface CommitRuntimeSelectionRequest {
  repository: string | null
  path: string | null
}

/** Mirrors the Plugins API's `CommitRuntimeRequest` schema. */
export interface CommitRuntimeRequest {
  message: string | null
  changes: CommitRuntimeSelectionRequest[] | null
}

/** Mirrors the Plugins API's `CommitRuntimeResponse` schema. */
export interface CommitRuntimeResponse {
  operationId: string
}

/** Mirrors the Plugins API's `RevertRuntimeRequest` schema. */
export interface RevertRuntimeRequest {
  changes: CommitRuntimeSelectionRequest[] | null
}

/** Mirrors the Plugins API's `RevertRuntimeResponse` schema. */
export interface RevertRuntimeResponse {
  operationId: string
}

/** Mirrors the Plugins API's `OperationSourceResponse` schema. */
export interface OperationSourceResponse {
  type: string
  release: string | null
}

/** Mirrors the Plugins API's `OperationStepResponse` schema. */
export interface OperationStepResponse {
  name: string
  status: string
}

/** Mirrors the Plugins API's `OperationFailureResponse` schema. */
export interface OperationFailureResponse {
  phase: string
  code: string
  message: string
  repository: string | null
  filePath: string | null
}

/** Mirrors the Plugins API's `OperationRollbackResponse` schema. */
export interface OperationRollbackResponse {
  status: string
  message: string | null
}

/** Mirrors the Plugins API's `OperationChangeCountsResponse` schema. */
export interface OperationChangeCountsResponse {
  added: number
  modified: number
  deleted: number
}

/** Mirrors the Plugins API's `OperationFileChangeResponse` schema. */
export interface OperationFileChangeResponse {
  repository: string
  path: string
  change: string
}

/** Mirrors the Plugins API's `OperationGitStateResponse` schema. */
export interface OperationGitStateResponse {
  repository: string
  branch: string | null
  head: string | null
  relation: string
  diagnostic: string | null
}

/** Mirrors the Plugins API's `OperationSyncResponse` schema. */
export interface OperationSyncResponse {
  repository: string
  initialRelation: string
  behavior: string
  manualInterventionRequired: boolean
}

/** Mirrors the Plugins API's `GetOperationResponse` schema. */
export interface GetPluginsOperationResponse {
  operationId: string
  type: string
  status: string
  currentPhase: string
  source: OperationSourceResponse | null
  steps: OperationStepResponse[]
  failure: OperationFailureResponse | null
  rollback: OperationRollbackResponse
  counts: OperationChangeCountsResponse
  changes: OperationFileChangeResponse[]
  sync: OperationSyncResponse | null
  finalGitState: OperationGitStateResponse[]
  previousBaseline: BaselineResponse | null
  finalBaseline: BaselineResponse | null
  startedAt: string
  completedAt: string | null
}

/** Mirrors the Plugins API's `OperationHistoryItemResponse` schema. */
export interface OperationHistoryItemResponse {
  operationId: string
  type: string
  status: string
  startedAt: string
  completedAt: string | null
  changedFiles: number
}

/** Mirrors the Plugins API's `GetOperationsResponse` schema. */
export interface GetPluginsOperationsResponse {
  items: OperationHistoryItemResponse[]
  nextCursor: string | null
}

/** Mirrors the Plugins API's shared `ApiErrorResponse` schema. */
export interface PluginsApiErrorResponse {
  code: string
  message: string
  details: Record<string, unknown | null> | null
}
