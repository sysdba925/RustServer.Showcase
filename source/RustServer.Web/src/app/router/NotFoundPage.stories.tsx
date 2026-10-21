import type { Meta, StoryObj } from '@storybook/react-vite'

import { NotFoundPage } from './NotFoundPage'

type Story = StoryObj<typeof NotFoundPage>

const meta = {
  title: 'App/Not Found Page',
  component: NotFoundPage,
} satisfies Meta<typeof NotFoundPage>

export default meta

export const MissingRoute: Story = {}
