/* API errors arrive as ProblemDetails (RFC 7807). */

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  /** Validation errors: field name -> messages. */
  errors?: Record<string, string[]>
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
    super(problem?.title || problem?.detail || `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isRateLimited(): boolean {
    return this.status === 429
  }

  /** Validation errors come first: their title is the generic "One or more validation errors". */
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
