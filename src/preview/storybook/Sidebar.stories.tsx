import type { Meta, StoryObj } from '@storybook/react-vite'
import { mocked } from 'storybook/test'
import { BrowserHostBridge } from '../../frontend/host_client/BrowserHostBridge.ts'
import type { SidebarConnection } from '../../frontend/host_client/BrowserHostBridge.ts'
import { PreviewScenario } from '../scenes/PreviewScenario.ts'
import { SidebarPreview } from '../scenes/SidebarPreview.tsx'

const meta: Meta<typeof SidebarPreview> = {
  title: 'Plugin/Sidebar',
  component: SidebarPreview,
  parameters: { layout: 'fullscreen' },
  beforeEach: (): void => {
    mocked(BrowserHostBridge.connect).mockImplementation((): SidebarConnection =>
      PreviewScenario.connect(PreviewScenario.initial),
    )
  },
  args: { calls: PreviewScenario.calls },
}
export default meta
export const Overview: StoryObj<typeof SidebarPreview> = {}
export const Waiting: StoryObj<typeof SidebarPreview> = {
  beforeEach: (): void => {
    mocked(BrowserHostBridge.connect).mockImplementation((): SidebarConnection =>
      PreviewScenario.connect({
        ...PreviewScenario.initial,
        status: '请打开包含 setting.json 的 CreatAI 工程',
        endpoint: '',
        targets: [],
        tools: [],
        canvas: '未接入',
        externalCalls: '未接入',
      }),
    )
  },
  args: { calls: [] },
}
export const Error: StoryObj<typeof SidebarPreview> = {
  beforeEach: (): void => {
    mocked(BrowserHostBridge.connect).mockImplementation((): SidebarConnection =>
      PreviewScenario.connect({
        ...PreviewScenario.initial,
        error: '模拟配置错误：Agent CLI 执行失败，请查看运行日志。',
      }),
    )
  },
}
