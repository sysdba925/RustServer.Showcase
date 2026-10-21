import {
  classifyOperationStatus,
  formatOperationStatus,
  formatOperationTarget,
  formatOperationType,
  type AcceptedOperationContext,
  type Operation,
  type ResourceState,
} from '../../control-plane/model'
import styles from '../OverviewPage.module.css'

interface OverviewActivityPanelProps {
  acceptedOperation: AcceptedOperationContext | null
  operationDetail: ResourceState<Operation | null>
}

function ActivityField({
  label, value,
}: {
  label: string
  value: string | number
}) {
  return (
    <div className={styles.activityEntry}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  )
}

export function OverviewActivityPanel({
  acceptedOperation,
  operationDetail,
}: OverviewActivityPanelProps) {
  const operation = operationDetail.status === 'available' ? operationDetail.data : null
  const kind = operation === null ? null : classifyOperationStatus(operation.status)
  const status = operation !== null ? formatOperationStatus(operation.status)
    : operationDetail.status === 'loading' ? 'Loading'
      : operationDetail.status === 'unavailable' ? 'Unavailable' : 'No operation'
  const tone = operation !== null
    ? operation.status === 'Failed' ? 'error'
      : operation.status === 'Succeeded' ? 'ok' : 'info'
    : operationDetail.status === 'unavailable' ? 'error'
      : operationDetail.status === 'loading' ? 'info' : 'off'

  return (
    <article aria-label="Operation activity" className={styles.card}>
      <header className={styles.cardHeader}>
        <h3>Current / last known operation</h3>
        <span className={styles.status} data-tone={tone}>{status}</span>
      </header>
      <div className={styles.cardBody}>
        {operationDetail.status === 'loading' && (
          <>
            {acceptedOperation && <p className={styles.description}>{formatOperationTarget(acceptedOperation.target)} accepted</p>}
            {acceptedOperation && (
              <dl className={styles.activityGrid}>
                <ActivityField label="Requested operation" value={formatOperationTarget(acceptedOperation.target)} />
                <ActivityField label="Operation ID" value={acceptedOperation.id} />
                <ActivityField label="Accepted status" value={acceptedOperation.status} />
              </dl>
            )}
            <p className={styles.loading} role="status">Waiting for authoritative operation detail.</p>
          </>
        )}
        {operationDetail.status === 'unavailable' && (
          <>
            <p className={styles.alert} role="alert">Operation detail unavailable.</p>
            {acceptedOperation && (
              <dl className={styles.activityGrid}>
                <ActivityField label="Requested operation" value={formatOperationTarget(acceptedOperation.target)} />
                <ActivityField label="Operation ID" value={acceptedOperation.id} />
                <ActivityField label="Accepted status" value={acceptedOperation.status} />
              </dl>
            )}
            {operationDetail.message && <p className={styles.subtle}>{operationDetail.message}</p>}
          </>
        )}
        {operationDetail.status === 'available' && operation === null && (
          <p className={styles.description}>No known operation.</p>
        )}
        {operation !== null && (
          <>
            <dl className={styles.activityGrid}>
              <ActivityField label="Type" value={formatOperationType(operation.type)} />
              <ActivityField label="Status" value={formatOperationStatus(operation.status)} />
              <div className={styles.activityEntry}><dt>Created</dt><dd><time dateTime={operation.createdAt}>{operation.createdAt}</time></dd></div>
              {operation.startedAt != null && (
                <div className={styles.activityEntry}><dt>Started</dt><dd><time dateTime={operation.startedAt}>{operation.startedAt}</time></dd></div>
              )}
              {operation.finishedAt != null && (
                <div className={styles.activityEntry}><dt>Finished</dt><dd><time dateTime={operation.finishedAt}>{operation.finishedAt}</time></dd></div>
              )}
              {operation.exitCode != null && <ActivityField label="Exit code" value={operation.exitCode} />}
              {operation.error != null && <ActivityField label="Error" value={operation.error} />}
            </dl>
            {kind === 'unknown' && (
              <p className={styles.subtle}>The operation status is not recognized; it may still be active.</p>
            )}
          </>
        )}
      </div>
    </article>
  )
}
