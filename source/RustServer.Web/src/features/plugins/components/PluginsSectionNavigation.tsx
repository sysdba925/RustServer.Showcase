import { Link, useInRouterContext } from 'react-router-dom'

import styles from './PluginsSectionNavigation.module.css'

type PluginsSection = 'management' | 'catalog'

interface PluginsSectionNavigationProps {
  active: PluginsSection
}

const sections: ReadonlyArray<{ id: PluginsSection; label: string; to: string }> = [
  { id: 'management', label: 'Management', to: '/plugins' },
  { id: 'catalog', label: 'Catalog', to: '/plugins/catalog' },
]

function dashboardHref(to: string): string {
  return `/dashboard${to}`
}

/** Local navigation between Plugins-owned Management and Catalog surfaces. */
export function PluginsSectionNavigation({ active }: PluginsSectionNavigationProps) {
  const hasRouter = useInRouterContext()

  return (
    <nav aria-label="Plugins sections" className={styles.navigation}>
      {sections.map((section) => {
        const commonProps = {
          'aria-current': active === section.id ? 'page' as const : undefined,
          className: active === section.id ? `${styles.link} ${styles.linkActive}` : styles.link,
        }

        return hasRouter ? (
          <Link {...commonProps} key={section.id} to={section.to}>
            {section.label}
          </Link>
        ) : (
          <a {...commonProps} href={dashboardHref(section.to)} key={section.id}>
            {section.label}
          </a>
        )
      })}
    </nav>
  )
}
