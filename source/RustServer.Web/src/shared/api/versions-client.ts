import { environment } from '../config/environment'

import { getJson } from './get-json'
import type { OxideVersionInfo, RustVersionInfo } from './versions-types'

const rustVersionsPath = '/api/versions/rust'
const oxideVersionsPath = '/api/versions/oxide'

function createVersionsUrl(path: string): string {
  return `${environment.versionsApiBaseUrl.replace(/\/+$/, '')}${path}`
}

/** Gets the installed, running, and available Rust versions. */
export function getRustVersions(
  signal?: AbortSignal,
): Promise<RustVersionInfo> {
  return getJson<RustVersionInfo>(createVersionsUrl(rustVersionsPath), signal)
}

/** Gets the installed, running, and available Oxide versions. */
export function getOxideVersions(
  signal?: AbortSignal,
): Promise<OxideVersionInfo> {
  return getJson<OxideVersionInfo>(createVersionsUrl(oxideVersionsPath), signal)
}
