import type {
  ActionAvailability,
  LifecycleAction,
  ResourceState,
  ServerStatus,
  StartupSnapshotResponse,
} from '../../control-plane/model'
import styles from '../ServerPage.module.css'
import { NetworkAccessPanel } from './NetworkAccessPanel'

interface ServerLifecycleBarProps {
  actionAvailability: ActionAvailability
  mutationsBlocked: boolean
  pendingLifecycleAction: LifecycleAction | null
  onStart: () => void
  onStop: () => void
  onRestart: () => void
  serverStatus: ResourceState<ServerStatus>
  startupSnapshot: ResourceState<StartupSnapshotResponse>
}

type IndicatorState = 'ok' | 'wait' | 'no' | 'unknown'

function StatusIndicator({
  label,
  state,
}: {
  label: string
  state: IndicatorState
}) {
  return (
    <span className={styles.statusIndicator} data-state={state}>
      <span aria-hidden="true" className={styles.statusIcon} />
      {label}
    </span>
  )
}

const workingText: Record<LifecycleAction, string> = {
  start: 'Starting...',
  stop: 'Stopping...',
  restart: 'Restarting...',
}

export function ServerLifecycleBar({
  actionAvailability,
  mutationsBlocked,
  pendingLifecycleAction,
  onStart,
  onStop,
  onRestart,
  serverStatus,
  startupSnapshot,
}: ServerLifecycleBarProps) {
  const status = serverStatus.status === 'available' ? serverStatus.data : null
  const running = status?.isRunning === true
  const inconsistent = status !== null && !status.isRunning && status.isReady
  const ready = running && status?.isReady === true
  const containerState: IndicatorState = status === null
    ? 'unknown'
    : running ? 'ok' : 'no'
  const readyState: IndicatorState = status === null || inconsistent
    ? 'unknown'
    : ready ? 'ok' : running ? 'wait' : 'no'

  // "Available" means the final Steam startup milestone was observed for this
  // active container session, not a live external network connectivity test.
  const availabilityKnown = status !== null && startupSnapshot.status === 'available'
  const milestoneReached = status !== null &&
    running &&
    startupSnapshot.status === 'available' &&
    startupSnapshot.data.sessionId !== null &&
    startupSnapshot.data.started
  const availableState: IndicatorState = !availabilityKnown
    ? 'unknown'
    : milestoneReached ? 'ok' : running ? 'wait' : 'no'

  const actions = [
    { action: 'start' as const, label: 'Start', enabled: actionAvailability.start, run: onStart },
    { action: 'stop' as const, label: 'Stop', enabled: actionAvailability.stop, run: onStop },
    { action: 'restart' as const, label: 'Restart', enabled: actionAvailability.restart, run: onRestart },
  ]

  return (
    <section aria-label="Lifecycle controls" className={styles.commandBar}>
      <div className={styles.lifecycleButtons}>
        {actions.map(({ action, label, enabled, run }) => {
          const working = pendingLifecycleAction === action
          return (
            <button
              aria-busy={working}
              className={`${styles.lifecycleButton} ${styles[action]} ${working ? styles.working : ''}`}
              disabled={mutationsBlocked || !enabled}
              key={action}
              onClick={run}
              type="button"
            >
              {working ? (
                <span aria-hidden="true" className={styles.spinner} />
              ) : (
                <svg aria-hidden="true" className={styles.lifecycleIcon} viewBox="0 0 20 20">
                  {action === 'start' ? (
                    <path d="M4 2.5 17 10 4 17.5Z" fill="currentColor" />
                  ) : action === 'stop' ? (
                    <rect x="3" y="3" width="14" height="14" fill="currentColor" />
                  ) : (
                    <path d="M16 7V3m0 4h-4m4 0a7 7 0 1 0 1 6" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" />
                  )}
                </svg>
              )}
              {working ? workingText[action] : label}
            </button>
          )
        })}
      </div>
      <div aria-label="Server readiness indicators" className={styles.indicators}>
        <StatusIndicator
          label={status === null ? 'Container Unknown' : running ? 'Container Running' : 'Container Stopped'}
          state={containerState}
        />
        <StatusIndicator
          label={status === null || inconsistent ? 'Rust Unknown' : ready ? 'Rust Ready' : running ? 'Rust Starting' : 'Rust Stopped'}
          state={readyState}
        />
        <StatusIndicator
          label={!availabilityKnown ? 'Availability Unknown' : milestoneReached ? 'Available' : 'Not Available'}
          state={availableState}
        />
      </div>
      <NetworkAccessPanel />
    </section>
  )
}
