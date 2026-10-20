import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { z } from 'zod'

const manifest = z
  .object({ version: z.string() })
  .parse(JSON.parse(readFileSync('src/frontend/package.json', 'utf8')))
export default defineConfig({
  root: 'src/preview/site',
  base: '/MechToolkit/',
  plugins: [react()],
  define: {
    'PreviewPage.version': JSON.stringify(manifest.version),
    'PreviewPage.builtAt': JSON.stringify(new Date().toISOString()),
  },
  build: { outDir: '../../../_build/preview/site', emptyOutDir: true, sourcemap: false },
  preview: { host: '127.0.0.1', port: 4173, strictPort: true },
})
