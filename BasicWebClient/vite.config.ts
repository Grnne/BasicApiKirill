/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * The <meta> CSP guards the dev server only. The built client is served by the API, whose CSP
 * header (Program.ContentSecurityPolicy) is the same policy plus the file storage origin; a meta
 * policy would be enforced on top of it and block files from storage on another port.
 */
export function devOnlyCsp(): Plugin {
  return {
    name: 'dev-only-csp',
    apply: 'build',
    transformIndexHtml: (html) =>
      html.replace(/<meta\s+http-equiv="Content-Security-Policy"[\s\S]*?\/>\s*/, ''),
  }
}

export default defineConfig(({ mode, command }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.VITE_API_TARGET || 'http://localhost:5235'
  const mediaTarget = env.VITE_MEDIA_TARGET || 'http://localhost:8333'

  return {
    plugins: [vue(), devOnlyCsp()],

    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },

    server: {
      port: 5173,
      // The proxy keeps the API same-origin: no CORS and no cross-site cookies.
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
        // Files by signed links, same origin as in prod behind Caddy. The Host header must stay
        // as signed (Storage:PublicUrl = this server), so no changeOrigin here.
        '/media': { target: mediaTarget, changeOrigin: false },
      },
    },

    build: {
      // The production build is served by the API as static files.
      outDir: '../BasicApi/wwwroot/client',
      emptyOutDir: true,
    },
    base: command === 'build' ? '/client/' : '/',

    test: {
      environment: 'happy-dom',
      include: ['src/**/*.test.ts'],
      restoreMocks: true,
    },
  }
})
