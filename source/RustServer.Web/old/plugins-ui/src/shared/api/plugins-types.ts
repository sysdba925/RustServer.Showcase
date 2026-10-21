/** Source shapes published by the RustServer.Plugins API. */
export type ServiceEnvironment = 'staging' | 'production'

export interface EnvironmentResponse {
  environment: ServiceEnvironment
}

export interface ChangeEnvironmentRequest {
  environment: ServiceEnvironment
}

export interface ReleaseResponse {
  releaseId: string
  pluginsSha: string
  oxideConfigsSha: string
  createdAt: string
}

export interface ReleaseDeploymentResponse {
  source: string
  releaseId: string
  pluginsSha: string
  oxideConfigsSha: string
  deployedFiles: number
  deployedAt: string
}

export interface DevelopmentDeploymentResponse {
  source: string
  pluginsSha: string
  oxideConfigsSha: string
  deployedFiles: number
  deployedAt: string
}

/** The schema name used by the production deployment read operation. */
export type ProductionDeploymentResponse = ReleaseDeploymentResponse

/** Exact repository vocabulary published for update-package workflows. */
export type GitRepository = 'plugins' | 'oxideConfigs'

/** Exact file classification vocabulary published for update packages. */
export type PackageFileClassification = 'managed' | 'runtime' | 'artifact' | 'ignored'

/** Exact incoming comparison vocabulary published for update packages. */
export type IncomingChangeType = 'added' | 'modified' | 'unchanged'

/** Exact lifecycle vocabulary published for an uploaded update package. */
export type UpdatePackageStatus = 'received' | 'inspected' | 'failed' | 'committed'

export interface InspectedPackageFileResponse {
  path: string
  classification: PackageFileClassification
}

export interface RepositoryCommitResponse {
  repository: GitRepository
  previousHead: string
  commitSha: string
  committedPaths: string[]
}

export interface UpdatePackageResponse {
  id: string
  originalFileName: string
  status: UpdatePackageStatus
  createdAt: string
  files: InspectedPackageFileResponse[]
  commits: RepositoryCommitResponse[]
}

export interface GitRepositoryHead {
  repository: GitRepository
  head: string
}

export interface IncomingChangeResponse {
  path: string
  repository: GitRepository
  classification: PackageFileClassification
  changeType: IncomingChangeType
  textDiffAvailable: boolean
}

export interface IncomingChangesResponse {
  packageId: string
  workspaceHeads: GitRepositoryHead[]
  changes: IncomingChangeResponse[]
}

export interface IncomingChangeDiffResponse {
  packageId: string
  path: string
  repository: GitRepository
  changeType: IncomingChangeType
  workspaceHead: string
  workspaceText?: string | null
  incomingText: string
}

export interface CommitUpdatePackageRequest {
  acceptedPaths: string[]
  expectedWorkspaceHeads: {
    plugins?: string
    oxideConfigs?: string
  }
  message: string
}

export interface CommitUpdatePackageResponse {
  packageId: string
  commits: RepositoryCommitResponse[]
}

/** Exact runtime-change vocabulary published for a Staging deployment baseline. */
export type RuntimeChangeType = 'added' | 'modified' | 'deleted'

export interface RuntimeChangeResponse {
  path: string
  repository: GitRepository
  changeType: RuntimeChangeType
  textDiffAvailable: boolean
  changeToken: string
}

/** The authoritative Staging deployment baseline and its current runtime comparison. */
export interface StagingRuntimeChangesResponse {
  source: string
  pluginsSha: string
  oxideConfigsSha: string
  changes: RuntimeChangeResponse[]
}

export interface StagingRuntimeChangeDiffResponse {
  path: string
  changeType: RuntimeChangeType
  baselineText?: string | null
  runtimeText?: string | null
}

export interface StagingRuntimeChangeImportSelectionRequest {
  path: string
  expectedChangeToken: string
}

export interface StagingRuntimeChangeImportRequest {
  message: string
  changes: StagingRuntimeChangeImportSelectionRequest[]
}

export interface StagingRuntimeChangeImportResponse {
  commits: RepositoryCommitResponse[]
}

export interface StagingRuntimeChangeDiscardSelectionRequest {
  path: string
  expectedChangeToken: string
}

export interface StagingRuntimeChangeDiscardRequest {
  changes: StagingRuntimeChangeDiscardSelectionRequest[]
}

export interface StagingRuntimeChangeDiscardResponse {
  discardedPaths: string[]
}
