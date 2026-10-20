import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import UnoCSS from 'unocss/vite'

export default defineConfig({
  root: 'src/frontend/ui',
  base: './',
  plugins: [react(), UnoCSS({ configFile: false })],
  build: {
    outDir: '../../../_build/frontend/ui',
    emptyOutDir: false,
    rollupOptions: { output: { entryFileNames: 'sidebar.js', assetFileNames: 'sidebar.[ext]' } },
  },
})
