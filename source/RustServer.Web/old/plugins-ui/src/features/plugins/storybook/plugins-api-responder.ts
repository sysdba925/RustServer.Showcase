export interface PluginsApiMockResponse {
  body: unknown
  status?: number
}

export interface PluginsApiRequest {
  method: string
  path: string
  url: URL
}

export type PluginsApiResponder = (
  request: PluginsApiRequest,
) => PluginsApiMockResponse

export function createPluginsApiResponder(
  routes: Readonly<Record<string, unknown>>,
): PluginsApiResponder {
  return ({ method, path }) => {
    const body = routes[`${method} ${path}`]

    return body === undefined
      ? { status: 404, body: null }
      : { body }
  }
}
