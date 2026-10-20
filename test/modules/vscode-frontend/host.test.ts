import { test } from 'vitest'
import assert from 'node:assert/strict'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { resolve, join } from 'node:path'
import { FrontendProcessModule } from '../../../src/frontend/plugin/FrontendProcess.ts'
import { VsCodeHost } from '../../../src/frontend/plugin/VsCodeHost.ts'
import { FrontendLog } from '../../../src/frontend/plugin/FrontendLog.ts'
import {
  JSONRPCResultResponseSchema,
  CallToolResultSchema,
  ListToolsResultSchema,
} from '@modelcontextprotocol/sdk/types.js'
import type {
  CallToolResult,
  JSONRPCResultResponse,
  ListToolsResult,
  Tool,
} from '@modelcontextprotocol/sdk/types.js'
import type { Logger } from 'pino'

/**
 * @anchor vscode-host-bootstrap
 * @anchor mcp-http-lifecycle
 * @anchor mcp-workspace-query
 * @anchor mcp-read-tool-migration
 * @anchor mcp-schema-result-convergence
 * @anchor pi-read-closure-reservation
 * @anchor deployment-lifecycle-contract
 */
export class VsCodeHostTest {
  private constructor() {}

  /** 本文件不断言日志内容，写入丢弃型 destination。 */
  private static testLogger(): Logger {
    return new FrontendLog({ append: (): void => {} }).logger()
  }

  static {
    test('工程设置缺失时启动失败并释放前端容器', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/vscode-missing-'))
      try {
        const openBefore: number = FrontendProcessModule.openCount()
        await assert.rejects((): Promise<VsCodeHost> =>
          VsCodeHost.open(workspaceFullPath, 0, VsCodeHostTest.testLogger()),
        )
        assert.equal(FrontendProcessModule.openCount(), openBefore)
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })

    test('VS Code Host 以文件模式对外提供真实 HTTP MCP 工程查询', async (): Promise<void> => {
      await mkdir('_build/tests', { recursive: true })
      const workspaceFullPath: string = await mkdtemp(resolve('_build/tests/vscode-'))
      try {
        await writeFile(
          join(workspaceFullPath, 'setting.json'),
          'guid: vscode\nname: 工程\nmainMap: ""',
          'utf8',
        )
        const session: VsCodeHost = await VsCodeHost.open(
          workspaceFullPath,
          0,
          VsCodeHostTest.testLogger(),
        )
        try {
          const headers: Readonly<Record<string, string>> = {
            'Content-Type': 'application/json',
            Accept: 'application/json, text/event-stream',
          }
          const initialization: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 0,
              method: 'initialize',
              params: {
                protocolVersion: '2025-03-26',
                capabilities: {},
                clientInfo: { name: 'test', version: '1.0.0' },
              },
            }),
          })
          assert.equal(initialization.status, 200)
          const initialized: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await initialization.json(),
          )
          assert.equal(initialized.id, 0)
          const toolsResponse: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list', params: {} }),
          })
          assert.equal(toolsResponse.status, 200)
          const toolsMessage: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await toolsResponse.json(),
          )
          const tools: ListToolsResult = ListToolsResultSchema.parse(toolsMessage.result)
          assert.deepEqual(
            tools.tools.map((tool: Tool): string => tool.name),
            ['workspace_status'],
          )
          assert.deepEqual(tools.tools[0]?.inputSchema, {
            $schema: 'http://json-schema.org/draft-07/schema#',
            type: 'object',
            properties: {},
          })
          assert.deepEqual(tools.tools[0].outputSchema, {
            $schema: 'http://json-schema.org/draft-07/schema#',
            type: 'object',
            properties: {
              viewMode: { type: 'string', enum: ['files', 'memory'] },
              settings: {
                type: 'object',
                properties: {
                  guid: { type: 'string' },
                  name: { type: 'string' },
                  mainMap: { type: 'string' },
                },
                required: ['guid', 'name', 'mainMap'],
                additionalProperties: false,
              },
            },
            required: ['viewMode', 'settings'],
            additionalProperties: false,
          })
          const legacyToolResponse: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 2,
              method: 'tools/call',
              params: { name: 'read_lint', arguments: {} },
            }),
          })
          const legacyToolMessage: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await legacyToolResponse.json(),
          )
          const legacyToolResult: CallToolResult = CallToolResultSchema.parse(
            legacyToolMessage.result,
          )
          assert.equal(legacyToolResponse.status, 200)
          assert.equal(legacyToolResult.isError, true)
          const closureToolResponse: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 3,
              method: 'tools/call',
              params: { name: 'read_closure', arguments: { viewRelativePath: 'map_/1.ts' } },
            }),
          })
          const closureToolMessage: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await closureToolResponse.json(),
          )
          const closureToolResult: CallToolResult = CallToolResultSchema.parse(
            closureToolMessage.result,
          )
          assert.equal(closureToolResponse.status, 200)
          assert.equal(closureToolResult.isError, true)
          const response: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 3,
              method: 'tools/call',
              params: { name: 'workspace_status', arguments: {} },
            }),
          })
          assert.equal(response.status, 200)
          const message: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await response.json(),
          )
          const result: CallToolResult = CallToolResultSchema.parse(message.result)
          assert.deepEqual(result.content, [
            {
              type: 'text',
              text: JSON.stringify({
                viewMode: 'files',
                settings: { guid: 'vscode', name: '工程', mainMap: '' },
              }),
            },
          ])
          assert.deepEqual(result.structuredContent, {
            viewMode: 'files',
            settings: { guid: 'vscode', name: '工程', mainMap: '' },
          })
          assert.equal((await session.host.status()).viewMode, 'files')
          await writeFile(
            join(workspaceFullPath, 'setting.json'),
            'guid: changed\nname: 新工程\nmainMap: map-guid',
            'utf8',
          )
          const updatedResponse: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 4,
              method: 'tools/call',
              params: { name: 'workspace_status', arguments: {} },
            }),
          })
          const updatedMessage: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await updatedResponse.json(),
          )
          const updatedResult: CallToolResult = CallToolResultSchema.parse(updatedMessage.result)
          assert.equal(updatedMessage.id, 4)
          assert.deepEqual(updatedResult.content, [
            {
              type: 'text',
              text: JSON.stringify({
                viewMode: 'files',
                settings: { guid: 'changed', name: '新工程', mainMap: 'map-guid' },
              }),
            },
          ])
          await writeFile(
            join(workspaceFullPath, 'setting.json'),
            'guid: broken\nname: 42\nmainMap: ""',
            'utf8',
          )
          const invalidResponse: Response = await fetch(session.endpoint, {
            method: 'POST',
            headers,
            body: JSON.stringify({
              jsonrpc: '2.0',
              id: 5,
              method: 'tools/call',
              params: { name: 'workspace_status', arguments: {} },
            }),
          })
          const invalidMessage: JSONRPCResultResponse = JSONRPCResultResponseSchema.parse(
            await invalidResponse.json(),
          )
          const invalidResult: CallToolResult = CallToolResultSchema.parse(invalidMessage.result)
          assert.equal(invalidMessage.id, 5)
          assert.equal(invalidResult.isError, true)
          const missingRoute: Response = await fetch(new URL('/missing', session.endpoint))
          assert.equal(missingRoute.status, 404)
        } finally {
          await session.close()
        }
        await assert.rejects(fetch(session.endpoint))
      } finally {
        await rm(workspaceFullPath, { recursive: true })
      }
    })
  }
}
