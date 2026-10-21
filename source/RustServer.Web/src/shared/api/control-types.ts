/** Source shapes published by the RustServer.Control API. */
export interface ServerStatus {
  isRunning: boolean
  isReady: boolean
}

/** Mirrors the Control API's `StartupMilestoneResponse` schema. */
export interface StartupMilestoneResponse {
  id: string
  label: string
}

/** Mirrors the Control API's `StartupSchemaResponse` schema. */
export interface StartupSchemaResponse {
  version: number
  milestones: StartupMilestoneResponse[]
}

/** The complete source vocabulary published for a startup milestone. */
export type StartupMilestoneState = 'pending' | 'reached' | 'skipped'

/** Mirrors the Control API's `StartupSnapshotResponse` schema. */
export interface StartupSnapshotResponse {
  sessionId: string | null
  started: boolean
  milestones: StartupMilestoneState[]
}

/** Mirrors the Control API's `ServerConfiguration` schema. */
export interface ServerConfiguration {
  hostname: string
  description: string
  maxPlayers: number
  tickRate: number
  saveInterval: number
  seed: number
  worldSize: number
}

/** Mirrors the Control API's `ServerConfigurationResponse` schema. */
export interface ServerConfigurationResponse {
  desired: ServerConfiguration
  effective: ServerConfiguration | null
}

/** Mirrors the Control API's `UpdateServerConfigurationRequest` schema. */
export interface UpdateServerConfigurationRequest {
  hostname: string
  description: string
  maxPlayers: number
  tickRate: number
  saveInterval: number
  seed: number
  worldSize: number
}

/** Mirrors the Control API's `NetworkAccessResponse` schema. */
export interface NetworkAccessResponse {
  publicEnabled: boolean
  maintenancePort: number
}

/** Mirrors the Control API's `UpdateNetworkAccessRequest` schema. */
export interface UpdateNetworkAccessRequest {
  publicEnabled: boolean
  maintenancePort: number
}

/** The exact string vocabulary published for a semantic wipe action. */
export type WipeAction = 'keep' | 'delete' | 'transform'

/** Mirrors the Control API's `PluginDataWipeActionRequest` schema. */
export interface PluginDataWipeActionRequest {
  target: string
  action: WipeAction
}

/** Mirrors the Control API's `WipePlanRequest` schema. */
export interface WipePlanRequest {
  world: WipeAction
  blueprints: WipeAction
  pluginData?: PluginDataWipeActionRequest[] | null
}

/** Mirrors the Control API's `WipeTargetResultResponse` schema. */
export interface WipeTargetResultResponse {
  target: string
  action: string
  outcome: string
  affectedItems: number
  error?: string | null
}

/** Mirrors the Control API's `WipeOperationResultResponse` schema. */
export interface WipeOperationResultResponse {
  succeeded: boolean
  targets: WipeTargetResultResponse[]
}

/**
 * The Control contract publishes operation type values as strings rather than
 * an enum, so the frontend must not narrow them to a local set of values.
 */
export type OperationType = string

/**
 * The Control contract publishes operation status values as strings rather
 * than an enum, so the frontend must not narrow them to a local set of values.
 */
export type OperationStatus = string

/** Mirrors the Control API's `OperationResponse` schema. */
export interface Operation {
  id: string
  type: OperationType
  status: OperationStatus
  createdAt: string
  startedAt?: string | null
  finishedAt?: string | null
  exitCode?: number | null
  error?: string | null
  wipeResult?: WipeOperationResultResponse | null
}

/** The schema name used by the Control API document. */
export type OperationResponse = Operation

/** Mirrors the Control API's `OperationAcceptedResponse` schema. */
export interface OperationAcceptedResponse {
  id: string
  status: OperationStatus
}

/** Mirrors the Control API's ProblemDetails schema. */
export interface ProblemDetails {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  instance?: string | null
  [key: string]: unknown
}
