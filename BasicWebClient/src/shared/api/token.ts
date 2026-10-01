import type { AuthBridge } from './http'

/** Refresh this long before expiry: the clocks of client and server differ a little. */
const EXPIRY_SKEW_MS = 30_000

/** exp of a JWT in ms, or null. The signature is not checked: that is the server's job. */
export function tokenExpiresAt(token: string): number | null {
  const payload = token.split('.')[1]
  if (!payload) return null
  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const exp = (JSON.parse(json) as { exp?: unknown }).exp
    return typeof exp === 'number' ? exp * 1000 : null
  } catch {
    return null
  }
}

/**
 * The access token for a hub (re)connect, refreshed first if it is missing or about to expire.
 * `force` — refresh anyway: after the server dropped the connection, a still valid access token
 * says nothing about the sign-in (signed out from another device); the refresh does.
 */
export async function freshAccessToken(bridge: AuthBridge, now = Date.now(), force = false): Promise<string> {
  const token = bridge.getAccessToken()
  const expiresAt = token ? tokenExpiresAt(token) : null
  if (force || !token || (expiresAt !== null && expiresAt - now < EXPIRY_SKEW_MS)) {
    await bridge.refreshTokens()
  }
  return bridge.getAccessToken() ?? ''
}
