import { useLayoutEffect, useRef, type PropsWithChildren } from 'react'
import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { ControlPlaneContext } from '../../features/control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../../features/control-plane/fixtures/create-control-plane-context-value'
import { OverviewPage } from '../../features/overview/OverviewPage'
import { PluginsPage } from '../../features/plugins/PluginsPage'
import { stagingPluginsDashboard } from '../../features/plugins/fixtures/plugins-dashboard-scenarios'
import { ServerPage } from '../../features/server/ServerPage'
import { WorldPage } from '../../features/world/WorldPage'
import { AppLayoutFrame } from './AppLayout'

type Story = StoryObj<typeof AppLayoutFrame>
type Page = typeof OverviewPage

const controlPlane = createControlPlaneContextValue()

/** Prevents the World preview's independently loaded snapshot list from using a live service. */
function SnapshotListFixture({ children }: PropsWithChildren) {
  const originalFetchRef = useRef<typeof fetch | null>(null)

  useLayoutEffect(() => {
    const originalFetch = globalThis.fetch
    const snapshotFetch: typeof fetch = async (input, init) => {
      const url =
        typeof input === 'string'
          ? input
          : input instanceof URL
            ? input.toString()
            : input.url

      if (url === '/snapshots-api/api/snapshots' && (init?.method ?? 'GET') === 'GET') {
        return new Response(JSON.stringify([]), {
          headers: { 'Content-Type': 'application/json' },
          status: 200,
        })
      }

      return originalFetch(input, init)
    }

    originalFetchRef.current = originalFetch
    globalThis.fetch = snapshotFetch

    return () => {
      if (globalThis.fetch === snapshotFetch && originalFetchRef.current !== null) {
        globalThis.fetch = originalFetchRef.current
      }
    }
  }, [])

  return children
}

function renderFullScreenPage(path: string, Page: Page) {
  return (
    <MemoryRouter initialEntries={[path]}>
      <ControlPlaneContext.Provider value={controlPlane}>
        <AppLayoutFrame>
          <Page />
        </AppLayoutFrame>
      </ControlPlaneContext.Provider>
    </MemoryRouter>
  )
}

const meta = {
  title: 'Pages / Full Screen',
  component: AppLayoutFrame,
  parameters: {
    layout: 'fullscreen',
  },
} satisfies Meta<typeof AppLayoutFrame>

export default meta

export const Overview: Story = {
  render: () => renderFullScreenPage('/', OverviewPage),
}

export const Server: Story = {
  render: () => renderFullScreenPage('/server', ServerPage),
}

export const World: Story = {
  render: () => (
    <SnapshotListFixture>
      {renderFullScreenPage('/world', WorldPage)}
    </SnapshotListFixture>
  ),
}

export const Plugins: Story = {
  render: () => (
    <MemoryRouter initialEntries={['/plugins']}>
      <ControlPlaneContext.Provider value={controlPlane}>
        <AppLayoutFrame>
          <PluginsPage dashboard={stagingPluginsDashboard} />
        </AppLayoutFrame>
      </ControlPlaneContext.Provider>
    </MemoryRouter>
  ),
}
