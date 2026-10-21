/** Mirrors the PluginCatalog API's `PluginMediaKind` enum. */
export type PluginMediaKind =
  | 'Icon'
  | 'Screenshot'
  | 'AnimatedImage'
  | 'Video'
  | 'Preview'
  | 'Technical'

/** Local-upload kinds accepted by the PluginCatalog media endpoint. */
export const localPluginMediaKinds = [
  'Icon',
  'Screenshot',
  'AnimatedImage',
  'Preview',
  'Technical',
] as const

/** Local-upload kinds accepted by the PluginCatalog media endpoint. */
export type LocalPluginMediaKind = typeof localPluginMediaKinds[number]

/** External-link kinds accepted by the PluginCatalog media endpoint. */
export const externalPluginMediaKinds = ['Video', 'Preview', 'Technical'] as const

/** External-link kinds accepted by the PluginCatalog media endpoint. */
export type ExternalPluginMediaKind = typeof externalPluginMediaKinds[number]

/** Mirrors the PluginCatalog API's `PluginIconDto` schema. */
export interface PluginIconDto {
  id: string
  accessUrl: string
  thumbnailAccessUrl: string
  contentType: string
}

/** Mirrors the PluginCatalog API's `PluginSourceDto` schema. */
export interface PluginSourceDto {
  id: string
  siteId: string | null
  url: string
  label: string | null
}

/** Mirrors the PluginCatalog API's `PluginMediaDto` schema. */
export interface PluginMediaDto {
  id: string
  kind: PluginMediaKind
  caption: string | null
  sortOrder: number
  contentType: string | null
  accessUrl: string
  thumbnailAccessUrl: string | null
}

/** Mirrors the PluginCatalog API's `PluginSummaryDto` schema. */
export interface PluginSummaryDto {
  id: string
  name: string
  slug: string
  shortDescription: string
  description: string | null
  author: string | null
  notes: string | null
  sourceDescription: string | null
  price: number | string | null
  currency: string | null
  tags: TagDto[]
  folders: FolderDto[]
  currentKnownVersion: string | null
  lastPublishedDate: string | null
  updatedAt: string
  icon: PluginIconDto | null
  sources: PluginSourceDto[]
}

/** Mirrors the PluginCatalog API's `PluginDetailsDto` schema. */
export interface PluginDetailsDto extends PluginSummaryDto {
  createdAt: string
  iconMediaId: string | null
  media: PluginMediaDto[]
}

/** Mirrors the PluginCatalog API's `PagedResultDto<T>` schema. */
export interface PagedResultDto<T> {
  items: T[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export interface CreatePluginRequest {
  name: string
  author?: string | null
  description?: string | null
  sourceDescription?: string | null
  notes?: string | null
  price?: number | string | null
  currency?: string | null
  currentKnownVersion?: string | null
  lastPublishedDate?: string | null
  sources?: PluginSourceInput[] | null
  tagIds?: string[] | null
  folderIds?: string[] | null
}
export type UpdatePluginRequest = CreatePluginRequest
export interface SiteDto { id: string; name: string; baseUrl: string | null }
export interface SiteRequest { name: string; baseUrl?: string | null }
export interface TagDto { id: string; name: string; color: string }
export interface TagRequest { name: string; color: string }
export interface FolderDto { id: string; name: string }
export interface FolderRequest { name: string }

/** Mirrors the PluginCatalog API's `PluginSourceInput` schema. */
export interface PluginSourceInput {
  siteId?: string | null
  url: string
  label?: string | null
}

/** Mirrors the PluginCatalog API's external-media request schema. */
export interface CreateExternalPluginMediaRequest {
  kind: ExternalPluginMediaKind
  externalUrl: string
  caption?: string | null
  sortOrder: number
}

/** Mirrors the PluginCatalog API's `UpdateMediaRequest` schema. */
export interface UpdatePluginMediaRequest {
  caption: string | null
  sortOrder: number
}

/** Mirrors the PluginCatalog API's icon-selection request schema. */
export interface UpdatePluginIconRequest {
  mediaId: string | null
}

/** Query parameters published for the PluginCatalog list operation. */
export interface ListPluginsQuery {
  page: number
  pageSize: number
  search?: string
  siteId?: string
  tagId?: string
  folderId?: string
}

// JSON import DTOs published by PluginCatalog Development OpenAPI.
export interface ImportSource { url: string | null; site: string | null; label: string | null }
export interface ImportPrice { amount: number | string | null; currency: string | null }
export interface ImportPlugin { name: string | null; sources: ImportSource[] | null; author: string | null; description: string | null; notes: string | null; sourceDescription: string | null; price: ImportPrice | null; tags: string[] | null; folders: string[] | null; currentKnownVersion?: string | null; lastPublishedDate?: string | null }
export interface ImportEntry { index: number | string; name: string | null; input: ImportPlugin | null; errors: string[]; duplicates: string[]; unknownSites: string[]; unknownTags: string[]; unknownFolders: string[] }
export interface ImportPreview { errors: string[]; entries: ImportEntry[] }
export interface ImportResult { preview: ImportPreview; created: number | string; skipped: number | string; committed: boolean }

export interface RestoreCounts { plugins: number | string; sites: number | string; tags: number | string; folders: number | string; sources: number | string; media: number | string }
export interface RestoreConflict { type: string; id: string; reason: string }
export interface RestorePreview { counts: RestoreCounts; conflicts: RestoreConflict[] }
export interface RestoreCommitResult { counts: RestoreCounts }
