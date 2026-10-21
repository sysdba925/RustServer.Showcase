import { environment } from '../config/environment'

import { getJson } from './get-json'
import { postJson, postJsonWithBody } from './post-json'
import { putJson } from './put-json'
import type {
  ApplySourceResponse,
  CommitRuntimeRequest,
  CommitRuntimeResponse,
  FetchGitResponse,
  GetBaselineResponse,
  GetEnvironmentResponse,
  GetGitStatusResponse,
  GetPluginsOperationResponse,
  GetPluginsOperationsResponse,
  GetReleasesResponse,
  GetRuntimeDiffContentResponse,
  GetRuntimeDiffResponse,
  GitSyncRequest,
  GitSyncResponse,
  RevertRuntimeRequest,
  RevertRuntimeResponse,
  SourcePreviewResponse,
  TargetSourceRequest,
  UpdateEnvironmentRequest,
  UpdateEnvironmentResponse,
} from './plugins-types'

const environmentPath = '/api/environment'
const baselinePath = '/api/baseline'
const gitStatusPath = '/api/git/status'
const runtimeDiffPath = '/api/runtime/diff'
const runtimeDiffContentPath = '/api/runtime/diff/content'
const runtimeCommitPath = '/api/runtime/commit'
const runtimeRevertPath = '/api/runtime/revert'
const releasesPath = '/api/releases'
const gitFetchPath = '/api/git/fetch'
const gitSyncPath = '/api/git/sync'
const sourcePreviewPath = '/api/source/preview'
const sourceApplyPath = '/api/source/apply'
const operationsPath = '/api/operations'

function createPluginsUrl(path: string): string {
  return `${environment.pluginsApiBaseUrl.replace(/\/+$/, '')}${path}`
}

/** Gets the persistent logical Plugins environment and high-level system state. */
export function getPluginsEnvironment(
  signal?: AbortSignal,
): Promise<GetEnvironmentResponse> {
  return getJson<GetEnvironmentResponse>(createPluginsUrl(environmentPath), signal)
}

/** Gets the persisted Applied Baseline reference, if one exists. */
export function getPluginsBaseline(signal?: AbortSignal): Promise<GetBaselineResponse> {
  return getJson<GetBaselineResponse>(createPluginsUrl(baselinePath), signal)
}

/** Gets read-only local and already-known remote Git state for both repositories. */
export function getPluginsGitStatus(signal?: AbortSignal): Promise<GetGitStatusResponse> {
  return getJson<GetGitStatusResponse>(createPluginsUrl(gitStatusPath), signal)
}

/** Gets the current managed Runtime-to-Baseline comparison. */
export function getPluginsRuntimeDiff(
  signal?: AbortSignal,
): Promise<GetRuntimeDiffResponse> {
  return getJson<GetRuntimeDiffResponse>(createPluginsUrl(runtimeDiffPath), signal)
}

/** Gets the lazily requested Baseline-to-Runtime content comparison for one changed file. */
export function getPluginsRuntimeDiffContent(
  repository: string,
  path: string,
  signal?: AbortSignal,
): Promise<GetRuntimeDiffContentResponse> {
  const search = new URLSearchParams({ repository, path })
  return getJson<GetRuntimeDiffContentResponse>(
    createPluginsUrl(`${runtimeDiffContentPath}?${search.toString()}`),
    signal,
  )
}

/** Gets the known Release catalog from existing remote-tracking refs. */
export function getPluginsReleases(signal?: AbortSignal): Promise<GetReleasesResponse> {
  return getJson<GetReleasesResponse>(createPluginsUrl(releasesPath), signal)
}

/** Changes only the persistent logical Plugins environment. */
export function updatePluginsEnvironment(
  request: UpdateEnvironmentRequest,
  signal?: AbortSignal,
): Promise<UpdateEnvironmentResponse> {
  return putJson<UpdateEnvironmentResponse, UpdateEnvironmentRequest>(
    createPluginsUrl(environmentPath),
    request,
    signal,
  )
}

/** Fetches remote-tracking refs for both managed repositories. */
export function fetchPluginsGit(signal?: AbortSignal): Promise<FetchGitResponse> {
  return postJson<FetchGitResponse>(createPluginsUrl(gitFetchPath), signal)
}

/** Lets the backend safely synchronize one named managed repository. */
export function syncPluginsGit(
  request: GitSyncRequest,
  signal?: AbortSignal,
): Promise<GitSyncResponse> {
  return postJsonWithBody<GitSyncResponse, GitSyncRequest>(
    createPluginsUrl(gitSyncPath),
    request,
    signal,
  )
}

/** Previews a source against the current managed Runtime files without mutation. */
export function previewPluginsSource(
  request: TargetSourceRequest,
  signal?: AbortSignal,
): Promise<SourcePreviewResponse> {
  return postJsonWithBody<SourcePreviewResponse, TargetSourceRequest>(
    createPluginsUrl(sourcePreviewPath),
    request,
    signal,
  )
}

/** Applies a selected Source and returns the persistent operation report identifier. */
export function applyPluginsSource(
  request: TargetSourceRequest,
  signal?: AbortSignal,
): Promise<ApplySourceResponse> {
  return postJsonWithBody<ApplySourceResponse, TargetSourceRequest>(
    createPluginsUrl(sourceApplyPath),
    request,
    signal,
  )
}

/** Commits and pushes a non-empty selection of current Runtime Diff entries. */
export function commitPluginsRuntime(
  request: CommitRuntimeRequest,
  signal?: AbortSignal,
): Promise<CommitRuntimeResponse> {
  return postJsonWithBody<CommitRuntimeResponse, CommitRuntimeRequest>(
    createPluginsUrl(runtimeCommitPath),
    request,
    signal,
  )
}

/** Restores selected Runtime Diff entries from the persisted Baseline. */
export function revertPluginsRuntime(
  request: RevertRuntimeRequest,
  signal?: AbortSignal,
): Promise<RevertRuntimeResponse> {
  return postJsonWithBody<RevertRuntimeResponse, RevertRuntimeRequest>(
    createPluginsUrl(runtimeRevertPath),
    request,
    signal,
  )
}

/** Restores the complete managed Runtime set from the persisted Baseline. */
export function revertAllPluginsRuntime(
  signal?: AbortSignal,
): Promise<RevertRuntimeResponse> {
  return postJson<RevertRuntimeResponse>(createPluginsUrl(runtimeRevertPath), signal)
}

/** Gets one newest-to-oldest page of compact persistent Plugins operation summaries. */
export function getPluginsOperations(
  limit: number,
  cursor?: string,
  signal?: AbortSignal,
): Promise<GetPluginsOperationsResponse> {
  const search = new URLSearchParams({ limit: String(limit) })
  if (cursor !== undefined) {
    search.set('cursor', cursor)
  }

  return getJson<GetPluginsOperationsResponse>(
    createPluginsUrl(`${operationsPath}?${search.toString()}`),
    signal,
  )
}

/** Gets the detailed persistent report for a known Plugins operation. */
export function getPluginsOperation(
  operationId: string,
  signal?: AbortSignal,
): Promise<GetPluginsOperationResponse> {
  return getJson<GetPluginsOperationResponse>(
    createPluginsUrl(`${operationsPath}/${encodeURIComponent(operationId)}`),
    signal,
  )
}
