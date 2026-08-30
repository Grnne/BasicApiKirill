/**
 * Обёртка над localStorage.
 *
 * Прямой доступ к localStorage бросает исключение в приватном режиме и при
 * запрете сторонних данных в браузере. Одно необработанное исключение на
 * старте — и приложение не загрузится вообще, поэтому читаем и пишем только так.
 */

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
    // Хранилище недоступно — работаем в пределах вкладки, это не повод падать.
  }
}

export function removeLocal(key: string): void {
  try {
    localStorage.removeItem(key)
  } catch {
    // см. выше
  }
}
