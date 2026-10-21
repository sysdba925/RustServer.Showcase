import { useCallback, useEffect, useRef, useState, type PropsWithChildren } from 'react'

import {
  getOperation,
  getOxideVersions,
  getRustVersions,
  getServerConfiguration,
  getServerStatus,
  getStartupSchema,
  getStartupSnapshot,
  HttpError,
  requestOxideUpdate,
  requestRustUpdate,
  requestWipe,
  restartServer,
  startServer,
  stopServer,
  type Operation,
  type OperationAcceptedResponse,
  type OxideVersionInfo,
  type RustVersionInfo,
  type ServerConfigurationResponse,
  type ServerStatus,
  type StartupSchemaResponse,
  type StartupSnapshotResponse,
  type UpdateServerConfigurationRequest,
  type WipePlanRequest,
  updateServerConfiguration,
} from '../../shared/api'
import {
  ControlPlaneContext,
  type ControlPlaneContextValue,
} from './ControlPlaneContext'
import {
  classifyOperationStatus,
  deriveActionAvailability,
  deriveServerState,
  getWipeUnavailableReason,
  type AcceptedOperationContext,
  type LifecycleAction,
  type ControlPlaneInput,
  type ResourceState,
  type UpdateTarget,
} from './model'

const noKnownOperation: ResourceState<Operation | null> = {
  status: 'available',
  data: null,
}

const readinessReconciliationIntervalMs = 2_000
const operationPollingIntervalMs = 2_000
const startupPollingIntervalMs = 2_000

type PendingMutation =
  | { kind: 'lifecycle'; action: LifecycleAction }
  | { kind: 'update'; target: UpdateTarget }
  | { kind: 'configuration' }
  | { kind: 'wipe' }

type LifecycleRequest = (signal: AbortSignal) => Promise<ServerStatus>
type UpdateRequest = (signal: AbortSignal) => Promise<OperationAcceptedResponse>
type ConfigurationRequest = (
  request: UpdateServerConfigurationRequest,
  signal: AbortSignal,
) => Promise<ServerConfigurationResponse>
type WipeRequest = (
  plan: WipePlanRequest,
  signal: AbortSignal,
) => Promise<OperationAcceptedResponse>

function getErrorMessage(error: unknown): string {
  return error instanceof Error && error.message !== ''
    ? error.message
    : 'Request failed.'
}

function getMutationErrorMessage(error: unknown): string {
  if (error instanceof HttpError) {
    const detail = error.problemDetails?.detail?.trim()

    if (detail) {
      return detail
    }

    const title = error.problemDetails?.title?.trim()

    if (title) {
      return title
    }
  }

  return getErrorMessage(error)
}

function operationBlocksActions(
  operationDetail: ResourceState<Operation | null>,
): boolean {
  return (
    operationDetail.status === 'loading' ||
    (operationDetail.status === 'available' &&
      operationDetail.data !== null &&
      classifyOperationStatus(operationDetail.data.status) !== 'terminal')
  )
}

function getUpdateUnavailableReason(
  controlPlane: ControlPlaneInput,
  actionsBlocked: boolean,
): string | null {
  if (actionsBlocked || operationBlocksActions(controlPlane.operationDetail)) {
    return 'An update operation is in progress.'
  }

  if (
    controlPlane.serverStatus.status === 'available' &&
    deriveServerState(controlPlane.serverStatus.data) !== 'stopped'
  ) {
    return 'Server must be stopped to update.'
  }

  return null
}

/**
 * Owns the single Control-plane resource set for all application routes.
 * Pages consume presentation-specific slices, while request cancellation,
 * stale-response guards, mutations, and polling remain authoritative here.
 */
export function ControlPlaneProvider({ children }: PropsWithChildren) {
  const [serverStatus, setServerStatus] = useState<ResourceState<ServerStatus>>({
    status: 'loading',
  })
  const [configuration, setConfiguration] = useState<
    ResourceState<ServerConfigurationResponse>
  >({ status: 'loading' })
  const [startupSchema, setStartupSchema] = useState<
    ResourceState<StartupSchemaResponse>
  >({ status: 'loading' })
  const [startupSnapshot, setStartupSnapshot] = useState<
    ResourceState<StartupSnapshotResponse>
  >({ status: 'loading' })
  const [startupPollingEpoch, setStartupPollingEpoch] = useState(0)
  const [rustVersions, setRustVersions] = useState<ResourceState<RustVersionInfo>>({
    status: 'loading',
  })
  const [oxideVersions, setOxideVersions] = useState<
    ResourceState<OxideVersionInfo>
  >({ status: 'loading' })
  const [operationDetail, setOperationDetail] = useState<
    ResourceState<Operation | null>
  >(noKnownOperation)
  const [acceptedOperation, setAcceptedOperation] =
    useState<AcceptedOperationContext | null>(null)
  const [pendingMutation, setPendingMutation] = useState<PendingMutation | null>(
    null,
  )
  const [lifecycleError, setLifecycleError] = useState<string | null>(null)
  const [rustUpdateError, setRustUpdateError] = useState<string | null>(null)
  const [oxideUpdateError, setOxideUpdateError] = useState<string | null>(null)
  const [configurationSaveError, setConfigurationSaveError] = useState<
    string | null
  >(null)
  const [configurationSaveSucceeded, setConfigurationSaveSucceeded] =
    useState(false)
  const [wipeError, setWipeError] = useState<string | null>(null)
  const [postOperationReconciliationPending, setPostOperationReconciliationPending] =
    useState(false)

  const pendingMutationRef = useRef<PendingMutation | null>(null)
  const mutationControllerRef = useRef<AbortController | null>(null)
  const serverRefreshControllerRef = useRef<AbortController | null>(null)
  const configurationRefreshControllerRef = useRef<AbortController | null>(null)
  const startupSchemaRefreshControllerRef = useRef<AbortController | null>(null)
  const startupSnapshotRefreshControllerRef = useRef<AbortController | null>(null)
  const softwareRefreshControllerRef = useRef<AbortController | null>(null)
  const cancelReadinessPollingRef = useRef<() => void>(() => {})
  const cancelStartupPollingRef = useRef<() => void>(() => {})
  const cancelOperationPollingRef = useRef<() => void>(() => {})
  const reconciliationGenerationRef = useRef(0)
  const reconciliationPendingRef = useRef(false)
  const startupSnapshotGenerationRef = useRef(0)

  const refreshServerStatus = useCallback(async (): Promise<void> => {
    serverRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    serverRefreshControllerRef.current = controller

    try {
      const data = await getServerStatus(controller.signal)

      if (
        !controller.signal.aborted &&
        serverRefreshControllerRef.current === controller
      ) {
        setServerStatus({ status: 'available', data })
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        serverRefreshControllerRef.current === controller
      ) {
        setServerStatus({
          status: 'unavailable',
          message: getErrorMessage(error),
        })
      }
    } finally {
      if (serverRefreshControllerRef.current === controller) {
        serverRefreshControllerRef.current = null
      }
    }
  }, [])

  const refreshConfiguration = useCallback(async (): Promise<void> => {
    configurationRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    configurationRefreshControllerRef.current = controller

    try {
      const data = await getServerConfiguration(controller.signal)

      if (
        !controller.signal.aborted &&
        configurationRefreshControllerRef.current === controller
      ) {
        setConfiguration({ status: 'available', data })
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        configurationRefreshControllerRef.current === controller
      ) {
        setConfiguration({
          status: 'unavailable',
          message: getErrorMessage(error),
        })
      }
    } finally {
      if (configurationRefreshControllerRef.current === controller) {
        configurationRefreshControllerRef.current = null
      }
    }
  }, [])

  const refreshStartupSchema = useCallback(async (): Promise<void> => {
    startupSchemaRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    startupSchemaRefreshControllerRef.current = controller

    try {
      const data = await getStartupSchema(controller.signal)

      if (
        !controller.signal.aborted &&
        startupSchemaRefreshControllerRef.current === controller
      ) {
        setStartupSchema({ status: 'available', data })
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        startupSchemaRefreshControllerRef.current === controller
      ) {
        setStartupSchema({
          status: 'unavailable',
          message: getErrorMessage(error),
        })
      }
    } finally {
      if (startupSchemaRefreshControllerRef.current === controller) {
        startupSchemaRefreshControllerRef.current = null
      }
    }
  }, [])

  const refreshStartupSnapshot = useCallback(async (): Promise<void> => {
    startupSnapshotGenerationRef.current += 1
    cancelStartupPollingRef.current()
    // A manual refresh invalidates the previous poll. Re-arm observation even
    // when both the previous and refreshed snapshots have no session yet.
    setStartupPollingEpoch((epoch) => epoch + 1)
    startupSnapshotRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    startupSnapshotRefreshControllerRef.current = controller
    const generation = startupSnapshotGenerationRef.current

    try {
      const data = await getStartupSnapshot(controller.signal)

      if (
        !controller.signal.aborted &&
        startupSnapshotRefreshControllerRef.current === controller &&
        startupSnapshotGenerationRef.current === generation
      ) {
        setStartupSnapshot({ status: 'available', data })
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        startupSnapshotRefreshControllerRef.current === controller &&
        startupSnapshotGenerationRef.current === generation
      ) {
        setStartupSnapshot({
          status: 'unavailable',
          message: getErrorMessage(error),
        })
      }
    } finally {
      if (startupSnapshotRefreshControllerRef.current === controller) {
        startupSnapshotRefreshControllerRef.current = null
      }
    }
  }, [])

  const refreshSoftwareVersions = useCallback(async (): Promise<void> => {
    softwareRefreshControllerRef.current?.abort()

    const controller = new AbortController()
    softwareRefreshControllerRef.current = controller

    await Promise.all([
      getRustVersions(controller.signal)
        .then((data) => {
          if (
            !controller.signal.aborted &&
            softwareRefreshControllerRef.current === controller
          ) {
            setRustVersions({ status: 'available', data })
          }
        })
        .catch((error: unknown) => {
          if (
            !controller.signal.aborted &&
            softwareRefreshControllerRef.current === controller
          ) {
            setRustVersions({
              status: 'unavailable',
              message: getErrorMessage(error),
            })
          }
        }),
      getOxideVersions(controller.signal)
        .then((data) => {
          if (
            !controller.signal.aborted &&
            softwareRefreshControllerRef.current === controller
          ) {
            setOxideVersions({ status: 'available', data })
          }
        })
        .catch((error: unknown) => {
          if (
            !controller.signal.aborted &&
            softwareRefreshControllerRef.current === controller
          ) {
            setOxideVersions({
              status: 'unavailable',
              message: getErrorMessage(error),
            })
          }
        }),
    ])

    if (softwareRefreshControllerRef.current === controller) {
      softwareRefreshControllerRef.current = null
    }
  }, [])

  const reconcileObservableResources = useCallback(async (): Promise<void> => {
    const generation = reconciliationGenerationRef.current + 1
    reconciliationGenerationRef.current = generation
    reconciliationPendingRef.current = true
    setPostOperationReconciliationPending(true)
    cancelReadinessPollingRef.current()

    await Promise.all([
      refreshServerStatus(),
      refreshConfiguration(),
      refreshSoftwareVersions(),
      refreshStartupSnapshot(),
    ])

    if (reconciliationGenerationRef.current === generation) {
      reconciliationPendingRef.current = false
      setPostOperationReconciliationPending(false)
    }
  }, [
    refreshConfiguration,
    refreshServerStatus,
    refreshSoftwareVersions,
    refreshStartupSnapshot,
  ])

  const beginOperationPolling = useCallback(
    (accepted: AcceptedOperationContext): void => {
      cancelOperationPollingRef.current()

      const controller = new AbortController()
      let timer: ReturnType<typeof setTimeout> | null = null
      let cancelled = false

      const cancelOperationPolling = () => {
        cancelled = true

        if (timer !== null) {
          clearTimeout(timer)
          timer = null
        }

        controller.abort()
      }

      const isCurrentPoll = () =>
        !cancelled &&
        !controller.signal.aborted &&
        cancelOperationPollingRef.current === cancelOperationPolling

      const schedulePoll = () => {
        timer = setTimeout(() => {
          timer = null
          void poll()
        }, operationPollingIntervalMs)
      }

      const finishPollingAndReconcile = () => {
        cancelOperationPolling()
        void reconcileObservableResources()
      }

      async function poll(): Promise<void> {
        try {
          const data = await getOperation(accepted.id, controller.signal)

          if (!isCurrentPoll()) {
            return
          }

          setOperationDetail({ status: 'available', data })

          if (classifyOperationStatus(data.status) === 'terminal') {
            finishPollingAndReconcile()
            return
          }

          schedulePoll()
        } catch (error: unknown) {
          if (!isCurrentPoll()) {
            return
          }

          setOperationDetail({
            status: 'unavailable',
            message:
              error instanceof HttpError && error.status === 404
                ? undefined
                : getErrorMessage(error),
          })
          finishPollingAndReconcile()
        }
      }

      cancelOperationPollingRef.current = cancelOperationPolling
      void poll()
    },
    [reconcileObservableResources],
  )

  useEffect(() => {
    // These callbacks start independent HTTP resources; their guarded async
    // completions, rather than this effect itself, update local request state.
    // oxlint-disable-next-line react/set-state-in-effect
    void refreshServerStatus()
    void refreshConfiguration()
    void refreshStartupSchema()
    void refreshStartupSnapshot()
    void refreshSoftwareVersions()
  }, [
    refreshServerStatus,
    refreshConfiguration,
    refreshStartupSchema,
    refreshStartupSnapshot,
    refreshSoftwareVersions,
  ])

  useEffect(() => {
    return () => {
      reconciliationGenerationRef.current += 1
      mutationControllerRef.current?.abort()
      serverRefreshControllerRef.current?.abort()
      configurationRefreshControllerRef.current?.abort()
      startupSchemaRefreshControllerRef.current?.abort()
      startupSnapshotGenerationRef.current += 1
      startupSnapshotRefreshControllerRef.current?.abort()
      softwareRefreshControllerRef.current?.abort()
      cancelReadinessPollingRef.current()
      cancelStartupPollingRef.current()
      cancelOperationPollingRef.current()
    }
  }, [])

  const readinessReconciliationRequired =
    pendingMutation === null &&
    !postOperationReconciliationPending &&
    !operationBlocksActions(operationDetail) &&
    serverStatus.status === 'available' &&
    deriveServerState(serverStatus.data) === 'starting'

  const lifecycleStartPending =
    pendingMutation?.kind === 'lifecycle' &&
    (pendingMutation.action === 'start' || pendingMutation.action === 'restart')

  const startupPollingRequired =
    !postOperationReconciliationPending &&
    (pendingMutation === null || lifecycleStartPending) &&
    startupSnapshot.status === 'available' &&
    !startupSnapshot.data.started &&
    (startupSnapshot.data.sessionId !== null ||
      lifecycleStartPending ||
      (serverStatus.status === 'available' &&
        deriveServerState(serverStatus.data) === 'starting'))

  useEffect(() => {
    if (!readinessReconciliationRequired) {
      cancelReadinessPollingRef.current = () => {}
      return
    }

    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout> | null = null
    let cancelled = false

    const cancelReadinessPolling = () => {
      cancelled = true

      if (timer !== null) {
        clearTimeout(timer)
        timer = null
      }

      controller.abort()
    }

    function schedulePoll(): void {
      timer = setTimeout(() => {
        timer = null
        void poll()
      }, readinessReconciliationIntervalMs)
    }

    async function poll(): Promise<void> {
      try {
        const data = await getServerStatus(controller.signal)

        if (
          cancelled ||
          controller.signal.aborted ||
          pendingMutationRef.current !== null ||
          reconciliationPendingRef.current
        ) {
          return
        }

        setServerStatus({ status: 'available', data })

        const serverState = deriveServerState(data)

        if (serverState === 'starting') {
          schedulePoll()
        } else if (serverState === 'ready' || serverState === 'stopped') {
          void refreshSoftwareVersions()
        }
      } catch (error: unknown) {
        if (
          !cancelled &&
          !controller.signal.aborted &&
          pendingMutationRef.current === null &&
          !reconciliationPendingRef.current
        ) {
          setServerStatus({
            status: 'unavailable',
            message: getErrorMessage(error),
          })
        }
      }
    }

    cancelReadinessPollingRef.current = cancelReadinessPolling
    schedulePoll()

    return () => {
      cancelReadinessPolling()

      if (cancelReadinessPollingRef.current === cancelReadinessPolling) {
        cancelReadinessPollingRef.current = () => {}
      }
    }
  }, [readinessReconciliationRequired, refreshSoftwareVersions])

  useEffect(() => {
    if (!startupPollingRequired) {
      cancelStartupPollingRef.current = () => {}
      return
    }

    const controller = new AbortController()
    const generation = startupSnapshotGenerationRef.current
    let timer: ReturnType<typeof setTimeout> | null = null
    let cancelled = false

    const cancelStartupPolling = () => {
      cancelled = true

      if (timer !== null) {
        clearTimeout(timer)
        timer = null
      }

      controller.abort()
    }

    const isCurrentPoll = () =>
      !cancelled &&
      !controller.signal.aborted &&
      cancelStartupPollingRef.current === cancelStartupPolling &&
      startupSnapshotGenerationRef.current === generation

    function schedulePoll(): void {
      timer = setTimeout(() => {
        timer = null
        void poll()
      }, startupPollingIntervalMs)
    }

    async function poll(): Promise<void> {
      try {
        const data = await getStartupSnapshot(controller.signal)

        if (!isCurrentPoll()) {
          return
        }

        setStartupSnapshot({ status: 'available', data })

        // Compose can still be creating its container. Keep checking even
        // when the first snapshot has no sessionId.
        if (!data.started) {
          schedulePoll()
        }
      } catch (error: unknown) {
        if (isCurrentPoll()) {
          setStartupSnapshot({
            status: 'unavailable',
            message: getErrorMessage(error),
          })
        }
      }
    }

    cancelStartupPollingRef.current = cancelStartupPolling
    schedulePoll()

    return () => {
      cancelStartupPolling()

      if (cancelStartupPollingRef.current === cancelStartupPolling) {
        cancelStartupPollingRef.current = () => {}
      }
    }
  }, [startupPollingRequired, startupPollingEpoch])

  const controlPlane: ControlPlaneInput = {
    serverStatus,
    rustVersions,
    oxideVersions,
    operationDetail,
  }
  const actionAvailability = deriveActionAvailability(controlPlane)
  const mutationsBlocked =
    pendingMutation !== null || postOperationReconciliationPending
  const pendingLifecycleAction =
    pendingMutation?.kind === 'lifecycle' ? pendingMutation.action : null
  const pendingUpdateTarget =
    pendingMutation?.kind === 'update' ? pendingMutation.target : null
  const pendingConfigurationSave = pendingMutation?.kind === 'configuration'
  const pendingWipe = pendingMutation?.kind === 'wipe'
  const updateUnavailableReason = getUpdateUnavailableReason(
    controlPlane,
    mutationsBlocked,
  )
  const wipeUnavailableReason = getWipeUnavailableReason(
    serverStatus,
    mutationsBlocked,
    operationDetail,
  )

  async function runLifecycleMutation(
    action: LifecycleAction,
    request: LifecycleRequest,
  ): Promise<void> {
    if (pendingMutationRef.current !== null || reconciliationPendingRef.current) {
      return
    }

    const mutation: PendingMutation = { kind: 'lifecycle', action }
    const controller = new AbortController()
    pendingMutationRef.current = mutation
    mutationControllerRef.current = controller
    cancelReadinessPollingRef.current()
    startupSnapshotGenerationRef.current += 1
    cancelStartupPollingRef.current()
    startupSnapshotRefreshControllerRef.current?.abort()
    serverRefreshControllerRef.current?.abort()
    setLifecycleError(null)
    setPendingMutation(mutation)

    try {
      const data = await request(controller.signal)

      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setServerStatus({ status: 'available', data })
        void refreshConfiguration()
        void refreshSoftwareVersions()
        void refreshStartupSnapshot()
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setLifecycleError(getMutationErrorMessage(error))
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
        pendingMutationRef.current = null
        setPendingMutation(null)
      }
    }
  }

  async function runUpdateMutation(
    target: UpdateTarget,
    request: UpdateRequest,
  ): Promise<void> {
    if (pendingMutationRef.current !== null || reconciliationPendingRef.current) {
      return
    }

    const mutation: PendingMutation = { kind: 'update', target }
    const controller = new AbortController()
    pendingMutationRef.current = mutation
    mutationControllerRef.current = controller
    cancelReadinessPollingRef.current()
    serverRefreshControllerRef.current?.abort()
    setPendingMutation(mutation)

    if (target === 'rust') {
      setRustUpdateError(null)
    } else {
      setOxideUpdateError(null)
    }

    try {
      const response = await request(controller.signal)

      if (
        controller.signal.aborted ||
        mutationControllerRef.current !== controller
      ) {
        return
      }

      const accepted: AcceptedOperationContext = {
        id: response.id,
        status: response.status,
        target,
      }
      setAcceptedOperation(accepted)
      setOperationDetail({ status: 'loading' })
      beginOperationPolling(accepted)
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        if (target === 'rust') {
          setRustUpdateError(getMutationErrorMessage(error))
        } else {
          setOxideUpdateError(getMutationErrorMessage(error))
        }

        if (error instanceof HttpError && error.status === 409) {
          void reconcileObservableResources()
        }
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
        pendingMutationRef.current = null
        setPendingMutation(null)
      }
    }
  }

  async function runConfigurationMutation(
    request: UpdateServerConfigurationRequest,
    save: ConfigurationRequest,
  ): Promise<void> {
    if (pendingMutationRef.current !== null || reconciliationPendingRef.current) {
      return
    }

    const mutation: PendingMutation = { kind: 'configuration' }
    const controller = new AbortController()
    pendingMutationRef.current = mutation
    mutationControllerRef.current = controller
    setConfigurationSaveError(null)
    setConfigurationSaveSucceeded(false)
    setPendingMutation(mutation)

    try {
      const data = await save(request, controller.signal)

      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setConfiguration({ status: 'available', data })
        setConfigurationSaveSucceeded(true)
      }
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setConfigurationSaveError(getMutationErrorMessage(error))
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
        pendingMutationRef.current = null
        setPendingMutation(null)
      }
    }
  }

  async function runWipeMutation(
    plan: WipePlanRequest,
    request: WipeRequest,
  ): Promise<void> {
    if (pendingMutationRef.current !== null || reconciliationPendingRef.current) {
      return
    }

    if (
      serverStatus.status !== 'available' ||
      deriveServerState(serverStatus.data) !== 'stopped' ||
      operationBlocksActions(operationDetail)
    ) {
      setWipeError(
        getWipeUnavailableReason(serverStatus, false, operationDetail) ??
          'Wipe is unavailable.',
      )
      return
    }

    const mutation: PendingMutation = { kind: 'wipe' }
    const controller = new AbortController()
    pendingMutationRef.current = mutation
    mutationControllerRef.current = controller
    cancelReadinessPollingRef.current()
    serverRefreshControllerRef.current?.abort()
    setWipeError(null)
    setPendingMutation(mutation)

    try {
      const response = await request(plan, controller.signal)

      if (
        controller.signal.aborted ||
        mutationControllerRef.current !== controller
      ) {
        return
      }

      const accepted: AcceptedOperationContext = {
        id: response.id,
        status: response.status,
        target: 'wipe',
      }
      setAcceptedOperation(accepted)
      setOperationDetail({ status: 'loading' })
      beginOperationPolling(accepted)
    } catch (error: unknown) {
      if (
        !controller.signal.aborted &&
        mutationControllerRef.current === controller
      ) {
        setWipeError(getMutationErrorMessage(error))

        if (error instanceof HttpError && error.status === 409) {
          void reconcileObservableResources()
        }
      }
    } finally {
      if (mutationControllerRef.current === controller) {
        mutationControllerRef.current = null
        pendingMutationRef.current = null
        setPendingMutation(null)
      }
    }
  }

  const value: ControlPlaneContextValue = {
    acceptedOperation,
    actionAvailability,
    configuration,
    configurationSaveError,
    configurationSaveSucceeded,
    lifecycleError,
    mutationsBlocked,
    operationDetail,
    oxideUpdateError,
    oxideVersions,
    pendingConfigurationSave,
    pendingLifecycleAction,
    pendingUpdateTarget,
    pendingWipe,
    requestOxideUpdate: () => void runUpdateMutation('oxide', requestOxideUpdate),
    requestRustUpdate: () => void runUpdateMutation('rust', requestRustUpdate),
    requestWipe: (plan) => void runWipeMutation(plan, requestWipe),
    restart: () => void runLifecycleMutation('restart', restartServer),
    rustUpdateError,
    rustVersions,
    saveConfiguration: (request) =>
      void runConfigurationMutation(request, updateServerConfiguration),
    serverStatus,
    start: () => void runLifecycleMutation('start', startServer),
    startupSchema,
    startupSnapshot,
    stop: () => void runLifecycleMutation('stop', stopServer),
    updateUnavailableReason,
    wipeError,
    wipeUnavailableReason,
  }

  return (
    <ControlPlaneContext.Provider value={value}>
      {children}
    </ControlPlaneContext.Provider>
  )
}
