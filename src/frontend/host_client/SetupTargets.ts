import type { ConfigurationField, ConfigurationFormat } from './ConfigurationEditor.ts'

/** @anchor frontend-setup-targets */
export type SetupTargetId =
  | 'claudeCode'
  | 'codex'
  | 'gemini'
  | 'openCode'
  | 'trae'
  | 'cursor'
  | 'factoryDroid'
  | 'kimiCode'
  | 'cline'
  | 'kiloCode'
  | 'crush'

/** @anchor frontend-setup-targets */
export type SetupLevel = 'basic' | 'full'

/** @anchor frontend-setup-targets */
type SetupCommand =
  'file' | 'claude' | 'codex' | 'gemini' | 'opencode' | 'droid' | 'kimi' | 'cline' | 'kilo'

/** @anchor frontend-setup-targets */
export type SetupRootKind = 'workspace' | 'home'

/** @anchor frontend-setup-targets */
export interface SetupTarget {
  readonly id: SetupTargetId
  readonly title: string
  readonly configRelativePath: string
  readonly configRoot: SetupRootKind
  readonly format: ConfigurationFormat
  readonly skillRelativePath: string
  readonly skillRoot: SetupRootKind
  readonly command: SetupCommand
  readonly endpoint: string
  readonly fields: readonly ConfigurationField[]
  readonly fullReady: () => boolean
}

/** @anchor frontend-setup-targets */
class FullSetup {
  private constructor() {}
  /** TODO: Claude 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static claudeCode(): boolean {
    return false
  }
  /** TODO: Codex 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static codex(): boolean {
    return false
  }
  /** TODO: Gemini 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static gemini(): boolean {
    return false
  }
  /** TODO: OpenCode 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static openCode(): boolean {
    return false
  }
  /** TODO: Trae 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static trae(): boolean {
    return false
  }
  /** TODO: Cursor 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static cursor(): boolean {
    return false
  }
  /** TODO: Factory 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static factoryDroid(): boolean {
    return false
  }
  /** TODO: Kimi 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static kimiCode(): boolean {
    return false
  }
  /** TODO: Cline 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static cline(): boolean {
    return false
  }
  /** TODO: Kilo 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static kiloCode(): boolean {
    return false
  }
  /** TODO: Crush 命令防护、文件防护、上下文注入未接入。
   * @anchor frontend-setup-targets
   */
  public static crush(): boolean {
    return false
  }
}

/**
 * TODO: 完整防护与新增正式 CLI 接入按官方参考基线实现；当前只迁移已有执行能力。
 * @anchor frontend-setup-targets
 */
export class SetupTargets {
  private constructor() {}

  /** @anchor frontend-setup-targets */
  public static forEndpoint(endpoint: string): readonly SetupTarget[] {
    return [
      {
        id: 'claudeCode',
        title: 'Claude Code',
        configRelativePath: '.mcp.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.claude/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'claude',
        endpoint,
        fields: [
          { segments: ['mcpServers', 'mechtoolkit'], value: { type: 'http', url: endpoint } },
        ],
        fullReady: (): boolean => FullSetup.claudeCode(),
      },
      {
        id: 'codex',
        title: 'Codex',
        configRelativePath: '.codex/config.toml',
        configRoot: 'home',
        format: 'toml',
        skillRelativePath: '.agents/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'codex',
        endpoint,
        fields: [{ segments: ['mcp_servers', 'mechtoolkit'], value: { url: endpoint } }],
        fullReady: (): boolean => FullSetup.codex(),
      },
      {
        id: 'gemini',
        title: 'Gemini',
        configRelativePath: '.gemini/settings.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.gemini/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'gemini',
        endpoint,
        fields: [{ segments: ['mcpServers', 'mechtoolkit'], value: { httpUrl: endpoint } }],
        fullReady: (): boolean => FullSetup.gemini(),
      },
      {
        id: 'openCode',
        title: 'OpenCode',
        configRelativePath: 'opencode.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.config/opencode/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'opencode',
        endpoint,
        fields: [
          {
            segments: ['mcp', 'servers', 'mechtoolkit'],
            value: { type: 'remote', url: endpoint },
          },
        ],
        fullReady: (): boolean => FullSetup.openCode(),
      },
      {
        id: 'trae',
        title: 'Trae',
        configRelativePath: '.trae/mcp.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.trae/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'file',
        endpoint,
        fields: [
          { segments: ['mcpServers', 'mechtoolkit'], value: { type: 'http', url: endpoint } },
        ],
        fullReady: (): boolean => FullSetup.trae(),
      },
      {
        id: 'cursor',
        title: 'Cursor',
        configRelativePath: '.cursor/mcp.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.cursor/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'file',
        endpoint,
        fields: [{ segments: ['mcpServers', 'mechtoolkit'], value: { url: endpoint } }],
        fullReady: (): boolean => FullSetup.cursor(),
      },
      {
        id: 'factoryDroid',
        title: 'Factory Droid',
        configRelativePath: '.factory/mcp.json',
        configRoot: 'home',
        format: 'jsonc',
        skillRelativePath: '.factory/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'droid',
        endpoint,
        fields: [
          { segments: ['mcpServers', 'mechtoolkit'], value: { type: 'http', url: endpoint } },
        ],
        fullReady: (): boolean => FullSetup.factoryDroid(),
      },
      {
        id: 'kimiCode',
        title: 'Kimi Code',
        configRelativePath: '.kimi/mcp.json',
        configRoot: 'home',
        format: 'jsonc',
        skillRelativePath: '.kimi/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'kimi',
        endpoint,
        fields: [{ segments: ['mcpServers', 'mechtoolkit'], value: { url: endpoint } }],
        fullReady: (): boolean => FullSetup.kimiCode(),
      },
      {
        id: 'cline',
        title: 'Cline',
        configRelativePath: '.cline/data/settings/cline_mcp_settings.json',
        configRoot: 'home',
        format: 'jsonc',
        skillRelativePath: '.cline/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'cline',
        endpoint,
        fields: [
          {
            segments: ['mcpServers', 'mechtoolkit'],
            value: { type: 'streamableHttp', url: endpoint, disabled: false },
          },
        ],
        fullReady: (): boolean => FullSetup.cline(),
      },
      {
        id: 'kiloCode',
        title: 'Kilo Code',
        configRelativePath: '.kilo/kilo.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.kilo/skills/mechtoolkit',
        skillRoot: 'home',
        command: 'kilo',
        endpoint,
        fields: [{ segments: ['mcp', 'mechtoolkit'], value: { type: 'remote', url: endpoint } }],
        fullReady: (): boolean => FullSetup.kiloCode(),
      },
      {
        id: 'crush',
        title: 'Crush',
        configRelativePath: 'crush.json',
        configRoot: 'workspace',
        format: 'jsonc',
        skillRelativePath: '.crush/skills/mechtoolkit',
        skillRoot: 'workspace',
        command: 'file',
        endpoint,
        fields: [{ segments: ['mcp', 'mechtoolkit'], value: { type: 'http', url: endpoint } }],
        fullReady: (): boolean => FullSetup.crush(),
      },
    ]
  }
}
