import {
  classifyOperationStatus,
  formatOperationTarget,
  formatOperationStatus,
  formatOperationType,
  type AcceptedOperationContext,
  type Operation,
  type ResourceState,
} from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'

interface ActivityPanelProps {
  operationDetail: ResourceState<Operation | null>
  acceptedOperation: AcceptedOperationContext | null
}

function AcceptedOperationContext({
  acceptedOperation,
}: {
  acceptedOperation: AcceptedOperationContext
}) {
  return (
    <dl className={styles.keyValueList}>
      <div>
        <dt>Requested operation</dt>
        <dd>{formatOperationTarget(acceptedOperation.target)}</dd>
      </div>
      <div>
        <dt>Operation ID</dt>
        <dd>{acceptedOperation.id}</dd>
      </div>
      <div>
        <dt>Accepted status</dt>
        <dd>{acceptedOperation.status}</dd>
      </div>
    </dl>
  )
}

export function ActivityPanel({
  operationDetail,
  acceptedOperation,
}: ActivityPanelProps) {
  if (operationDetail.status === 'loading') {
    return (
      <article aria-label="Operation activity" className={styles.panel}>
        {acceptedOperation && <h3>{formatOperationTarget(acceptedOperation.target)} accepted</h3>}
        {acceptedOperation && <AcceptedOperationContext acceptedOperation={acceptedOperation} />}
        <p className={styles.loadingState} role="status">
          Waiting for authoritative operation detail.
        </p>
      </article>
    )
  }

  if (operationDetail.status === 'unavailable') {
    return (
      <article aria-label="Operation activity" className={styles.panel}>
        <p className={styles.unavailableState} role="alert">
          Operation detail unavailable.
        </p>
        {acceptedOperation && <AcceptedOperationContext acceptedOperation={acceptedOperation} />}
        {operationDetail.message && (
          <p className={styles.secondaryText}>{operationDetail.message}</p>
        )}
      </article>
    )
  }

  if (operationDetail.data === null) {
    return (
      <article aria-label="Operation activity" className={styles.panel}>
        <p className={styles.description}>No known operation.</p>
      </article>
    )
  }

  const operation = operationDetail.data
  const statusKind = classifyOperationStatus(operation.status)
  const terminal = statusKind === 'terminal'

  return (
    <article
      aria-label="Operation activity"
      className={`${styles.panel} ${
        terminal ? styles.operationTerminal : styles.operationActive
      }`}
    >
      <div className={styles.panelHeader}>
        <h3>{formatOperationType(operation.type)}</h3>
        <span
          className={`${styles.statusBadge} ${
            terminal ? styles.statusTerminal : styles.statusActive
          }`}
        >
          {formatOperationStatus(operation.status)}
        </span>
      </div>

      <dl className={styles.keyValueList}>
        <div>
          <dt>Type</dt>
          <dd>{formatOperationType(operation.type)}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{formatOperationStatus(operation.status)}</dd>
        </div>
        <div>
          <dt>Created</dt>
          <dd>
            <time dateTime={operation.createdAt}>{operation.createdAt}</time>
          </dd>
        </div>
        {operation.startedAt != null && (
          <div>
            <dt>Started</dt>
            <dd>
              <time dateTime={operation.startedAt}>{operation.startedAt}</time>
            </dd>
          </div>
        )}
        {operation.finishedAt != null && (
          <div>
            <dt>Finished</dt>
            <dd>
              <time dateTime={operation.finishedAt}>{operation.finishedAt}</time>
            </dd>
          </div>
        )}
        {operation.exitCode != null && (
          <div>
            <dt>Exit code</dt>
            <dd>{operation.exitCode}</dd>
          </div>
        )}
        {operation.error != null && (
          <div>
            <dt>Error</dt>
            <dd>{operation.error}</dd>
          </div>
        )}
      </dl>
    </article>
  )
}
