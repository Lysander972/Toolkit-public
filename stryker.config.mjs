import { tsImport } from 'tsx/esm/api'
import { Option } from 'effect'

const { ArchitectureProject } = await tsImport(
  './tools/cli/ArchitectureProject.ts',
  import.meta.url,
)
const { TestMetrics } = await tsImport('./tools/cli/TestMetrics.ts', import.meta.url)
const { Git } = await tsImport('./tools/cli/Git.ts', import.meta.url)
const model = await ArchitectureProject.load(process.cwd(), Option.none())

export default {
  plugins: ['@stryker-mutator/vitest-runner', '@stryker-mutator/typescript-checker'],
  testRunner: 'vitest',
  checkers: ['typescript'],
  symlinkNodeModules: false,
  buildCommand: 'pnpm build:mutation:sandbox',
  tsconfigFile: 'tools/tsconfig.json',
  vitest: { configFile: 'vitest.config.mjs', related: false },
  mutate: TestMetrics.measuredFiles(model, process.cwd()),
  ignorePatterns: [
    'legacy',
    'docs',
    'requirements/.ub_cache',
    '_build/**',
    '!_build/frontend/**',
    '!_build/preview/**',
  ],
  incremental: true,
  dryRunTimeoutMinutes: 30,
  incrementalFile: Git.incrementalFile(process.cwd()),
  tempDirName: '_build/testing/stryker-sandbox',
  cleanTempDir: 'always',
  reporters: ['progress-append-only', 'clear-text', 'json'],
  jsonReporter: { fileName: '_build/testing/mutation/mutation.json' },
}
