import type { PluginsOperationState } from '../hooks/usePluginsOperation'

import { PluginsDialog } from './PluginsDialog'
import styles from '../PluginsPage.module.css'

interface OperationReportDialogProps {
  operation: PluginsOperationState
  onClose: () => void
  onRetryStatus: () => void
  /** A persisted History report stays inspectable even if its saved status is in progress. */
  readOnly?: boolean
  closeLabel?: string
}

function formatPhase(phase: PluginsOperationState['phase']): string {
  switch (phase) {
    case 'pending': return 'Pending'
    case 'running': return 'Running'
    case 'statusUnavailable': return 'Status unavailable'
    case 'succeeded': return 'Succeeded'
    case 'failed': return 'Failed'
    case 'interrupted': return 'Interrupted'
    default: return 'Unknown'
  }
}

function statusClassName(phase: PluginsOperationState['phase']): string {
  switch (phase) {
    case 'pending': return styles.operationPending
    case 'running': return styles.operationRunning
    case 'statusUnavailable': return styles.operationStatusUnavailable
    case 'succeeded': return styles.operationSucceeded
    case 'failed': return styles.operationFailed
    case 'interrupted': return styles.operationInterrupted
    default: return ''
  }
}

/** Shows the persistent Plugins operation report, including rollback outcome. */
export function OperationReportDialog({
  operation,
  onClose,
  onRetryStatus,
  readOnly = false,
  closeLabel = 'Close',
}: OperationReportDialogProps) {
  const active = !readOnly && (operation.phase === 'pending' || operation.phase === 'running')
  const statusUnavailable = !readOnly && operation.phase === 'statusUnavailable'
  const report = operation.report

  return (
    <PluginsDialog
      footer={statusUnavailable ? (
        <div className={styles.operationFooter}>
          <span role="status">The operation outcome is unknown until its status can be retrieved.</span>
          <button className={styles.commandButton} onClick={onRetryStatus} type="button">Retry status</button>
        </div>
      ) : active ? (
        <span className={styles.operationFooter} role="status">Tracking backend operation report…</span>
      ) : (
        <button className={styles.commandButton} onClick={onClose} type="button">{closeLabel}</button>
      )}
      onClose={active || statusUnavailable ? undefined : onClose}
      title={operation.title ?? 'Operation Report'}
    >
      <div className={`${styles.operationStatus} ${statusClassName(operation.phase)}`} role={active ? 'status' : undefined}>
        {active ? (
          <div className={styles.operationActiveState}>
            <span>Operation in progress</span>
            <div
              aria-label="Operation progress"
              aria-valuetext="In progress"
              className={styles.operationProgress}
              role="progressbar"
            />
          </div>
        ) : (
          <span>{formatPhase(operation.phase)}</span>
        )}
        {operation.operationId !== null && (
          <div className={styles.operationMetadata}>
            <span>Operation ID</span>
            <code>{operation.operationId}</code>
          </div>
        )}
      </div>
      {operation.error !== null && <p className={styles.errorState} role="alert">{operation.error}</p>}
      {report !== null && (
        <>
          <dl className={styles.dialogSummary}>
            <div><dt>Type</dt><dd>{report.type}</dd></div>
            <div><dt>Current phase</dt><dd>{report.currentPhase}</dd></div>
            <div><dt>Started</dt><dd>{report.startedAt}</dd></div>
            <div><dt>Completed</dt><dd>{report.completedAt ?? 'In progress'}</dd></div>
          </dl>
          <section className={styles.reportSection}>
            <h3>Steps</h3>
            <ul className={styles.reportList}>
              {report.steps.map((step) => <li key={step.name}>{step.name}: <strong>{step.status}</strong></li>)}
            </ul>
          </section>
          {report.failure !== null && (
            <section className={styles.reportSection}>
              <h3>Failure</h3>
              <p><strong>{report.failure.code}</strong> during {report.failure.phase}: {report.failure.message}</p>
              {(report.failure.repository !== null || report.failure.filePath !== null) && (
                <p>{report.failure.repository ?? 'Runtime'}{report.failure.filePath !== null ? ` · ${report.failure.filePath}` : ''}</p>
              )}
            </section>
          )}
          <section className={styles.reportSection}>
            <h3>Rollback</h3>
            <p><strong>{report.rollback.status}</strong>{report.rollback.message !== null ? ` · ${report.rollback.message}` : ''}</p>
          </section>
          {report.sync !== null && (
            <section className={styles.reportSection}>
              <h3>Git synchronization</h3>
              <p>{report.sync.repository}: {report.sync.initialRelation} → {report.sync.behavior}</p>
              {report.sync.manualInterventionRequired && <p className={styles.errorState}>Manual Git intervention is required.</p>}
            </section>
          )}
          {report.finalGitState.length > 0 && (
            <section className={styles.reportSection}>
              <h3>Final Git state</h3>
              <ul className={styles.reportList}>
                {report.finalGitState.map((state) => <li key={state.repository}>{state.repository}: {state.branch ?? 'unknown'} · {state.relation}</li>)}
              </ul>
            </section>
          )}
        </>
      )}
      {operation.refreshError !== null && (
        <p className={styles.errorState} role="alert">Operation report is final, but current screen data could not refresh: {operation.refreshError}</p>
      )}
    </PluginsDialog>
  )
}
