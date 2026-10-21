import type { ComponentProps } from 'react'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import {
  controlPlaneScenarios,
  serverStatusUnavailable,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { deriveActionAvailability } from '../../control-plane/model'
import { ServerRuntimePanel } from './ServerRuntimePanel'

type Story = StoryObj<typeof ServerRuntimePanel>
type Props = ComponentProps<typeof ServerRuntimePanel>

const readyArgs = {
  actionAvailability: deriveActionAvailability(controlPlaneScenarios.ready),
  pendingLifecycleAction: null,
  mutationsBlocked: false,
  lifecycleError: null,
  onStart: fn(),
  onStop: fn(),
  onRestart: fn(),
  serverStatus: controlPlaneScenarios.ready.serverStatus,
  rustVersions: controlPlaneScenarios.ready.rustVersions,
  operationDetail: controlPlaneScenarios.ready.operationDetail,
} satisfies Props

const meta = {
  title: 'Server/Panels/Runtime',
  component: ServerRuntimePanel,
  args: readyArgs,
} satisfies Meta<typeof ServerRuntimePanel>

export default meta

export const Ready: Story = {}

export const Stopped: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(controlPlaneScenarios.stopped),
    serverStatus: controlPlaneScenarios.stopped.serverStatus,
    rustVersions: controlPlaneScenarios.stopped.rustVersions,
    operationDetail: controlPlaneScenarios.stopped.operationDetail,
  },
}

export const Starting: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(controlPlaneScenarios.starting),
    serverStatus: controlPlaneScenarios.starting.serverStatus,
    rustVersions: controlPlaneScenarios.starting.rustVersions,
  },
}

export const Maintenance: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(controlPlaneScenarios.maintenance),
    serverStatus: controlPlaneScenarios.maintenance.serverStatus,
    rustVersions: controlPlaneScenarios.maintenance.rustVersions,
    operationDetail: controlPlaneScenarios.maintenance.operationDetail,
  },
}

export const LifecycleActionPending: Story = {
  args: {
    ...readyArgs,
    mutationsBlocked: true,
    pendingLifecycleAction: 'restart',
  },
}

export const LifecycleError: Story = {
  args: {
    ...readyArgs,
    lifecycleError: 'The lifecycle request could not be completed.',
  },
}

export const Loading: Story = {
  args: {
    ...readyArgs,
    serverStatus: { status: 'loading' },
  },
}

export const Unavailable: Story = {
  args: {
    ...readyArgs,
    serverStatus: serverStatusUnavailable,
  },
}

export const Inconsistent: Story = {
  args: {
    ...readyArgs,
    actionAvailability: deriveActionAvailability(
      controlPlaneScenarios['inconsistent-status'],
    ),
    serverStatus: controlPlaneScenarios['inconsistent-status'].serverStatus,
    rustVersions: controlPlaneScenarios['inconsistent-status'].rustVersions,
  },
}
