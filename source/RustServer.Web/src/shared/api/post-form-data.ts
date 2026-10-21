import { requestJson } from './request-json'

/** Performs a multipart POST request and returns JSON only for successful responses. */
export function postFormData<T>(url: string, body: FormData, signal?: AbortSignal): Promise<T> {
  return requestJson<T>('POST', url, signal, body)
}
