// @vitest-environment happy-dom
import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import type { Root } from 'react-dom/client'
import { Option } from 'effect'
import { Sidebar } from '../../../src/frontend/ui/Sidebar.tsx'
import type { SidebarProps } from '../../../src/frontend/ui/Sidebar.tsx'
import { BrowserHostBridge } from '../../../src/frontend/host_client/BrowserHostBridge.ts'

/** @anchor frontend-readonly-surface */
interface SidebarInteraction {
  readonly action: string
  readonly target: Parameters<SidebarProps['send']>[1]
}

/** @anchor frontend-readonly-surface */
export class SidebarTest {
  private constructor() {}

  static {
    test('侧栏接收状态与配置交互，日志展示输入支持展开结果和改写文本', async (): Promise<void> => {
      vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
      vi.stubGlobal('acquireVsCodeApi', (): { postMessage: (message: unknown) => void } => ({
        postMessage: (message: unknown): void => {
          assert.ok(typeof message === 'object')
        },
      }))
      const element: HTMLDivElement = document.createElement('div')
      document.body.appendChild(element)
      const root: Root = createRoot(element)
      const interactions: SidebarInteraction[] = []
      try {
        await act(async (): Promise<void> => {
          root.render(
            <Sidebar
              bridge={BrowserHostBridge.connect()}
              calls={[
                {
                  id: 'one',
                  durationMs: 2,
                  tool: 'workspace_status',
                  arguments: { query: 'project' },
                  status: 'success',
                  result: Option.some('result'),
                  rewrittenText: Option.some('改写后的文本'),
                },
              ]}
              send={(action: string, target: Parameters<SidebarProps['send']>[1]): void => {
                interactions.push({ action, target })
              }}
            />,
          )
          await Promise.resolve()
        })
        assert.deepEqual(interactions, [{ action: 'ready', target: Option.none() }])
        await act(async (): Promise<void> => {
          window.dispatchEvent(
            new MessageEvent('message', {
              data: {
                kind: 'state',
                payload: {
                  status: '示例工程',
                  endpoint: 'http://localhost:5050/mcp',
                  version: '0.1.0',
                  error: '',
                  tools: ['workspace_status'],
                  targets: [{ id: 'codex', title: 'Codex', level: 'basic' }],
                  language: '未接入',
                  canvas: '未接入',
                  externalCalls: '未接入',
                },
              },
            }),
          )
          await Promise.resolve()
        })
        assert.ok(element.textContent.includes('示例工程'))
        assert.ok(element.textContent.includes('http://localhost:5050/mcp'))
        assert.ok(element.textContent.includes('workspace_status'))
        assert.equal(element.querySelectorAll('[role="alert"]').length, 0)
        assert.equal(element.querySelectorAll('main').length, 1)
        const configure: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLButtonElement>('.tk-targets button')),
        )
        await act(async (): Promise<void> => {
          configure.click()
          await Promise.resolve()
        })
        assert.deepEqual(interactions[1], { action: 'configure', target: Option.some('codex') })
        const actions: Readonly<Record<string, string>> = {
          刷新: 'status',
          扩展设置: 'settings',
          检查版本: 'version',
          开发指南: 'documentation',
          查看运行日志: 'logs',
        }
        for (const [label, action] of Object.entries(actions)) {
          const button: HTMLButtonElement = Option.getOrThrow(
            Option.fromNullishOr(
              Array.from(element.querySelectorAll('button')).find(
                (entry: HTMLButtonElement): boolean => entry.textContent === label,
              ),
            ),
          )
          await act(async (): Promise<void> => {
            button.click()
            await Promise.resolve()
          })
          assert.deepEqual(interactions.at(-1), { action, target: Option.none() })
        }
        const architecture: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLButtonElement>('[aria-label="架构图"]')),
        )
        await act(async (): Promise<void> => {
          architecture.click()
          await Promise.resolve()
        })
        assert.deepEqual(interactions.at(-1), { action: 'architecture', target: Option.none() })
        assert.equal(element.querySelectorAll('pre').length, 0)
        const details: HTMLDetailsElement = Option.getOrThrow(
          Option.fromNullishOr(
            element.querySelector<HTMLDetailsElement>('[data-component="call-details"]'),
          ),
        )
        await act(async (): Promise<void> => {
          details.open = true
          details.dispatchEvent(new Event('toggle'))
          await Promise.resolve()
        })
        const detail: HTMLPreElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector('pre')),
        )
        assert.ok(detail.textContent.includes('project'))
        assert.ok(element.textContent.includes('改写后的文本'))
        await act(async (): Promise<void> => {
          window.dispatchEvent(
            new MessageEvent('message', {
              data: {
                kind: 'state',
                payload: {
                  status: '请打开包含 setting.json 的 CreatAI 工程',
                  endpoint: '',
                  version: '0.1.0',
                  error: '配置失败，请查看运行日志',
                  tools: [],
                  targets: [],
                  language: '未接入',
                  canvas: '未接入',
                  externalCalls: '未接入',
                },
              },
            }),
          )
          await Promise.resolve()
        })
        const alert: Element = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector('[role="alert"]')),
        )
        assert.equal(alert.textContent, '配置失败，请查看运行日志')
        assert.ok(element.textContent.includes('打开 CreatAI 工程后可配置 Agent。'))
        assert.ok(element.textContent.includes('当前工程暂无可用工具。'))
        assert.equal(element.querySelectorAll('.tk-targets button').length, 0)
        assert.equal(element.querySelectorAll('.tk-endpoint').length, 0)
      } finally {
        await act(async (): Promise<void> => {
          root.unmount()
          await Promise.resolve()
        })
        element.remove()
        vi.unstubAllGlobals()
      }
    })
  }
}
