import type { ComponentPropsWithoutRef } from 'react'

import styles from './Panel.module.css'

/** Shared framed surface for grouped application content. */
export function Panel({ className, ...props }: ComponentPropsWithoutRef<'section'>) {
  const panelClassName = className ? `${styles.panel} ${className}` : styles.panel

  return <section className={panelClassName} {...props} />
}
