import type {
  Operation,
  OperationStatus,
  OperationType,
  OxideVersionInfo,
  RustRunningInfo,
  RustVersionInfo,
  ServerConfigurationResponse,
  ServerStatus,
  StartupMilestoneResponse,
  StartupMilestoneState,
  StartupSchemaResponse,
  StartupSnapshotResponse,
  WipePlanRequest,
} from '../../shared/api'

/** DERIVED: local request state for an independently loaded resource. */
export type ResourceState<T> =
  | { status: 'loading' }
  | { status: 'available'; data: T }
  | { status: 'unavailable'; message?: string }

/** DERIVED: frontend runtime state; this is not an API field. */
export type ServerState = 'stopped' | 'starting' | 'ready' | 'inconsistent'

export function deriveServerState(status: ServerStatus): ServerState {
  if (!status.isRunning) {
    return status.isReady ? 'inconsistent' : 'stopped'
  }

  return status.isReady ? 'ready' : 'starting'
}

export type LifecycleAction = 'start' | 'stop' | 'restart'
export type UpdateTarget = 'rust' | 'oxide'
export type OperationTarget = UpdateTarget | 'wipe'

/**
 * The accepted response has no type or timestamps, so it stays distinct from
 * the authoritative OperationResponse obtained by a later lookup.
 */
export interface AcceptedOperationContext {
  id: string
  status: OperationStatus
  target: OperationTarget
}

export function formatUpdateTarget(target: UpdateTarget): string {
  return target === 'rust' ? 'Rust' : 'Oxide'
}

export function formatOperationTarget(target: OperationTarget): string {
  return target === 'wipe' ? 'Wipe' : `${formatUpdateTarget(target)} update`
}

export type OperationStatusKind = 'active' | 'terminal' | 'unknown'

/**
 * New backend operation statuses remain explicit and must not be treated as
 * terminal until their contract semantics are understood.
 */
export function classifyOperationStatus(
  status: OperationStatus,
): OperationStatusKind {
  switch (status) {
    case 'Pending':
    case 'Running':
      return 'active'
    case 'Succeeded':
    case 'Failed':
      return 'terminal'
    default:
      return 'unknown'
  }
}

/** A Maintenance overlay requires a documented operation type and active status. */
export function isKnownActiveOperation(operation: Operation): boolean {
  return (
    (operation.type === 'RustUpdate' ||
      operation.type === 'OxideUpdate' ||
      operation.type === 'Wipe') &&
    classifyOperationStatus(operation.status) === 'active'
  )
}

/** Whether an active operation is one of the software update operations. */
export function isKnownActiveUpdate(operation: Operation): boolean {
  return (
    (operation.type === 'RustUpdate' || operation.type === 'OxideUpdate') &&
    classifyOperationStatus(operation.status) === 'active'
  )
}

export function formatOperationType(type: OperationType): string {
  switch (type) {
    case 'RustUpdate':
      return 'Rust update'
    case 'OxideUpdate':
      return 'Oxide update'
    case 'Wipe':
      return 'Wipe'
    default:
      return 'Unknown operation'
  }
}

export function formatOperationStatus(status: OperationStatus): string {
  return classifyOperationStatus(status) === 'unknown'
    ? `Unknown status (${status})`
    : status
}

/**
 * DERIVED: Oxide needs a restart only when a running version is known and it
 * differs from the installed version. Rust runtime metadata has no Build ID,
 * so an equivalent Rust rule cannot be derived.
 */
export function deriveOxideRestartRequired(oxide: OxideVersionInfo): boolean {
  return (
    oxide.installedVersion != null &&
    oxide.runningVersion != null &&
    oxide.runningVersion !== oxide.installedVersion
  )
}

/** Input to shared lifecycle and update availability rules. */
export interface ControlPlaneInput {
  serverStatus: ResourceState<ServerStatus>
  rustVersions: ResourceState<RustVersionInfo>
  oxideVersions: ResourceState<OxideVersionInfo>
  operationDetail: ResourceState<Operation | null>
}

export function getWipeUnavailableReason(
  serverStatus: ResourceState<ServerStatus>,
  mutationsBlocked: boolean,
  operationDetail: ResourceState<Operation | null>,
): string | null {
  const operationBlocked =
    operationDetail.status === 'loading' ||
    (operationDetail.status === 'available' &&
      operationBlocksActions(operationDetail.data))

  if (mutationsBlocked || operationBlocked) {
    return 'Another server mutation is in progress.'
  }

  if (serverStatus.status !== 'available') {
    return 'Server status must be available to wipe.'
  }

  return deriveServerState(serverStatus.data) === 'stopped'
    ? null
    : 'Server must be stopped to wipe.'
}

export interface ActionAvailability {
  start: boolean
  stop: boolean
  restart: boolean
  rustUpdate: boolean
  oxideUpdate: boolean
}

function createUnavailableActions(): ActionAvailability {
  return {
    start: false,
    stop: false,
    restart: false,
    rustUpdate: false,
    oxideUpdate: false,
  }
}

function operationBlocksActions(operation: Operation | null): boolean {
  return (
    operation !== null &&
    classifyOperationStatus(operation.status) !== 'terminal'
  )
}

/** Derives documented lifecycle and update availability from source resources. */
export function deriveActionAvailability(
  controlPlane: ControlPlaneInput,
): ActionAvailability {
  if (controlPlane.serverStatus.status !== 'available') {
    return createUnavailableActions()
  }

  if (controlPlane.operationDetail.status === 'loading') {
    return createUnavailableActions()
  }

  if (
    controlPlane.operationDetail.status === 'available' &&
    operationBlocksActions(controlPlane.operationDetail.data)
  ) {
    return createUnavailableActions()
  }

  const serverState = deriveServerState(controlPlane.serverStatus.data)

  if (serverState === 'inconsistent') {
    return createUnavailableActions()
  }

  if (serverState === 'starting') {
    return { ...createUnavailableActions(), stop: true }
  }

  if (serverState === 'ready') {
    return { ...createUnavailableActions(), stop: true, restart: true }
  }

  // Starting Rust before SteamCMD has installed its files creates a broken
  // container. Versions distinguishes this from a merely stopped server.
  const rustInstalled =
    controlPlane.rustVersions.status === 'available' &&
    controlPlane.rustVersions.data.installedBuildId !== null

  return {
    ...createUnavailableActions(),
    start: rustInstalled,
    rustUpdate:
      controlPlane.rustVersions.status === 'available' &&
      controlPlane.rustVersions.data.updateAvailable,
    oxideUpdate:
      rustInstalled &&
      controlPlane.oxideVersions.status === 'available' &&
      controlPlane.oxideVersions.data.updateAvailable,
  }
}

export type {
  Operation,
  OperationStatus,
  OperationType,
  OxideVersionInfo,
  RustRunningInfo,
  RustVersionInfo,
  ServerStatus,
  ServerConfigurationResponse,
  StartupMilestoneResponse,
  StartupMilestoneState,
  StartupSchemaResponse,
  StartupSnapshotResponse,
  WipePlanRequest,
}
