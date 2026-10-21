import { useEffect, useRef, useState, type ReactNode } from 'react'

import type {
  BaselineResponse,
  GetRuntimeDiffContentResponse,
  GetRuntimeDiffResponse,
  RuntimeDiffChangeResponse,
  RuntimeDiffContentSideResponse,
} from '../../../shared/api'
import { getPluginsErrorMessage } from '../model/plugins-error'
import {
  createRuntimeSideBySideLineDiff,
  type RuntimeDiffLine,
  type RuntimeDiffLineKind,
} from '../model/runtime-line-diff'
import styles from '../PluginsPage.module.css'

import { PluginsDialog } from './PluginsDialog'

type RuntimeDialogView = 'diff' | 'content' | 'commit' | 'revertSelected' | 'revertAll'

interface RuntimeDiffDialogProps {
  baseline: BaselineResponse | null
  environment: string
  runtimeDiff: GetRuntimeDiffResponse
  getContent: (
    repository: string,
    path: string,
    signal?: AbortSignal,
  ) => Promise<GetRuntimeDiffContentResponse>
  onClose: () => void
  onCommit: (message: string, changes: RuntimeDiffChangeResponse[]) => void
  onRevertSelected: (changes: RuntimeDiffChangeResponse[]) => void
  onRevertAll: () => void
}

function changeKey({ repository, path }: RuntimeDiffChangeResponse): string {
  return `${repository}\u0000${path}`
}

function changeClassName(change: string): string {
  switch (change) {
    case 'added': return styles.changeAdded
    case 'modified': return styles.changeModified
    case 'deleted': return styles.changeDeleted
    default: return ''
  }
}

function changeLabel(change: string): string {
  switch (change) {
    case 'added': return 'Added'
    case 'modified': return 'Modified'
    case 'deleted': return 'Deleted'
    default: return change
  }
}

function commitTarget(baseline: BaselineResponse | null): string {
  if (baseline?.source === 'development') {
    return 'develop'
  }

  if (baseline?.source === 'release' && baseline.release !== null) {
    return `release/${baseline.release}`
  }

  return 'the validated Baseline Source'
}

function ContentSide({ label, side }: { label: string; side: RuntimeDiffContentSideResponse }) {
  let body: ReactNode
  if (side.kind === 'missing') {
    body = <p className={styles.localState}>No file</p>
  } else if (side.kind === 'binary') {
    body = <p className={styles.localState}>Binary content cannot be shown.</p>
  } else {
    body = <pre className={styles.contentPreview}>{side.text ?? ''}</pre>
  }

  return (
    <section className={styles.contentSide}>
      <h3>{label}</h3>
      <span className={styles.contentKind}>{side.kind}</span>
      {body}
    </section>
  )
}

function lineClassName(kind: RuntimeDiffLineKind): string {
  switch (kind) {
    case 'added': return styles.diffLineAdded
    case 'removed': return styles.diffLineRemoved
    case 'unchanged': return styles.diffLineUnchanged
  }
}

function linePrefix(kind: RuntimeDiffLineKind): string {
  switch (kind) {
    case 'added': return '+'
    case 'removed': return '−'
    case 'unchanged': return ' '
  }
}

function DiffLine({ line }: { line: RuntimeDiffLine | null }) {
  if (line === null) {
    return <span aria-hidden="true" className={`${styles.diffLine} ${styles.diffLinePlaceholder}`}> </span>
  }

  return (
    <span className={`${styles.diffLine} ${lineClassName(line.kind)}`}>
      <span aria-hidden="true" className={styles.diffLinePrefix}>{linePrefix(line.kind)}</span>
      {line.text === '' ? ' ' : line.text}
    </span>
  )
}

function ContentComparison({
  baseline,
  runtime,
}: {
  baseline: RuntimeDiffContentSideResponse
  runtime: RuntimeDiffContentSideResponse
}) {
  if (baseline.kind === 'binary' || runtime.kind === 'binary') {
    return (
      <div className={styles.contentComparison}>
        <ContentSide label="Baseline" side={baseline} />
        <ContentSide label="Runtime" side={runtime} />
      </div>
    )
  }

  const lines = createRuntimeSideBySideLineDiff(
    baseline.kind === 'text' ? baseline.text ?? '' : '',
    runtime.kind === 'text' ? runtime.text ?? '' : '',
  )

  if (lines.length === 0) {
    return <p className={styles.localState}>The file has no text lines.</p>
  }

  return (
    <div aria-label="Line-by-line comparison" className={styles.sideBySideDiff}>
      <div className={styles.diffColumnHeader}>
        <strong>Baseline</strong>
        <span className={styles.contentKind}>{baseline.kind}</span>
      </div>
      <div className={styles.diffColumnHeader}>
        <strong>Runtime</strong>
        <span className={styles.contentKind}>{runtime.kind}</span>
      </div>
      {lines.map((line, index) => (
        <div className={styles.sideBySideDiffRow} key={`comparison-row-${index}`}>
          <DiffLine line={line.baseline} />
          <DiffLine line={line.runtime} />
        </div>
      ))}
    </div>
  )
}

/** V15 Runtime Diff workflow: inspect, select, then confirm an explicit mutation. */
export function RuntimeDiffDialog({
  baseline,
  environment,
  runtimeDiff,
  getContent,
  onClose,
  onCommit,
  onRevertSelected,
  onRevertAll,
}: RuntimeDiffDialogProps) {
  const [view, setView] = useState<RuntimeDialogView>('diff')
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(
    () => new Set(runtimeDiff.changes.map(changeKey)),
  )
  const [commitMessage, setCommitMessage] = useState('Update runtime configuration')
  const [content, setContent] = useState<GetRuntimeDiffContentResponse | null>(null)
  const [contentLoading, setContentLoading] = useState(false)
  const [contentError, setContentError] = useState<string | null>(null)
  const contentControllerRef = useRef<AbortController | null>(null)

  useEffect(() => () => contentControllerRef.current?.abort(), [])

  const selectedChanges = runtimeDiff.changes.filter((change) => selectedKeys.has(changeKey(change)))
  const commitAvailable = environment === 'staging' && runtimeDiff.status === 'ready'

  function updateSelection(change: RuntimeDiffChangeResponse, selected: boolean): void {
    const key = changeKey(change)
    setSelectedKeys((current) => {
      const next = new Set(current)
      if (selected) {
        next.add(key)
      } else {
        next.delete(key)
      }
      return next
    })
  }

  async function showContent(change: RuntimeDiffChangeResponse): Promise<void> {
    contentControllerRef.current?.abort()
    const controller = new AbortController()
    contentControllerRef.current = controller
    setContent(null)
    setContentError(null)
    setContentLoading(true)
    setView('content')

    try {
      const response = await getContent(change.repository, change.path, controller.signal)
      if (!controller.signal.aborted && contentControllerRef.current === controller) {
        setContent(response)
      }
    } catch (error: unknown) {
      if (!controller.signal.aborted && contentControllerRef.current === controller) {
        setContentError(getPluginsErrorMessage(error))
      }
    } finally {
      if (contentControllerRef.current === controller) {
        contentControllerRef.current = null
        setContentLoading(false)
      }
    }
  }

  function backToDiff(): void {
    contentControllerRef.current?.abort()
    setContentLoading(false)
    setView('diff')
  }

  if (view === 'content') {
    const contentEntry = content?.content
    return (
      <PluginsDialog
        footer={<button className={styles.commandButton} onClick={backToDiff} type="button">Back to Runtime Diff</button>}
        onClose={backToDiff}
        title={contentEntry === undefined ? 'Runtime file content' : `File Diff · ${contentEntry.path}`}
      >
        {contentLoading && <p className={styles.localState} role="status">Loading content comparison…</p>}
        {contentError !== null && <p className={styles.errorState} role="alert">{contentError}</p>}
        {contentEntry !== undefined && (
          <>
            <p className={styles.dialogHint}>
              {contentEntry.repository} · <span className={changeClassName(contentEntry.change)}>{changeLabel(contentEntry.change)}</span> · Baseline ↔ Runtime
            </p>
            <ContentComparison baseline={contentEntry.baseline} runtime={contentEntry.runtime} />
          </>
        )}
      </PluginsDialog>
    )
  }

  if (view === 'commit') {
    const messageIsValid = commitMessage.trim().length > 0
    return (
      <PluginsDialog
        footer={(
          <>
            <button className={styles.commandButton} onClick={() => setView('diff')} type="button">Back</button>
            <button
              className={`${styles.commandButton} ${styles.primaryCommand}`}
              disabled={!messageIsValid}
              onClick={() => onCommit(commitMessage.trim(), selectedChanges)}
              type="button"
            >
              Commit + Push
            </button>
          </>
        )}
        onClose={() => setView('diff')}
        title="Commit + Push"
      >
        <p className={styles.dialogHint}>
          {selectedChanges.length} selected file{selectedChanges.length === 1 ? '' : 's'} will be committed to <code>{commitTarget(baseline)}</code>. The backend validates the current Runtime Diff and Git safety again before it changes either repository.
        </p>
        <label className={styles.fieldLabel} htmlFor="runtime-commit-message">Commit message</label>
        <input
          className={styles.textInput}
          id="runtime-commit-message"
          onChange={(event) => setCommitMessage(event.target.value)}
          value={commitMessage}
        />
      </PluginsDialog>
    )
  }

  if (view === 'revertSelected') {
    return (
      <PluginsDialog
        footer={(
          <>
            <button className={styles.commandButton} onClick={() => setView('diff')} type="button">Back</button>
            <button className={`${styles.commandButton} ${styles.dangerCommand}`} onClick={() => onRevertSelected(selectedChanges)} type="button">Confirm Revert</button>
          </>
        )}
        onClose={() => setView('diff')}
        title="Revert selected Runtime changes?"
      >
        <p className={styles.dialogHint}>
          Restore {selectedChanges.length} selected file{selectedChanges.length === 1 ? '' : 's'} from the applied Baseline? Existing changes in this selected scope will be discarded.
        </p>
      </PluginsDialog>
    )
  }

  if (view === 'revertAll') {
    return (
      <PluginsDialog
        footer={(
          <>
            <button className={styles.commandButton} onClick={() => setView('diff')} type="button">Back</button>
            <button className={`${styles.commandButton} ${styles.dangerCommand}`} onClick={onRevertAll} type="button">Revert all</button>
          </>
        )}
        onClose={() => setView('diff')}
        title="Revert all Runtime changes?"
      >
        <p className={styles.dialogHint}>
          Restore the complete managed Runtime set from the applied Baseline. This discards all managed Runtime changes, not only the selected files.
        </p>
      </PluginsDialog>
    )
  }

  if (runtimeDiff.status !== 'ready') {
    return (
      <PluginsDialog
        footer={<button className={styles.commandButton} onClick={onClose} type="button">Close</button>}
        onClose={onClose}
        title="Runtime Diff"
      >
        <p className={styles.localState}>No applied Baseline is initialized, so Runtime changes cannot be compared yet.</p>
      </PluginsDialog>
    )
  }

  if (runtimeDiff.changes.length === 0) {
    return (
      <PluginsDialog
        footer={<button className={styles.commandButton} onClick={onClose} type="button">Close</button>}
        onClose={onClose}
        title="Runtime Diff"
      >
        <p className={styles.localState}>Runtime matches the applied Baseline.</p>
      </PluginsDialog>
    )
  }

  const repositories = ['plugins', 'configs']
  const unknownRepositoryChanges = runtimeDiff.changes.filter(
    (change) => !repositories.includes(change.repository),
  )

  return (
    <PluginsDialog
      footer={(
        <>
          <button className={`${styles.commandButton} ${styles.dangerCommand}`} onClick={() => setView('revertAll')} type="button">Revert all</button>
          <button className={`${styles.commandButton} ${styles.dangerCommand}`} disabled={selectedChanges.length === 0} onClick={() => setView('revertSelected')} type="button">Revert selected</button>
          {commitAvailable && (
            <button className={`${styles.commandButton} ${styles.primaryCommand}`} disabled={selectedChanges.length === 0} onClick={() => setView('commit')} type="button">Commit selected + Push</button>
          )}
          <button className={styles.commandButton} onClick={onClose} type="button">Close</button>
        </>
      )}
      onClose={onClose}
      title="Runtime Diff"
    >
      <p className={styles.dialogHint}>
        <strong>Baseline ↔ Runtime</strong><span className={styles.hintSeparator}>Commit → Git · Revert → Baseline</span>
      </p>
      <p className={styles.localState}>Select files to process; select a colored path to view its content comparison.</p>
      {repositories.map((repository) => {
        const changes = runtimeDiff.changes.filter((change) => change.repository === repository)
        if (changes.length === 0) {
          return null
        }

        return (
          <section aria-labelledby={`runtime-diff-${repository}`} className={styles.runtimeRepository} key={repository}>
            <h3 id={`runtime-diff-${repository}`}>{repository === 'plugins' ? 'Plugins' : 'Configs'}</h3>
            <div className={styles.runtimeChangeList}>
              {changes.map((change) => (
                <div className={styles.runtimeChange} key={changeKey(change)}>
                  <input
                    aria-label={`Select ${change.path}`}
                    checked={selectedKeys.has(changeKey(change))}
                    onChange={(event) => updateSelection(change, event.target.checked)}
                    type="checkbox"
                  />
                  <span className={`${styles.changeType} ${changeClassName(change.change)}`}>{changeLabel(change.change)}</span>
                  <button className={`${styles.fileLink} ${changeClassName(change.change)}`} onClick={() => void showContent(change)} type="button">{change.path}</button>
                </div>
              ))}
            </div>
          </section>
        )
      })}
      {unknownRepositoryChanges.length > 0 && (
        <section aria-labelledby="runtime-diff-other" className={styles.runtimeRepository}>
          <h3 id="runtime-diff-other">Other managed files</h3>
          <div className={styles.runtimeChangeList}>
            {unknownRepositoryChanges.map((change) => (
              <div className={styles.runtimeChange} key={changeKey(change)}>
                <input aria-label={`Select ${change.path}`} checked={selectedKeys.has(changeKey(change))} onChange={(event) => updateSelection(change, event.target.checked)} type="checkbox" />
                <span className={`${styles.changeType} ${changeClassName(change.change)}`}>{changeLabel(change.change)}</span>
                <button className={`${styles.fileLink} ${changeClassName(change.change)}`} onClick={() => void showContent(change)} type="button">{change.repository} / {change.path}</button>
              </div>
            ))}
          </div>
        </section>
      )}
      {!commitAvailable && <p className={styles.productionNotice}>Runtime Commit + Push is unavailable in Production.</p>}
    </PluginsDialog>
  )
}
