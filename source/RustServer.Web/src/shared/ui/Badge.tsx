import type { ComponentPropsWithoutRef } from 'react'

import styles from './Badge.module.css'

type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger'

type BadgeProps = ComponentPropsWithoutRef<'span'> & {
  tone?: BadgeTone
}

/** Compact status label with presentation that is independent of feature state. */
export function Badge({ className, tone = 'neutral', ...props }: BadgeProps) {
  const badgeClassName = [styles.badge, styles[tone], className].filter(Boolean).join(' ')

  return <span className={badgeClassName} {...props} />
}
