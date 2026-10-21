import { environment } from '../config/environment'

import { fetchApi, HttpError } from './request-json'
import { getJson } from './get-json'
import { deleteNoContent } from './delete-no-content'
import { postFormData } from './post-form-data'
import { postJsonWithBody } from './post-json'
import { putJson } from './put-json'
import type {
  SiteDto, SiteRequest, TagDto, TagRequest, FolderDto, FolderRequest, ImportPreview, ImportResult,
  CreateExternalPluginMediaRequest,
  CreatePluginRequest,
  ListPluginsQuery,
  PagedResultDto,
  PluginDetailsDto,
  PluginMediaDto,
  PluginSummaryDto,
  UpdatePluginIconRequest,
  UpdatePluginMediaRequest,
  UpdatePluginRequest,
} from './plugin-catalog-types'

const pluginsPath = '/api/plugins'

/** Downloads the complete archive assembled by the Catalog service. */
export async function exportPluginCatalog(signal?: AbortSignal): Promise<Blob> {
  const url = createPluginCatalogUrl('/api/export')
  const response = await fetchApi(url, { method: 'GET', signal })
  if (!response.ok) throw new HttpError({ method: 'GET', url, status: response.status, statusText: response.statusText })
  return response.blob()
}

function createPluginCatalogUrl(path: string): string {
  const baseUrl = environment.pluginCatalogApiBaseUrl.replace(/\/+$/, '')
  const normalizedPath = path.startsWith('/') ? path : `/${path}`

  return normalizedPath === baseUrl || normalizedPath.startsWith(`${baseUrl}/`)
    ? normalizedPath
    : `${baseUrl}${normalizedPath}`
}

/** Resolves a Catalog-relative media URL through the Catalog's same-origin prefix. */
export function resolvePluginCatalogContentUrl(apiUrl: string): string {
  return /^https?:\/\//i.test(apiUrl) ? apiUrl : createPluginCatalogUrl(apiUrl)
}

/** Gets one backend-paged, backend-filtered catalog result. */
export function getPluginCatalogPlugins(
  query: ListPluginsQuery,
  signal?: AbortSignal,
): Promise<PagedResultDto<PluginSummaryDto>> {
  const search = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  })

  if (query.search !== undefined && query.search !== '') {
    search.set('search', query.search)
  }

  for (const key of ['siteId', 'tagId', 'folderId'] as const) {
    if (query[key] !== undefined) search.set(key, query[key])
  }

  return getJson<PagedResultDto<PluginSummaryDto>>(
    createPluginCatalogUrl(`${pluginsPath}?${search.toString()}`),
    signal,
  )
}

/** Creates a catalog entry; the Catalog service owns validation and slug normalization. */
export function createPluginCatalogPlugin(
  request: CreatePluginRequest,
  signal?: AbortSignal,
): Promise<PluginDetailsDto> {
  return postJsonWithBody<PluginDetailsDto, CreatePluginRequest>(
    createPluginCatalogUrl(pluginsPath),
    request,
    signal,
  )
}

/** Gets full catalog metadata needed to edit a list entry. */
export function getPluginCatalogPlugin(
  pluginId: string,
  signal?: AbortSignal,
): Promise<PluginDetailsDto> {
  return getJson<PluginDetailsDto>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}`),
    signal,
  )
}

/** Updates catalog metadata. */
export function updatePluginCatalogPlugin(
  pluginId: string,
  request: UpdatePluginRequest,
  signal?: AbortSignal,
): Promise<PluginDetailsDto> {
  return putJson<PluginDetailsDto, UpdatePluginRequest>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}`),
    request,
    signal,
  )
}

/** Deletes a catalog entry and its associated metadata and media. */
export function deletePluginCatalogPlugin(pluginId: string, signal?: AbortSignal): Promise<void> {
  return deleteNoContent(createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}`), signal)
}

/** Uploads one local image. FormData deliberately leaves Content-Type to the browser. */
export function uploadPluginCatalogMedia(
  pluginId: string,
  request: FormData,
  signal?: AbortSignal,
): Promise<PluginMediaDto> {
  return postFormData<PluginMediaDto>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}/media`),
    request,
    signal,
  )
}

/** Adds an external Video or Preview link without fetching or proxying it. */
export function createExternalPluginCatalogMedia(
  pluginId: string,
  request: CreateExternalPluginMediaRequest,
  signal?: AbortSignal,
): Promise<PluginMediaDto> {
  return postJsonWithBody<PluginMediaDto, CreateExternalPluginMediaRequest>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}/media/external`),
    request,
    signal,
  )
}

/** Changes media metadata only; media bytes and kind remain immutable. */
export function updatePluginCatalogMedia(
  pluginId: string,
  mediaId: string,
  request: UpdatePluginMediaRequest,
  signal?: AbortSignal,
): Promise<PluginMediaDto> {
  return putJson<PluginMediaDto, UpdatePluginMediaRequest>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}/media/${encodeURIComponent(mediaId)}`),
    request,
    signal,
  )
}

/** Deletes one media record, clearing the selected icon server-side when needed. */
export function deletePluginCatalogMedia(
  pluginId: string,
  mediaId: string,
  signal?: AbortSignal,
): Promise<void> {
  return deleteNoContent(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}/media/${encodeURIComponent(mediaId)}`),
    signal,
  )
}

/** Selects an owned Icon media record, or clears the current selection with null. */
export function updatePluginCatalogIcon(
  pluginId: string,
  request: UpdatePluginIconRequest,
  signal?: AbortSignal,
): Promise<PluginDetailsDto> {
  return putJson<PluginDetailsDto, UpdatePluginIconRequest>(
    createPluginCatalogUrl(`${pluginsPath}/${encodeURIComponent(pluginId)}/icon`),
    request,
    signal,
  )
}

export function getPluginCatalogSites(signal?: AbortSignal): Promise<SiteDto[]> { return getJson(createPluginCatalogUrl('/api/sites'), signal) }
export function getPluginCatalogSite(id: string, signal?: AbortSignal): Promise<SiteDto> { return getJson(createPluginCatalogUrl(`/api/sites/${encodeURIComponent(id)}`), signal) }
export function createPluginCatalogSite(request: SiteRequest, signal?: AbortSignal): Promise<SiteDto> { return postJsonWithBody(createPluginCatalogUrl('/api/sites'), request, signal) }
export function updatePluginCatalogSite(id: string, request: SiteRequest, signal?: AbortSignal): Promise<SiteDto> { return putJson(createPluginCatalogUrl(`/api/sites/${encodeURIComponent(id)}`), request, signal) }
export function deletePluginCatalogSite(id: string, signal?: AbortSignal): Promise<void> { return deleteNoContent(createPluginCatalogUrl(`/api/sites/${encodeURIComponent(id)}`), signal) }

export function getPluginCatalogTags(signal?: AbortSignal): Promise<TagDto[]> { return getJson(createPluginCatalogUrl('/api/tags'), signal) }
export function getPluginCatalogTag(id: string, signal?: AbortSignal): Promise<TagDto> { return getJson(createPluginCatalogUrl(`/api/tags/${encodeURIComponent(id)}`), signal) }
export function createPluginCatalogTag(request: TagRequest, signal?: AbortSignal): Promise<TagDto> { return postJsonWithBody(createPluginCatalogUrl('/api/tags'), request, signal) }
export function updatePluginCatalogTag(id: string, request: TagRequest, signal?: AbortSignal): Promise<TagDto> { return putJson(createPluginCatalogUrl(`/api/tags/${encodeURIComponent(id)}`), request, signal) }
export function deletePluginCatalogTag(id: string, signal?: AbortSignal): Promise<void> { return deleteNoContent(createPluginCatalogUrl(`/api/tags/${encodeURIComponent(id)}`), signal) }

export function getPluginCatalogFolders(signal?: AbortSignal): Promise<FolderDto[]> { return getJson(createPluginCatalogUrl('/api/folders'), signal) }
export function getPluginCatalogFolder(id: string, signal?: AbortSignal): Promise<FolderDto> { return getJson(createPluginCatalogUrl(`/api/folders/${encodeURIComponent(id)}`), signal) }
export function createPluginCatalogFolder(request: FolderRequest, signal?: AbortSignal): Promise<FolderDto> { return postJsonWithBody(createPluginCatalogUrl('/api/folders'), request, signal) }
export function updatePluginCatalogFolder(id: string, request: FolderRequest, signal?: AbortSignal): Promise<FolderDto> { return putJson(createPluginCatalogUrl(`/api/folders/${encodeURIComponent(id)}`), request, signal) }
export function deletePluginCatalogFolder(id: string, signal?: AbortSignal): Promise<void> { return deleteNoContent(createPluginCatalogUrl(`/api/folders/${encodeURIComponent(id)}`), signal) }

/** Sends the original document to the Catalog for validation. */
export function previewPluginCatalogImport(document: unknown): Promise<ImportPreview> {
  return postJsonWithBody(createPluginCatalogUrl('/api/import/preview'), document)
}
export function commitPluginCatalogImport(document: unknown): Promise<ImportResult> {
  return postJsonWithBody(createPluginCatalogUrl('/api/import/commit'), document)
}

/** Sends the original ZIP bytes; the service validates the archive on both calls. */
async function restorePluginCatalog<T>(stage: 'preview' | 'commit', archive: File): Promise<T> {
  const url = createPluginCatalogUrl(`/api/restore/${stage}`)
  const response = await fetchApi(url, { method: 'POST', headers: { 'Content-Type': 'application/zip' }, body: archive })
  if (!response.ok) throw new HttpError({ method: 'POST', url, status: response.status, statusText: response.statusText })
  return await response.json() as T
}
export function previewPluginCatalogRestore(archive: File): Promise<import('./plugin-catalog-types').RestorePreview> { return restorePluginCatalog('preview', archive) }
export function commitPluginCatalogRestore(archive: File): Promise<import('./plugin-catalog-types').RestoreCommitResult> { return restorePluginCatalog('commit', archive) }
