import {
  createExternalPluginCatalogMedia,
  createPluginCatalogPlugin,
  deletePluginCatalogMedia,
  deletePluginCatalogPlugin,
  getPluginCatalogPlugin,
  getPluginCatalogPlugins,
  updatePluginCatalogIcon,
  updatePluginCatalogMedia,
  updatePluginCatalogPlugin,
  uploadPluginCatalogMedia,
  type CreateExternalPluginMediaRequest,
  type CreatePluginRequest,
  type ListPluginsQuery,
  type PagedResultDto,
  type PluginDetailsDto,
  type PluginMediaDto,
  type PluginSummaryDto,
  type UpdatePluginIconRequest,
  type UpdatePluginMediaRequest,
  type UpdatePluginRequest,
} from '../../../shared/api'

/** Catalog-only HTTP boundary used by the page and replaceable in focused UI tests. */
export interface PluginCatalogApi {
  listPlugins: (
    query: ListPluginsQuery,
    signal?: AbortSignal,
  ) => Promise<PagedResultDto<PluginSummaryDto>>
  createPlugin: (
    request: CreatePluginRequest,
    signal?: AbortSignal,
  ) => Promise<PluginDetailsDto>
  getPlugin: (pluginId: string, signal?: AbortSignal) => Promise<PluginDetailsDto>
  updatePlugin: (
    pluginId: string,
    request: UpdatePluginRequest,
    signal?: AbortSignal,
  ) => Promise<PluginDetailsDto>
  deletePlugin: (pluginId: string, signal?: AbortSignal) => Promise<void>
  uploadMedia: (pluginId: string, request: FormData, signal?: AbortSignal) => Promise<PluginMediaDto>
  createExternalMedia: (
    pluginId: string,
    request: CreateExternalPluginMediaRequest,
    signal?: AbortSignal,
  ) => Promise<PluginMediaDto>
  updateMedia: (
    pluginId: string,
    mediaId: string,
    request: UpdatePluginMediaRequest,
    signal?: AbortSignal,
  ) => Promise<PluginMediaDto>
  deleteMedia: (pluginId: string, mediaId: string, signal?: AbortSignal) => Promise<void>
  updateIcon: (
    pluginId: string,
    request: UpdatePluginIconRequest,
    signal?: AbortSignal,
  ) => Promise<PluginDetailsDto>
}

/** Production adapter for the independent PluginCatalog service. */
export const pluginCatalogApi: PluginCatalogApi = {
  listPlugins: getPluginCatalogPlugins,
  createPlugin: createPluginCatalogPlugin,
  getPlugin: getPluginCatalogPlugin,
  updatePlugin: updatePluginCatalogPlugin,
  deletePlugin: deletePluginCatalogPlugin,
  uploadMedia: uploadPluginCatalogMedia,
  createExternalMedia: createExternalPluginCatalogMedia,
  updateMedia: updatePluginCatalogMedia,
  deleteMedia: deletePluginCatalogMedia,
  updateIcon: updatePluginCatalogIcon,
}
