import type { Meta, StoryObj } from '@storybook/react-vite'

import {
  noActiveStartup,
  startupCompleted,
  startupInProgress,
  startupSchema,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { StartupTracking } from './StartupTracking'

type Story = StoryObj<typeof StartupTracking>

const meta = {
  title: 'Server/Startup Tracking',
  component: StartupTracking,
  args: {
    startupSchema: { status: 'available', data: startupSchema },
    startupSnapshot: { status: 'available', data: noActiveStartup },
  },
} satisfies Meta<typeof StartupTracking>

export default meta

export const NoActiveStartup: Story = {}

export const InProgress: Story = {
  args: {
    startupSnapshot: { status: 'available', data: startupInProgress },
  },
}

export const Completed: Story = {
  args: {
    startupSnapshot: { status: 'available', data: startupCompleted },
  },
}

export const Loading: Story = {
  args: {
    startupSchema: { status: 'loading' },
  },
}

export const Unavailable: Story = {
  args: {
    startupSnapshot: {
      status: 'unavailable',
      message: 'Startup milestones are not available.',
    },
  },
}
