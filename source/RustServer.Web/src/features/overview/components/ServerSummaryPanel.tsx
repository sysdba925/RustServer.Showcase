import { Link } from 'react-router-dom'

import {
  deriveServerState,
  formatOperationType,
  isKnownActiveOperation,
  type Operation,
  type ResourceState,
  type ServerState,
  type ServerStatus,
  type StartupSchemaResponse,
  type StartupSnapshotResponse,
} from '../../control-plane/model'
import styles from '../OverviewPage.module.css'

interface ServerSummaryPanelProps {
  serverStatus: ResourceState<ServerStatus>
  operationDetail: ResourceState<Operation | null>
  startupSchema: ResourceState<StartupSchemaResponse>
  startupSnapshot: ResourceState<StartupSnapshotResponse>
}

const serverStateLabels: Record<ServerState, string> = {
  stopped: 'Stopped',
  starting: 'Starting',
  ready: 'Ready',
  inconsistent: 'Inconsistent / Unavailable',
}

const serverStateDescriptions: Record<Exclude<ServerState, 'inconsistent'>, string> = {
  stopped: 'Rust server is stopped.',
  starting: 'Rust process is running. Waiting for server readiness.',
  ready: 'Rust server is running and ready.',
}

function CompactStartupSummary({
  startupSchema,
  startupSnapshot,
}: Pick<ServerSummaryPanelProps, 'startupSchema' | 'startupSnapshot'>) {
  if (startupSchema.status === 'loading' || startupSnapshot.status === 'loading') {
    return <p className={styles.subtle}>Startup information is loading.</p>
  }
  if (startupSchema.status === 'unavailable' || startupSnapshot.status === 'unavailable') {
    return <p className={styles.subtle}>Startup information unavailable.</p>
  }
  if (startupSnapshot.data.sessionId === null) {
    return <p className={styles.subtle}>No active startup session.</p>
  }

  const total = startupSchema.data.milestones.length
  const resolved = startupSnapshot.data.milestones
    .slice(0, total)
    .filter((state) => state !== 'pending').length

  return <p className={styles.subtle}>Startup: {resolved} / {total} milestones</p>
}

export function ServerSummaryPanel({
  serverStatus,
  operationDetail,
  startupSchema,
  startupSnapshot,
}: ServerSummaryPanelProps) {
  const state = serverStatus.status === 'available'
    ? deriveServerState(serverStatus.data)
    : null
  const activeOperation =
    operationDetail.status === 'available' &&
    operationDetail.data !== null &&
    isKnownActiveOperation(operationDetail.data)
      ? operationDetail.data
      : null

  const label = serverStatus.status === 'loading'
    ? 'Loading'
    : serverStatus.status === 'unavailable'
      ? 'Unavailable'
      : activeOperation !== null
        ? 'Maintenance'
        : serverStateLabels[state!]

  const tone = serverStatus.status !== 'available'
    ? serverStatus.status === 'loading' ? 'info' : 'error'
    : activeOperation !== null ? 'info'
      : state === 'ready' ? 'ok'
        : state === 'starting' ? 'warn'
          : state === 'stopped' ? 'off' : 'error'

  return (
    <article aria-label="Server summary" className={styles.card}>
      <header className={styles.cardHeader}>
        <h3>Server status</h3>
        <div className={styles.statusTools}>
          <span className={styles.status} data-tone={tone} aria-live="polite">{label}</span>
          <Link className={styles.manageServer} to="/server">
            Manage server <span aria-hidden="true">↗</span>
          </Link>
        </div>
      </header>
      <div className={styles.cardBody}>
        {serverStatus.status === 'loading' ? (
          <p className={styles.loading} role="status">Loading server status.</p>
        ) : serverStatus.status === 'unavailable' ? (
          <>
            <p className={styles.alert} role="alert">Server status unavailable.</p>
            {serverStatus.message && <p className={styles.subtle}>{serverStatus.message}</p>}
          </>
        ) : (
          <>
            {activeOperation !== null ? (
              <p className={styles.description}>
                {formatOperationType(activeOperation.type)} is {activeOperation.status.toLowerCase()}.
              </p>
            ) : state === 'inconsistent' ? (
              <p className={styles.alert} role="alert">
                Server reported an invalid running and ready combination.
              </p>
            ) : (
              <p className={styles.description}>{serverStateDescriptions[state!]}</p>
            )}
            {state === 'starting' && (
              <CompactStartupSummary
                startupSchema={startupSchema}
                startupSnapshot={startupSnapshot}
              />
            )}
          </>
        )}
      </div>
    </article>
  )
}
