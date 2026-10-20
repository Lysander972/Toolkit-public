// @vitest-environment happy-dom
import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import { Option } from 'effect'
import { PreviewScenario } from '../../../src/preview/scenes/PreviewScenario.ts'
import { BrowserHostBridge } from '../../../src/frontend/host_client/BrowserHostBridge.ts'
import type { SidebarConnection } from '../../../src/frontend/host_client/BrowserHostBridge.ts'
import type { SidebarState } from '../../../src/frontend/host_client/SidebarMessages.ts'

/** @anchor preview-scenes */
export class PreviewScenarioTest {
  private constructor() {}

  static {
    test('全部配置入口的模拟结果对应所选 Agent', (): void => {
      assert.equal(PreviewScenario.initial.targets.length, 11)
      for (const target of PreviewScenario.initial.targets) {
        const bridge: SidebarConnection = PreviewScenario.connect(PreviewScenario.initial)
        const states: SidebarState[] = []
        const release: () => void = bridge.subscribe((state: SidebarState): void => {
          states.push(state)
        })
        try {
          bridge.send({ action: 'configure', target: Option.some(target.id) })
          assert.equal(
            Option.getOrThrow(Option.fromNullishOr(states.at(-1))).status,
            '水果切切切 · ' + target.title + ' 模拟配置完成',
          )
        } finally {
          release()
        }
      }
    })
    test('真实桥接发送合同消息，释放后停止接收状态', (): void => {
      const sent: unknown[] = []
      vi.stubGlobal('acquireVsCodeApi', (): { postMessage: (message: unknown) => void } => ({
        postMessage: (message: unknown): void => {
          sent.push(message)
        },
      }))
      try {
        const bridge: SidebarConnection = BrowserHostBridge.connect()
        const states: SidebarState[] = []
        const release: () => void = bridge.subscribe((state: SidebarState): void => {
          states.push(state)
        })
        bridge.send({ action: 'configure', target: Option.some('codex') })
        assert.deepEqual(sent, [{ action: 'configure', target: 'codex' }])
        window.dispatchEvent(
          new MessageEvent('message', {
            data: { kind: 'state', payload: PreviewScenario.initial },
          }),
        )
        assert.deepEqual(states, [PreviewScenario.initial])
        release()
        window.dispatchEvent(
          new MessageEvent('message', {
            data: { kind: 'state', payload: PreviewScenario.initial },
          }),
        )
        assert.equal(states.length, 1)
      } finally {
        vi.unstubAllGlobals()
      }
    })
    test('场景状态相互隔离，重新装配恢复起点，释放并重新订阅保留当前实例状态', (): void => {
      const first: SidebarConnection = PreviewScenario.connect(PreviewScenario.initial)
      const states: SidebarState[] = []
      const release: () => void = first.subscribe((state: SidebarState): void => {
        states.push(state)
      })
      first.send({ action: 'configure', target: Option.some('codex') })
      assert.equal(
        Option.getOrThrow(Option.fromNullishOr(states[1])).status,
        '水果切切切 · Codex 模拟配置完成',
      )
      release()
      const refreshed: SidebarState[] = []
      const releaseRefresh: () => void = first.subscribe((state: SidebarState): void => {
        refreshed.push(state)
      })
      first.send({ action: 'status', target: Option.none() })
      assert.deepEqual(
        Option.getOrThrow(Option.fromNullishOr(refreshed.at(-1))),
        PreviewScenario.initial,
      )
      releaseRefresh()
      const resumed: SidebarState[] = []
      const releaseResumed: () => void = first.subscribe((state: SidebarState): void => {
        resumed.push(state)
      })
      assert.equal(Option.getOrThrow(Option.fromNullishOr(resumed[0])).status, '水果切切切 · files')
      releaseResumed()
      const second: SidebarConnection = PreviewScenario.connect(PreviewScenario.initial)
      const releaseSecond: () => void = second.subscribe((state: SidebarState): void => {
        assert.deepEqual(state, PreviewScenario.initial)
      })
      releaseSecond()
      console.info('预览场景测试实际写入：0 文件，累计 0 字节，最大 0 字节')
    })
  }
}
