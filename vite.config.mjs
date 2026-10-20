import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import UnoCSS from 'unocss/vite'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  root: 'src/frontend/ui',
  cacheDir: fileURLToPath(new URL('_build/vite/frontend', import.meta.url)),
  base: './',
  plugins: [react(), UnoCSS({ configFile: false })],
  build: {
    outDir: '../../../_build/frontend/ui',
    emptyOutDir: false,
    rollupOptions: { output: { entryFileNames: 'sidebar.js', assetFileNames: 'sidebar.[ext]' } },
  },
})
