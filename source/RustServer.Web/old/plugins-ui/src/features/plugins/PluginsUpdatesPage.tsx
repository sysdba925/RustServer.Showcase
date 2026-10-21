import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'

import { uploadUpdatePackage } from '../../shared/api'
import styles from './Plugins.module.css'
import { getOperatorErrorMessage } from './model'

const uploadErrors = {
  forbidden: 'Upload Update is unavailable',
  invalid: 'The update package could not be uploaded',
}

export function PluginsUpdatesPage() {
  const navigate = useNavigate()
  const [file, setFile] = useState<File | null>(null)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const uploadInFlight = useRef(false)

  async function upload() {
    if (!file || uploadInFlight.current) return

    uploadInFlight.current = true
    setUploading(true)
    setError(null)
    try {
      const updatePackage = await uploadUpdatePackage(file)
      await navigate(`/plugins/updates/${encodeURIComponent(updatePackage.id)}`)
    } catch (uploadError) {
      setError(getOperatorErrorMessage(uploadError, uploadErrors))
    } finally {
      uploadInFlight.current = false
      setUploading(false)
    }
  }

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div><h1>Updates</h1><p>Upload one source file or package, inspect Incoming Changes, then explicitly commit selected Managed paths.</p></div>
        <Link className={styles.secondaryLink} to="/plugins">Back to Plugins</Link>
      </header>

      <section aria-labelledby="upload-update-heading" className={styles.section}>
        <h2 id="upload-update-heading">Upload Update</h2>
        <article className={styles.panel}>
          <p className={styles.sectionDescription}>Accepted input is enforced by the backend. This workflow is available only when the current instance safety context permits it.</p>
          <label className={styles.fileField}>Update source or package
            <input accept=".cs,.zip" aria-label="Update source or package" disabled={uploading} onChange={(event) => setFile(event.target.files?.[0] ?? null)} type="file" />
          </label>
          {file && <p className={styles.selectedFile}>Selected: <strong>{file.name}</strong></p>}
          {error && <p className={styles.error} role="alert">{error}</p>}
          <div className={styles.actions}>
            <button disabled={!file || uploading} onClick={() => void upload()} type="button">{uploading ? 'Uploading…' : 'Upload Update'}</button>
          </div>
        </article>
      </section>
    </div>
  )
}
