import { environment } from '../config/environment'

import { getJson } from './get-json'
import { postJson, postJsonWithBody } from './post-json'
import { putJson } from './put-json'
import type {
  OperationAcceptedResponse,
  OperationResponse,
  NetworkAccessResponse,
  ServerConfigurationResponse,
  ServerStatus,
  StartupSchemaResponse,
  StartupSnapshotResponse,
  UpdateNetworkAccessRequest,
  UpdateServerConfigurationRequest,
  WipePlanRequest,
} from './control-types'

const serverStatusPath = '/api/server/status'
const serverConfigurationPath = '/api/server/configuration'
const networkAccessPath = '/api/server/network-access'
const startServerPath = '/api/server/start'
const stopServerPath = '/api/server/stop'
const restartServerPath = '/api/server/restart'
const startupSchemaPath = '/api/server/startup/schema'
const startupSnapshotPath = '/api/server/startup'
const rustUpdatePath = '/api/server/update'
const oxideUpdatePath = '/api/updates/oxide'
const operationPath = '/api/operations'
const wipePath = '/api/server/wipe'

function createControlUrl(path: string): string {
  return `${environment.controlApiBaseUrl.replace(/\/+$/, '')}${path}`
}

/** Gets Docker lifecycle and Rust server readiness state. */
export function getServerStatus(signal?: AbortSignal): Promise<ServerStatus> {
  return getJson<ServerStatus>(createControlUrl(serverStatusPath), signal)
}

/** Gets desired configuration and the configuration effective for the current Rust process. */
export function getServerConfiguration(
  signal?: AbortSignal,
): Promise<ServerConfigurationResponse> {
  return getJson<ServerConfigurationResponse>(
    createControlUrl(serverConfigurationPath),
    signal,
  )
}

/** Updates desired configuration without restarting the Rust server. */
export function updateServerConfiguration(
  request: UpdateServerConfigurationRequest,
  signal?: AbortSignal,
): Promise<ServerConfigurationResponse> {
  return putJson<ServerConfigurationResponse, UpdateServerConfigurationRequest>(
    createControlUrl(serverConfigurationPath),
    request,
    signal,
  )
}

/** Gets the persisted public reachability mode and maintenance UDP port. */
export function getNetworkAccess(signal?: AbortSignal): Promise<NetworkAccessResponse> {
  return getJson<NetworkAccessResponse>(createControlUrl(networkAccessPath), signal)
}

/** Applies network reachability without restarting the Rust server. */
export function updateNetworkAccess(
  request: UpdateNetworkAccessRequest,
  signal?: AbortSignal,
): Promise<NetworkAccessResponse> {
  return putJson<NetworkAccessResponse, UpdateNetworkAccessRequest>(
    createControlUrl(networkAccessPath),
    request,
    signal,
  )
}

/** Starts the Rust server. */
export function startServer(signal?: AbortSignal): Promise<ServerStatus> {
  return postJson<ServerStatus>(createControlUrl(startServerPath), signal)
}

/** Stops the Rust server. */
export function stopServer(signal?: AbortSignal): Promise<ServerStatus> {
  return postJson<ServerStatus>(createControlUrl(stopServerPath), signal)
}

/** Restarts the Rust server. */
export function restartServer(signal?: AbortSignal): Promise<ServerStatus> {
  return postJson<ServerStatus>(createControlUrl(restartServerPath), signal)
}

/** Gets the fixed presentation schema for Rust server startup milestones. */
export function getStartupSchema(
  signal?: AbortSignal,
): Promise<StartupSchemaResponse> {
  return getJson<StartupSchemaResponse>(createControlUrl(startupSchemaPath), signal)
}

/** Gets startup progress for the current Rust server run. */
export function getStartupSnapshot(
  signal?: AbortSignal,
): Promise<StartupSnapshotResponse> {
  return getJson<StartupSnapshotResponse>(
    createControlUrl(startupSnapshotPath),
    signal,
  )
}

/** Queues an asynchronous Rust server update operation. */
export function requestRustUpdate(
  signal?: AbortSignal,
): Promise<OperationAcceptedResponse> {
  return postJson<OperationAcceptedResponse>(
    createControlUrl(rustUpdatePath),
    signal,
  )
}

/** Queues an asynchronous Oxide update operation. */
export function requestOxideUpdate(
  signal?: AbortSignal,
): Promise<OperationAcceptedResponse> {
  return postJson<OperationAcceptedResponse>(
    createControlUrl(oxideUpdatePath),
    signal,
  )
}

/** Queues an asynchronous wipe operation with an explicit semantic plan. */
export function requestWipe(
  plan: WipePlanRequest,
  signal?: AbortSignal,
): Promise<OperationAcceptedResponse> {
  return postJsonWithBody<OperationAcceptedResponse, WipePlanRequest>(
    createControlUrl(wipePath),
    plan,
    signal,
  )
}

/** Gets the current detail for one operation ID already known by the UI. */
export function getOperation(
  id: string,
  signal?: AbortSignal,
): Promise<OperationResponse> {
  return getJson<OperationResponse>(
    createControlUrl(`${operationPath}/${encodeURIComponent(id)}`),
    signal,
  )
}
