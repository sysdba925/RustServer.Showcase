import type { IncomingChangeResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'

interface Props {
  filename: string
  selectedChanges: IncomingChangeResponse[]
  message: string
  committing: boolean
  onMessageChange: (message: string) => void
  onCommit: () => void
}

export function CommitUpdateReview({ filename, selectedChanges, message, committing, onMessageChange, onCommit }: Props) {
  const byRepository = selectedChanges.reduce<Record<string, number>>((counts, change) => {
    counts[change.repository] = (counts[change.repository] ?? 0) + 1
    return counts
  }, {})

  return (
    <section aria-labelledby="commit-update-heading" className={styles.confirmation}>
      <h2 id="commit-update-heading">Commit UpdatePackage</h2>
      <p>Update package: {filename}</p>
      <dl className={styles.keyValueList}>
        <div><dt>Accepted files</dt><dd>{selectedChanges.length}</dd></div>
        {Object.entries(byRepository).map(([repository, count]) => <div key={repository}><dt>{repository}</dt><dd>{count}</dd></div>)}
      </dl>
      <label className={styles.confirmationField}>Commit message
        <input aria-label="Commit message" onChange={(event) => onMessageChange(event.target.value)} value={message} />
      </label>
      <div className={styles.actions}>
        <button disabled={committing || selectedChanges.length === 0 || message.trim().length === 0} onClick={onCommit} type="button">
          {committing ? 'Committing…' : 'Commit UpdatePackage'}
        </button>
      </div>
    </section>
  )
}
