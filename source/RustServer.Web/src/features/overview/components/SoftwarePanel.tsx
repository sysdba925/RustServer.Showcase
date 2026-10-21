import {
  deriveOxideRestartRequired,
  type ActionAvailability,
  type OxideVersionInfo,
  type ResourceState,
  type RustVersionInfo,
  type UpdateTarget,
} from '../../control-plane/model'
import styles from '../../control-plane/ControlPlane.module.css'
import serverStyles from '../../server/ServerPage.module.css'

interface SoftwarePanelProps {
  compact?: boolean
  actionAvailability: ActionAvailability
  actionsBlocked: boolean
  pendingUpdateTarget: UpdateTarget | null
  rustUpdateError: string | null
  oxideUpdateError: string | null
  rustUpdateUnavailableReason: string | null
  oxideUpdateUnavailableReason: string | null
  onRustUpdate: () => void
  onOxideUpdate: () => void
  rustVersions: ResourceState<RustVersionInfo>
  oxideVersions: ResourceState<OxideVersionInfo>
}

interface RustCardProps {
  compact?: boolean
  actionEnabled: boolean
  actionsBlocked: boolean
  pending: boolean
  updateError: string | null
  updateUnavailableReason: string | null
  onUpdate: () => void
  rustVersions: ResourceState<RustVersionInfo>
}

interface OxideCardProps {
  compact?: boolean
  actionEnabled: boolean
  actionsBlocked: boolean
  pending: boolean
  updateError: string | null
  updateUnavailableReason: string | null
  onUpdate: () => void
  oxideVersions: ResourceState<OxideVersionInfo>
}

function RustCard({
  compact = false,
  actionEnabled,
  actionsBlocked,
  pending,
  updateError,
  updateUnavailableReason,
  onUpdate,
  rustVersions,
}: RustCardProps) {
  return (
    <article aria-label="Rust version information" className={compact ? serverStyles.softwareItem : styles.softwareCard}>
      <div className={compact ? serverStyles.softwareHeading : styles.panelHeader}>
        <h3>Rust</h3>
        {rustVersions.status === 'available' && rustVersions.data.installedBuildId === null && (
          <span className={compact ? `${serverStyles.softwareBadge} ${serverStyles.softwareBad}` : `${styles.statusBadge} ${styles.statusUnavailable}`}>
            Not installed
          </span>
        )}
        {rustVersions.status === 'available' && rustVersions.data.installedBuildId !== null && rustVersions.data.updateAvailable && (
          <span className={compact ? serverStyles.softwareBadge : `${styles.statusBadge} ${styles.statusUpdate}`}>
            Update available
          </span>
        )}
        {compact && rustVersions.status === 'available' &&
          rustVersions.data.installedBuildId !== null &&
          !rustVersions.data.updateAvailable && (
            <span className={`${serverStyles.softwareBadge} ${serverStyles.softwareGood}`}>
              Up to date
            </span>
          )}
      </div>

      {rustVersions.status === 'loading' && (
        <p className={compact ? serverStyles.runtimeMuted : styles.loadingState} role="status">
          Loading Rust version information.
        </p>
      )}

      {rustVersions.status === 'unavailable' && (
        <>
          <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
            Rust version information unavailable.
          </p>
          {rustVersions.message && (
            <p className={compact ? serverStyles.runtimeMuted : styles.secondaryText}>{rustVersions.message}</p>
          )}
        </>
      )}

      {rustVersions.status === 'available' && (
        <>
          <dl className={compact ? serverStyles.versionRows : styles.keyValueList}>
            <div>
              <dt>Installed</dt>
              <dd>{rustVersions.data.installedBuildId ?? 'Not installed'}</dd>
            </div>
            <div>
              <dt>Available</dt>
              <dd>{rustVersions.data.availableBuildId}</dd>
            </div>
            <div>
              <dt>Branch</dt>
              <dd>{rustVersions.data.branch}</dd>
            </div>
            <div>
              <dt>Running</dt>
              <dd>{rustVersions.data.running == null ? 'Not running' : 'Running'}</dd>
            </div>
          </dl>

          {rustVersions.data.running != null && (
            compact ? (
              <details className={serverStyles.softwareDetails}>
                <summary>Running metadata</summary>
                <dl className={serverStyles.softwareMetadata}>
                  <div><dt>Protocol</dt><dd>{rustVersions.data.running.protocol}</dd></div>
                  <div><dt>Build date</dt><dd>{rustVersions.data.running.buildDate}</dd></div>
                  <div><dt>Unity</dt><dd>{rustVersions.data.running.unityVersion}</dd></div>
                  <div><dt>Changeset</dt><dd>{rustVersions.data.running.changeset}</dd></div>
                  <div><dt>Branch</dt><dd>{rustVersions.data.running.branch}</dd></div>
                </dl>
              </details>
            ) : (
              <div className={styles.metadata}>
                <h4>Running metadata</h4>
                <dl className={`${styles.keyValueList} ${styles.metadataList}`}>
                  <div><dt>Protocol</dt><dd>{rustVersions.data.running.protocol}</dd></div>
                  <div><dt>Build date</dt><dd>{rustVersions.data.running.buildDate}</dd></div>
                  <div><dt>Unity</dt><dd>{rustVersions.data.running.unityVersion}</dd></div>
                  <div><dt>Changeset</dt><dd>{rustVersions.data.running.changeset}</dd></div>
                  <div><dt>Branch</dt><dd>{rustVersions.data.running.branch}</dd></div>
                </dl>
              </div>
            )
          )}

          {rustVersions.data.updateAvailable && (
            <div className={compact ? serverStyles.softwareFooter : styles.softwareActions}>
              <button
                className={compact ? serverStyles.updateButton : undefined}
                disabled={actionsBlocked || !actionEnabled}
                onClick={onUpdate}
                type="button"
              >
                {pending
                  ? rustVersions.data.installedBuildId === null
                    ? 'Requesting Rust installation...'
                    : 'Requesting Rust update...'
                  : rustVersions.data.installedBuildId === null
                    ? 'Install Rust'
                    : `Update to ${rustVersions.data.availableBuildId}`}
              </button>
              {!actionsBlocked && !actionEnabled && updateUnavailableReason && (
                <p className={compact ? serverStyles.softwareHint : styles.secondaryText}>
                  {updateUnavailableReason}
                </p>
              )}
            </div>
          )}

          {updateError && (
            <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
              {updateError}
            </p>
          )}
        </>
      )}
    </article>
  )
}

function OxideCard({
  compact = false,
  actionEnabled,
  actionsBlocked,
  pending,
  updateError,
  updateUnavailableReason,
  onUpdate,
  oxideVersions,
}: OxideCardProps) {
  const restartRequired =
    oxideVersions.status === 'available' &&
    deriveOxideRestartRequired(oxideVersions.data)

  return (
    <article aria-label="Oxide version information" className={compact ? serverStyles.softwareItem : styles.softwareCard}>
      <div className={compact ? serverStyles.softwareHeading : styles.panelHeader}>
        <h3>Oxide</h3>
        {oxideVersions.status === 'available' && oxideVersions.data.installedVersion === null && (
          <span className={compact ? `${serverStyles.softwareBadge} ${serverStyles.softwareBad}` : `${styles.statusBadge} ${styles.statusUnavailable}`}>
            Not installed
          </span>
        )}
        {oxideVersions.status === 'available' && oxideVersions.data.installedVersion !== null && oxideVersions.data.updateAvailable && (
          <span className={compact ? serverStyles.softwareBadge : `${styles.statusBadge} ${styles.statusUpdate}`}>
            Update available
          </span>
        )}
        {compact && oxideVersions.status === 'available' &&
          oxideVersions.data.installedVersion !== null &&
          !oxideVersions.data.updateAvailable && (
            <span className={`${serverStyles.softwareBadge} ${serverStyles.softwareGood}`}>
              Up to date
            </span>
          )}
      </div>

      {restartRequired && (
        <p className={compact ? serverStyles.softwareBadge : `${styles.indicator} ${styles.restartIndicator}`}>
          Restart required
        </p>
      )}

      {oxideVersions.status === 'loading' && (
        <p className={compact ? serverStyles.runtimeMuted : styles.loadingState} role="status">
          Loading Oxide version information.
        </p>
      )}

      {oxideVersions.status === 'unavailable' && (
        <>
          <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
            Oxide version information unavailable.
          </p>
          {oxideVersions.message && (
            <p className={compact ? serverStyles.runtimeMuted : styles.secondaryText}>{oxideVersions.message}</p>
          )}
        </>
      )}

      {oxideVersions.status === 'available' && (
        <>
          <dl className={compact ? serverStyles.versionRows : styles.keyValueList}>
            <div>
              <dt>Installed</dt>
              <dd>{oxideVersions.data.installedVersion ?? 'Not installed'}</dd>
            </div>
            <div>
              <dt>Running</dt>
              <dd>{oxideVersions.data.runningVersion ?? 'Not running'}</dd>
            </div>
            <div>
              <dt>Available</dt>
              <dd>{oxideVersions.data.availableVersion}</dd>
            </div>
          </dl>

          {oxideVersions.data.updateAvailable && (
            <div className={compact ? serverStyles.softwareFooter : styles.softwareActions}>
              <button
                className={compact ? serverStyles.updateButton : undefined}
                disabled={actionsBlocked || !actionEnabled}
                onClick={onUpdate}
                type="button"
              >
                {pending
                  ? oxideVersions.data.installedVersion === null
                    ? 'Requesting Oxide installation...'
                    : 'Requesting Oxide update...'
                  : oxideVersions.data.installedVersion === null
                    ? 'Install Oxide'
                    : `Update to ${oxideVersions.data.availableVersion}`}
              </button>
              {!actionsBlocked && !actionEnabled && updateUnavailableReason && (
                <p className={compact ? serverStyles.softwareHint : styles.secondaryText}>
                  {updateUnavailableReason}
                </p>
              )}
            </div>
          )}

          {updateError && (
            <p className={compact ? serverStyles.runtimeWarning : styles.unavailableState} role="alert">
              {updateError}
            </p>
          )}
        </>
      )}
    </article>
  )
}

export function SoftwarePanel({
  compact = false,
  actionAvailability,
  actionsBlocked,
  pendingUpdateTarget,
  rustUpdateError,
  oxideUpdateError,
  rustUpdateUnavailableReason,
  oxideUpdateUnavailableReason,
  onRustUpdate,
  onOxideUpdate,
  rustVersions,
  oxideVersions,
}: SoftwarePanelProps) {
  return (
    <div className={compact ? serverStyles.softwareStack : styles.softwareGrid}>
      <RustCard
        compact={compact}
        actionEnabled={actionAvailability.rustUpdate}
        actionsBlocked={actionsBlocked}
        onUpdate={onRustUpdate}
        pending={pendingUpdateTarget === 'rust'}
        rustVersions={rustVersions}
        updateError={rustUpdateError}
        updateUnavailableReason={rustUpdateUnavailableReason}
      />
      <OxideCard
        compact={compact}
        actionEnabled={actionAvailability.oxideUpdate}
        actionsBlocked={actionsBlocked}
        onUpdate={onOxideUpdate}
        oxideVersions={oxideVersions}
        pending={pendingUpdateTarget === 'oxide'}
        updateError={oxideUpdateError}
        updateUnavailableReason={
          rustVersions.status === 'available' &&
          rustVersions.data.installedBuildId === null
            ? 'Install Rust before installing Oxide.'
            : oxideUpdateUnavailableReason
        }
      />
    </div>
  )
}
