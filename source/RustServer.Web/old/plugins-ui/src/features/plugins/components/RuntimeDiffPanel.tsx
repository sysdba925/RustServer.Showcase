import ReactDiffViewer from 'react-diff-viewer-continued'

import type { StagingRuntimeChangeDiffResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'

interface Props {
  diff: StagingRuntimeChangeDiffResponse | null
  loading: boolean
  error: string | null
  onClose: () => void
}

function baselineTitle(diff: StagingRuntimeChangeDiffResponse): string {
  return diff.changeType === 'added' ? 'Deployed baseline: no file' : 'Deployed baseline'
}

function runtimeTitle(diff: StagingRuntimeChangeDiffResponse): string {
  return diff.changeType === 'deleted' ? 'Runtime: file deleted' : 'Runtime'
}

/** Diff rendering only; runtime loading and concurrency state remain feature-local. */
export function RuntimeDiffPanel({ diff, loading, error, onClose }: Props) {
  if (!loading && !error && !diff) return null

  return (
    <section aria-labelledby="runtime-diff-heading" className={styles.diffPanel}>
      <div className={styles.sectionHeader}>
        <h2 id="runtime-diff-heading">Runtime file diff</h2>
        <button className={styles.cancelButton} disabled={loading} onClick={onClose} type="button">Close diff</button>
      </div>
      {loading && <p className={styles.loading} role="status">Loading diff…</p>}
      {error && <p className={styles.error} role="alert">Diff unavailable. {error}</p>}
      {diff && (
        <div className={styles.diffContent}>
          <p className={styles.sectionDescription}><code>{diff.path}</code> · {diff.changeType}</p>
          <ReactDiffViewer
            leftTitle={baselineTitle(diff)}
            newValue={diff.runtimeText ?? ''}
            oldValue={diff.baselineText ?? ''}
            rightTitle={runtimeTitle(diff)}
            splitView
            disableWorker
          />
        </div>
      )}
    </section>
  )
}
