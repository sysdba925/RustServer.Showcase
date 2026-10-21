import type { Meta, StoryObj } from '@storybook/react-vite'

import { Panel } from './Panel'

type Story = StoryObj<typeof Panel>

const meta = {
  title: 'Shared UI/Panel',
  component: Panel,
  args: {
    children: (
      <div style={{ padding: '1.25rem' }}>
        <h2 style={{ margin: 0 }}>Panel title</h2>
        <p style={{ marginBottom: 0 }}>A grouped application surface.</p>
      </div>
    ),
  },
} satisfies Meta<typeof Panel>

export default meta

export const Default: Story = {}
