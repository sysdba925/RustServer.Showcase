import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'

import { runtimeChanges } from '../fixtures/plugin-fixtures'
import { RuntimeChangesPanel } from './RuntimeChangesPanel'

type Story = StoryObj<typeof RuntimeChangesPanel>

const meta = {
  title: 'Plugins/Runtime Changes',
  component: RuntimeChangesPanel,
  args: {
    changes: runtimeChanges,
    selectedPaths: new Set<string>(),
    onSelectionChange: fn(),
    onViewDiff: fn(),
  },
} satisfies Meta<typeof RuntimeChangesPanel>

export default meta

export const MixedChanges: Story = {}

export const SelectedChanges: Story = {
  args: {
    selectedPaths: new Set([
      'oxide/plugins/RaidableBases.cs',
      'oxide/config/RaidableBases.json',
    ]),
  },
}

export const Empty: Story = {
  args: {
    changes: [],
  },
}
