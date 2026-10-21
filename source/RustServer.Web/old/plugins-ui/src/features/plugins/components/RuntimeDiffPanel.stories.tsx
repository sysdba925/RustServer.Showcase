import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { runtimeChangeDiff } from '../fixtures/plugin-fixtures'
import { RuntimeDiffPanel } from './RuntimeDiffPanel'

type Story = StoryObj<typeof RuntimeDiffPanel>

const meta = {
  title: 'Plugins/Runtime Changes/Diff',
  component: RuntimeDiffPanel,
  args: {
    diff: runtimeChangeDiff,
    loading: false,
    error: null,
    onClose: fn(),
  },
} satisfies Meta<typeof RuntimeDiffPanel>

export default meta

export const Loaded: Story = {}

export const Loading: Story = {
  args: {
    diff: null,
    loading: true,
  },
}

export const Error: Story = {
  args: {
    diff: null,
    error: 'The runtime comparison changed while loading the diff.',
  },
}
