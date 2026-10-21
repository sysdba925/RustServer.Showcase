import { useState, type PropsWithChildren } from 'react'
import { NavLink, Outlet } from 'react-router-dom'

import { ControlPlaneProvider } from '../../features/control-plane/ControlPlaneProvider'
import styles from './AppLayout.module.css'

const navigationItems = [
  {
    icon: '▦',
    label: 'Overview',
    to: '/',
  },
  {
    icon: '▣',
    label: 'Server',
    to: '/server',
  },
  {
    icon: '◈',
    label: 'World',
    to: '/world',
  },
  {
    icon: '⬡',
    label: 'Plugins',
    to: '/plugins',
  },
] as const

/** Visual application shell, kept separate from route and data composition. */
export function AppLayoutFrame({ children }: PropsWithChildren) {
  const [logoUnavailable, setLogoUnavailable] = useState(false)

  return (
    <div className={styles.application}>
      <header className={styles.header}>
        <div className={styles.logoSlot}>
          <span className={styles.logoTile}>
            {logoUnavailable ? (
              <span aria-label="Rust game logo unavailable" role="img">R</span>
            ) : (
              <img
                alt="Rust game logo"
                className={styles.logoImage}
                height={34}
                onError={() => setLogoUnavailable(true)}
                src="https://rust.facepunch.com/favicon.ico"
                width={34}
              />
            )}
          </span>
        </div>
        <p className={styles.productIdentity}>RUSTSERVER</p>
        <span className={styles.productContext}>/ Control Panel</span>
      </header>

      <div className={styles.body}>
        <aside className={styles.sidebar}>
          <nav aria-label="Primary navigation" className={styles.navigation}>
            <p className={styles.navigationLabel}>Platform</p>
            {navigationItems.map((item) => (
              <NavLink
                className={({ isActive }) =>
                  isActive
                    ? `${styles.navigationLink} ${styles.navigationLinkActive}`
                    : styles.navigationLink
                }
                end={item.to !== '/plugins'}
                key={item.to}
                to={item.to}
              >
                <span aria-hidden="true" className={styles.navigationIcon}>
                  {item.icon}
                </span>
                {item.label}
              </NavLink>
            ))}
          </nav>
        </aside>

        <main className={styles.mainContent}>
          {children}
        </main>
      </div>
    </div>
  )
}

export function AppLayout() {
  return (
    <AppLayoutFrame>
      <ControlPlaneProvider>
        <Outlet />
      </ControlPlaneProvider>
    </AppLayoutFrame>
  )
}
