// MCP 工具白名单测试：guard 模板与一键配置共用，条目唯一性/前缀形态在此锁定。
import { test } from 'vitest'
import { McpToolCatalog } from '../../../src/frontend/host_client/mcpToolCatalog.ts'

/** @anchor frontend-mcp-tool-catalog */
export class McpToolCatalogTest {
  /** @anchor frontend-mcp-tool-catalog */
  private static entriesUniqueAndPrefixed(this: void): void {
    const entries: readonly string[] = McpToolCatalog.entries
    if (entries.length <= 0) {
      throw new Error('工具名单为空')
    }
    if (new Set(entries).size !== entries.length) {
      throw new Error('工具入口名存在重复')
    }
  }

  /** @anchor frontend-mcp-tool-catalog */
  private static namesMatchEntries(this: void): void {
    const entries: readonly string[] = McpToolCatalog.entries
    const names: readonly string[] = McpToolCatalog.names
    if (names.length !== entries.length) {
      throw new Error('完整名数量与入口名不一致')
    }
    for (const [index, entry] of entries.entries()) {
      const fullName: string = names[index] ?? ''
      if (fullName !== `${McpToolCatalog.prefix}${entry}`) {
        throw new Error(`完整名 ${fullName} 不符合前缀展开`)
      }
      if (!McpToolCatalog.nameSet.has(fullName)) {
        throw new Error(`查找集合缺少完整名 ${fullName}`)
      }
    }
  }

  static {
    test('mcpToolCatalog:条目非空且无重复', (): void => {
      McpToolCatalogTest.entriesUniqueAndPrefixed()
    })
    test('mcpToolCatalog:完整名称带前缀且与查找集合一致', (): void => {
      McpToolCatalogTest.namesMatchEntries()
    })
  }
}
