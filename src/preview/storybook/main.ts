import type { StorybookConfig } from '@storybook/react-vite'
import type { InlineConfig } from 'vite'

const config: StorybookConfig = {
  framework: '@storybook/react-vite',
  stories: ['*.stories.tsx'],
  addons: [],
  core: { disableTelemetry: true, disableProjectJson: true },
  viteFinal: (config: InlineConfig): InlineConfig => ({
    ...config,
    base: '/MechToolkit/preview/',
    esbuild: { jsx: 'automatic' },
    build: { ...config.build, sourcemap: false },
  }),
}
export default config
