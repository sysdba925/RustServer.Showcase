import { requestJson } from './request-json'

/** Performs a JSON PUT request and returns JSON only for successful responses. */
export function putJson<TResponse, TRequest>(
  url: string,
  body: TRequest,
  signal?: AbortSignal,
): Promise<TResponse> {
  return requestJson<TResponse>('PUT', url, signal, body)
}
