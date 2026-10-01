/**
 * Where to go after signing in. The target comes from the URL, so only same-origin paths pass:
 * "//evil.com" and "https://evil.com" are absolute URLs and would make an open redirect.
 */
export function safeRedirect(target: unknown): string {
  if (typeof target !== 'string') return '/chat'
  if (!target.startsWith('/') || target.startsWith('//')) return '/chat'
  return target
}
