/**
 * The server sends UTC with a `Z` suffix. A safety net for zone-less strings: `new Date()` would
 * read them as local time, so they are treated as UTC.
 */
export function parseApiDate(value: string): Date {
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value)
  return new Date(hasZone ? value : `${value}Z`)
}

export function formatTime(value: string): string {
  return parseApiDate(value).toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function formatDay(value: string): string {
  return parseApiDate(value).toLocaleDateString()
}
