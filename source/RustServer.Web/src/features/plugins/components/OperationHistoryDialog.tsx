import { useCallback, useEffect, useRef, useState } from 'react'

import type {
  GetPluginsOperationResponse,
  GetPluginsOperationsResponse,
  OperationHistoryItemResponse,
} from '../../../shared/api'
import type { PluginsOperationPhase, PluginsOperationState } from '../hooks/usePluginsOperation'
import { getPluginsErrorMessage } from '../model/plugins-error'
import styles from '../PluginsPage.module.css'

import { OperationReportDialog } from './OperationReportDialog'
import { PluginsDialog } from './PluginsDialog'

type HistoryView =
  | { type: 'list' }
  | { type: 'reportLoading'; operationId: string }
  | { type: 'report'; report: GetPluginsOperationResponse }
  | { type: 'reportError'; operationId: string; message: string }

interface OperationHistoryDialogProps {
  getOperation: (operationId: string, signal?: AbortSignal) => Promise<GetPluginsOperationResponse>
  getOperations: (
    limit: number,
    cursor?: string,
    signal?: AbortSignal,
  ) => Promise<GetPluginsOperationsResponse>
  onClose: () => void
}

const pageSizes = [10, 50, 200] as const

function formatTimestamp(value: string | null): string {
  if (value === null) {
    return 'In progress'
  }

  const timestamp = new Date(value)
  if (Number.isNaN(timestamp.getTime())) {
    return 'Invalid timestamp'
  }

  return `${new Intl.DateTimeFormat('en-GB', {
    dateStyle: 'medium',
    timeStyle: 'medium',
    timeZone: 'UTC',
  }).format(timestamp)} UTC`
}

function statusClassName(status: string): string {
  switch (status) {
    case 'succeeded': return styles.operationSucceeded
    case 'failed': return styles.operationFailed
    case 'interrupted': return styles.operationInterrupted
    case 'inProgress': return styles.operationRunning
    default: return styles.operationStatusUnavailable
  }
}

function reportPhase(report: GetPluginsOperationResponse): PluginsOperationPhase {
  switch (report.status) {
    case 'succeeded': return 'succeeded'
    case 'failed': return 'failed'
    case 'interrupted': return 'interrupted'
    case 'inProgress': return 'running'
    default: return 'statusUnavailable'
  }
}

function reportState(report: GetPluginsOperationResponse): PluginsOperationState {
  return {
    phase: reportPhase(report),
    title: 'Operation Report',
    operationId: report.operationId,
    report,
    error: null,
    refreshError: null,
  }
}

function SummaryRow({ item, onOpen }: { item: OperationHistoryItemResponse; onOpen: () => void }) {
  return (
    <button className={styles.historyRow} onClick={onOpen} type="button">
      <span className={styles.historyOperation}>
        <strong>{item.type}</strong>
        <code>{item.operationId}</code>
      </span>
      <span className={`${styles.historyStatus} ${statusClassName(item.status)}`}>{item.status}</span>
      <span className={styles.historyTimestamp}>
        <span>Started {formatTimestamp(item.startedAt)}</span>
        <span>Completed {formatTimestamp(item.completedAt)}</span>
      </span>
      <span className={styles.historySummary}>{item.changedFiles} changed file{item.changedFiles === 1 ? '' : 's'}</span>
    </button>
  )
}

/** Cursor-paginated compact operation history with on-demand detailed report lookup. */
export function OperationHistoryDialog({ getOperation, getOperations, onClose }: OperationHistoryDialogProps) {
  const [pageSize, setPageSize] = useState<(typeof pageSizes)[number]>(10)
  const [items, setItems] = useState<OperationHistoryItemResponse[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)
  const [view, setView] = useState<HistoryView>({ type: 'list' })
  const historyControllerRef = useRef<AbortController | null>(null)
  const reportControllerRef = useRef<AbortController | null>(null)

  const loadInitialPage = useCallback(async (limit: (typeof pageSizes)[number]): Promise<void> => {
    historyControllerRef.current?.abort()
    const controller = new AbortController()
    historyControllerRef.current = controller

    try {
      const response = await getOperations(limit, undefined, controller.signal)
      if (!controller.signal.aborted && historyControllerRef.current === controller) {
        setItems(response.items)
        setNextCursor(response.nextCursor)
      }
    } catch (loadError: unknown) {
      if (!controller.signal.aborted && historyControllerRef.current === controller) {
        setItems([])
        setNextCursor(null)
        setError(getPluginsErrorMessage(loadError))
      }
    } finally {
      if (historyControllerRef.current === controller) {
        historyControllerRef.current = null
        setLoading(false)
      }
    }
  }, [getOperations])

  useEffect(() => {
    let mounted = true
    void Promise.resolve().then(() => {
      if (mounted) {
        return loadInitialPage(10)
      }

      return undefined
    })

    return () => {
      mounted = false
    }
  }, [loadInitialPage])

  useEffect(() => () => {
    historyControllerRef.current?.abort()
    reportControllerRef.current?.abort()
  }, [])

  async function loadOlder(): Promise<void> {
    if (nextCursor === null || loadingMore) {
      return
    }

    historyControllerRef.current?.abort()
    const controller = new AbortController()
    historyControllerRef.current = controller
    setLoadingMore(true)
    setLoadMoreError(null)

    try {
      const response = await getOperations(pageSize, nextCursor, controller.signal)
      if (!controller.signal.aborted && historyControllerRef.current === controller) {
        setItems((current) => [...current, ...response.items])
        setNextCursor(response.nextCursor)
      }
    } catch (loadError: unknown) {
      if (!controller.signal.aborted && historyControllerRef.current === controller) {
        setLoadMoreError(getPluginsErrorMessage(loadError))
      }
    } finally {
      if (historyControllerRef.current === controller) {
        historyControllerRef.current = null
        setLoadingMore(false)
      }
    }
  }

  function changePageSize(size: (typeof pageSizes)[number]): void {
    if (size === pageSize) {
      return
    }

    setPageSize(size)
    setLoading(true)
    setError(null)
    setLoadMoreError(null)
    void loadInitialPage(size)
  }

  function retryInitialPage(): void {
    setLoading(true)
    setError(null)
    setLoadMoreError(null)
    void loadInitialPage(pageSize)
  }

  async function openReport(operationId: string): Promise<void> {
    reportControllerRef.current?.abort()
    const controller = new AbortController()
    reportControllerRef.current = controller
    setView({ type: 'reportLoading', operationId })

    try {
      const report = await getOperation(operationId, controller.signal)
      if (!controller.signal.aborted && reportControllerRef.current === controller) {
        setView({ type: 'report', report })
      }
    } catch (reportError: unknown) {
      if (!controller.signal.aborted && reportControllerRef.current === controller) {
        setView({
          type: 'reportError',
          operationId,
          message: getPluginsErrorMessage(reportError),
        })
      }
    } finally {
      if (reportControllerRef.current === controller) {
        reportControllerRef.current = null
      }
    }
  }

  if (view.type === 'report') {
    return (
      <OperationReportDialog
        closeLabel="Back to History"
        onClose={() => setView({ type: 'list' })}
        onRetryStatus={() => undefined}
        operation={reportState(view.report)}
        readOnly
      />
    )
  }

  if (view.type === 'reportLoading') {
    return (
      <PluginsDialog
        footer={<button className={styles.commandButton} onClick={() => setView({ type: 'list' })} type="button">Back to History</button>}
        onClose={() => setView({ type: 'list' })}
        title="Operation Report"
      >
        <p className={styles.localState} role="status">Loading operation report for <code>{view.operationId}</code>…</p>
      </PluginsDialog>
    )
  }

  if (view.type === 'reportError') {
    return (
      <PluginsDialog
        footer={(
          <>
            <button className={styles.commandButton} onClick={() => setView({ type: 'list' })} type="button">Back to History</button>
            <button className={styles.commandButton} onClick={() => void openReport(view.operationId)} type="button">Retry report</button>
          </>
        )}
        onClose={() => setView({ type: 'list' })}
        title="Operation Report"
      >
        <p className={styles.errorState} role="alert">{view.message}</p>
      </PluginsDialog>
    )
  }

  return (
    <PluginsDialog
      footer={<button className={styles.commandButton} onClick={onClose} type="button">Close</button>}
      onClose={onClose}
      title="Operation History"
    >
      <p className={styles.dialogHint}>Select an operation to open its persistent detailed report. Summaries are newest first.</p>
      <div aria-label="History page size" className={styles.historyControls} role="group">
        <span>Page size</span>
        {pageSizes.map((size) => (
          <button
            aria-pressed={pageSize === size}
            className={`${styles.tableCommand} ${pageSize === size ? styles.historyPageSizeSelected : ''}`}
            disabled={loading && pageSize === size}
            key={size}
            onClick={() => changePageSize(size)}
            type="button"
          >
            {size}
          </button>
        ))}
      </div>
      {loading && <p className={styles.localState} role="status">Loading operation history…</p>}
      {error !== null && (
        <div className={styles.errorState} role="alert">
          <p>Operation history is unavailable: {error}</p>
          <button className={styles.commandButton} onClick={retryInitialPage} type="button">Retry</button>
        </div>
      )}
      {!loading && error === null && items.length === 0 && (
        <p className={styles.localState}>No operations have been recorded.</p>
      )}
      {!loading && error === null && items.length > 0 && (
        <section aria-label="Operation history" className={styles.historyList}>
          <div className={styles.historyHeader}>
            <span>Operation / ID</span>
            <span>Status</span>
            <span>Timestamps</span>
            <span>Summary</span>
          </div>
          {items.map((item) => <SummaryRow item={item} key={item.operationId} onOpen={() => void openReport(item.operationId)} />)}
        </section>
      )}
      {loadMoreError !== null && (
        <div className={styles.errorState} role="alert">
          <p>Could not load older operations: {loadMoreError}</p>
          <button className={styles.commandButton} onClick={() => void loadOlder()} type="button">Retry older operations</button>
        </div>
      )}
      {nextCursor !== null && !loading && error === null && (
        <button className={styles.commandButton} disabled={loadingMore} onClick={() => void loadOlder()} type="button">
          {loadingMore ? 'Loading older operations…' : 'Load older operations'}
        </button>
      )}
    </PluginsDialog>
  )
}
