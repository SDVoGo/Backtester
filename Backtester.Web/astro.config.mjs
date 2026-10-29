import { defineConfig } from 'astro/config';
import solid from '@astrojs/solid-js';
import UnoCSS from 'unocss/astro';

// The backend (Backtester project, "http" launch profile) answers on 5014: the dev server proxies /api to it
export default defineConfig({
  integrations: [UnoCSS({ injectReset: true }), solid()],
  server: { port: 4321 },
  vite: {
    server: {
      proxy: { '/api': 'http://localhost:5014' },
    },
  },
});
