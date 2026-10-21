import {
  deriveServerState,
  formatOperationType,
  isKnownActiveOperation,
  type ActionAvailability,
  type LifecycleAction,
  type Operation,
  type ResourceState,
  type RustVersionInfo,
  type ServerState,
  type ServerStatus,
  type StartupSnapshotResponse,
} from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import serverStyles from '../ServerPage.module.css'

interface ServerRuntimePanelProps {
  actionAvailability: ActionAvailability
  pendingLifecycleAction: LifecycleAction | null
  mutationsBlocked: boolean
  lifecycleError: string | null
  onStart: () => void
  onStop: () => void
  onRestart: () => void
  serverStatus: ResourceState<ServerStatus>
  rustVersions: ResourceState<RustVersionInfo>
  operationDetail: ResourceState<Operation | null>
  compact?: boolean
  startupSnapshot?: ResourceState<StartupSnapshotResponse>
}

const serverStateLabels: Record<ServerState, string> = {
  stopped: 'Stopped',
  starting: 'Starting',
  ready: 'Ready',
  inconsistent: 'Inconsistent / Unavailable',
}

const serverStateDescriptions: Record<
  Exclude<ServerState, 'inconsistent'>,
  string
> = {
  stopped: 'Rust server is stopped.',
  starting: 'Rust process is running. Waiting for server readiness.',
  ready: 'Rust server is running and ready.',
}

const pendingLifecycleLabels: Record<LifecycleAction, string> = {
  start: 'Starting...',
  stop: 'Stopping...',
  restart: 'Restarting...',
}

function getActiveOperation(
  operationDetail: ResourceState<Operation | null>,
): Operation | null {
  if (operationDetail.status !== 'available') {
    return null
  }

  const operation = operationDetail.data

  if (operation === null || !isKnownActiveOperation(operation)) {
    return null
  }

  return operation
}

export function ServerRuntimePanel({
  actionAvailability,
  pendingLifecycleAction,
  mutationsBlocked,
  lifecycleError,
  onStart,
  onStop,
  onRestart,
  serverStatus,
  rustVersions,
  operationDetail,
  compact = false,
  startupSnapshot,
}: ServerRuntimePanelProps) {
  if (serverStatus.status === 'loading') {
    return (
      <article aria-label="Server state" className={compact ? serverStyles.runtimeCompact : `${styles.panel} ${styles.serverPanel}`}>
        <p className={styles.loadingState} role="status">
          Loading server status.
        </p>
      </article>
    )
  }

  if (serverStatus.status === 'unavailable') {
    return (
      <article aria-label="Server state" className={compact ? serverStyles.runtimeCompact : `${styles.panel} ${styles.serverPanel}`}>
        <p className={styles.unavailableState} role="alert">
          Server status unavailable.
        </p>
        {serverStatus.message && (
          <p className={styles.secondaryText}>{serverStatus.message}</p>
        )}
      </article>
    )
  }

  const serverState = deriveServerState(serverStatus.data)
  const activeOperation = getActiveOperation(operationDetail)
  const maintenanceActive = activeOperation !== null

  if (compact) {
    const currentOperation = pendingLifecycleAction
      ? pendingLifecycleLabels[pendingLifecycleAction]
      : activeOperation
        ? `${formatOperationType(activeOperation.type)} · ${activeOperation.status}`
        : 'None'
    const session = startupSnapshot?.status === 'available'
      ? startupSnapshot.data.sessionId ?? '—'
      : startupSnapshot?.status === 'unavailable' ? 'Unavailable' : '—'

    return (
      <div aria-label="Server state" className={serverStyles.runtimeCompact}>
        <div className={serverStyles.runtimeState}>
          <strong>{maintenanceActive ? 'Maintenance' : serverStateLabels[serverState]}</strong>
          <span className={serverStyles.runtimeMuted}>
            Running: {serverStatus.data.isRunning ? 'Yes' : 'No'} · Ready: {serverStatus.data.isReady ? 'Yes' : 'No'}
          </span>
        </div>
        <dl className={serverStyles.runtimeRows}>
          <div><dt>Container</dt><dd>{serverStatus.data.isRunning ? 'Running' : 'Stopped'}</dd></div>
          <div><dt>Startup session</dt><dd>{session}</dd></div>
          <div><dt>Current operation</dt><dd>{currentOperation}</dd></div>
        </dl>
        {activeOperation && (
          <p className={serverStyles.runtimeMuted}>
            Underlying server state: {serverStateLabels[serverState]}.
          </p>
        )}
        {serverState === 'inconsistent' && (
          <p className={serverStyles.runtimeWarning} role="alert">
            Server reported an invalid running and ready combination.
          </p>
        )}
        {lifecycleError && (
          <p className={serverStyles.runtimeWarning} role="alert">{lifecycleError}</p>
        )}
        {serverState === 'stopped' && rustVersions.status === 'available' &&
          rustVersions.data.installedBuildId === null && (
            <p className={serverStyles.runtimeNote}>
              Rust is not installed. Use Install Rust below before starting the server.
            </p>
          )}
      </div>
    )
  }

  return (
    <article
      aria-label="Server state"
      className={compact ? serverStyles.runtimeCompact : `${styles.panel} ${styles.serverPanel}`}
    >
      <div className={styles.panelHeader}>
        <h3>Server status</h3>
        <span
          className={`${styles.statusBadge} ${
            maintenanceActive
              ? styles.statusMaintenance
              : serverState === 'inconsistent'
                ? styles.statusUnavailable
                : styles[`status${serverState[0].toUpperCase()}${serverState.slice(1)}`]
          }`}
        >
          {maintenanceActive ? 'Maintenance' : serverStateLabels[serverState]}
        </span>
      </div>

      {activeOperation ? (
        <>
          <div className={styles.maintenanceContext}>
            <p>
              {formatOperationType(activeOperation.type)} is{' '}
              {activeOperation.status.toLowerCase()}.
            </p>
          </div>
          <p className={styles.secondaryText}>
            Underlying server state: {serverStateLabels[serverState]}.
          </p>
        </>
      ) : serverState === 'inconsistent' ? (
        <p className={styles.unavailableState} role="alert">
          Server reported an invalid running and ready combination.
        </p>
      ) : (
        <p className={styles.description}>{serverStateDescriptions[serverState]}</p>
      )}

      {maintenanceActive && serverState === 'inconsistent' && (
        <p className={styles.unavailableState} role="alert">
          Server reported an invalid running and ready combination.
        </p>
      )}

      <dl className={styles.keyValueList}>
        <div>
          <dt>Running</dt>
          <dd>{serverStatus.data.isRunning ? 'Yes' : 'No'}</dd>
        </div>
        <div>
          <dt>Ready</dt>
          <dd>{serverStatus.data.isReady ? 'Yes' : 'No'}</dd>
        </div>
      </dl>

      {serverState === 'stopped' && rustVersions.status === 'available' &&
        rustVersions.data.installedBuildId === null && (
          <p className={styles.secondaryText}>
            Rust is not installed. Use Install Rust in Software above before starting the server.
          </p>
        )}

      {lifecycleError && (
        <p className={styles.unavailableState} role="alert">
          {lifecycleError}
        </p>
      )}

      {pendingLifecycleAction && (
        <p className={styles.secondaryText} role="status">
          {pendingLifecycleLabels[pendingLifecycleAction]}
        </p>
      )}

      <div aria-label="Lifecycle controls" className={styles.lifecycleActions}>
        <button
          disabled={mutationsBlocked || !actionAvailability.start}
          onClick={onStart}
          type="button"
        >
          {pendingLifecycleAction === 'start' ? 'Starting...' : 'Start'}
        </button>
        <button
          disabled={mutationsBlocked || !actionAvailability.stop}
          onClick={onStop}
          type="button"
        >
          {pendingLifecycleAction === 'stop' ? 'Stopping...' : 'Stop'}
        </button>
        <button
          disabled={mutationsBlocked || !actionAvailability.restart}
          onClick={onRestart}
          type="button"
        >
          {pendingLifecycleAction === 'restart' ? 'Restarting...' : 'Restart'}
        </button>
      </div>
    </article>
  )
}
