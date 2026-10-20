import assert from 'node:assert/strict'
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import pino from 'pino'
import { test } from 'vitest'
import { LanguageClient } from '../../../src/frontend/language/LanguageClient.ts'
import { Host } from '../../../src/toolkit/host/Host.ts'

/** @anchor frontend-language-contract */
export class LanguageClientTest {
  private constructor() {}

  static {
    test('标准语言客户端接收真实 Host 后明确失败，选项和工程保持原状', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/language-client-'))
      try {
        const settings: string = 'guid: language-client\nname: 语言接入\nmainMap: ""'
        await writeFile(join(workspaceFullPath, 'setting.json'), settings, 'utf8')
        const options: Parameters<typeof LanguageClient.connect>[1] = {
          documentSelector: [
            { scheme: 'file', language: 'typescript' },
            { scheme: 'file', language: 'html' },
            { scheme: 'file', language: 'bescript' },
            { scheme: 'file', language: 'mech-hjson' },
          ],
          diagnosticCollectionName: 'Toolkit',
        }
        const originalOptions: Parameters<typeof LanguageClient.connect>[1] =
          structuredClone(options)
        const modes: readonly Parameters<typeof Host.open>[1][] = ['files', 'memory']
        for (const mode of modes) {
          const host: Host = await Host.open(workspaceFullPath, mode, pino({ enabled: false }))
          try {
            await assert.rejects(
              LanguageClient.connect(host, options),
              (error: unknown): boolean =>
                error instanceof Error &&
                error.message === 'TODO: 标准只读 LSP 进程内连接、Host 查询适配与客户端释放',
            )
            assert.deepEqual(options, originalOptions)
            assert.deepEqual(await host.status(), {
              viewMode: mode,
              settings: { guid: 'language-client', name: '语言接入', mainMap: '' },
            })
          } finally {
            await host.close()
          }
        }
        assert.deepEqual(await readdir(workspaceFullPath), ['setting.json'])
        assert.equal(await readFile(join(workspaceFullPath, 'setting.json'), 'utf8'), settings)
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
