import { describe, expect, it, vi } from 'vitest'

import { freshAccessToken, tokenExpiresAt } from './token'

/** An unsigned JWT with the given exp (seconds); only the payload matters to the client. */
function jwt(expSeconds: number): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return `${encode({ alg: 'HS256' })}.${encode({ sub: 'u', exp: expSeconds })}.signature`
}

const NOW = Date.UTC(2026, 9, 1, 12, 0, 0)

function bridge(token: string | null, next: string | null) {
  let current = token
  return {
    getAccessToken: () => current,
    refreshTokens: vi.fn(async () => {
      current = next
      return next !== null
    }),
  }
}

describe('tokenExpiresAt', () => {
  it('reads exp from the payload', () => {
    expect(tokenExpiresAt(jwt(NOW / 1000 + 60))).toBe(NOW + 60_000)
  })

  it('is null for something that is not a JWT', () => {
    expect(tokenExpiresAt('nonsense')).toBeNull()
  })
})

describe('freshAccessToken', () => {
  it('uses a live token as is', async () => {
    const live = jwt(NOW / 1000 + 600)
    const b = bridge(live, null)

    expect(await freshAccessToken(b, NOW)).toBe(live)
    expect(b.refreshTokens).not.toHaveBeenCalled()
  })

  it('refreshes an expired token before a (re)connect instead of sending it', async () => {
    // The bug: the hub refreshed only when there was no token at all, so a reconnect after the
    // tab sat idle longer than the token lifetime got 401 and gave up.
    const fresh = jwt(NOW / 1000 + 600)
    const b = bridge(jwt(NOW / 1000 - 5), fresh)

    expect(await freshAccessToken(b, NOW)).toBe(fresh)
    expect(b.refreshTokens).toHaveBeenCalledOnce()
  })

  it('refreshes a token about to expire', async () => {
    const b = bridge(jwt(NOW / 1000 + 10), jwt(NOW / 1000 + 600))

    await freshAccessToken(b, NOW)

    expect(b.refreshTokens).toHaveBeenCalledOnce()
  })

  it('refreshes when there is no token', async () => {
    const b = bridge(null, jwt(NOW / 1000 + 600))

    await freshAccessToken(b, NOW)

    expect(b.refreshTokens).toHaveBeenCalledOnce()
  })

  it('gives an empty string when the session is gone', async () => {
    expect(await freshAccessToken(bridge(null, null), NOW)).toBe('')
  })
})
