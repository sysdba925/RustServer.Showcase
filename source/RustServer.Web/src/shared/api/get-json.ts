import { requestJson } from './request-json'

/** Performs a GET request and returns JSON only for successful responses. */
export async function getJson<T>(
  url: string,
  signal?: AbortSignal,
): Promise<T> {
  return requestJson<T>('GET', url, signal)
}
