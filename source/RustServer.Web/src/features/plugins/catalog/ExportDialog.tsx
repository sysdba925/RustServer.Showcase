import { useRef, useState } from 'react'
import { exportPluginCatalog } from '../../../shared/api/plugin-catalog-client'
import { HttpError } from '../../../shared/api/request-json'
import { Modal } from '../../catalog-prototype/Modal'
import styles from '../../catalog-prototype/prototype.module.css'

export function ExportDialog({ onClose }: { onClose: () => void }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState('')
  const pending = useRef(false)

  const download = async () => {
    if (pending.current) return
    pending.current = true; setBusy(true); setError(''); setResult('')
    try {
      const archive = await exportPluginCatalog()
      const url = URL.createObjectURL(archive)
      try {
        const link = document.createElement('a')
        link.href = url; link.download = 'plugin-catalog.zip'; link.click()
      } finally { URL.revokeObjectURL(url) }
      setResult('Archive prepared for download.')
    } catch (reason) {
      setError(reason instanceof HttpError && reason.status === 503
        ? 'Export unavailable. A catalog media original may be unavailable. Please try again.'
        : 'Could not export the catalog. Please try again.')
    } finally { pending.current = false; setBusy(false) }
  }

  return <Modal titleId="export-title" onClose={() => { if (!busy) onClose() }}><header className={styles.dialogHeader}><div><h2 id="export-title">Export Catalog (ZIP)</h2><p>Catalog backup · format version 2</p></div><button type="button" disabled={busy} onClick={onClose}>Close</button></header><div className={styles.importBody}>
    <p>Exports the entire catalog, regardless of filters or the current page.</p>
    <p>Local media originals are included. External media URLs are saved as links; thumbnails are not included.</p>
    <button type="button" disabled={busy} onClick={() => void download()}>{busy ? 'Preparing…' : 'Download ZIP'}</button>
    {busy && <p role="status">Preparing archive…</p>}{error && <p role="alert" className={styles.error}>{error}</p>}{result && <p role="status">{result}</p>}
  </div></Modal>
}
