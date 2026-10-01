// @vitest-environment node
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'

// The rules of README / CLAUDE.md, checked over the sources so a break shows up as a failing test.
const SRC = fileURLToPath(new URL('..', import.meta.url))
const LAYERS = ['app', 'pages', 'features', 'entities', 'shared']

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) return sources(path)
    return /\.(ts|vue)$/.test(name) && !name.endsWith('.test.ts') && !name.endsWith('.d.ts') ? [path] : []
  })
}

const files = sources(SRC).map((path) => ({
  parts: relative(SRC, path).split(sep),
  imports: [...readFileSync(path, 'utf8').matchAll(/from '@\/([^']+)'/g)].map((m) => m[1]!.split('/')),
}))

it('imports go only down: app → pages → features → entities → shared', () => {
  const upward = files.flatMap(({ parts, imports }) =>
    imports
      .filter((target) => LAYERS.indexOf(target[0]!) < LAYERS.indexOf(parts[0]!))
      .map((target) => `${parts.join('/')} → @/${target.join('/')}`),
  )
  expect(upward).toEqual([])
})

it("a feature does not import another feature's model or lib", () => {
  const crossing = files
    .filter(({ parts }) => parts[0] === 'features')
    .flatMap(({ parts, imports }) =>
      imports
        .filter((t) => t[0] === 'features' && t[1] !== parts[1] && (t[2] === 'model' || t[2] === 'lib'))
        .map((t) => `${parts.join('/')} → @/${t.join('/')}`),
    )
  expect(crossing).toEqual([])
})

it('no v-html anywhere: every piece of user text is a text node', () => {
  const withHtml = sources(SRC).filter((path) => /\sv-html=/.test(readFileSync(path, 'utf8')))
  expect(withHtml).toEqual([])
})
