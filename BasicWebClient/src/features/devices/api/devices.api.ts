import { http } from '@/shared/api/http'
import type { DeviceListDto } from '@/shared/api/schema'

/** Every sign-in still open; `isCurrent` marks this one. */
export function getDevices(): Promise<DeviceListDto> {
  return http.get<DeviceListDto>('/api/devices')
}

/** Its refresh token stops working and its hub connections close at once. */
export function signOutDevice(deviceId: string): Promise<void> {
  return http.delete<void>(`/api/devices/${deviceId}`)
}
