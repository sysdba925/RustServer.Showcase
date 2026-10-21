import { environment } from '../config/environment'

import { getJson } from './get-json'
import { postFormData } from './post-form-data'
import { postJson } from './post-json'
import { postJsonWithBody } from './post-json'
import { putJson } from './put-json'
import type {
  ChangeEnvironmentRequest,
  CommitUpdatePackageRequest,
  CommitUpdatePackageResponse,
  DevelopmentDeploymentResponse,
  EnvironmentResponse,
  IncomingChangeDiffResponse,
  IncomingChangesResponse,
  ProductionDeploymentResponse,
  ReleaseDeploymentResponse,
  ReleaseResponse,
  StagingRuntimeChangeDiffResponse,
  StagingRuntimeChangeDiscardRequest,
  StagingRuntimeChangeDiscardResponse,
  StagingRuntimeChangeImportRequest,
  StagingRuntimeChangeImportResponse,
  StagingRuntimeChangesResponse,
  UpdatePackageResponse,
} from './plugins-types'

const releasesPath = '/api/releases'
const environmentPath = '/api/environment'
const developmentDeploymentPath = '/api/deployments/development'
const productionDeploymentPath = '/api/production/deployment'
const updatePackagesPath = '/api/update-packages'
const stagingRuntimeChangesPath = '/api/staging/runtime-changes'

function createPluginsUrl(path: string): string {
  return `${environment.pluginsApiBaseUrl.replace(/\/+$/, '')}${path}`
}

/** Gets the current persistent logical Plugins Environment. */
export function getEnvironment(signal?: AbortSignal): Promise<EnvironmentResponse> {
  return getJson<EnvironmentResponse>(createPluginsUrl(environmentPath), signal)
}

/** Changes the persistent logical Plugins Environment without deploying. */
export function changeEnvironment(
  request: ChangeEnvironmentRequest,
  signal?: AbortSignal,
): Promise<EnvironmentResponse> {
  return putJson<EnvironmentResponse, ChangeEnvironmentRequest>(
    createPluginsUrl(environmentPath),
    request,
    signal,
  )
}

/** Lists immutable releases, newest first. */
export function getReleases(signal?: AbortSignal): Promise<ReleaseResponse[]> {
  return getJson<ReleaseResponse[]>(createPluginsUrl(releasesPath), signal)
}

/** Gets one immutable release by its stable ID. */
export function getRelease(
  releaseId: string,
  signal?: AbortSignal,
): Promise<ReleaseResponse> {
  return getJson<ReleaseResponse>(
    createPluginsUrl(`${releasesPath}/${encodeURIComponent(releaseId)}`),
    signal,
  )
}

/** Creates an immutable release from the current clean Development state. */
export function createRelease(signal?: AbortSignal): Promise<ReleaseResponse> {
  return postJson<ReleaseResponse>(createPluginsUrl(releasesPath), signal)
}

/** Deploys one immutable Release while the logical Environment is Staging. */
export function deployReleaseToStaging(
  releaseId: string,
  signal?: AbortSignal,
): Promise<ReleaseDeploymentResponse> {
  return postJson<ReleaseDeploymentResponse>(
    createPluginsUrl(`/api/staging/releases/${encodeURIComponent(releaseId)}/deploy`),
    signal,
  )
}

/** Deploys the current clean Development state while the logical Environment is Staging. */
export function deployDevelopmentToStaging(
  signal?: AbortSignal,
): Promise<DevelopmentDeploymentResponse> {
  return postJson<DevelopmentDeploymentResponse>(
    createPluginsUrl(developmentDeploymentPath),
    signal,
  )
}

/** Deploys one immutable Release while the logical Environment is Production. */
export function deployReleaseToProduction(
  releaseId: string,
  signal?: AbortSignal,
): Promise<ProductionDeploymentResponse> {
  return postJson<ProductionDeploymentResponse>(
    createPluginsUrl(`/api/production/releases/${encodeURIComponent(releaseId)}/deploy`),
    signal,
  )
}

/** Gets the immutable Release currently recorded in the Production Environment. */
export function getProductionDeployment(
  signal?: AbortSignal,
): Promise<ProductionDeploymentResponse> {
  return getJson<ProductionDeploymentResponse>(
    createPluginsUrl(productionDeploymentPath),
    signal,
  )
}

/** Uploads and synchronously inspects one .cs or .zip update package. */
export function uploadUpdatePackage(file: File, signal?: AbortSignal): Promise<UpdatePackageResponse> {
  const formData = new FormData()
  formData.append('file', file)
  return postFormData<UpdatePackageResponse>(createPluginsUrl(updatePackagesPath), formData, signal)
}

/** Gets an update package by its server-owned, reloadable ID. */
export function getUpdatePackage(id: string, signal?: AbortSignal): Promise<UpdatePackageResponse> {
  return getJson<UpdatePackageResponse>(
    createPluginsUrl(`${updatePackagesPath}/${encodeURIComponent(id)}`),
    signal,
  )
}

/** Gets the current incoming comparison and its optimistic-concurrency heads. */
export function getIncomingChanges(id: string, signal?: AbortSignal): Promise<IncomingChangesResponse> {
  return getJson<IncomingChangesResponse>(
    createPluginsUrl(`${updatePackagesPath}/${encodeURIComponent(id)}/changes`),
    signal,
  )
}

/** Gets a text diff using the exact workspace head supplied by the comparison. */
export function getIncomingChangeDiff(
  id: string,
  path: string,
  workspaceHead: string,
  signal?: AbortSignal,
): Promise<IncomingChangeDiffResponse> {
  const query = new URLSearchParams({ path, workspaceHead })
  return getJson<IncomingChangeDiffResponse>(
    createPluginsUrl(`${updatePackagesPath}/${encodeURIComponent(id)}/diff?${query.toString()}`),
    signal,
  )
}

/** Commits selected managed paths as one logical update-package operation. */
export function commitUpdatePackage(
  id: string,
  request: CommitUpdatePackageRequest,
  signal?: AbortSignal,
): Promise<CommitUpdatePackageResponse> {
  return postJsonWithBody<CommitUpdatePackageResponse, CommitUpdatePackageRequest>(
    createPluginsUrl(`${updatePackagesPath}/${encodeURIComponent(id)}/commit`),
    request,
    signal,
  )
}

/** Reads runtime changes against the exact baseline recorded for the Staging deployment. */
export function getStagingRuntimeChanges(
  signal?: AbortSignal,
): Promise<StagingRuntimeChangesResponse> {
  return getJson<StagingRuntimeChangesResponse>(createPluginsUrl(stagingRuntimeChangesPath), signal)
}

/** Reads a current Staging runtime text diff for one managed logical path. */
export function getStagingRuntimeChangeDiff(
  path: string,
  signal?: AbortSignal,
): Promise<StagingRuntimeChangeDiffResponse> {
  const query = new URLSearchParams({ path })
  return getJson<StagingRuntimeChangeDiffResponse>(
    createPluginsUrl(`${stagingRuntimeChangesPath}/diff?${query.toString()}`),
    signal,
  )
}

/** Imports selected current Staging runtime changes into Development as one logical operation. */
export function importStagingRuntimeChanges(
  request: StagingRuntimeChangeImportRequest,
  signal?: AbortSignal,
): Promise<StagingRuntimeChangeImportResponse> {
  return postJsonWithBody<StagingRuntimeChangeImportResponse, StagingRuntimeChangeImportRequest>(
    createPluginsUrl(`${stagingRuntimeChangesPath}/import`),
    request,
    signal,
  )
}

/** Restores selected current Staging runtime changes to the deployment baseline. */
export function discardStagingRuntimeChanges(
  request: StagingRuntimeChangeDiscardRequest,
  signal?: AbortSignal,
): Promise<StagingRuntimeChangeDiscardResponse> {
  return postJsonWithBody<StagingRuntimeChangeDiscardResponse, StagingRuntimeChangeDiscardRequest>(
    createPluginsUrl(`${stagingRuntimeChangesPath}/discard`),
    request,
    signal,
  )
}
