import type { GetGitStatusResponse } from '../../../shared/api'

import { PluginsDialog } from './PluginsDialog'
import styles from '../PluginsPage.module.css'

export type GitOperationDialogState =
  | { type: 'fetch'; gitStatus: GetGitStatusResponse }
  | { type: 'sync'; repository: 'plugins' | 'configs'; relation: string }

interface GitOperationDialogProps {
  dialog: GitOperationDialogState
  submitting: boolean
  onCancel: () => void
  onConfirm: () => void
}

function formatRepository(repository: 'plugins' | 'configs'): string {
  return repository === 'plugins' ? 'Plugins' : 'Configs'
}

/** Confirmation copy reflects what the backend will actually perform. */
export function GitOperationDialog({
  dialog,
  submitting,
  onCancel,
  onConfirm,
}: GitOperationDialogProps) {
  const isFetch = dialog.type === 'fetch'
  const title = isFetch ? 'Fetch Git' : `Synchronize ${formatRepository(dialog.repository)}`

  return (
    <PluginsDialog
      footer={(
        <>
          <button className={styles.commandButton} disabled={submitting} onClick={onCancel} type="button">
            Cancel
          </button>
          <button className={`${styles.commandButton} ${styles.primaryCommand}`} disabled={submitting} onClick={onConfirm} type="button">
            {submitting ? 'Submitting…' : isFetch ? 'Fetch Git' : 'Synchronize repository'}
          </button>
        </>
      )}
      onClose={submitting ? undefined : onCancel}
      title={title}
    >
      {isFetch ? (
        <>
          <p className={styles.dialogHint}>
            Fetch updates known remote-tracking refs for both repositories. It does
            not change local branches, the applied Baseline, Runtime files, or the
            Environment.
          </p>
          <dl className={styles.dialogSummary}>
            <div><dt>Plugins</dt><dd>{dialog.gitStatus.plugins.relation}</dd></div>
            <div><dt>Configs</dt><dd>{dialog.gitStatus.configs.relation}</dd></div>
          </dl>
        </>
      ) : (
        <>
          <p className={styles.dialogHint}>
            The backend will fetch and re-read Git state before choosing the only
            safe behavior. No force push, rebase, or hidden source apply
            will be performed, including while Rust is running.
          </p>
          <dl className={styles.dialogSummary}>
            <div><dt>Repository</dt><dd>{formatRepository(dialog.repository)}</dd></div>
            <div><dt>Reported relation</dt><dd>{dialog.relation}</dd></div>
          </dl>
        </>
      )}
    </PluginsDialog>
  )
}
