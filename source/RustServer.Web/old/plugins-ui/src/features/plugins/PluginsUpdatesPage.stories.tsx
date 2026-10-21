import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { PluginsUpdatesPage } from './PluginsUpdatesPage'

type Story = StoryObj<typeof PluginsUpdatesPage>

const meta = {
  title: 'Plugins/Pages/Updates',
  component: PluginsUpdatesPage,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/plugins/updates']}>
        <Story />
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsUpdatesPage>

export default meta

export const Ready: Story = {}
