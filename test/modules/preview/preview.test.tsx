// @vitest-environment happy-dom
import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import type { Root } from 'react-dom/client'
import { Option } from 'effect'
import { PreviewPage } from '../../../src/preview/site/PreviewPage.tsx'
import { SidebarPreview } from '../../../src/preview/scenes/SidebarPreview.tsx'
import { PreviewScenario } from '../../../src/preview/scenes/PreviewScenario.ts'
import { BrowserHostBridge } from '../../../src/frontend/host_client/BrowserHostBridge.ts'
import type { SidebarConnection } from '../../../src/frontend/host_client/BrowserHostBridge.ts'

/**
 * @anchor preview-site
 * @anchor preview-browser
 */
export class PreviewPageTest {
  private constructor() {}

  static {
    test('正式侧栏场景完成配置、筛选、展开与刷新', async (): Promise<void> => {
      vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
      const bridge: SidebarConnection = PreviewScenario.connect(PreviewScenario.initial)
      vi.spyOn(BrowserHostBridge, 'connect').mockReturnValue(bridge)
      const element: HTMLDivElement = document.createElement('div')
      document.body.appendChild(element)
      const root: Root = createRoot(element)
      try {
        await act(async (): Promise<void> => {
          await Promise.resolve()
          root.render(<SidebarPreview calls={PreviewScenario.calls} />)
        })
        assert.ok(element.textContent.includes('水果切切切 · files'))
        assert.equal(element.querySelectorAll('iframe').length, 0)
        assert.ok(element.textContent.includes('PNG 和 XML 产物待接入'))
        const configure: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(
            Array.from(element.querySelectorAll('button')).find(
              (button: HTMLButtonElement): boolean => button.textContent.includes('Codex'),
            ),
          ),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          configure.click()
        })
        assert.ok(element.textContent.includes('水果切切切 · Codex 模拟配置完成'))
        const filter: HTMLInputElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLInputElement>('input[type="search"]')),
        )
        const setValue: (value: string) => void = Option.getOrThrow(
          Option.fromNullishOr(
            Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.bind(filter),
          ),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          setValue('no-match')
          filter.dispatchEvent(new Event('input', { bubbles: true }))
        })
        assert.equal(element.querySelectorAll('[data-component="call-details"]').length, 0)
        await act(async (): Promise<void> => {
          await Promise.resolve()
          setValue('workspace_status')
          filter.dispatchEvent(new Event('input', { bubbles: true }))
        })
        assert.equal(element.querySelectorAll('[data-component="call-details"]').length, 2)
        const detail: HTMLDetailsElement = Option.getOrThrow(
          Option.fromNullishOr(
            element.querySelector<HTMLDetailsElement>('[data-component="call-details"]'),
          ),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          detail.open = true
          detail.dispatchEvent(new Event('toggle'))
        })
        assert.deepEqual(
          Array.from(detail.querySelectorAll('pre')).map((block: HTMLPreElement): unknown =>
            JSON.parse(block.textContent),
          ),
          [{}, { message: '演示错误：工程未打开' }],
        )
        const refresh: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(
            Array.from(element.querySelectorAll('button')).find(
              (button: HTMLButtonElement): boolean => button.textContent === '刷新',
            ),
          ),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          refresh.click()
        })
        assert.ok(element.textContent.includes('水果切切切 · files'))
      } finally {
        await act(async (): Promise<void> => {
          await Promise.resolve()
          root.unmount()
        })
        element.remove()
        vi.restoreAllMocks()
        vi.unstubAllGlobals()
      }
    })
    test('主页切换 Canvas 状态、成品轮播并提供架构入口', async (): Promise<void> => {
      vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true)
      const element: HTMLDivElement = document.createElement('div')
      document.body.appendChild(element)
      const root: Root = createRoot(element)
      try {
        await act(async (): Promise<void> => {
          await Promise.resolve()
          root.render(<PreviewPage />)
        })
        const menu: HTMLElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLElement>('#canvas-panel-menu')),
        )
        const settlement: HTMLElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLElement>('#canvas-panel-game-over')),
        )
        assert.equal(menu.hidden, false)
        assert.equal(settlement.hidden, true)
        assert.equal(menu.textContent, '菜单 Canvas：PNG 和 XML 产物待接入。')
        assert.equal(element.querySelectorAll('iframe').length, 0)
        const tab: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLButtonElement>('#canvas-tab-game-over')),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          tab.click()
        })
        assert.equal(tab.getAttribute('aria-selected'), 'true')
        assert.equal(menu.hidden, true)
        assert.equal(settlement.hidden, false)
        assert.equal(settlement.textContent, '结算 Canvas：PNG 和 XML 产物待接入。')
        const architecture: HTMLAnchorElement = Option.getOrThrow(
          Option.fromNullishOr(
            element.querySelector<HTMLAnchorElement>('a[href="/MechToolkit/architecture/"]'),
          ),
        )
        assert.equal(architecture.textContent, '查看工程结构 ↗')
        assert.ok(element.textContent.includes('运行画面一'))
        const next: HTMLButtonElement = Option.getOrThrow(
          Option.fromNullishOr(element.querySelector<HTMLButtonElement>('[aria-label="下一张"]')),
        )
        await act(async (): Promise<void> => {
          await Promise.resolve()
          next.click()
        })
        assert.ok(element.textContent.includes('运行画面二'))
      } finally {
        await act(async (): Promise<void> => {
          await Promise.resolve()
          root.unmount()
        })
        element.remove()
        vi.unstubAllGlobals()
      }
    })
  }
}
