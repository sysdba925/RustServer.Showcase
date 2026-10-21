import { requestJson } from './request-json'

/** Performs a bodyless POST request and returns JSON only for successful responses. */
export function postJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  return requestJson<T>('POST', url, signal)
}

/** Performs a JSON POST request and returns JSON only for successful responses. */
export function postJsonWithBody<TResponse, TRequest>(
  url: string,
  body: TRequest,
  signal?: AbortSignal,
): Promise<TResponse> {
  return requestJson<TResponse>('POST', url, signal, body)
}
