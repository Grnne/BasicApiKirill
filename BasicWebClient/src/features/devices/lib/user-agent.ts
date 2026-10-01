// A device named the way a person recognises it: "Chrome, Windows". Rough on purpose — the
// user agent is only a hint, and an unknown one still reads.

const BROWSERS: [RegExp, string][] = [
  [/Edg\//, 'Edge'],
  [/OPR\/|Opera/, 'Opera'],
  [/YaBrowser\//, 'Яндекс Браузер'],
  [/Firefox\//, 'Firefox'],
  [/Chrome\//, 'Chrome'],
  [/Safari\//, 'Safari'],
]

const SYSTEMS: [RegExp, string][] = [
  [/Android/, 'Android'],
  [/iPhone|iPad|iPod/, 'iOS'],
  [/Windows/, 'Windows'],
  [/Mac OS X|Macintosh/, 'macOS'],
  [/Linux/, 'Linux'],
]

const find = (list: [RegExp, string][], ua: string) => list.find(([re]) => re.test(ua))?.[1] ?? null

export function deviceName(userAgent: string | null): string {
  if (!userAgent) return 'Неизвестное устройство'
  const parts = [find(BROWSERS, userAgent), find(SYSTEMS, userAgent)].filter((p): p is string => p !== null)
  return parts.length > 0 ? parts.join(', ') : userAgent.slice(0, 60)
}
