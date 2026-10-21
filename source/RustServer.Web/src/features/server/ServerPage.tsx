import { useControlPlane } from '../control-plane/ControlPlaneContext'
import { ActivityPanel } from '../overview/components/ActivityPanel'
import { SoftwarePanel } from '../overview/components/SoftwarePanel'
import { ServerConfigurationPanel } from './components/ServerConfigurationPanel'
import { ServerLifecycleBar } from './components/ServerLifecycleBar'
import { ServerRuntimePanel } from './components/ServerRuntimePanel'
import { StartupTracking } from './components/StartupTracking'
import styles from './ServerPage.module.css'

/**
 * Server v7 layout, using the existing authoritative ControlPlane resources.
 * Plugins v15 is the visual reference for the connected command bar and panels.
 */
export function ServerPage() {
  const controlPlane = useControlPlane()

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <h1>Server</h1>
      </header>

      <ServerLifecycleBar
        actionAvailability={controlPlane.actionAvailability}
        mutationsBlocked={controlPlane.mutationsBlocked}
        onRestart={controlPlane.restart}
        onStart={controlPlane.start}
        onStop={controlPlane.stop}
        pendingLifecycleAction={controlPlane.pendingLifecycleAction}
        serverStatus={controlPlane.serverStatus}
        startupSnapshot={controlPlane.startupSnapshot}
      />

      <div className={styles.panelGrid}>
        <section aria-labelledby="runtime-heading" className={styles.panel}>
          <header className={styles.panelHeader}>
            <h2 id="runtime-heading">Runtime</h2>
          </header>
          <div className={styles.panelBody}>
            <ServerRuntimePanel
              compact
              actionAvailability={controlPlane.actionAvailability}
              lifecycleError={controlPlane.lifecycleError}
              mutationsBlocked={controlPlane.mutationsBlocked}
              onRestart={controlPlane.restart}
              onStart={controlPlane.start}
              onStop={controlPlane.stop}
              operationDetail={controlPlane.operationDetail}
              pendingLifecycleAction={controlPlane.pendingLifecycleAction}
              serverStatus={controlPlane.serverStatus}
              startupSnapshot={controlPlane.startupSnapshot}
              rustVersions={controlPlane.rustVersions}
            />

            <SoftwarePanel
              compact
              actionAvailability={controlPlane.actionAvailability}
              actionsBlocked={controlPlane.mutationsBlocked}
              onOxideUpdate={controlPlane.requestOxideUpdate}
              onRustUpdate={controlPlane.requestRustUpdate}
              oxideUpdateError={controlPlane.oxideUpdateError}
              oxideUpdateUnavailableReason={controlPlane.updateUnavailableReason}
              oxideVersions={controlPlane.oxideVersions}
              pendingUpdateTarget={controlPlane.pendingUpdateTarget}
              rustUpdateError={controlPlane.rustUpdateError}
              rustUpdateUnavailableReason={controlPlane.updateUnavailableReason}
              rustVersions={controlPlane.rustVersions}
            />
          </div>
        </section>

        <section aria-labelledby="startup-heading" className={styles.panel}>
          <header className={styles.panelHeader}>
            <h2 id="startup-heading">Startup Tracking</h2>
            {controlPlane.startupSchema.status === 'available' &&
              controlPlane.startupSnapshot.status === 'available' &&
              controlPlane.startupSnapshot.data.sessionId !== null && (
                <span className={styles.panelMeta}>
                  {controlPlane.startupSnapshot.data.milestones.filter(
                    (state) => state !== 'pending',
                  ).length}
                  {' / '}
                  {controlPlane.startupSchema.data.milestones.length}
                </span>
              )}
          </header>
          <div className={styles.panelBody}>
            <StartupTracking
              compact
              startupSchema={controlPlane.startupSchema}
              startupSnapshot={controlPlane.startupSnapshot}
            />
          </div>
        </section>
      </div>

      {controlPlane.acceptedOperation !== null && (
        <section aria-labelledby="activity-heading" className={styles.activity}>
          <header className={styles.panelHeader}>
            <h2 id="activity-heading">Operation Activity</h2>
          </header>
          <div className={styles.panelBody}>
            <ActivityPanel
              acceptedOperation={controlPlane.acceptedOperation}
              operationDetail={controlPlane.operationDetail}
            />
          </div>
        </section>
      )}

      <section aria-labelledby="configuration-heading" className={styles.configurationPanel}>
        <header className={styles.panelHeader}>
          <h2 id="configuration-heading">Server Configuration</h2>
          <span className={styles.panelMeta}>NEXT START / RESTART</span>
        </header>
        <div className={styles.panelBody}>
          <ServerConfigurationPanel
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
    </div>
  )
}
