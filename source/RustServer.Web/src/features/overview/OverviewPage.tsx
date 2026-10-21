import { useControlPlane } from '../control-plane/ControlPlaneContext'
import { OverviewActivityPanel } from './components/OverviewActivityPanel'
import { OverviewSoftwarePanel } from './components/OverviewSoftwarePanel'
import { ServerSummaryPanel } from './components/ServerSummaryPanel'
import styles from './OverviewPage.module.css'

/** Overview V5 is a read-only summary. Mutations belong to Server → Runtime. */
export function OverviewPage() {
  const controlPlane = useControlPlane()

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <h1 id="overview-heading">Overview</h1>
      </header>

      <div className={styles.layout}>
        <section aria-labelledby="overview-server-heading" className={styles.section}>
          <h2 className={styles.sectionTitle} id="overview-server-heading">Server</h2>
          <ServerSummaryPanel
            operationDetail={controlPlane.operationDetail}
            serverStatus={controlPlane.serverStatus}
            startupSchema={controlPlane.startupSchema}
            startupSnapshot={controlPlane.startupSnapshot}
          />
        </section>

        <section aria-labelledby="overview-software-heading" className={styles.section}>
          <h2 className={styles.sectionTitle} id="overview-software-heading">Software</h2>
          <OverviewSoftwarePanel
            rustVersions={controlPlane.rustVersions}
            oxideVersions={controlPlane.oxideVersions}
          />
        </section>

        <section aria-labelledby="overview-activity-heading" className={styles.section}>
          <h2 className={styles.sectionTitle} id="overview-activity-heading">Activity</h2>
          <OverviewActivityPanel
            acceptedOperation={controlPlane.acceptedOperation}
            operationDetail={controlPlane.operationDetail}
          />
        </section>
      </div>
    </div>
  )
}
