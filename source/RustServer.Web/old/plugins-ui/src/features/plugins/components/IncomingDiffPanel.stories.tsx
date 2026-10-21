import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { incomingChangeDiff } from '../fixtures/plugin-fixtures'
import { IncomingDiffPanel } from './IncomingDiffPanel'

type Story = StoryObj<typeof IncomingDiffPanel>

const meta = {
  title: 'Plugins/Incoming Changes/Diff',
  component: IncomingDiffPanel,
  args: {
    diff: incomingChangeDiff,
    loading: false,
    error: null,
    onClose: fn(),
  },
} satisfies Meta<typeof IncomingDiffPanel>

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
    error: 'The comparison is no longer current.',
  },
}
