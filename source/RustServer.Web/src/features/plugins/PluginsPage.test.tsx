import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import {
  loadingPluginsDashboard,
  unavailablePluginsDashboard,
} from './fixtures/plugins-dashboard-scenarios'
import { PluginsPage } from './PluginsPage'

describe('PluginsPage', () => {
  it('renders local loading and unavailable states without substituting successful data', () => {
    const { rerender } = render(<PluginsPage dashboard={loadingPluginsDashboard} />)

    expect(screen.getByText('Loading Git status…')).toBeVisible()
    expect(screen.getByText('Loading applied baseline…')).toBeVisible()
    expect(screen.getByText(/Release catalog: Loading releases/)).toBeVisible()

    rerender(<PluginsPage dashboard={unavailablePluginsDashboard} />)

    expect(screen.getByText(/Git status unavailable/)).toBeVisible()
    expect(screen.getByText(/Baseline unavailable/)).toBeVisible()
    expect(screen.getByText('Unknown / unavailable')).toBeVisible()
    expect(screen.getByText(/Release catalog: Releases unavailable/)).toBeVisible()
  })
})
