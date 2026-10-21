import type {
  ResourceState,
  StartupMilestoneState,
  StartupSchemaResponse,
  StartupSnapshotResponse,
} from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import serverStyles from '../ServerPage.module.css'

interface StartupTrackingProps {
  startupSchema: ResourceState<StartupSchemaResponse>
  startupSnapshot: ResourceState<StartupSnapshotResponse>
  compact?: boolean
}

const milestoneStateLabels: Record<StartupMilestoneState, string> = {
  pending: 'Pending',
  reached: 'Reached',
  skipped: 'Skipped',
}

/**
 * Presents the ordered startup schema with the authoritative snapshot for one
 * Rust run. The snapshot is rendered as received; no milestone progress is
 * merged or inferred by the UI.
 */
export function StartupTracking({
  startupSchema,
  startupSnapshot,
  compact = false,
}: StartupTrackingProps) {
  if (startupSchema.status === 'loading' || startupSnapshot.status === 'loading') {
    return (
      <section aria-label="Startup progress" className={compact ? serverStyles.startupCompact : styles.panel}>
        {!compact && <h3>Startup progress</h3>}
        <p className={styles.secondaryText} role="status">
          Loading startup progress.
        </p>
      </section>
    )
  }

  if (
    startupSchema.status === 'unavailable' ||
    startupSnapshot.status === 'unavailable'
  ) {
    const unavailableMessage = [
      startupSchema.status === 'unavailable'
        ? startupSchema.message
        : undefined,
      startupSnapshot.status === 'unavailable'
        ? startupSnapshot.message
        : undefined,
    ]
      .filter((message): message is string => message !== undefined)
      .join(' ')

    return (
      <section aria-label="Startup progress" className={compact ? serverStyles.startupCompact : styles.panel}>
        {!compact && <h3>Startup progress</h3>}
        <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
          Startup progress unavailable.
        </p>
        {unavailableMessage && (
          <p className={styles.secondaryText}>{unavailableMessage}</p>
        )}
      </section>
    )
  }

  const schema = startupSchema.data
  const snapshot = startupSnapshot.data

  return (
    <section aria-label="Startup progress" className={compact ? serverStyles.startupCompact : styles.panel}>
      {!compact && <h3>Startup progress</h3>}

      {snapshot.sessionId === null ? (
        <p className={compact ? serverStyles.startupNote : styles.secondaryText}>No active startup session.</p>
      ) : (
        <>
          <p className={compact ? serverStyles.startupNote : styles.secondaryText}>
            {snapshot.started
              ? 'Startup milestones complete.'
              : 'Startup milestones are being observed.'}
          </p>
          <ol aria-label="Startup milestones" className={compact ? serverStyles.milestoneList : styles.startupMilestoneList}>
            {schema.milestones.map((milestone, index) => {
              const state = snapshot.milestones[index]

              return (
                <li className={compact ? serverStyles.milestoneRow : styles.startupMilestone} data-state={state} key={milestone.id}>
                  <span className={compact ? serverStyles.milestoneLabel : undefined}>{milestone.label}</span>
                  <span
                    className={compact
                      ? serverStyles.milestoneState
                      : `${styles.startupMilestoneState} ${styles[`startupMilestone${state[0].toUpperCase()}${state.slice(1)}`]}`
                    }
                  >
                    {milestoneStateLabels[state]}
                  </span>
                </li>
              )
            })}
          </ol>
        </>
      )}
    </section>
  )
}
