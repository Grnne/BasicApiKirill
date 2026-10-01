import { describe, expect, it } from 'vitest'

import { ApiError, describeError, NetworkError } from './problem'

describe('ApiError.userMessage', () => {
  it('validation errors come as {code, message} per field and read in Russian', () => {
    // The server's shape (InvalidModelStateResponseFactory); joining them as strings gave "[object Object]".
    const error = new ApiError(400, {
      errorCode: 'VALIDATION_ERROR',
      detail: 'One or more validation errors occurred.',
      errors: {
        Username: [{ code: 'REQUIRED', message: 'The Username field is required.' }],
        Password: [{ code: 'MIN_LENGTH', message: 'too short' }],
      },
    })

    expect(error.userMessage).toBe('Логин: не заполнено\nПароль: слишком коротко')
  })

  it('knows error codes, so the server English never reaches the user', () => {
    const error = new ApiError(409, { errorCode: 'USERNAME_TAKEN', detail: 'Username already exists' })
    expect(error.code).toBe('USERNAME_TAKEN')
    expect(error.userMessage).toBe('Этот логин уже занят')
  })

  it('mentions Retry-After on 429', () => {
    expect(new ApiError(429, { errorCode: 'RATE_LIMITED' }, 7).userMessage).toContain('7 с')
  })

  it('an unknown code falls back to a generic Russian text with the status', () => {
    expect(new ApiError(418, { errorCode: 'SOMETHING_NEW', detail: 'teapot' }).userMessage).toBe('Ошибка сервера (418)')
  })
})

describe('describeError', () => {
  it('covers network failures and anything else', () => {
    expect(describeError(new NetworkError(new TypeError('fetch failed')))).toBe('Нет связи с сервером')
    expect(describeError(new Error('boom'))).toBe('Что-то пошло не так')
  })
})
