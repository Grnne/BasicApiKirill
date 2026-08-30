/**
 * Разбор дат, пришедших от API.
 *
 * Зачем отдельная функция: колонки в БД — timestamp БЕЗ часового пояса
 * (FluentMigrator `AsDateTime()`), поэтому .NET отдаёт часть дат строкой без
 * суффикса `Z`: "2026-08-31T12:00:00". `new Date(...)` от такой строки считает
 * время ЛОКАЛЬНЫМ — и сообщения уезжают на несколько часов.
 * Сервер везде пишет UTC, так что дописываем `Z` сами, если его нет.
 */
export function parseApiDate(value: string): Date {
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(value)
  return new Date(hasZone ? value : `${value}Z`)
}

/** "14:05" — время сообщения в ленте. */
export function formatTime(value: string): string {
  return parseApiDate(value).toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  })
}

/** "31.08.2026" — разделитель между днями. */
export function formatDay(value: string): string {
  return parseApiDate(value).toLocaleDateString()
}
