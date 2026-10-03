// Notification settings of this device: kept in the browser, the server knows nothing of them.
// Storage may be unavailable (private mode, blocked site data): then the defaults hold.

const TURNED_OFF = 'notifications.off'
const SOUND = 'notifications.sound'

function read(key: string): boolean {
  try {
    return localStorage.getItem(key) === '1'
  } catch {
    return false
  }
}

function write(key: string, on: boolean): void {
  try {
    if (on) localStorage.setItem(key, '1')
    else localStorage.removeItem(key)
  } catch {
    // Kept for this page only.
  }
}

/** The user turned notifications off here; they are on by default. */
export const turnedOff = (): boolean => read(TURNED_OFF)
export const setTurnedOff = (off: boolean): void => write(TURNED_OFF, off)

/** A sound for new messages; off by default. */
export const sound = (): boolean => read(SOUND)
export const setSound = (on: boolean): void => write(SOUND, on)
