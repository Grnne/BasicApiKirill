/* localStorage throws in private mode or when site data is blocked; an unhandled throw at startup
   would keep the app from loading, so all access goes through these wrappers. */

export function readLocal(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

export function writeLocal(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    // Storage unavailable: the value lives only for this tab.
  }
}

export function removeLocal(key: string): void {
  try {
    localStorage.removeItem(key)
  } catch {
    // Storage unavailable.
  }
}
