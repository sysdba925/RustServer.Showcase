import {
  applyPluginsSource,
  commitPluginsRuntime,
  fetchPluginsGit,
  getPluginsBaseline,
  getPluginsEnvironment,
  getPluginsGitStatus,
  getPluginsOperation,
  getPluginsOperations,
  getPluginsReleases,
  getPluginsRuntimeDiffContent,
  getPluginsRuntimeDiff,
  previewPluginsSource,
  revertAllPluginsRuntime,
  revertPluginsRuntime,
  syncPluginsGit,
  updatePluginsEnvironment,
  type ApplySourceResponse,
  type CommitRuntimeRequest,
  type CommitRuntimeResponse,
  type FetchGitResponse,
  type GetBaselineResponse,
  type GetEnvironmentResponse,
  type GetGitStatusResponse,
  type GetPluginsOperationResponse,
  type GetPluginsOperationsResponse,
  type GetReleasesResponse,
  type GetRuntimeDiffContentResponse,
  type GetRuntimeDiffResponse,
  type GitSyncRequest,
  type GitSyncResponse,
  type RevertRuntimeRequest,
  type RevertRuntimeResponse,
  type SourcePreviewResponse,
  type TargetSourceRequest,
  type UpdateEnvironmentRequest,
  type UpdateEnvironmentResponse,
} from '../../shared/api'

/** Plugins-only HTTP boundary used by the screen's loaders and workflows. */
export interface PluginsApi {
  getEnvironment: (signal?: AbortSignal) => Promise<GetEnvironmentResponse>
  getBaseline: (signal?: AbortSignal) => Promise<GetBaselineResponse>
  getGitStatus: (signal?: AbortSignal) => Promise<GetGitStatusResponse>
  getRuntimeDiff: (signal?: AbortSignal) => Promise<GetRuntimeDiffResponse>
  getRuntimeDiffContent: (
    repository: string,
    path: string,
    signal?: AbortSignal,
  ) => Promise<GetRuntimeDiffContentResponse>
  getReleases: (signal?: AbortSignal) => Promise<GetReleasesResponse>
  updateEnvironment: (
    request: UpdateEnvironmentRequest,
    signal?: AbortSignal,
  ) => Promise<UpdateEnvironmentResponse>
  fetchGit: (signal?: AbortSignal) => Promise<FetchGitResponse>
  syncGit: (
    request: GitSyncRequest,
    signal?: AbortSignal,
  ) => Promise<GitSyncResponse>
  previewSource: (
    request: TargetSourceRequest,
    signal?: AbortSignal,
  ) => Promise<SourcePreviewResponse>
  applySource: (
    request: TargetSourceRequest,
    signal?: AbortSignal,
  ) => Promise<ApplySourceResponse>
  commitRuntime: (
    request: CommitRuntimeRequest,
    signal?: AbortSignal,
  ) => Promise<CommitRuntimeResponse>
  revertRuntime: (
    request: RevertRuntimeRequest,
    signal?: AbortSignal,
  ) => Promise<RevertRuntimeResponse>
  revertAllRuntime: (signal?: AbortSignal) => Promise<RevertRuntimeResponse>
  getOperations: (
    limit: number,
    cursor?: string,
    signal?: AbortSignal,
  ) => Promise<GetPluginsOperationsResponse>
  getOperation: (
    operationId: string,
    signal?: AbortSignal,
  ) => Promise<GetPluginsOperationResponse>
}

/** The production adapter; stories and focused tests can provide a deterministic one. */
export const pluginsApi: PluginsApi = {
  getEnvironment: getPluginsEnvironment,
  getBaseline: getPluginsBaseline,
  getGitStatus: getPluginsGitStatus,
  getRuntimeDiff: getPluginsRuntimeDiff,
  getRuntimeDiffContent: getPluginsRuntimeDiffContent,
  getReleases: getPluginsReleases,
  updateEnvironment: updatePluginsEnvironment,
  fetchGit: fetchPluginsGit,
  syncGit: syncPluginsGit,
  previewSource: previewPluginsSource,
  applySource: applyPluginsSource,
  commitRuntime: commitPluginsRuntime,
  revertRuntime: revertPluginsRuntime,
  revertAllRuntime: revertAllPluginsRuntime,
  getOperations: getPluginsOperations,
  getOperation: getPluginsOperation,
}
