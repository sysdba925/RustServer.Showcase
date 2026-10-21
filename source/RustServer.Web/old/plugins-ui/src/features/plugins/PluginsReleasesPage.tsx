import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { getRelease, type ReleaseResponse } from '../../shared/api'
import styles from './Plugins.module.css'
import { formatTimestamp, getOperatorErrorMessage, shortSha } from './model'
import { useReleases } from './usePluginsResources'

function ReleaseDetails({ release }: { release: ReleaseResponse }) {
  return (
    <aside aria-label={`Release ${release.releaseId} details`} className={styles.details}>
      <h3>Release {release.releaseId}</h3>
      <dl className={styles.keyValueList}>
        <div><dt>Created</dt><dd>{formatTimestamp(release.createdAt)}</dd></div>
        <div><dt>Plugins SHA</dt><dd title={release.pluginsSha}>{release.pluginsSha}</dd></div>
        <div><dt>Config SHA</dt><dd title={release.oxideConfigsSha}>{release.oxideConfigsSha}</dd></div>
      </dl>
    </aside>
  )
}

export function PluginsReleasesPage() {
  const { releases, create, creating, createError, createdRelease } = useReleases()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selected, setSelected] = useState<ReleaseResponse | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    if (selectedId === null) return

    const controller = new AbortController()
    // The catalog provides a useful immediate selection, while this read keeps
    // the detail panel tied to the published item resource.
    void getRelease(selectedId, controller.signal)
      .then((release) => {
        if (!controller.signal.aborted) setSelected(release)
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setDetailError(getOperatorErrorMessage(error))
      })
      .finally(() => {
        if (!controller.signal.aborted) setDetailLoading(false)
      })

    return () => controller.abort()
  }, [selectedId])

  function inspectRelease(release: ReleaseResponse) {
    setSelected(release)
    setSelectedId(release.releaseId)
    setDetailLoading(true)
    setDetailError(null)
  }

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <div>
          <h1>Releases</h1>
          <p>Immutable snapshots of the exact plugins and configuration Git commits.</p>
        </div>
        <Link className={styles.secondaryLink} to="/plugins/deploy">Deploy a release</Link>
      </header>

      <section aria-labelledby="release-catalog-heading" className={styles.section}>
        <div className={styles.sectionHeader}>
          <h2 id="release-catalog-heading">Release catalog</h2>
          <button disabled={creating} onClick={() => void create()} type="button">
            {creating ? 'Creating release…' : 'Create release'}
          </button>
        </div>
        <p className={styles.sectionDescription}>Creating a release does not deploy or switch a source.</p>

        {createError && <p className={styles.error} role="alert">{createError}</p>}
        {createdRelease && <p className={styles.success} role="status">Release {createdRelease.releaseId} created.</p>}

        {releases.status === 'loading' && <p className={styles.loading} role="status">Loading release catalog…</p>}
        {releases.status === 'unavailable' && <p className={styles.error} role="alert">Release catalog unavailable. {releases.message}</p>}
        {releases.status === 'available' && releases.data.length === 0 && <p className={styles.empty}>No releases have been created.</p>}
        {releases.status === 'available' && releases.data.length > 0 && (
          <div className={styles.catalogLayout}>
            <div className={styles.tableWrap}>
              <table>
                <thead><tr><th>Release ID</th><th>Created</th><th>Plugins SHA</th><th>Config SHA</th><th><span className={styles.srOnly}>Details</span></th></tr></thead>
                <tbody>
                  {releases.data.map((release) => (
                    <tr key={release.releaseId}>
                      <td>{release.releaseId}</td>
                      <td>{formatTimestamp(release.createdAt)}</td>
                      <td title={release.pluginsSha}><code>{shortSha(release.pluginsSha)}</code></td>
                      <td title={release.oxideConfigsSha}><code>{shortSha(release.oxideConfigsSha)}</code></td>
                      <td><button className={styles.textButton} onClick={() => inspectRelease(release)} type="button">Inspect</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {selected && <ReleaseDetails release={selected} />}
            {detailLoading && <p className={styles.loading} role="status">Loading release details…</p>}
            {detailError && <p className={styles.error} role="alert">Release details unavailable. {detailError}</p>}
          </div>
        )}
      </section>
    </div>
  )
}
