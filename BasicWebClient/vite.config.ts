import { fileURLToPath, URL } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig(({ mode, command }) => {
  // Читаем .env, чтобы узнать адрес бэкенда. Третий аргумент '' — забрать
  // все переменные, а не только с префиксом VITE_.
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.VITE_API_TARGET || 'http://localhost:5235'

  return {
    plugins: [vue()],

    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url)),
      },
    },

    server: {
      port: 5173,
      // Прокси делает бэкенд «своим» origin'ом: браузер видит только
      // localhost:5173. Значит — никакого CORS и никаких cross-site cookie.
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
        '/hubs': { target: apiTarget, changeOrigin: true, ws: true },
      },
    },

    build: {
      // Прод-сборка ложится в статику API. Отдаёт её Kestrel, nginx не нужен.
      outDir: '../BasicApi/wwwroot/client',
      emptyOutDir: true,
    },
    // В проде приложение живёт по /client/, в деве — в корне 5173-го порта.
    base: command === 'build' ? '/client/' : '/',
  }
})
