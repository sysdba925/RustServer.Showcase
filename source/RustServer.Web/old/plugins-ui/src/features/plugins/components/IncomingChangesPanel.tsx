import type { IncomingChangeResponse } from '../../../shared/api'
import styles from '../Plugins.module.css'
import { isIncomingChangeCommitEligible } from '../model'

interface Props {
  changes: IncomingChangeResponse[]
  selectedPaths: ReadonlySet<string>
  onSelectionChange: (path: string, selected: boolean) => void
  onSelectAll: () => void
  onViewDiff: (change: IncomingChangeResponse) => void
}

export function IncomingChangesPanel({
  changes,
  selectedPaths,
  onSelectionChange,
  onSelectAll,
  onViewDiff,
}: Props) {
  const eligible = changes.filter(isIncomingChangeCommitEligible)
  const allEligibleSelected = eligible.length > 0 && eligible.every((change) => selectedPaths.has(change.path))

  return (
    <section aria-labelledby="incoming-changes-heading" className={styles.section}>
      <div className={styles.sectionHeader}>
        <div>
          <h2 id="incoming-changes-heading">Incoming Changes</h2>
          <p className={styles.sectionDescription}>Only changed Managed paths can be accepted into this update package commit.</p>
        </div>
        <button disabled={eligible.length === 0 || allEligibleSelected} onClick={onSelectAll} type="button">
          Select all changed managed files
        </button>
      </div>
      {changes.length === 0 ? <p className={styles.empty}>The package has no incoming changes.</p> : (
        <div className={styles.tableWrap}>
          <table>
            <thead><tr><th>Select</th><th>Path</th><th>Repository</th><th>Classification</th><th>Change</th><th>Text diff</th></tr></thead>
            <tbody>{changes.map((change) => {
              const eligibleChange = isIncomingChangeCommitEligible(change)
              return <tr key={change.path}>
                <td>
                  <input
                    aria-label={`Accept ${change.path}`}
                    checked={selectedPaths.has(change.path)}
                    disabled={!eligibleChange}
                    onChange={(event) => onSelectionChange(change.path, event.target.checked)}
                    type="checkbox"
                  />
                </td>
                <td><code>{change.path}</code></td>
                <td>{change.repository}</td>
                <td>{change.classification}</td>
                <td>{change.changeType}</td>
                <td>{change.textDiffAvailable ? <button className={styles.textButton} onClick={() => onViewDiff(change)} type="button">View diff</button> : 'Text diff unavailable'}</td>
              </tr>
            })}</tbody>
          </table>
        </div>
      )}
    </section>
  )
}
