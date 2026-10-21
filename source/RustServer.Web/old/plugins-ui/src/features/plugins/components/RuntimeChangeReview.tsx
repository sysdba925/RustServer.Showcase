import type { RuntimeChangeResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'

interface ImportProps {
  selectedChanges: RuntimeChangeResponse[]
  message: string
  importing: boolean
  onMessageChange: (message: string) => void
  onConfirm: () => void
  onCancel: () => void
}

interface DiscardProps {
  selectedChanges: RuntimeChangeResponse[]
  discarding: boolean
  onConfirm: () => void
  onCancel: () => void
}

function RepositoryCounts({ changes }: { changes: RuntimeChangeResponse[] }) {
  const counts = changes.reduce<Record<string, number>>((current, change) => {
    current[change.repository] = (current[change.repository] ?? 0) + 1
    return current
  }, {})
  return <>{Object.entries(counts).map(([repository, count]) => <div key={repository}><dt>{repository === 'plugins' ? 'PluginFiles' : 'ConfigFiles'}</dt><dd>{count}</dd></div>)}</>
}

function SelectedPaths({ changes }: { changes: RuntimeChangeResponse[] }) {
  return <details className={styles.selectedPaths}><summary>Selected paths ({changes.length})</summary><ul>{changes.map((change) => <li key={change.path}><code>{change.path}</code></li>)}</ul></details>
}

export function RuntimeImportReview({ selectedChanges, message, importing, onMessageChange, onConfirm, onCancel }: ImportProps) {
  return (
    <section aria-labelledby="runtime-import-heading" className={styles.confirmation}>
      <h2 id="runtime-import-heading">Import Runtime Changes into Development</h2>
      <dl className={styles.keyValueList}><div><dt>Selected paths</dt><dd>{selectedChanges.length}</dd></div><RepositoryCounts changes={selectedChanges} /></dl>
      <label className={styles.confirmationField}>Commit message<input aria-label="Runtime import commit message" onChange={(event) => onMessageChange(event.target.value)} value={message} /></label>
      <SelectedPaths changes={selectedChanges} />
      <p className={styles.sectionDescription}>This commits selected runtime state into Development. It does not modify the current Rust target or deployed baseline.</p>
      <div className={styles.actions}><button disabled={importing || selectedChanges.length === 0 || message.trim().length === 0} onClick={onConfirm} type="button">{importing ? 'Importing…' : 'Import Runtime Changes'}</button><button className={styles.cancelButton} disabled={importing} onClick={onCancel} type="button">Cancel</button></div>
    </section>
  )
}

export function RuntimeDiscardReview({ selectedChanges, discarding, onConfirm, onCancel }: DiscardProps) {
  return (
    <section aria-labelledby="runtime-discard-heading" className={`${styles.confirmation} ${styles.dangerConfirmation}`}>
      <h2 id="runtime-discard-heading">Discard selected runtime changes?</h2>
      <p>{selectedChanges.length} selected paths will be restored to the exact deployed baseline.</p>
      <SelectedPaths changes={selectedChanges} />
      <p className={styles.sectionDescription}>This changes selected paths on the current Rust target. It does not change Development Git, Release metadata, or the deployment manifest.</p>
      <div className={styles.actions}><button className={styles.dangerButton} disabled={discarding || selectedChanges.length === 0} onClick={onConfirm} type="button">{discarding ? 'Discarding…' : 'Discard Runtime Changes'}</button><button className={styles.cancelButton} disabled={discarding} onClick={onCancel} type="button">Cancel</button></div>
    </section>
  )
}
