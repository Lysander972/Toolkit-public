import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { tsImport } from 'tsx/esm/api'
import { Option } from 'effect'

const { ArchitectureProject } = await tsImport(
  './tools/cli/ArchitectureProject.ts',
  import.meta.url,
)
const { TestMetrics } = await tsImport('./tools/cli/TestMetrics.ts', import.meta.url)
const model = await ArchitectureProject.load(process.cwd(), Option.none())

export default defineConfig({
  cacheDir: '_build/vitest',
  plugins: [react()],
  test: {
    reporters: ['default'],
    server: { deps: { inline: ['@actions/workflow-parser'] } },
    include: ['test/modules/**/*.test.{ts,tsx}', 'test/modules/tools/*.ts'],
    pool: 'forks',
    maxWorkers: '50%',
    testTimeout: 300_000,
    isolate: true,
    restoreMocks: true,
    passWithNoTests: false,
    coverage: {
      provider: 'v8',
      include: TestMetrics.measuredFiles(model, process.cwd()),
      reportsDirectory: '_build/testing/coverage',
      reporter: ['text', 'json-summary'],
    },
  },
})
