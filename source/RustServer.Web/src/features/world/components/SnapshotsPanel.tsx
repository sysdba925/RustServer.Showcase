import { useCallback, useEffect, useRef, useState } from 'react'

import {
  createSnapshot,
  deleteSnapshot,
  getSnapshotOperation,
  getSnapshots,
  HttpError,
  restoreSnapshot,
  type ServerStatus,
  type SnapshotOperationResponse,
  type SnapshotSummaryResponse,
  type SnapshotType,
} from '../../../shared/api'
import type { ResourceState } from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import worldStyles from '../WorldPage.module.css'

const operationPollingIntervalMs = 2_000

type SnapshotListState =
  | { status: 'loading' }
  | { status: 'available'; data: SnapshotSummaryResponse[] }
  | { status: 'unavailable'; message: string }

type SnapshotOperationContext =
  | { action: 'create'; type: SnapshotType }
  | { action: 'restore'; snapshot: SnapshotSummaryResponse }

type SnapshotConfirmation =
  | { action: 'restore'; snapshot: SnapshotSummaryResponse }
  | { action: 'delete'; snapshot: SnapshotSummaryResponse }

interface SnapshotsPanelProps {
  serverStatus: ResourceState<ServerStatus>
  compact?: boolean
}

function getErrorMessage(error: unknown): string {
  return error instanceof Error && error.message !== ''
    ? error.message
    : 'Request failed.'
}

function getProblemMessage(error: HttpError): string | null {
  const detail = error.problemDetails?.detail?.trim()

  if (detail) {
    return detail
  }

  const title = error.problemDetails?.title?.trim()

  return title || null
}

function formatSnapshotType(type: SnapshotType): string {
  return type === 'fast' ? 'Fast' : 'Consistent'
}

function formatSnapshotSize(sizeBytes: number): string {
  if (sizeBytes < 1024) {
    return `${sizeBytes} B`
  }

  const units = ['KB', 'MB', 'GB', 'TB']
  const unitIndex = Math.min(
    Math.floor(Math.log(sizeBytes) / Math.log(1024)) - 1,
    units.length - 1,
  )
  const value = sizeBytes / 1024 ** (unitIndex + 1)

  return `${value.toFixed(1)} ${units[unitIndex]}`
}

function formatSnapshotDate(createdAt: string): string {
  const date = new Date(createdAt)

  if (Number.isNaN(date.getTime())) {
    return createdAt
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

function getOperationProgressMessage(operation: SnapshotOperationContext): string {
  if (operation.action === 'restore') {
    return 'Restoring snapshot...'
  }

  return `Creating ${operation.type} snapshot...`
}

function getOperationFailureMessage(
  operation: SnapshotOperationContext,
  error: string | null,
): string {
  const subject = operation.action === 'restore' ? 'restore' : 'creation'

  return `Snapshot ${subject} failed: ${error ?? 'The operation did not complete.'}`
}

function getRequestFailureMessage(
  operation: SnapshotOperationContext,
  error: unknown,
): string {
  if (error instanceof HttpError) {
    if (error.status === 409) {
      if (operation.action === 'restore') {
        return 'Stop the Rust server before restoring a snapshot.'
      }

      if (operation.type === 'consistent') {
        return 'Stop the Rust server before creating a consistent snapshot.'
      }
    }

    const problemMessage = getProblemMessage(error)

    if (problemMessage) {
      return getOperationFailureMessage(operation, problemMessage)
    }
  }

  return getOperationFailureMessage(operation, getErrorMessage(error))
}

function getDeleteFailureMessage(error: unknown): string {
  if (error instanceof HttpError) {
    if (error.status === 409) {
      return 'A snapshot operation is in progress. Try again when it finishes.'
    }

    const problemMessage = getProblemMessage(error)

    if (problemMessage) {
      return `Snapshot deletion failed: ${problemMessage}`
    }
  }

  return `Snapshot deletion failed: ${getErrorMessage(error)}`
}

function SnapshotConfirmationDialog({
  confirmation,
  onCancel,
  onConfirm,
  compact = false,
}: {
  confirmation: SnapshotConfirmation
  onCancel: () => void
  onConfirm: () => void
  compact?: boolean
}) {
  const { snapshot } = confirmation
  const restoring = confirmation.action === 'restore'
  const title = restoring ? 'Restore snapshot' : 'Delete snapshot'

  return (
    <div className={compact ? worldStyles.modalBackdrop : undefined}>
      <section
        aria-labelledby="snapshot-confirmation-heading"
        aria-modal={compact || undefined}
        className={compact ? worldStyles.modalDialog : styles.snapshotConfirmation}
        role="dialog"
      >
      <h4 id="snapshot-confirmation-heading">{title}</h4>
      <p>
        {restoring ? 'Restore' : 'Delete'} the {formatSnapshotType(snapshot.type).toLowerCase()}{' '}
        snapshot from {formatSnapshotDate(snapshot.createdAt)}?
      </p>
      {restoring ? (
        <p>
          Current Rust server state will be replaced. The server must be stopped
          before restore.
        </p>
      ) : (
        <p>This permanently deletes the selected snapshot.</p>
      )}
      <div className={compact ? worldStyles.actions : styles.formActions}>
        <button onClick={onCancel} type="button">
          Cancel
        </button>
        <button
          className={compact ? worldStyles.danger : restoring ? undefined : styles.destructiveButton}
          onClick={onConfirm}
          type="button"
        >
          {restoring ? 'Restore snapshot' : 'Delete snapshot'}
        </button>
      </div>
      </section>
    </div>
  )
}

/** Lists and manages runtime snapshots through the Snapshots public API. */
export function SnapshotsPanel({ serverStatus, compact = false }: SnapshotsPanelProps) {
  const [snapshots, setSnapshots] = useState<SnapshotListState>({
    status: 'loading',
  })
  const [activeOperation, setActiveOperation] =
    useState<SnapshotOperationContext | null>(null)
  const [deletingSnapshotId, setDeletingSnapshotId] = useState<string | null>(
    null,
  )
  const [confirmation, setConfirmation] = useState<SnapshotConfirmation | null>(
    null,
  )
  const [error, setError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const listRefreshControllerRef = useRef<AbortController | null>(null)
  const mutationControllerRef = useRef<AbortController | null>(null)
  const operationControllerRef = useRef<AbortController | null>(null)

  const refreshSnapshots = useCallback(async (): Promise<void> => {
    listRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    listRefreshControllerRef.current = controller
    setSnapshots({ status: 'loading' })

    try {
      const data = await getSnapshots(controller.signal)

      if (
        !controller.signal.aborted &&
        listRefreshControllerRef.current === controller
      ) {
        setSnapshots({ status: 'available', data })
      }
    } catch (requestError: unknown) {
      if (
        !controller.signal.aborted &&
        listRefreshControllerRef.current === controller
      ) {
        setSnapshots({
          status: 'unavailable',
          message: getErrorMessage(requestError),
        })
      }
    } finally {
      if (listRefreshControllerRef.current === controller) {
        listRefreshControllerRef.current = null
      }
    }
  }, [])

  const pollOperation = useCallback(
    (operationId: string, operation: SnapshotOperationContext): void => {
      operationControllerRef.current?.abort()

      const controller = new AbortController()
      let timer: ReturnType<typeof setTimeout> | null = null

      const stopPolling = () => {
        if (timer !== null) {
          clearTimeout(timer)
          timer = null
        }

        controller.abort()
      }

      const isCurrentPoll = () =>
        !controller.signal.aborted && operationControllerRef.current === controller

      const finish = () => {
        if (operationControllerRef.current === controller) {
          operationControllerRef.current = null
        }

        stopPolling()
      }

      const schedulePoll = () => {
        timer = setTimeout(() => {
          timer = null
          void poll()
        }, operationPollingIntervalMs)
      }

      const completeSuccessfully = () => {
        finish()
        setActiveOperation(null)
        setSuccessMessage(
          operation.action === 'restore'
            ? 'Snapshot restored.'
            : `${formatSnapshotType(operation.type)} snapshot created.`,
        )
        void refreshSnapshots()
      }

      const failOperation = (message: string) => {
        finish()
        setActiveOperation(null)
        setError(message)
      }

      async function poll(): Promise<void> {
        try {
          const response: SnapshotOperationResponse = await getSnapshotOperation(
            operationId,
            controller.signal,
          )

          if (!isCurrentPoll()) {
            return
          }

          switch (response.status) {
            case 'pending':
            case 'running':
              schedulePoll()
              return
            case 'succeeded':
              completeSuccessfully()
              return
            case 'failed':
              failOperation(getOperationFailureMessage(operation, response.error))
          }
        } catch (requestError: unknown) {
          if (!isCurrentPoll()) {
            return
          }

          if (requestError instanceof HttpError && requestError.status === 404) {
            failOperation(
              'Snapshot operation status is no longer available. Snapshot list was refreshed.',
            )
            void refreshSnapshots()
            return
          }

          failOperation(getOperationFailureMessage(operation, getErrorMessage(requestError)))
        }
      }

      operationControllerRef.current = controller
      void poll()
    },
    [refreshSnapshots],
  )

  useEffect(() => {
    // This starts an independent resource request; the guarded completion owns
    // the state update so an aborted request cannot overwrite newer data.
    // oxlint-disable-next-line react/set-state-in-effect
    void refreshSnapshots()
  }, [refreshSnapshots])

  useEffect(() => {
    return () => {
      listRefreshControllerRef.current?.abort()
      mutationControllerRef.current?.abort()
      operationControllerRef.current?.abort()
    }
  }, [])

  const operationInProgress = activeOperation !== null
  const mutationInProgress = operationInProgress || deletingSnapshotId !== null
  const serverIsRunning =
    serverStatus.status === 'available' && serverStatus.data.isRunning
  const stoppedServerMessage =
    'Stop the Rust server before creating a consistent snapshot or restoring a snapshot.'

  async function requestCreate(type: SnapshotType): Promise<void> {
    if (mutationInProgress) {
      return
    }

    const operation: SnapshotOperationContext = { action: 'create', type }
    const controller = new AbortController()
    mutationControllerRef.current = controller
    setError(null)
    setSuccessMessage(null)
    setActiveOperation(operation)

    try {
      const accepted = await createSnapshot(type, controller.signal)

      if (
        controller.signal.aborted ||
        mutationControllerRef.current !== controller
      ) {
        return
      }

      mutationControllerRef.current = null
      pollOperation(accepted.operationId, operation)
    } catch (requestError: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setActiveOperation(null)
        setError(getRequestFailureMessage(operation, requestError))
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
      }
    }
  }

  async function requestRestore(snapshot: SnapshotSummaryResponse): Promise<void> {
    if (mutationInProgress) {
      return
    }

    const operation: SnapshotOperationContext = { action: 'restore', snapshot }
    const controller = new AbortController()
    mutationControllerRef.current = controller
    setConfirmation(null)
    setError(null)
    setSuccessMessage(null)
    setActiveOperation(operation)

    try {
      const accepted = await restoreSnapshot(snapshot.id, controller.signal)

      if (
        controller.signal.aborted ||
        mutationControllerRef.current !== controller
      ) {
        return
      }

      mutationControllerRef.current = null
      pollOperation(accepted.operationId, operation)
    } catch (requestError: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setActiveOperation(null)
        setError(getRequestFailureMessage(operation, requestError))
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
      }
    }
  }

  async function requestDelete(snapshot: SnapshotSummaryResponse): Promise<void> {
    if (mutationInProgress) {
      return
    }

    const controller = new AbortController()
    mutationControllerRef.current = controller
    setConfirmation(null)
    setError(null)
    setSuccessMessage(null)
    setDeletingSnapshotId(snapshot.id)

    try {
      await deleteSnapshot(snapshot.id, controller.signal)

      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setSuccessMessage('Snapshot deleted.')
        void refreshSnapshots()
      }
    } catch (requestError: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setError(getDeleteFailureMessage(requestError))
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
        setDeletingSnapshotId(null)
      }
    }
  }

  return (
    <article aria-label="Snapshots" className={compact ? worldStyles.card : styles.panel}>
      <div className={compact ? worldStyles.cardHeader : styles.panelHeader}>
        <h3>Snapshots</h3>
        {compact && (
          <div className={worldStyles.headerTools}>
            <span className={worldStyles.chip} aria-live="polite">
              {snapshots.status === 'available'
                ? `${snapshots.data.length} stored`
                : snapshots.status === 'loading' ? 'Loading...' : 'Unavailable'}
            </span>
            <button
              disabled={mutationInProgress}
              onClick={() => void refreshSnapshots()}
              type="button"
            >
              ↻ Refresh
            </button>
          </div>
        )}
      </div>
      <div className={compact ? worldStyles.cardBody : undefined}>
      <p className={compact ? worldStyles.note : styles.secondaryText}>
        Fast snapshots can be created while the server is running. Best-effort
        consistency. Consistent snapshots require the Rust server to be stopped.
      </p>

      <div className={compact ? worldStyles.snapshotButtons : styles.snapshotActions}>
        <button
          className={compact ? worldStyles.primary : undefined}
          disabled={mutationInProgress}
          onClick={() => void requestCreate('fast')}
          type="button"
        >
          Create Fast Snapshot
        </button>
        <button
          disabled={mutationInProgress || serverIsRunning}
          onClick={() => void requestCreate('consistent')}
          title={serverIsRunning ? stoppedServerMessage : undefined}
          type="button"
        >
          Create Consistent Snapshot
        </button>
      </div>

      {serverIsRunning && (
        <p className={compact ? worldStyles.muted : styles.secondaryText}>{stoppedServerMessage}</p>
      )}
      {activeOperation && (
        <p className={compact ? worldStyles.muted : styles.secondaryText} role="status">
          {getOperationProgressMessage(activeOperation)}
        </p>
      )}
      {successMessage && (
        <p className={compact ? worldStyles.success : styles.successState} role="status">
          {successMessage}
        </p>
      )}
      {error && (
        <p className={compact ? worldStyles.error : styles.unavailableState} role="alert">
          {error}
        </p>
      )}

      {snapshots.status === 'loading' && (
        <p className={compact ? worldStyles.muted : styles.loadingState} role="status">
          Loading snapshots.
        </p>
      )}
      {snapshots.status === 'unavailable' && (
        <p className={compact ? worldStyles.error : styles.unavailableState} role="alert">
          Snapshots are unavailable. {snapshots.message}
        </p>
      )}
      {snapshots.status === 'available' && snapshots.data.length === 0 && (
        <p className={compact ? worldStyles.muted : styles.secondaryText}>No snapshots yet.</p>
      )}
      {snapshots.status === 'available' && snapshots.data.length > 0 && (
        <div className={compact ? worldStyles.snapshotTableWrap : styles.snapshotTableWrapper}>
          <table className={compact ? worldStyles.snapshotTable : styles.snapshotTable}>
            <thead>
              <tr>
                <th scope="col">Created</th>
                <th scope="col">Type</th>
                <th scope="col">Size</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {snapshots.data.map((snapshot) => (
                <tr key={snapshot.id}>
                  <td>{formatSnapshotDate(snapshot.createdAt)}</td>
                  <td>{formatSnapshotType(snapshot.type)}</td>
                  <td>{formatSnapshotSize(snapshot.sizeBytes)}</td>
                  <td>
                    <div className={compact ? worldStyles.snapshotRowActions : styles.snapshotRowActions}>
                      <button
                        disabled={mutationInProgress || serverIsRunning}
                        onClick={() =>
                          setConfirmation({ action: 'restore', snapshot })
                        }
                        title={serverIsRunning ? stoppedServerMessage : undefined}
                        type="button"
                      >
                        Restore
                      </button>
                      <button
                        className={compact ? worldStyles.danger : styles.destructiveButton}
                        disabled={mutationInProgress}
                        onClick={() =>
                          setConfirmation({ action: 'delete', snapshot })
                        }
                        type="button"
                      >
                        {deletingSnapshotId === snapshot.id
                          ? 'Deleting...'
                          : 'Delete'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {confirmation && (
        <SnapshotConfirmationDialog
          compact={compact}
          confirmation={confirmation}
          onCancel={() => setConfirmation(null)}
          onConfirm={() => {
            if (confirmation.action === 'restore') {
              void requestRestore(confirmation.snapshot)
            } else {
              void requestDelete(confirmation.snapshot)
            }
          }}
        />
      )}
      {compact && (
        <div className={worldStyles.sectionDivider}>
          <dl className={worldStyles.detailRows}>
            <div >
              <dt>Active snapshot operation</dt>
              <dd>{activeOperation === null
                ? 'None'
                : activeOperation.action === 'restore' ? 'Restore' : `Create ${formatSnapshotType(activeOperation.type)}`}</dd>
            </div>
            <div >
              <dt>Operation state</dt>
              <dd>{mutationInProgress ? 'In progress' : '—'}</dd>
            </div>
          </dl>
        </div>
      )}
      </div>
    </article>
  )
}
