import type { ReactNode } from 'react'

import styles from '../PluginsPage.module.css'

interface PluginsDialogProps {
  title: string
  children: ReactNode
  footer: ReactNode
  onClose?: () => void
}

/** Shared modal frame for Plugins workflows; workflow state remains outside it. */
export function PluginsDialog({ title, children, footer, onClose }: PluginsDialogProps) {
  const titleId = `plugins-dialog-${title.toLowerCase().replaceAll(/[^a-z0-9]+/g, '-')}`

  return (
    <div className={styles.modalVeil}>
      <section aria-labelledby={titleId} aria-modal="true" className={styles.modal} role="dialog">
        <header className={styles.modalHeader}>
          <h2 id={titleId}>{title}</h2>
          {onClose !== undefined && (
            <button aria-label="Close dialog" className={styles.tableCommand} onClick={onClose} type="button">
              ✕
            </button>
          )}
        </header>
        <div className={styles.modalBody}>{children}</div>
        <footer className={styles.modalFooter}>{footer}</footer>
      </section>
    </div>
  )
}
