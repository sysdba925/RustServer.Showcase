import type { ComponentProps } from 'react'

import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import {
  serverConfiguration,
  serverConfigurationUnconfirmed,
} from '../../control-plane/fixtures/control-plane-scenarios'
import { NextWorldConfigurationPanel } from './NextWorldConfigurationPanel'

type Story = StoryObj<typeof NextWorldConfigurationPanel>
type Props = ComponentProps<typeof NextWorldConfigurationPanel>

const loadedArgs = {
  configuration: { status: 'available', data: serverConfiguration },
  mutationsBlocked: false,
  pendingSave: false,
  saveError: null,
  saveSucceeded: false,
  onSave: fn(),
} satisfies Props

const meta = {
  title: 'World/Panels/Next World Configuration',
  component: NextWorldConfigurationPanel,
  args: loadedArgs,
} satisfies Meta<typeof NextWorldConfigurationPanel>

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
    saveError: 'World size must be within the configured range.',
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
