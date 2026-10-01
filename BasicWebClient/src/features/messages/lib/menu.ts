interface Edges {
  top: number
  bottom: number
}

/**
 * Whether a message's menu opens below its button: above, the top of the list would clip it
 * under the chat header. `area` is the scrolling list.
 */
export function opensBelow(anchor: Edges, area: Edges, menuHeight: number): boolean {
  const above = anchor.top - area.top
  const below = area.bottom - anchor.bottom
  return above < menuHeight && below > above
}

interface Sides {
  left: number
  right: number
}

/**
 * Whether the menu opens towards the start (its right edge at the button) rather than towards
 * the end (its left edge at the button). `prefers`: the side it opens to when it fits, 'end' for
 * incoming messages on the left, 'start' for own ones on the right.
 */
export function opensToStart(anchor: Sides, area: Sides, menuWidth: number, prefers: 'start' | 'end'): boolean {
  const fitsEnd = anchor.left + menuWidth <= area.right
  const fitsStart = anchor.right - menuWidth >= area.left
  if (prefers === 'end') return !fitsEnd && fitsStart
  return fitsStart || !fitsEnd
}
