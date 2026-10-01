import { describe, expect, it } from 'vitest'

import { ApiError } from './problem'

describe('ApiError.userMessage', () => {
  it('prefers validation errors over the generic title', () => {
    const error = new ApiError(400, {
      title: 'One or more validation errors occurred.',
      errors: { Text: ['too long'], Name: ['required'] },
    })
    expect(error.userMessage).toBe('too long\nrequired')
  })

  it('mentions Retry-After on 429', () => {
    expect(new ApiError(429, null, 7).userMessage).toContain('7')
  })
})
