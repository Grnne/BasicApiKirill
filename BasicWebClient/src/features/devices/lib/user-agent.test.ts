import { expect, it } from 'vitest'

import { deviceName } from './user-agent'

it('names a device by browser and system; Chrome-based browsers by their own name', () => {
  const chromeWin = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36'
  const edge = chromeWin + ' Edg/140.0'
  const iphone = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1'
  const firefoxLinux = 'Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0'

  expect(deviceName(chromeWin)).toBe('Chrome, Windows')
  expect(deviceName(edge)).toBe('Edge, Windows')
  expect(deviceName(iphone)).toBe('Safari, iOS')
  expect(deviceName(firefoxLinux)).toBe('Firefox, Linux')
})

it('an unknown or missing user agent still reads', () => {
  expect(deviceName(null)).toBe('Неизвестное устройство')
  expect(deviceName('curl/8.5')).toBe('curl/8.5')
})
