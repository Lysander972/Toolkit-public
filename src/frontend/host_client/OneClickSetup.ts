import { mkdir, readdir, readFile } from 'node:fs/promises'
import type { Dirent } from 'node:fs'
import { dirname, join } from 'node:path'
import { Option } from 'effect'
import writeFileAtomic from 'write-file-atomic'
import { execa } from 'execa'
import { ConfigurationEditor } from './ConfigurationEditor.ts'
import type { ConfigurationPlan } from './ConfigurationEditor.ts'
import type { SetupTarget, SetupLevel, SetupRootKind } from './SetupTargets.ts'

/** @anchor frontend-setup */
export interface SetupRoots {
  readonly workspaceFullPath: string
  readonly homeFullPath: string
  readonly skillSourceFullPath: string
}

/** @anchor frontend-setup */
export interface SkillFile {
  readonly relativePath: string
  readonly contents: Buffer
}

/** @anchor frontend-setup */
export interface SetupPlan {
  readonly target: SetupTarget
  readonly configuration: ConfigurationPlan
  readonly skillFiles: readonly SkillFile[]
  readonly skillDestinationFullPath: string
  readonly level: SetupLevel
}

/**
 * TODO: 新增 Agent CLI、Skill 安装器与 full 防护接入保持明确失败，合同见官方参考基线。
 * @anchor frontend-setup
 */
export class OneClickSetup {
  private readonly roots: SetupRoots

  /** @anchor frontend-setup */
  public constructor(roots: SetupRoots) {
    this.roots = roots
  }

  /** @anchor frontend-setup */
  public async plan(target: SetupTarget): Promise<SetupPlan> {
    const rootFullPath: string = this.root(target.configRoot)
    const fileFullPath: string = join(rootFullPath, target.configRelativePath)
    const previous: Option.Option<string> = await ConfigurationEditor.read(fileFullPath)
    const configuration: ConfigurationPlan = ConfigurationEditor.plan(
      fileFullPath,
      target.format,
      previous,
      target.fields,
    )
    const skillFiles: readonly SkillFile[] = await this.skillFiles('')
    return {
      target,
      configuration,
      skillFiles,
      skillDestinationFullPath: join(this.root(target.skillRoot), target.skillRelativePath),
      level: target.fullReady() ? 'full' : 'basic',
    }
  }

  /**
   * @anchor frontend-setup
   * @fallback 已规划但未变化的配置无需重复调用外部 CLI。
   */
  public async apply(plan: SetupPlan): Promise<number> {
    switch (plan.target.command) {
      case 'gemini':
      case 'opencode':
      case 'droid':
      case 'kimi':
      case 'cline':
      case 'kilo':
        throw new Error('TODO: ' + plan.target.title + ' 正式 CLI 与 Skill 安装接入未实现')
      case 'file':
      case 'claude':
      case 'codex':
        break
    }
    let written: number = 0
    for (const file of plan.skillFiles) {
      const fileFullPath: string = join(plan.skillDestinationFullPath, file.relativePath)
      const current: Option.Option<Buffer> = await this.readSkill(fileFullPath)
      if (Option.isSome(current) && current.value.equals(file.contents)) continue
      await mkdir(dirname(fileFullPath), { recursive: true })
      await writeFileAtomic(fileFullPath, file.contents)
      written += 1
    }
    if (!plan.configuration.changed) return written
    switch (plan.target.command) {
      case 'claude':
        await execa(
          'claude',
          [
            'mcp',
            'add',
            '--transport',
            'http',
            '--scope',
            'project',
            'mechtoolkit',
            plan.target.endpoint,
          ],
          { cwd: this.roots.workspaceFullPath, timeout: 10000, windowsHide: true },
        )
        break
      case 'codex':
        await execa('codex', ['mcp', 'add', 'mechtoolkit', '--url', plan.target.endpoint], {
          cwd: this.roots.workspaceFullPath,
          timeout: 10000,
          windowsHide: true,
        })
        break
      case 'file':
        await ConfigurationEditor.apply(plan.configuration)
    }
    return written + 1
  }

  /** @anchor frontend-setup */
  private root(kind: SetupRootKind): string {
    switch (kind) {
      case 'workspace':
        return this.roots.workspaceFullPath
      case 'home':
        return this.roots.homeFullPath
    }
  }

  /**
   * @anchor frontend-setup
   * @fallback 根目录白名单之外的文档不属于 MechToolkit 技能内容。
   */
  private async skillFiles(directoryRelativePath: string): Promise<readonly SkillFile[]> {
    const entries: Dirent[] = await readdir(
      join(this.roots.skillSourceFullPath, directoryRelativePath),
      { withFileTypes: true },
    )
    const files: SkillFile[] = []
    for (const entry of entries) {
      if (
        directoryRelativePath === '' &&
        ![
          'SKILL.md',
          'LICENSE',
          'README.md',
          'agents_claude.template.md',
          'references',
          'assets',
          'agents',
        ].includes(entry.name)
      )
        continue
      const relativePath: string =
        directoryRelativePath === '' ? entry.name : directoryRelativePath + '/' + entry.name
      const sourceFullPath: string = join(this.roots.skillSourceFullPath, relativePath)
      if (entry.isDirectory()) files.push(...(await this.skillFiles(relativePath)))
      else if (entry.isFile())
        files.push({ relativePath, contents: await readFile(sourceFullPath) })
    }
    return files
  }

  /** @anchor frontend-setup */
  private async readSkill(fileFullPath: string): Promise<Option.Option<Buffer>> {
    try {
      return Option.some(await readFile(fileFullPath))
    } catch (error: unknown) {
      if (error instanceof Error && 'code' in error && error.code === 'ENOENT') return Option.none()
      throw error
    }
  }
}
