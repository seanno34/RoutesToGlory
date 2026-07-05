import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

const APP_PATH = '/rtg';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiBase = env.VITE_API_BASE ?? `${APP_PATH}/api`;

  return {
    base: `${APP_PATH}/`,
    plugins: [
      react(),
      VitePWA({
        registerType: 'autoUpdate',
        manifest: {
          name: 'Routes to Glory',
          short_name: 'RtG',
          description: 'Build trade empires in the real world',
          theme_color: '#0f172a',
          background_color: '#0f172a',
          display: 'standalone',
          start_url: `${APP_PATH}/`,
          scope: `${APP_PATH}/`,
          icons: [
            {
              src: `${APP_PATH}/icon-192.png`,
              sizes: '192x192',
              type: 'image/png',
            },
            {
              src: `${APP_PATH}/icon-512.png`,
              sizes: '512x512',
              type: 'image/png',
            },
          ],
        },
      }),
    ],
    define: {
      __API_BASE__: JSON.stringify(apiBase),
    },
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: 'http://localhost:3001',
          changeOrigin: true,
        },
        [`${APP_PATH}/api`]: {
          target: 'http://localhost:3001',
          changeOrigin: true,
          rewrite: (path) => path.replace(`${APP_PATH}/api`, '/api'),
        },
      },
    },
  };
});
