// The browser side of push: the service worker and the push subscription. Kept apart so the store
// can be tested without a browser that has a push service.

/** The worker sits at the client's root, so its scope is the whole client. */
const WORKER_URL = `${import.meta.env.BASE_URL}sw.js`

export function isSupported(): boolean {
  return typeof window !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
}

export function permission(): NotificationPermission {
  return isSupported() ? Notification.permission : 'denied'
}

export function requestPermission(): Promise<NotificationPermission> {
  return Notification.requestPermission()
}

/** The subscription this browser holds for the client, if any; registers nothing. */
export async function currentSubscription(): Promise<PushSubscription | null> {
  if (!isSupported()) return null
  const registration = await navigator.serviceWorker.getRegistration(import.meta.env.BASE_URL)
  return registration ? registration.pushManager.getSubscription() : null
}

export async function subscribe(vapidPublicKey: string): Promise<PushSubscription> {
  await navigator.serviceWorker.register(WORKER_URL, { scope: import.meta.env.BASE_URL })
  const registration = await navigator.serviceWorker.ready
  return registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: base64UrlToBytes(vapidPublicKey),
  })
}

/** VAPID keys come base64url-encoded; pushManager wants the bytes. */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const base64 = (value + '='.repeat((4 - (value.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}
