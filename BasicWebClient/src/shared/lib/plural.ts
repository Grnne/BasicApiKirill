/** Russian plural form: plural(5, ['участник', 'участника', 'участников']) -> 'участников'. */
export function plural(n: number, forms: readonly [string, string, string]): string {
  const tens = Math.abs(n) % 100
  const units = tens % 10
  if (tens > 10 && tens < 20) return forms[2]
  if (units === 1) return forms[0]
  if (units >= 2 && units <= 4) return forms[1]
  return forms[2]
}
