import { defineConfig, presetWind3 } from 'unocss';

export default defineConfig({
  presets: [presetWind3()],
  theme: {
    colors: {
      bg: '#0f1115',
      panel: '#171a21',
      line: '#262b36',
      muted: '#8a93a6',
      up: '#26a69a',
      down: '#ef5350',
      accent: '#5b8def',
    },
  },
  shortcuts: {
    panel: 'bg-panel border border-line rounded-lg',
    label: 'text-xs uppercase tracking-wide text-muted',
    field: 'bg-bg border border-line rounded px-2 py-1 text-sm outline-none focus:border-accent',
    btn: 'px-3 py-1.5 rounded text-sm font-medium bg-accent text-white hover:brightness-110 disabled:opacity-50 cursor-pointer',
  },
});
