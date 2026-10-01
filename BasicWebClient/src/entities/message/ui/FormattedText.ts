// Renders message text with its entities as VNodes: no v-html anywhere, every piece of text is a
// text node. Links pass safeUrl again even though the server checked them.

import { defineComponent, h, type PropType, type VNode, type VNodeChild } from 'vue'

import type { MessageEntityDto } from '@/shared/api/schema'
import { formatText, safeUrl, type FormattedNode } from '../lib/formatted'

const TAGS: Record<string, string> = {
  bold: 'strong',
  italic: 'em',
  underline: 'u',
  strikethrough: 's',
  code: 'code',
}

function reveal(event: Event): void {
  (event.currentTarget as HTMLElement).classList.add('revealed')
}

function revealByKey(event: KeyboardEvent): void {
  if (event.key !== 'Enter' && event.key !== ' ') return
  event.preventDefault()
  reveal(event)
}

function renderNode(node: FormattedNode, mentionClass: (userId: string | null | undefined) => string): VNodeChild {
  if (node.kind === 'text') return node.text

  const children = node.children.map((child) => renderNode(child, mentionClass))
  const { entity } = node
  // An own property only: "constructor" or "toString" would find Object's members.
  const tag = Object.prototype.hasOwnProperty.call(TAGS, entity.type) ? TAGS[entity.type] : undefined
  if (tag) return h(tag, children)

  switch (entity.type) {
    case 'pre':
      return h('pre', h('code', entity.language ? { 'data-language': entity.language } : {}, children))
    case 'spoiler':
      return h(
        'span',
        { class: 'spoiler', title: 'Показать', role: 'button', tabindex: 0, onClick: reveal, onKeydown: revealByKey },
        children,
      )
    case 'mention':
      return h('span', { class: mentionClass(entity.userId) }, children)
    case 'link': {
      const href = safeUrl(entity.url)
      if (!href) return h('span', children)
      // The address in the title: the text of a link may claim to be another site.
      return h('a', { href, title: href, target: '_blank', rel: 'noopener noreferrer nofollow' }, children)
    }
    default:
      // A type this client does not know yet: the text without decoration.
      return h('span', children)
  }
}

export default defineComponent({
  name: 'FormattedText',
  props: {
    text: { type: String, required: true },
    entities: { type: Array as PropType<readonly MessageEntityDto[]>, default: () => [] },
    /** Mentions of this user are highlighted. */
    meId: { type: String as PropType<string | null>, default: null },
  },
  setup(props) {
    const mentionClass = (userId: string | null | undefined) =>
      userId && userId === props.meId ? 'mention me' : 'mention'
    return (): VNode => h('span', { class: 'formatted' }, formatText(props.text, props.entities).map((n) => renderNode(n, mentionClass)))
  },
})
