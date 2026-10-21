import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { AppLayoutFrame } from './AppLayout'

type Story = StoryObj<typeof AppLayoutFrame>

const meta = {
  title: 'App/Layout',
  component: AppLayoutFrame,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/world']}>
        <Story />
      </MemoryRouter>
    ),
  ],
  args: {
    children: (
      <section>
        <h1>World</h1>
        <p>Storybook renders the application shell without starting providers.</p>
      </section>
    ),
  },
} satisfies Meta<typeof AppLayoutFrame>

export default meta

export const WorldNavigationActive: Story = {}
