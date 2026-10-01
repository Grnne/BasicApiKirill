/* No base URL on purpose: the API is always same-origin (Vite proxy in dev, served by the API in
   prod), so there is no CORS and the token can never be sent to another host. */

import { ApiError, NetworkError, type ProblemDetails } from './problem'

/** The auth store registers itself at startup; http knows nothing of Pinia (no import cycle). */
export interface AuthBridge {
  /** Read on every request: it changes after a refresh. */
  getAccessToken: () => string | null
  /** Resolves true when the request can be retried. */
  refreshTokens: () => Promise<boolean>
}

let authBridge: AuthBridge | null = null

export function setAuthBridge(bridge: AuthBridge): void {
  authBridge = bridge
}

export function getAuthBridge(): AuthBridge | null {
  return authBridge
}

export type QueryParams = Record<string, string | number | boolean | undefined | null>

export interface RequestOptions {
  query?: QueryParams
  /** false: no token is sent and a 401 does not trigger a refresh (login, register, refresh). */
  auth?: boolean
  signal?: AbortSignal
  /** The request outlives the page: for what is sent as the tab is hidden or closed. */
  keepalive?: boolean
}

function buildPath(path: string, query?: QueryParams): string {
  // Relative paths only: a request can never reach another host, even with user input in path.
  if (!path.startsWith('/')) {
    throw new Error(`Path must start with "/": ${path}`)
  }

  if (!query) return path

  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }

  const queryString = search.toString()
  return queryString ? `${path}?${queryString}` : path
}

async function send(
  method: string,
  url: string,
  body: unknown,
  options: RequestOptions,
): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' }

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  if (options.auth !== false) {
    const token = authBridge?.getAccessToken() ?? null
    if (token) headers['Authorization'] = `Bearer ${token}`
  }

  const init: RequestInit = { method, headers }
  if (body !== undefined) init.body = JSON.stringify(body)
  if (options.signal) init.signal = options.signal
  if (options.keepalive) init.keepalive = true

  try {
    return await fetch(url, init)
  } catch (error) {
    // fetch rejects only on network failure or abort, never on HTTP status codes.
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new NetworkError(error)
  }
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    const data: unknown = await response.json()
    return typeof data === 'object' && data !== null ? (data as ProblemDetails) : null
  } catch {
    return null
  }
}

async function toError(response: Response): Promise<ApiError> {
  const problem = await readProblem(response)
  const retryAfter = Number(response.headers.get('Retry-After'))
  return new ApiError(
    response.status,
    problem,
    Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : null,
  )
}

async function readBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T
  }
  const text = await response.text()
  if (text.length === 0) return undefined as T
  return JSON.parse(text) as T
}

async function request<T>(
  method: string,
  path: string,
  body: unknown,
  options: RequestOptions = {},
): Promise<T> {
  const url = buildPath(path, options.query)

  let response = await send(method, url, body, options)

  // Exactly one retry after a refresh; more would loop forever if refresh is broken.
  if (response.status === 401 && options.auth !== false && authBridge) {
    const refreshed = await authBridge.refreshTokens()
    if (refreshed) {
      response = await send(method, url, body, options)
    }
  }

  if (!response.ok) throw await toError(response)

  return readBody<T>(response)
}

export const http = {
  get: <T>(path: string, options?: RequestOptions): Promise<T> =>
    request<T>('GET', path, undefined, options),

  post: <T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> =>
    request<T>('POST', path, body, options),

  put: <T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> =>
    request<T>('PUT', path, body, options),

  patch: <T>(path: string, body?: unknown, options?: RequestOptions): Promise<T> =>
    request<T>('PATCH', path, body, options),

  delete: <T>(path: string, options?: RequestOptions): Promise<T> =>
    request<T>('DELETE', path, undefined, options),
}
