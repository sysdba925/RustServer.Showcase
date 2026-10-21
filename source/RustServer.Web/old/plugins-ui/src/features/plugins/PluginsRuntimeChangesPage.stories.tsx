import { MemoryRouter } from 'react-router-dom'

import type { Meta, StoryObj } from '@storybook/react-vite'

import { stagingRuntimeChanges } from './fixtures/plugin-fixtures'
import { PluginsRuntimeChangesPage } from './PluginsRuntimeChangesPage'
import { PluginsApiMock } from './storybook/PluginsApiMock'
import { createPluginsApiResponder } from './storybook/plugins-api-responder'

type Story = StoryObj<typeof PluginsRuntimeChangesPage>

const responder = createPluginsApiResponder({
  'GET /api/staging/runtime-changes': stagingRuntimeChanges,
})

const meta = {
  title: 'Plugins/Pages/Runtime Changes',
  component: PluginsRuntimeChangesPage,
  decorators: [
    (Story) => (
      <MemoryRouter initialEntries={['/plugins/runtime-changes']}>
        <PluginsApiMock responder={responder}>
          <Story />
        </PluginsApiMock>
      </MemoryRouter>
    ),
  ],
} satisfies Meta<typeof PluginsRuntimeChangesPage>

export default meta

export const Ready: Story = {}
