import { randomUUID } from 'node:crypto'
import { deflateSync } from 'node:zlib'
import { expect, type APIRequestContext } from '@playwright/test'

/** A user registered for one test. The password is random and never printed. */
export interface TestUser {
  userId: string
  username: string
  displayName: string
  email: string
  password: string
  token: string
}

let counter = 0

/** Unique within a run and across runs: tests share one database. */
export function uniqueName(prefix: string): string {
  counter += 1
  return `${prefix}_${Date.now().toString(36)}${counter}${randomUUID().slice(0, 4)}`
}

export async function registerUser(
  request: APIRequestContext,
  name: string,
  displayName?: string,
): Promise<TestUser> {
  const username = uniqueName(name).toLowerCase()
  const password = randomUUID()
  const email = `${username}@e2e.test`
  const shown = displayName ?? `${name[0]!.toUpperCase()}${name.slice(1)} ${username.slice(-4)}`
  const response = await request.post('/api/auth/register', {
    data: { username, email, password, displayName: shown },
  })
  expect(response.status(), await response.text()).toBe(201)
  const body = await response.json()
  return { userId: body.userId, username, displayName: shown, email, password, token: body.token }
}

/** A fresh access token: the one from registration may be rotated away by a test. */
export async function login(request: APIRequestContext, user: TestUser): Promise<string> {
  const response = await request.post('/api/auth/login', {
    data: { usernameOrEmail: user.username, password: user.password },
  })
  expect(response.status()).toBe(200)
  return (await response.json()).token
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** An API call on behalf of a user, for setting up what a test is not about. */
export async function api<T = any>(
  request: APIRequestContext,
  user: TestUser,
  method: Method,
  path: string,
  data?: unknown,
): Promise<T> {
  let response = await request.fetch(path, { method, data, headers: { Authorization: `Bearer ${user.token}` } })
  // Setup may outrun the per-user command limit: wait it out like a patient client.
  for (let i = 0; i < 3 && response.status() === 429; i++) {
    await new Promise((r) => setTimeout(r, (Number(response.headers()['retry-after']) || 5) * 1000))
    response = await request.fetch(path, { method, data, headers: { Authorization: `Bearer ${user.token}` } })
  }
  expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true)
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export async function privateChat(request: APIRequestContext, a: TestUser, b: TestUser): Promise<string> {
  const chat = await api(request, a, 'POST', `/api/chats/private/${b.userId}`)
  return chat.chatId ?? chat.id
}

export async function group(
  request: APIRequestContext,
  owner: TestUser,
  title: string,
  members: TestUser[],
): Promise<string> {
  const chat = await api(request, owner, 'POST', '/api/chats/groups', {
    title,
    memberIds: members.map((m) => m.userId),
  })
  return chat.chatId ?? chat.id
}

export async function send(
  request: APIRequestContext,
  user: TestUser,
  chatId: string,
  text: string,
): Promise<{ id: string; seq: number }> {
  return api(request, user, 'POST', `/api/chats/${chatId}/messages`, { text, clientMessageId: randomUUID() })
}

/* ── Files ── */

const crcTable = Array.from({ length: 256 }, (_, n) => {
  let c = n
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})

function crc32(data: Buffer): number {
  let c = 0xffffffff
  for (const byte of data) c = crcTable[(c ^ byte) & 0xff]! ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([length, body, crc])
}

/** A real PNG with a gradient, so thumbnails and previews have something to show. */
export function png(width = 320, height = 200, seed = 0): Buffer {
  const header = Buffer.alloc(13)
  header.writeUInt32BE(width, 0)
  header.writeUInt32BE(height, 4)
  header[8] = 8 // bit depth
  header[9] = 2 // RGB
  const rows: Buffer[] = []
  for (let y = 0; y < height; y++) {
    const row = Buffer.alloc(1 + width * 3)
    for (let x = 0; x < width; x++) {
      row[1 + x * 3] = (x + seed * 40) & 0xff
      row[2 + x * 3] = (y * 2) & 0xff
      row[3 + x * 3] = (128 + seed * 70) & 0xff
    }
    rows.push(row)
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(Buffer.concat(rows))),
    chunk('IEND', Buffer.alloc(0)),
  ])
}
