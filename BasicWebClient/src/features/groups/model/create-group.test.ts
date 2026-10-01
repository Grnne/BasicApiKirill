import { describe, expect, it, vi } from 'vitest'

import type { AttachmentDto } from '@/shared/api/schema'
import { chat } from '@/testing/fixtures'
import { createGroupWithPhoto, type CreateGroupDeps } from './create-group'

const group = chat({ chatId: 'g1', type: 'group', title: 'Team' })

function deps(overrides: Partial<CreateGroupDeps> = {}): CreateGroupDeps {
  return {
    createGroup: vi.fn(async () => group),
    uploadPhoto: vi.fn(async () => ({ id: 'av-1' }) as AttachmentDto),
    setChatAvatar: vi.fn(async () => {}),
    ...overrides,
  }
}

describe('create a group', () => {
  it('without a photo: the title trimmed, the members as chosen', async () => {
    const d = deps()
    const result = await createGroupWithPhoto({ title: '  Team ', memberIds: ['bob'], photo: null }, d)

    expect(d.createGroup).toHaveBeenCalledWith('Team', ['bob'])
    expect(d.uploadPhoto).not.toHaveBeenCalled()
    expect(result).toEqual({ chat: group, avatarError: null })
  })

  it('the photo is set after the group exists', async () => {
    const d = deps()
    const result = await createGroupWithPhoto({ title: 'Team', memberIds: [], photo: new File(['x'], 'a.png') }, d)

    expect(d.setChatAvatar).toHaveBeenCalledWith('g1', 'av-1')
    expect(result.chat.avatarId).toBe('av-1')
  })

  it('a failed photo keeps the group and says what went wrong', async () => {
    const d = deps({ uploadPhoto: vi.fn(async () => Promise.reject(new Error('network'))) })
    const result = await createGroupWithPhoto({ title: 'Team', memberIds: [], photo: new File(['x'], 'a.png') }, d)

    expect(result.chat).toBe(group)
    expect(result.avatarError).toBeTruthy()
  })

  it('a failed group is the caller error', async () => {
    const d = deps({ createGroup: vi.fn(async () => Promise.reject(new Error('400'))) })
    await expect(createGroupWithPhoto({ title: 'Team', memberIds: [], photo: null }, d)).rejects.toThrow('400')
  })
})
