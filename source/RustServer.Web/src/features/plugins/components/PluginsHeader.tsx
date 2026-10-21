import { Badge } from '../../../shared/ui/Badge'
import type { PluginsDashboardState } from '../model/plugins-dashboard'
import { PluginsSectionNavigation } from './PluginsSectionNavigation'
import styles from '../PluginsPage.module.css'

function environmentBadge(
  environment: PluginsDashboardState['environment'],
): { label: string; tone: 'success' | 'danger' | 'warning' } {
  if (environment.status === 'loading') {
    return { label: 'Environment loading', tone: 'warning' }
  }

  if (environment.status === 'unavailable') {
    return { label: 'Environment unavailable', tone: 'danger' }
  }

  return environment.data.environment === 'production'
    ? { label: 'Production', tone: 'danger' }
    : { label: environment.data.environment, tone: 'success' }
}

function appliedSource(baseline: PluginsDashboardState['baseline']): string {
  if (baseline.status !== 'available') {
    return baseline.status === 'loading' ? 'Source loading' : 'Source unavailable'
  }

  if (baseline.data.status === 'notInitialized' || baseline.data.baseline === null) {
    return 'Not initialized'
  }

  const { source, release } = baseline.data.baseline
  if (source === 'release' && release !== null) {
    return `Release · release/${release}`
  }

  return source === 'development' ? 'Development · develop' : source
}

interface PluginsHeaderProps {
  dashboard: PluginsDashboardState
  actionsBlocked: boolean
  onChangeEnvironment: () => void
  onSwitchSource: () => void
  onApplySource: () => void
  onOpenHistory: () => void
}

/** V15 header and source/environment command placement for the Plugins surface. */
export function PluginsHeader({
  dashboard,
  actionsBlocked,
  onChangeEnvironment,
  onSwitchSource,
  onApplySource,
  onOpenHistory,
}: PluginsHeaderProps) {
  const environment = environmentBadge(dashboard.environment)
  const hasEnvironment = dashboard.environment.status === 'available'

  return (
    <>
      <header className={styles.pageHeader}>
        <div className={styles.identity}>
          <h1>Plugins</h1>
          <Badge tone={environment.tone}>{environment.label}</Badge>
          <Badge className={styles.sourceBadge}>{appliedSource(dashboard.baseline)}</Badge>
        </div>
        <button className={styles.commandButton} disabled={actionsBlocked} onClick={onOpenHistory} type="button">
          ◷ History
        </button>
      </header>

      <PluginsSectionNavigation active="management" />

      <div aria-label="Plugins commands" className={styles.commandBar}>
        <button className={styles.commandButton} disabled={!hasEnvironment || actionsBlocked} onClick={onChangeEnvironment} type="button">
          ⚙ Environment
        </button>
        <button className={styles.commandButton} disabled={!hasEnvironment || actionsBlocked} onClick={onSwitchSource} type="button">
          ⇄ Switch Source
        </button>
        <button className={`${styles.commandButton} ${styles.primaryCommand}`} disabled={!hasEnvironment || actionsBlocked} onClick={onApplySource} type="button">
          ⇧ Apply Source
        </button>
      </div>
    </>
  )
}
