import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  controlPlaneScenarios,
  serverStatusUnavailable,
  startupInProgress,
  startupSchema,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { ServerSummaryPanel } from './ServerSummaryPanel'

type Story = StoryObj<typeof ServerSummaryPanel>

const meta = {
  title: 'Overview/Server Summary',
  component: ServerSummaryPanel,
  decorators: [
    (Story) => (
      <MemoryRouter>
        <Story />
      </MemoryRouter>
    ),
  ],
  args: {
    serverStatus: controlPlaneScenarios.ready.serverStatus,
    operationDetail: controlPlaneScenarios.ready.operationDetail,
    startupSchema: { status: 'available', data: startupSchema },
    startupSnapshot: { status: 'available', data: startupInProgress },
  },
} satisfies Meta<typeof ServerSummaryPanel>

export default meta

export const Ready: Story = {}

export const Starting: Story = {
  args: {
    serverStatus: controlPlaneScenarios.starting.serverStatus,
    operationDetail: controlPlaneScenarios.starting.operationDetail,
  },
}

export const Maintenance: Story = {
  args: {
    serverStatus: controlPlaneScenarios.maintenance.serverStatus,
    operationDetail: controlPlaneScenarios.maintenance.operationDetail,
  },
}

export const Loading: Story = {
  args: {
    serverStatus: { status: 'loading' },
  },
}

export const Unavailable: Story = {
  args: {
    serverStatus: serverStatusUnavailable,
  },
}

export const Inconsistent: Story = {
  args: {
    serverStatus: controlPlaneScenarios['inconsistent-status'].serverStatus,
  },
}
