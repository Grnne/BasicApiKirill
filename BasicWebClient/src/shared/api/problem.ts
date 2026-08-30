/**
 * Ошибки API. Бэкенд отвечает ProblemDetails (RFC 7807) —
 * см. BasicApi/Middleware/ExceptionHandlingMiddleware.cs.
 */

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  /** Ошибки валидации: имя поля -> список сообщений. */
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null
  /** Сколько секунд ждать до повтора — заполняется для 429. */
  readonly retryAfterSeconds: number | null

  constructor(
    status: number,
    problem: ProblemDetails | null,
    retryAfterSeconds: number | null = null,
  ) {
    super(problem?.title || problem?.detail || `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  /** Токен недействителен или сессия закончилась. */
  get isUnauthorized(): boolean {
    return this.status === 401
  }

  /** Слишком часто — на логине бэкенд включает rate limit. */
  get isRateLimited(): boolean {
    return this.status === 429
  }

  /**
   * Текст для показа пользователю. Первым делом — ошибки валидации,
   * потому что title у них общий и бесполезный ("One or more validation errors").
   */
  get userMessage(): string {
    const fieldErrors = this.problem?.errors
    if (fieldErrors) {
      const messages = Object.values(fieldErrors).flat()
      if (messages.length > 0) return messages.join('\n')
    }
    if (this.isRateLimited) {
      const wait = this.retryAfterSeconds
      return wait ? `Слишком много попыток. Повтори через ${wait} с.` : 'Слишком много попыток.'
    }
    return this.problem?.detail || this.problem?.title || `Ошибка ${this.status}`
  }
}

/** Сеть недоступна / запрос оборвался — до сервера не дошли. */
export class NetworkError extends Error {
  /** Исходная ошибка fetch — для логов, не для показа пользователю. */
  readonly reason: unknown

  constructor(reason: unknown) {
    super('Нет связи с сервером')
    this.name = 'NetworkError'
    this.reason = reason
  }
}
