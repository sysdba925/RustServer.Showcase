import ReactDiffViewer from 'react-diff-viewer-continued'

import type { IncomingChangeDiffResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'

interface Props {
  diff: IncomingChangeDiffResponse | null
  loading: boolean
  error: string | null
  onClose: () => void
}

export function IncomingDiffPanel({ diff, loading, error, onClose }: Props) {
  if (!loading && !error && !diff) return null

  return (
    <section aria-labelledby="incoming-diff-heading" className={styles.diffPanel}>
      <div className={styles.sectionHeader}>
        <h2 id="incoming-diff-heading">Incoming file diff</h2>
        <button className={styles.cancelButton} disabled={loading} onClick={onClose} type="button">Close diff</button>
      </div>
      {loading && <p className={styles.loading} role="status">Loading diff…</p>}
      {error && <p className={styles.error} role="alert">Diff unavailable. {error}</p>}
      {diff && (
        <div className={styles.diffContent}>
          <p className={styles.sectionDescription}><code>{diff.path}</code> · {diff.changeType}</p>
          <ReactDiffViewer
            leftTitle={diff.changeType === 'added' ? 'No existing file' : 'Development workspace'}
            newValue={diff.incomingText}
            oldValue={diff.workspaceText ?? ''}
            rightTitle="Incoming package"
            splitView
            disableWorker
          />
        </div>
      )}
    </section>
  )
}
