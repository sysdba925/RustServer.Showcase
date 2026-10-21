import type { RuntimeChangeResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'

interface Props {
  changes: RuntimeChangeResponse[]
  selectedPaths: ReadonlySet<string>
  onSelectionChange: (change: RuntimeChangeResponse, selected: boolean) => void
  onViewDiff: (change: RuntimeChangeResponse) => void
}

/** Presentation for the independent Staging runtime-change workflow. */
export function RuntimeChangesPanel({ changes, selectedPaths, onSelectionChange, onViewDiff }: Props) {
  return (
    <section aria-labelledby="runtime-changes-heading" className={styles.section}>
      <div className={styles.sectionHeader}>
        <div>
          <h2 id="runtime-changes-heading">Runtime Changes</h2>
          <p className={styles.sectionDescription}>Changes made on Staging since the exact deployed baseline.</p>
        </div>
      </div>
      <div className={styles.tableWrap}>
        <table>
          <thead><tr><th>Select</th><th>Path</th><th>Repository</th><th>Change</th><th>Text diff</th></tr></thead>
          <tbody>{changes.map((change) => (
            <tr key={change.path}>
              <td><input aria-label={`Select ${change.path}`} checked={selectedPaths.has(change.path)} onChange={(event) => onSelectionChange(change, event.target.checked)} type="checkbox" /></td>
              <td><code>{change.path}</code></td>
              <td>{change.repository}</td>
              <td>{change.changeType}</td>
              <td>{change.textDiffAvailable ? <button className={styles.textButton} onClick={() => onViewDiff(change)} type="button">View diff</button> : 'Text diff unavailable'}</td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    </section>
  )
}
