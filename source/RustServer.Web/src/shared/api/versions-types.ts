/** Source shapes published by the RustServer.Versions API. */
export interface RustRunningInfo {
  protocol: string
  buildDate: string
  unityVersion: string
  changeset: number
  branch: string
}

/** The schema name used by the Versions API document. */
export type RustRunningVersion = RustRunningInfo

export interface RustVersionInfo {
  branch: string
  installedBuildId: number | null
  running?: RustRunningInfo | null
  availableBuildId: number
  updateAvailable: boolean
}

export interface OxideVersionInfo {
  installedVersion: string | null
  runningVersion?: string | null
  availableVersion: string
  updateAvailable: boolean
}
