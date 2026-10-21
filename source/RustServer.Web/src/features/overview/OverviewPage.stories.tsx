import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { ControlPlaneContext, type ControlPlaneContextValue } from '../control-plane/ControlPlaneContext'
import { createControlPlaneContextValue } from '../control-plane/fixtures/create-control-plane-context-value'
import {
  controlPlaneScenarios, startupInProgress,
} from '../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability } from '../control-plane/model'
import { OverviewPage } from './OverviewPage'

type Story = StoryObj<typeof OverviewPage>

function renderPage(value: ControlPlaneContextValue) {
  return (
    <MemoryRouter>
      <ControlPlaneContext.Provider value={value}>
        <OverviewPage />
      </ControlPlaneContext.Provider>
    </MemoryRouter>
  )
}

const meta = {
  title: 'Overview/Page',
  component: OverviewPage,
} satisfies Meta<typeof OverviewPage>

export default meta

export const Ready: Story = {
  render: () => renderPage(createControlPlaneContextValue()),
}

export const Maintenance: Story = {
  render: () => {
    const maintenance = controlPlaneScenarios.maintenance

    return renderPage(
      createControlPlaneContextValue({
        serverStatus: maintenance.serverStatus,
        rustVersions: maintenance.rustVersions,
        oxideVersions: maintenance.oxideVersions,
        operationDetail: maintenance.operationDetail,
        actionAvailability: deriveActionAvailability(maintenance),
      }),
    )
  },
}

export const Starting: Story = {
  render: () => {
    const starting = controlPlaneScenarios.starting
    return renderPage(createControlPlaneContextValue({
      serverStatus: starting.serverStatus,
      rustVersions: starting.rustVersions,
      oxideVersions: starting.oxideVersions,
      operationDetail: starting.operationDetail,
      startupSnapshot: { status: 'available', data: startupInProgress },
      actionAvailability: deriveActionAvailability(starting),
    }))
  },
}

export const PartialUnavailable: Story = {
  render: () => {
    const partial = controlPlaneScenarios['partial-unavailable']
    return renderPage(createControlPlaneContextValue({
      serverStatus: partial.serverStatus,
      rustVersions: partial.rustVersions,
      oxideVersions: partial.oxideVersions,
      operationDetail: partial.operationDetail,
      actionAvailability: deriveActionAvailability(partial),
    }))
  },
}
