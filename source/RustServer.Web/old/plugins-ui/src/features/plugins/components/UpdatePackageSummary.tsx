import type { UpdatePackageResponse } from '../../../shared/api'
import { formatTimestamp } from '../model'
import styles from '../Plugins.module.css'

const statusLabels: Record<UpdatePackageResponse['status'], string> = {
  received: 'Received',
  inspected: 'Inspected',
  failed: 'Inspection failed',
  committed: 'Committed',
}

export function UpdatePackageSummary({ updatePackage }: { updatePackage: UpdatePackageResponse }) {
  return (
    <section aria-labelledby="package-summary-heading" className={styles.section}>
      <h2 id="package-summary-heading">Update package</h2>
      <article className={styles.panel}>
        <dl className={styles.keyValueList}>
          <div><dt>Original filename</dt><dd>{updatePackage.originalFileName}</dd></div>
          <div><dt>Package ID</dt><dd>{updatePackage.id}</dd></div>
          <div><dt>Created</dt><dd>{formatTimestamp(updatePackage.createdAt)}</dd></div>
          <div><dt>Status</dt><dd>{statusLabels[updatePackage.status]}</dd></div>
        </dl>
        {updatePackage.files.length > 0 && (
          <div className={styles.inspectedFiles}>
            <h3>Inspected files</h3>
            <ul>{updatePackage.files.map((file) => <li key={file.path}><code>{file.path}</code> · {file.classification}</li>)}</ul>
          </div>
        )}
      </article>
    </section>
  )
}
