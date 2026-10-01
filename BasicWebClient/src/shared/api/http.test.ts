import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { http, setAuthBridge } from './http'
import { ApiError, NetworkError } from './problem'

const json = (status: number, body: unknown = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

let token = 'old'
const refreshTokens = vi.fn(async () => {
  token = 'new'
  return true
})
const fetchMock = vi.fn<typeof fetch>()

beforeEach(() => {
  token = 'old'
  refreshTokens.mockClear()
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
  setAuthBridge({ getAccessToken: () => token, refreshTokens })
})
afterEach(() => vi.unstubAllGlobals())

const authHeader = (call: number) => (fetchMock.mock.calls[call]![1]!.headers as Record<string, string>)['Authorization']

describe('http', () => {
  it('a 401 refreshes the tokens once and repeats the request with the new one', async () => {
    fetchMock.mockResolvedValueOnce(json(401)).mockResolvedValueOnce(json(200, { ok: 1 }))

    expect(await http.get('/api/x')).toEqual({ ok: 1 })

    expect(refreshTokens).toHaveBeenCalledTimes(1)
    expect(authHeader(0)).toBe('Bearer old')
    expect(authHeader(1)).toBe('Bearer new')
  })

  it('a second 401 is the answer: no loop of refreshes', async () => {
    fetchMock.mockResolvedValue(json(401, { errorCode: 'SESSION_REVOKED' }))

    await expect(http.get('/api/x')).rejects.toMatchObject({ status: 401, code: 'SESSION_REVOKED' })

    expect(refreshTokens).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('a refresh that fails gives the 401 back without repeating the request', async () => {
    refreshTokens.mockResolvedValueOnce(false)
    fetchMock.mockResolvedValueOnce(json(401))

    await expect(http.get('/api/x')).rejects.toBeInstanceOf(ApiError)

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('auth: false sends no token and never refreshes (a wrong password is not an expired token)', async () => {
    fetchMock.mockResolvedValueOnce(json(401, { errorCode: 'INVALID_CREDENTIALS' }))

    await expect(http.post('/api/auth/login', {}, { auth: false })).rejects.toBeInstanceOf(ApiError)

    expect(authHeader(0)).toBeUndefined()
    expect(refreshTokens).not.toHaveBeenCalled()
  })

  it('a dropped connection is a NetworkError; an abort stays an abort', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('Failed to fetch'))
    await expect(http.get('/api/x')).rejects.toBeInstanceOf(NetworkError)

    fetchMock.mockRejectedValueOnce(new DOMException('aborted', 'AbortError'))
    await expect(http.get('/api/x')).rejects.toMatchObject({ name: 'AbortError' })
  })

  it('a request can only go to this site: the path stays relative', async () => {
    fetchMock.mockResolvedValueOnce(json(200))

    await http.get('/api/search', { query: { q: 'a b', page: 2, empty: '' } })

    expect(fetchMock.mock.calls[0]![0]).toBe('/api/search?q=a+b&page=2')
  })
})
