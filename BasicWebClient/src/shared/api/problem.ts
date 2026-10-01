/* API errors arrive as ProblemDetails (RFC 7807) with a machine-readable errorCode. The server's
   detail is English and meant for developers: users get the Russian text for the code. */

import { ERROR_TEXTS, FIELD_NAMES, VALIDATION_TEXTS } from './error-texts'

export interface FieldError {
  code: string
  message: string
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errorCode?: string
  traceId?: string
  /** Validation errors: field name -> its errors. */
  errors?: Record<string, FieldError[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null
  /** Set for 429 from the Retry-After header. */
  readonly retryAfterSeconds: number | null

  constructor(
    status: number,
    problem: ProblemDetails | null,
    retryAfterSeconds: number | null = null,
  ) {
    super(problem?.errorCode || problem?.title || `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  get code(): string | null {
    return this.problem?.errorCode ?? null
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isRateLimited(): boolean {
    return this.status === 429
  }

  get userMessage(): string {
    const fieldErrors = this.problem?.errors
    if (fieldErrors) {
      const lines = Object.entries(fieldErrors).flatMap(([field, errors]) =>
        errors.map((e) => `${fieldName(field)}: ${VALIDATION_TEXTS[e.code] ?? 'неверное значение'}`),
      )
      if (lines.length > 0) return lines.join('\n')
    }
    if (this.isRateLimited) {
      const wait = this.retryAfterSeconds
      return wait ? `Слишком часто. Повторите через ${wait} с.` : 'Слишком часто. Повторите позже.'
    }
    const known = this.code ? ERROR_TEXTS[this.code] : undefined
    return known ?? `Ошибка сервера (${this.status})`
  }
}

function fieldName(field: string): string {
  const key = field.charAt(0).toLowerCase() + field.slice(1)
  return FIELD_NAMES[key] ?? field
}

/** The request never reached the server. */
export class NetworkError extends Error {
  /** The original fetch error, for logs only. */
  readonly reason: unknown

  constructor(reason: unknown) {
    super('Нет связи с сервером')
    this.name = 'NetworkError'
    this.reason = reason
  }
}

/** Text for the user about any failure of an API call. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.userMessage
  if (error instanceof NetworkError) return 'Нет связи с сервером'
  return 'Что-то пошло не так'
}
