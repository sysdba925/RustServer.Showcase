import {
  deriveOxideRestartRequired,
  type OxideVersionInfo,
  type ResourceState,
  type RustVersionInfo,
} from '../../control-plane/model'
import styles from '../OverviewPage.module.css'

interface OverviewSoftwarePanelProps {
  rustVersions: ResourceState<RustVersionInfo>
  oxideVersions: ResourceState<OxideVersionInfo>
}

function VersionRow({ label, value }: { label: string; value: string | number }) {
  return <div className={styles.versionRow}><dt>{label}</dt><dd>{value}</dd></div>
}

function SoftwareStatus({
  state, installed, updateAvailable,
}: {
  state: ResourceState<unknown>['status']
  installed?: boolean
  updateAvailable?: boolean
}) {
  const tone = state === 'loading' ? 'info'
    : state === 'unavailable' ? 'error'
      : installed === false ? 'off' : updateAvailable ? 'warn' : 'ok'
  const label = state === 'loading' ? 'Loading'
    : state === 'unavailable' ? 'Unavailable'
      : installed === false ? 'Not installed' : updateAvailable ? 'Update available' : 'Up to date'
  return <span className={styles.status} data-tone={tone}>{label}</span>
}

function RustVersionCard({ versions }: { versions: ResourceState<RustVersionInfo> }) {
  return (
    <article aria-label="Rust version information" className={styles.card}>
      <header className={styles.cardHeader}>
        <h3>Rust</h3>
        <SoftwareStatus
          state={versions.status}
          installed={versions.status === 'available' ? versions.data.installedBuildId !== null : undefined}
          updateAvailable={versions.status === 'available' ? versions.data.updateAvailable : undefined}
        />
      </header>
      <div className={styles.cardBody}>
        {versions.status === 'loading' && (
          <p className={styles.loading} role="status">Loading Rust version information.</p>
        )}
        {versions.status === 'unavailable' && (
          <>
            <p className={styles.alert} role="alert">Rust version information unavailable.</p>
            {versions.message && <p className={styles.subtle}>{versions.message}</p>}
          </>
        )}
        {versions.status === 'available' && (
          <>
            <dl className={styles.versionRows}>
              <VersionRow label="Installed" value={versions.data.installedBuildId ?? 'Not installed'} />
              <VersionRow label="Available" value={versions.data.availableBuildId} />
              <VersionRow label="Branch" value={versions.data.branch} />
              <VersionRow label="Running" value={versions.data.running == null ? 'Not running' : 'Running'} />
            </dl>
            {versions.data.running != null && (
              <details className={styles.metadata}>
                <summary>Running metadata</summary>
                <dl className={styles.metadataRows}>
                  <VersionRow label="Protocol" value={versions.data.running.protocol} />
                  <VersionRow label="Build date" value={versions.data.running.buildDate} />
                  <VersionRow label="Unity" value={versions.data.running.unityVersion} />
                  <VersionRow label="Changeset" value={versions.data.running.changeset} />
                  <VersionRow label="Branch" value={versions.data.running.branch} />
                </dl>
              </details>
            )}
          </>
        )}
      </div>
    </article>
  )
}

function OxideVersionCard({ versions }: { versions: ResourceState<OxideVersionInfo> }) {
  const restartRequired = versions.status === 'available' && deriveOxideRestartRequired(versions.data)
  return (
    <article aria-label="Oxide version information" className={styles.card}>
      <header className={styles.cardHeader}>
        <h3>Oxide</h3>
        <SoftwareStatus
          state={versions.status}
          installed={versions.status === 'available' ? versions.data.installedVersion !== null : undefined}
          updateAvailable={versions.status === 'available' ? versions.data.updateAvailable : undefined}
        />
      </header>
      <div className={styles.cardBody}>
        {versions.status === 'loading' && (
          <p className={styles.loading} role="status">Loading Oxide version information.</p>
        )}
        {versions.status === 'unavailable' && (
          <>
            <p className={styles.alert} role="alert">Oxide version information unavailable.</p>
            {versions.message && <p className={styles.subtle}>{versions.message}</p>}
          </>
        )}
        {versions.status === 'available' && (
          <>
            <dl className={styles.versionRows}>
              <VersionRow label="Installed" value={versions.data.installedVersion ?? 'Not installed'} />
              <VersionRow label="Running" value={versions.data.runningVersion ?? 'Not running'} />
              <VersionRow label="Available" value={versions.data.availableVersion} />
            </dl>
            {restartRequired && (
              <p className={styles.restartRequired}>
                <span className={styles.status} data-tone="warn">Restart required</span>
              </p>
            )}
          </>
        )}
      </div>
    </article>
  )
}

/** Overview v5: read-only versions; installation and updates live on Server. */
export function OverviewSoftwarePanel({
  rustVersions, oxideVersions,
}: OverviewSoftwarePanelProps) {
  return (
    <div className={styles.softwareGrid}>
      <RustVersionCard versions={rustVersions} />
      <OxideVersionCard versions={oxideVersions} />
    </div>
  )
}
