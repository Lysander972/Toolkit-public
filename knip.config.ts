import type { KnipConfig } from 'knip'

const config: KnipConfig = {
  workspaces: {
    '.': {
      entry: [
        'test/modules/**/*.{test,spec}.{ts,tsx}',
        'src/canvas_render/CanvasViewSchema.ts',
        'src/preview/storybook/*.ts',
        'src/preview/storybook/*.stories.tsx',
      ],
      project: [
        'tools/**/*.ts',
        'src/canvas_render/**/*.ts',
        'test/modules/**/*.{ts,tsx}',
        'src/preview/**/*.{ts,tsx}',
      ],
      vitest: { config: ['vitest.config.mjs'] },
      storybook: {
        config: ['src/preview/storybook/main.ts'],
        entry: ['src/preview/storybook/preview.ts', 'src/preview/storybook/*.stories.tsx'],
      },
    },
    'src/frontend': {
      entry: ['ui/main.tsx'],
      project: ['host_client/*.ts', 'plugin/*.ts', 'language/*.ts', 'ui/*.{ts,tsx}'],
    },
    'src/toolkit': {
      entry: [],
      project: [
        'host/*.ts',
        'files/*.ts',
        'entities/*.ts',
        'semantic/*.ts',
        'change_plan/*.ts',
        'views/*.ts',
        'conversion/**/*.ts',
        'lint/*.ts',
        'diagnostics/*.ts',
        'editing/*.ts',
        'preview/*.ts',
        'assets/*.ts',
        'invocations/*.ts',
        'language/*.ts',
        'asset_generation/*.ts',
        'asset_processing/*.ts',
        'asset_candidates/*.ts',
      ],
    },
    'src/toolkit/*': {
      entry: [],
      project: ['**/*.ts'],
    },
    'src/toolkit/pi_plugin': {
      entry: [],
      project: ['*.ts'],
    },
  },
  include: [
    'dependencies',
    'unlisted',
    'files',
    'exports',
    'types',
    'enumMembers',
    'namespaceMembers',
    'duplicates',
    'unresolved',
  ],
}

export default config
