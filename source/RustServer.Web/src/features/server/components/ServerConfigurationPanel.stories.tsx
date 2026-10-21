import type { ComponentProps } from 'react'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import {
  serverConfiguration,
  serverConfigurationUnconfirmed,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { ServerConfigurationPanel } from './ServerConfigurationPanel'

type Story = StoryObj<typeof ServerConfigurationPanel>
type Props = ComponentProps<typeof ServerConfigurationPanel>

const loadedArgs = {
  configuration: { status: 'available', data: serverConfiguration },
  mutationsBlocked: false,
  pendingSave: false,
  saveError: null,
  saveSucceeded: false,
  onSave: fn(),
} satisfies Props

const meta = {
  title: 'Server/Panels/Configuration',
  component: ServerConfigurationPanel,
  args: loadedArgs,
} satisfies Meta<typeof ServerConfigurationPanel>

export default meta

export const Loaded: Story = {}

export const Saving: Story = {
  args: {
    ...loadedArgs,
    pendingSave: true,
  },
}

export const SaveSucceeded: Story = {
  args: {
    ...loadedArgs,
    saveSucceeded: true,
  },
}

export const SaveError: Story = {
  args: {
    ...loadedArgs,
    saveError: 'Seed must be within the configured range.',
  },
}

export const MutationsBlocked: Story = {
  args: {
    ...loadedArgs,
    mutationsBlocked: true,
  },
}

export const EffectiveConfigurationUnconfirmed: Story = {
  args: {
    ...loadedArgs,
    configuration: { status: 'available', data: serverConfigurationUnconfirmed },
  },
}

export const Loading: Story = {
  args: {
    ...loadedArgs,
    configuration: { status: 'loading' },
  },
}

export const Unavailable: Story = {
  args: {
    ...loadedArgs,
    configuration: {
      status: 'unavailable',
      message: 'Control service did not respond.',
    },
  },
}
