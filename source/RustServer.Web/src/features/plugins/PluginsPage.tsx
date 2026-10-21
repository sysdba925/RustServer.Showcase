import { useEffect, useRef, useState } from 'react'

import type {
  GetGitStatusResponse,
  ReleaseResponse,
  RuntimeDiffChangeResponse,
  SourcePreviewResponse,
} from '../../shared/api'
import { pluginsApi, type PluginsApi } from './api'
import { AppliedBaselinePanel } from './components/AppliedBaselinePanel'
import { EnvironmentDialog } from './components/EnvironmentDialog'
import {
  GitOperationDialog,
  type GitOperationDialogState,
} from './components/GitOperationDialog'
import { GitRepositoriesPanel } from './components/GitRepositoriesPanel'
import { OperationHistoryDialog } from './components/OperationHistoryDialog'
import { OperationReportDialog } from './components/OperationReportDialog'
import { PluginsHeader } from './components/PluginsHeader'
import { RuntimeDiffDialog } from './components/RuntimeDiffDialog'
import {
  SourcePreviewDialog,
  SourceSelectionDialog,
} from './components/SourceDialog'
import { usePluginsDashboard } from './hooks/usePluginsDashboard'
import { usePluginsOperation } from './hooks/usePluginsOperation'
import { getPluginsErrorMessage } from './model/plugins-error'
import type { PluginsDashboardState } from './model/plugins-dashboard'
import {
  draftFromBaseline,
  toTargetSourceRequest,
  type SourceDraft,
} from './model/source'
import styles from './PluginsPage.module.css'

type DialogState =
  | { type: 'environment' }
  | { type: 'git'; operation: GitOperationDialogState }
  | { type: 'history' }
  | { type: 'runtime' }
  | { type: 'source'; view: 'selection' | 'preview' }
  | { type: 'operation' }

interface PluginsPageProps {
  /** Storybook and focused UI tests can supply deterministic resource snapshots. */
  dashboard?: PluginsDashboardState
  /** Stories and tests provide this adapter to mock published API responses. */
  api?: PluginsApi
}

function availableReleases(dashboard: PluginsDashboardState) {
  return dashboard.releases.status === 'available'
    ? dashboard.releases.data.releases.filter((release) => release.status === 'complete')
    : []
}

function initialSourceDraft(dashboard: PluginsDashboardState): SourceDraft {
  const baseline = dashboard.baseline.status === 'available'
    ? dashboard.baseline.data.baseline
    : null
  const fromBaseline = draftFromBaseline(baseline)
  if (fromBaseline !== null) {
    return fromBaseline
  }

  const environment = dashboard.environment.status === 'available'
    ? dashboard.environment.data.environment
    : 'staging'
  const release = availableReleases(dashboard)[0]

  return environment === 'production' && release !== undefined
    ? { kind: 'release', version: release.version }
    : { kind: 'development' }
}

function canPreviewSource(
  draft: SourceDraft,
  environment: string,
  releases: ReleaseResponse[],
): boolean {
  return draft.kind === 'release'
    ? releases.some((release) => release.version === draft.version && release.status === 'complete')
    : environment !== 'production'
}

/** Stage 2.4 Plugins Management screen, scoped to Plugins-owned workflows. */
export function PluginsPage({ dashboard: suppliedDashboard, api = pluginsApi }: PluginsPageProps) {
  const { dashboard, refresh } = usePluginsDashboard({
    api,
    initialDashboard: suppliedDashboard,
  })
  const { operation, operationActive, reset: resetOperation, retryStatus, run: runOperation } =
    usePluginsOperation(api)
  const [dialog, setDialog] = useState<DialogState | null>(null)
  const [environmentSubmitting, setEnvironmentSubmitting] = useState(false)
  const [environmentError, setEnvironmentError] = useState<string | null>(null)
  const [sourceDraft, setSourceDraft] = useState<SourceDraft>(() =>
    initialSourceDraft(suppliedDashboard ?? dashboard),
  )
  const [sourcePreview, setSourcePreview] = useState<
    { status: 'idle' | 'loading' } | { status: 'available'; data: SourcePreviewResponse }
  >({ status: 'idle' })
  const [sourcePreviewError, setSourcePreviewError] = useState<string | null>(null)
  const environmentControllerRef = useRef<AbortController | null>(null)
  const sourcePreviewControllerRef = useRef<AbortController | null>(null)

  useEffect(() => () => {
    environmentControllerRef.current?.abort()
    sourcePreviewControllerRef.current?.abort()
  }, [])

  const currentEnvironment = dashboard.environment.status === 'available'
    ? dashboard.environment.data.environment
    : null
  const releases = dashboard.releases.status === 'available'
    ? dashboard.releases.data.releases
    : []

  function closeDialog(): void {
    if (!operationActive && !environmentSubmitting) {
      sourcePreviewControllerRef.current?.abort()
      setDialog(null)
    }
  }

  function openSourceSelection(): void {
    setSourceDraft(initialSourceDraft(dashboard))
    setSourcePreview({ status: 'idle' })
    setSourcePreviewError(null)
    setDialog({ type: 'source', view: 'selection' })
  }

  function openRuntimeDiff(): void {
    if (dashboard.runtimeDiff.status === 'available') {
      setDialog({ type: 'runtime' })
    }
  }

  function openHistory(): void {
    setDialog({ type: 'history' })
  }

  async function requestSourcePreview(draft: SourceDraft): Promise<void> {
    sourcePreviewControllerRef.current?.abort()
    const controller = new AbortController()
    sourcePreviewControllerRef.current = controller
    setSourcePreview({ status: 'loading' })
    setSourcePreviewError(null)
    setDialog({ type: 'source', view: 'preview' })

    try {
      const data = await api.previewSource(toTargetSourceRequest(draft), controller.signal)
      if (!controller.signal.aborted && sourcePreviewControllerRef.current === controller) {
        setSourcePreview({ status: 'available', data })
      }
    } catch (error: unknown) {
      if (!controller.signal.aborted && sourcePreviewControllerRef.current === controller) {
        setSourcePreview({ status: 'idle' })
        setSourcePreviewError(getPluginsErrorMessage(error))
        setDialog({ type: 'source', view: 'selection' })
      }
    } finally {
      if (sourcePreviewControllerRef.current === controller) {
        sourcePreviewControllerRef.current = null
      }
    }
  }

  async function changeEnvironment(targetEnvironment: string): Promise<void> {
    if (environmentSubmitting) {
      return
    }

    environmentControllerRef.current?.abort()
    const controller = new AbortController()
    environmentControllerRef.current = controller
    setEnvironmentSubmitting(true)
    setEnvironmentError(null)

    try {
      await api.updateEnvironment({ environment: targetEnvironment }, controller.signal)
      if (!controller.signal.aborted && environmentControllerRef.current === controller) {
        // The PUT has succeeded even when a follow-up read is unavailable.
        // Each affected dashboard resource displays that read failure itself.
        await refresh(['environment', 'baseline']).catch(() => undefined)
        setDialog(null)
      }
    } catch (error: unknown) {
      if (!controller.signal.aborted && environmentControllerRef.current === controller) {
        setEnvironmentError(getPluginsErrorMessage(error))
      }
    } finally {
      if (environmentControllerRef.current === controller) {
        environmentControllerRef.current = null
        setEnvironmentSubmitting(false)
      }
    }
  }

  function openFetchDialog(gitStatus: GetGitStatusResponse): void {
    setDialog({ type: 'git', operation: { type: 'fetch', gitStatus } })
  }

  function openSyncDialog(repository: 'plugins' | 'configs'): void {
    if (dashboard.gitStatus.status !== 'available') {
      return
    }

    setDialog({
      type: 'git',
      operation: {
        type: 'sync',
        repository,
        relation: dashboard.gitStatus.data[repository].relation,
      },
    })
  }

  function beginGitOperation(gitOperation: GitOperationDialogState): void {
    setDialog({ type: 'operation' })

    if (gitOperation.type === 'fetch') {
      runOperation(
        'Fetch Git',
        (signal) => api.fetchGit(signal),
        () => refresh(['gitStatus', 'releases']),
      )
      return
    }

    runOperation(
      `Synchronize ${gitOperation.repository === 'plugins' ? 'Plugins' : 'Configs'}`,
      (signal) => api.syncGit({ repository: gitOperation.repository }, signal),
      () => refresh(['gitStatus', 'releases']),
    )
  }

  function openApplySource(): void {
    const baseline = dashboard.baseline.status === 'available'
      ? dashboard.baseline.data.baseline
      : null
    const baselineDraft = draftFromBaseline(baseline)

    if (baselineDraft === null) {
      openSourceSelection()
      return
    }

    setSourceDraft(baselineDraft)
    void requestSourcePreview(baselineDraft)
  }

  function beginApplySource(): void {
    setDialog({ type: 'operation' })
    runOperation(
      'Apply Source',
      (signal) => api.applySource(toTargetSourceRequest(sourceDraft), signal),
      () => refresh(),
    )
  }

  function beginRuntimeCommit(message: string, changes: RuntimeDiffChangeResponse[]): void {
    setDialog({ type: 'operation' })
    runOperation(
      'Commit + Push Runtime Changes',
      (signal) => api.commitRuntime({
        message,
        changes: changes.map(({ repository, path }) => ({ repository, path })),
      }, signal),
      () => refresh(['gitStatus', 'baseline', 'runtimeDiff']),
    )
  }

  function beginRuntimeRevert(changes: RuntimeDiffChangeResponse[]): void {
    setDialog({ type: 'operation' })
    runOperation(
      'Revert selected Runtime Changes',
      (signal) => api.revertRuntime({
        changes: changes.map(({ repository, path }) => ({ repository, path })),
      }, signal),
      () => refresh(['runtimeDiff']),
    )
  }

  function beginRuntimeRevertAll(): void {
    setDialog({ type: 'operation' })
    runOperation(
      'Revert all Runtime Changes',
      (signal) => api.revertAllRuntime(signal),
      () => refresh(['runtimeDiff']),
    )
  }

  const commandsBlocked = operationActive || environmentSubmitting

  return (
    <div className={styles.page}>
      <PluginsHeader
        actionsBlocked={commandsBlocked}
        dashboard={dashboard}
        onApplySource={openApplySource}
        onChangeEnvironment={() => {
          setEnvironmentError(null)
          setDialog({ type: 'environment' })
        }}
        onOpenHistory={openHistory}
        onSwitchSource={openSourceSelection}
      />

      <div className={styles.panelGrid}>
        <GitRepositoriesPanel
          actionsBlocked={commandsBlocked}
          gitStatus={dashboard.gitStatus}
          onFetch={() => {
            if (dashboard.gitStatus.status === 'available') {
              openFetchDialog(dashboard.gitStatus.data)
            }
          }}
          onSynchronize={openSyncDialog}
        />
        <AppliedBaselinePanel
          dashboard={dashboard}
          onOpenRuntimeDiff={openRuntimeDiff}
          onRefreshRuntimeDiff={() => {
            void refresh(['runtimeDiff']).catch(() => undefined)
          }}
        />
      </div>

      <p className={styles.notice}>
        Git synchronization, Source Apply, and logical Environment changes are separate
        Plugins workflows. A successful operation report is followed by a backend refresh.
      </p>

      {dialog?.type === 'environment' && currentEnvironment !== null && (
        <EnvironmentDialog
          currentEnvironment={currentEnvironment}
          error={environmentError}
          onCancel={closeDialog}
          onConfirm={(target) => void changeEnvironment(target)}
          submitting={environmentSubmitting}
        />
      )}
      {dialog?.type === 'git' && (
        <GitOperationDialog
          dialog={dialog.operation}
          onCancel={closeDialog}
          onConfirm={() => beginGitOperation(dialog.operation)}
          submitting={operationActive}
        />
      )}
      {dialog?.type === 'source' && currentEnvironment !== null && dialog.view === 'selection' && (
        <SourceSelectionDialog
          draft={sourceDraft}
          environment={currentEnvironment}
          hasValidSelection={canPreviewSource(sourceDraft, currentEnvironment, releases)}
          onCancel={closeDialog}
          onChange={(draft) => {
            setSourceDraft(draft)
            setSourcePreviewError(null)
          }}
          onPreview={() => void requestSourcePreview(sourceDraft)}
          previewError={sourcePreviewError}
          previewing={sourcePreview.status === 'loading'}
          releases={releases}
        />
      )}
      {dialog?.type === 'source' && dialog.view === 'preview' && (
        <SourcePreviewDialog
          applying={operationActive}
          onBack={() => {
            sourcePreviewControllerRef.current?.abort()
            setSourcePreview({ status: 'idle' })
            setDialog({ type: 'source', view: 'selection' })
          }}
          onCancel={closeDialog}
          onConfirmApply={beginApplySource}
          preview={sourcePreview.status === 'available' ? sourcePreview.data : null}
        />
      )}
      {dialog?.type === 'runtime' && dashboard.runtimeDiff.status === 'available' && (
        <RuntimeDiffDialog
          baseline={dashboard.baseline.status === 'available' ? dashboard.baseline.data.baseline : null}
          environment={currentEnvironment ?? ''}
          getContent={api.getRuntimeDiffContent}
          onClose={closeDialog}
          onCommit={beginRuntimeCommit}
          onRevertAll={beginRuntimeRevertAll}
          onRevertSelected={beginRuntimeRevert}
          runtimeDiff={dashboard.runtimeDiff.data}
        />
      )}
      {dialog?.type === 'history' && (
        <OperationHistoryDialog
          getOperation={api.getOperation}
          getOperations={api.getOperations}
          onClose={closeDialog}
        />
      )}
      {dialog?.type === 'operation' && (
        <OperationReportDialog
          onClose={() => {
            resetOperation()
            setDialog(null)
          }}
          operation={operation}
          onRetryStatus={retryStatus}
        />
      )}
    </div>
  )
}
