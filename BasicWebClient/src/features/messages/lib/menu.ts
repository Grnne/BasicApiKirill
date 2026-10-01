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
