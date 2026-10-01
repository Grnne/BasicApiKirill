import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import FormattedText from './FormattedText'

describe('FormattedText', () => {
  it('renders formatting as elements', () => {
    const wrapper = mount(FormattedText, {
      props: {
        text: 'bold code',
        entities: [
          { type: 'bold', offset: 0, length: 4 },
          { type: 'code', offset: 5, length: 4 },
        ],
      },
    })
    expect(wrapper.html()).toContain('<strong>bold</strong> <code>code</code>')
  })

  it('markup in the text stays text', () => {
    const wrapper = mount(FormattedText, { props: { text: '<img src=x onerror=alert(1)>' } })
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.text()).toBe('<img src=x onerror=alert(1)>')
  })

  it('a javascript: link from the server is not a link', () => {
    const wrapper = mount(FormattedText, {
      props: { text: 'click', entities: [{ type: 'link', offset: 0, length: 5, url: 'javascript:alert(1)' }] },
    })
    expect(wrapper.find('a').exists()).toBe(false)
    expect(wrapper.text()).toBe('click')
  })

  it('a link opens in a new tab without opener and shows its address', () => {
    const wrapper = mount(FormattedText, {
      props: { text: 'docs', entities: [{ type: 'link', offset: 0, length: 4, url: 'https://example.com/docs' }] },
    })
    const a = wrapper.get('a')
    expect(a.attributes('href')).toBe('https://example.com/docs')
    expect(a.attributes('title')).toBe('https://example.com/docs')
    expect(a.attributes('target')).toBe('_blank')
    expect(a.attributes('rel')).toContain('noopener')
  })

  it('highlights a mention of me', () => {
    const wrapper = mount(FormattedText, {
      props: {
        text: '@me and @bob',
        meId: 'me-1',
        entities: [
          { type: 'mention', offset: 0, length: 3, userId: 'me-1' },
          { type: 'mention', offset: 8, length: 4, userId: 'bob-1' },
        ],
      },
    })
    expect(wrapper.findAll('.mention')).toHaveLength(2)
    expect(wrapper.findAll('.mention.me')).toHaveLength(1)
  })

  it('a spoiler can be revealed from the keyboard too', async () => {
    const wrapper = mount(FormattedText, {
      props: { text: 'secret', entities: [{ type: 'spoiler', offset: 0, length: 6 }] },
    })
    const spoiler = wrapper.get('.spoiler')
    expect(spoiler.attributes('tabindex')).toBe('0')
    expect(spoiler.attributes('role')).toBe('button')

    await spoiler.trigger('keydown', { key: 'Enter' })

    expect(spoiler.classes()).toContain('revealed')
  })

  it('a spoiler is revealed by a click', async () => {
    const wrapper = mount(FormattedText, {
      props: { text: 'secret', entities: [{ type: 'spoiler', offset: 0, length: 6 }] },
    })
    await wrapper.get('.spoiler').trigger('click')
    expect(wrapper.get('.spoiler').classes()).toContain('revealed')
  })
})
