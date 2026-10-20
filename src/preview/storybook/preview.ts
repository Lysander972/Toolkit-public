import type { Preview } from '@storybook/react-vite'
import { sb } from 'storybook/test'
import '../../../res/frontend/styles/tk-tokens.css'
import '../../../res/frontend/styles/sidebar.css'
import '../../../res/preview/preview.css'

sb.mock(import('../../frontend/host_client/BrowserHostBridge.ts'))
const preview: Preview = { parameters: { backgrounds: { disable: true } } }
export default preview
