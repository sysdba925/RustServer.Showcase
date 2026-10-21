import type { Meta, StoryObj } from '@storybook/react-vite'

import { Badge } from './Badge'

type Story = StoryObj<typeof Badge>

const meta = {
  title: 'Shared UI/Badge',
  component: Badge,
  args: {
    children: 'Status',
  },
} satisfies Meta<typeof Badge>

export default meta

export const Neutral: Story = {}

export const Success: Story = { args: { tone: 'success' } }

export const Warning: Story = { args: { tone: 'warning' } }

export const Danger: Story = { args: { tone: 'danger' } }
