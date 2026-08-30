/**
 * HTTP-клиент. Одна точка входа для всех запросов к API.
 *
 * Что он берёт на себя:
 *  - подставляет Authorization: Bearer;
 *  - при 401 один раз обновляет токен и повторяет запрос;
 *  - превращает ProblemDetails в ApiError;
 *  - разбирает пустые ответы (204).
 *
 * Базового URL нет намеренно: в деве Vite проксирует /api и /hubs на бэкенд,
 * в проде фронт лежит рядом с API. Всегда один origin — значит нет CORS
 * и нет соблазна разослать токен на чужой домен.
 */

import { ApiError, NetworkError, type ProblemDetails } from './problem'

/**
 * Мостик к auth-стору. Стор регистрирует себя один раз при старте, а http
 * ничего про Pinia не знает — иначе получилось бы кольцо импортов
 * (стор -> api -> стор).
 */
export interface AuthBridge {
  /** Текущий access-токен или null. Читается на каждый запрос — после refresh он другой. */
  getAccessToken: () => string | null
  /** Обновить пару токенов. true — получилось, можно повторять запрос. */
  refreshTokens: () => Promise<boolean>
}

let authBridge: AuthBridge | null = null

export function setAuthBridge(bridge: AuthBridge): void {
  authBridge = bridge
}

/** Тем же мостиком пользуется SignalR-клиент — ему тоже нужен свежий токен. */
export function getAuthBridge(): AuthBridge | null {
  return authBridge
}

export type QueryParams = Record<string, string | number | boolean | undefined | null>

export interface RequestOptions {
  query?: QueryParams
  /** false — не слать токен и не пытаться обновлять его (логин, регистрация, refresh). */
  auth?: boolean
  signal?: AbortSignal
}

function buildPath(path: string, query?: QueryParams): string {
  // Только относительные пути: так запрос физически не может уйти на чужой хост,
  // даже если в path попадёт что-то из пользовательского ввода.
  if (!path.startsWith('/')) {
    throw new Error(`Путь должен начинаться с "/": ${path}`)
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

  try {
    return await fetch(url, init)
  } catch (error) {
    // fetch падает только на сетевых проблемах и отмене; HTTP-коды сюда не попадают.
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
  // 204 и пустое тело — у вызывающего кода тип будет void.
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

  // Один повтор после обновления токена. Ровно один — иначе при сломанном
  // refresh получим бесконечный цикл запросов.
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

  delete: <T>(path: string, options?: RequestOptions): Promise<T> =>
    request<T>('DELETE', path, undefined, options),
}
