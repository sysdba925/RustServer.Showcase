import { createBrowserRouter } from 'react-router-dom'
import type { RouteObject } from 'react-router-dom'

import { OverviewPage } from '../../features/overview/OverviewPage'
import { CatalogPage } from '../../features/plugins/catalog/CatalogPage'
import { PluginDetailsPage } from '../../features/plugins/catalog/PluginDetailsPage'
import { PluginsPage } from '../../features/plugins/PluginsPage'
import { ServerPage } from '../../features/server/ServerPage'
import { WorldPage } from '../../features/world/WorldPage'
import { AppLayout } from '../layout/AppLayout'
import { NotFoundPage } from './NotFoundPage'

export const dashboardBasename = '/dashboard'

export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppLayout />,
    children: [
      {
        index: true,
        element: <OverviewPage />,
      },
      {
        path: 'server',
        element: <ServerPage />,
      },
      {
        path: 'world',
        element: <WorldPage />,
      },
      {
        path: 'plugins',
        element: <PluginsPage />,
      },
      {
        path: 'plugins/catalog',
        element: <CatalogPage />,
      },
      {
        path: 'plugins/catalog/:pluginId',
        element: <PluginDetailsPage />,
      },
      {
        path: '*',
        element: <NotFoundPage />,
      },
    ],
  },
]

export const router = createBrowserRouter(routes, { basename: dashboardBasename })
