import { useControlPlane } from '../control-plane/ControlPlaneContext'
import { NextWorldConfigurationPanel } from './components/NextWorldConfigurationPanel'
import { SnapshotsPanel } from './components/SnapshotsPanel'
import { WipePanel } from './components/WipePanel'
import styles from './WorldPage.module.css'

/** World V1 layout; all actions remain backed by the existing API clients. */
export function WorldPage() {
  const controlPlane = useControlPlane()
  // Treat the Control status as a read-only UX preflight. The backend
  // remains authoritative for the stopped-server wipe invariant.
  const serverRunning = controlPlane.serverStatus.status === 'available' &&
    controlPlane.serverStatus.data.isRunning
  const statusUnknown = controlPlane.serverStatus.status !== 'available'
  const wipeUnavailableReason = controlPlane.wipeUnavailableReason ??
    (serverRunning ? 'Stop the Rust server before wiping.'
      : statusUnknown ? 'Server status must be available before wiping.' : null)
  const wipeBlocked = controlPlane.mutationsBlocked ||
    wipeUnavailableReason !== null ||
    controlPlane.pendingWipe

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <h1>World</h1>
      </header>

      <div className={styles.worldGrid}>
        <div className={styles.worldStack}>
          <section aria-labelledby="next-world-heading" className={styles.card}>
            <div className={styles.cardHeader}>
              <h2 id="next-world-heading">Next World</h2>
              <span className={styles.chip}>NEXT START</span>
            </div>
            <div className={styles.cardBody}>
              <NextWorldConfigurationPanel
                compact
                configuration={controlPlane.configuration}
                mutationsBlocked={controlPlane.mutationsBlocked}
                onSave={controlPlane.saveConfiguration}
                pendingSave={controlPlane.pendingConfigurationSave}
                saveError={controlPlane.configurationSaveError}
                saveSucceeded={controlPlane.configurationSaveSucceeded}
              />
            </div>
          </section>

          <section aria-labelledby="wipe-heading" className={styles.card}>
            <div className={styles.cardHeader}>
              <h2 id="wipe-heading">Wipe</h2>
              <span className={styles.status} data-tone={wipeBlocked ? 'off' : 'ok'}>
                {serverRunning ? 'Stopped server required' : wipeBlocked ? 'Unavailable' : 'Available'}
              </span>
            </div>
            <div className={styles.cardBody}>
              <WipePanel
                compact
                mutationsBlocked={controlPlane.mutationsBlocked}
                onWipe={controlPlane.requestWipe}
                operationDetail={controlPlane.operationDetail}
                pendingWipe={controlPlane.pendingWipe}
                unavailableReason={wipeUnavailableReason}
                wipeError={controlPlane.wipeError}
              />
            </div>
          </section>
        </div>

        <div className={styles.snapshotColumn}>
          <SnapshotsPanel compact serverStatus={controlPlane.serverStatus} />
        </div>
      </div>
    </div>
  )
}
