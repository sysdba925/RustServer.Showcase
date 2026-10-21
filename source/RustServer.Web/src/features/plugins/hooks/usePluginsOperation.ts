import { useCallback, useEffect, useRef, useState } from 'react'

import { HttpError, type GetPluginsOperationResponse } from '../../../shared/api'
import type { PluginsApi } from '../api'
import { getPluginsErrorMessage, getPluginsOperationIdFromError } from '../model/plugins-error'

const operationPollingIntervalMs = 1_000

export type PluginsOperationPhase =
  | 'idle'
  | 'pending'
  | 'running'
  /** Frontend state: the persisted backend report could not be retrieved. */
  | 'statusUnavailable'
  | 'succeeded'
  | 'failed'
  | 'interrupted'

export interface PluginsOperationState {
  phase: PluginsOperationPhase
  title: string | null
  operationId: string | null
  report: GetPluginsOperationResponse | null
  error: string | null
  refreshError: string | null
}

interface AcceptedPluginsOperation {
  operationId: string
}

type OperationRequest = (signal: AbortSignal) => Promise<AcceptedPluginsOperation>

interface TrackedPluginsOperation {
  title: string
  operationId: string
  refresh: () => Promise<void>
}

const idleOperation: PluginsOperationState = {
  phase: 'idle',
  title: null,
  operationId: null,
  report: null,
  error: null,
  refreshError: null,
}

function phaseFromReport(report: GetPluginsOperationResponse): PluginsOperationPhase {
  switch (report.status) {
    case 'succeeded':
      return 'succeeded'
    case 'failed':
      return 'failed'
    case 'interrupted':
      return 'interrupted'
    default:
      return 'running'
  }
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

function waitForNextPoll(signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', abort)
      resolve()
    }, operationPollingIntervalMs)

    function abort(): void {
      clearTimeout(timer)
      reject(new DOMException('The operation was aborted.', 'AbortError'))
    }

    signal.addEventListener('abort', abort, { once: true })
  })
}

/**
 * Tracks one Plugins mutation and its persistent backend report. The component
 * that owns this hook may supply only the resources affected by the command.
 */
export function usePluginsOperation(api: PluginsApi) {
  const [operation, setOperation] = useState<PluginsOperationState>(idleOperation)
  const controllerRef = useRef<AbortController | null>(null)
  const trackedOperationRef = useRef<TrackedPluginsOperation | null>(null)

  const refreshAfterTerminalState = useCallback(async (
    refresh: () => Promise<void>,
  ): Promise<string | null> => {
    try {
      await refresh()
      return null
    } catch (error: unknown) {
      return getPluginsErrorMessage(error)
    }
  }, [])

  const startStatusPolling = useCallback((trackedOperation: TrackedPluginsOperation): void => {
    if (controllerRef.current !== null) {
      return
    }

    const controller = new AbortController()
    controllerRef.current = controller
    setOperation((current) => ({
      phase: 'running',
      title: trackedOperation.title,
      operationId: trackedOperation.operationId,
      report: current.operationId === trackedOperation.operationId ? current.report : null,
      error: null,
      refreshError: null,
    }))

    void (async () => {
      try {
        while (!controller.signal.aborted) {
          const report = await api.getOperation(trackedOperation.operationId, controller.signal)

          if (controller.signal.aborted || controllerRef.current !== controller) {
            return
          }

          const phase = phaseFromReport(report)
          if (phase === 'running') {
            setOperation({
              phase,
              title: trackedOperation.title,
              operationId: trackedOperation.operationId,
              report,
              error: null,
              refreshError: null,
            })
            await waitForNextPoll(controller.signal)
            continue
          }

          const refreshError = await refreshAfterTerminalState(trackedOperation.refresh)
          if (controller.signal.aborted || controllerRef.current !== controller) {
            return
          }

          controllerRef.current = null
          trackedOperationRef.current = null
          setOperation({
            phase,
            title: trackedOperation.title,
            operationId: trackedOperation.operationId,
            report,
            error: null,
            refreshError,
          })
          return
        }
      } catch (error: unknown) {
        if (controller.signal.aborted || controllerRef.current !== controller || isAbortError(error)) {
          return
        }

        controllerRef.current = null
        setOperation((current) => ({
          phase: 'statusUnavailable',
          title: trackedOperation.title,
          operationId: trackedOperation.operationId,
          report: current.operationId === trackedOperation.operationId ? current.report : null,
          error: getPluginsErrorMessage(error),
          refreshError: null,
        }))
      }
    })()
  }, [api, refreshAfterTerminalState])

  const run = useCallback((
    title: string,
    request: OperationRequest,
    refresh: () => Promise<void>,
  ): void => {
    if (controllerRef.current !== null || trackedOperationRef.current !== null) {
      return
    }

    const controller = new AbortController()
    controllerRef.current = controller
    setOperation({
      phase: 'pending',
      title,
      operationId: null,
      report: null,
      error: null,
      refreshError: null,
    })

    void (async () => {
      try {
        const accepted = await request(controller.signal)

        if (controller.signal.aborted || controllerRef.current !== controller) {
          return
        }

        const trackedOperation: TrackedPluginsOperation = {
          title,
          operationId: accepted.operationId,
          refresh,
        }
        trackedOperationRef.current = trackedOperation
        controllerRef.current = null
        startStatusPolling(trackedOperation)
      } catch (error: unknown) {
        if (controller.signal.aborted || controllerRef.current !== controller || isAbortError(error)) {
          return
        }

        const reportedOperationId = getPluginsOperationIdFromError(error)
        if (reportedOperationId !== null) {
          const trackedOperation: TrackedPluginsOperation = {
            title,
            operationId: reportedOperationId,
            refresh,
          }
          trackedOperationRef.current = trackedOperation
          controllerRef.current = null
          startStatusPolling(trackedOperation)
          return
        }

        const refreshError = error instanceof HttpError && error.status === 409
          ? await refreshAfterTerminalState(refresh)
          : null

        if (controller.signal.aborted || controllerRef.current !== controller) {
          return
        }

        controllerRef.current = null
        setOperation({
          phase: 'failed',
          title,
          operationId: null,
          report: null,
          error: getPluginsErrorMessage(error),
          refreshError,
        })
      }
    })()
  }, [refreshAfterTerminalState, startStatusPolling])

  const retryStatus = useCallback((): void => {
    const trackedOperation = trackedOperationRef.current
    if (trackedOperation !== null && controllerRef.current === null) {
      startStatusPolling(trackedOperation)
    }
  }, [startStatusPolling])

  const reset = useCallback((): void => {
    if (controllerRef.current === null && trackedOperationRef.current === null) {
      setOperation(idleOperation)
    }
  }, [])

  useEffect(() => () => {
    controllerRef.current?.abort()
    controllerRef.current = null
    trackedOperationRef.current = null
  }, [])

  return {
    operation,
    operationActive: operation.phase === 'pending' ||
      operation.phase === 'running' ||
      operation.phase === 'statusUnavailable',
    reset,
    retryStatus,
    run,
  }
}
