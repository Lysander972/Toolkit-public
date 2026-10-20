import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { Option } from 'effect'
import { join, resolve } from 'node:path'
import { cruise } from 'dependency-cruiser'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { CheckArchitecture } from '../../../tools/cli/CheckArchitecture.ts'
import { WorkspaceFiles } from '../../../tools/cli/WorkspaceFiles.ts'
import { spawnSync } from 'node:child_process'
import type { SpawnSyncReturns } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { Git } from '../../../tools/cli/Git.ts'
import { CommitMessageGuard } from '../../../tools/cli/HookGuards.ts'
import type {
  ArchitectureDeclarations,
  ArchitectureElement,
} from '../../../tools/governance/derive-rules.ts'
import type { IReporterOutput, IForbiddenRuleType } from 'dependency-cruiser'

/** @anchor selfcheck-gate */
export class GovernanceTest {
  private constructor() {}

  // 检视记录时刻（录入入口自动生成的元数据，测试固定值）。

  private static source(): string {
    return (
      'specification { element system\n element container\n tag tool\n tag doc\n tag acl }\nmodel {\n' +
      " app = system 'app' { description '工具系统；不接管产品' metadata { sourcePath 'tools'\n checks 'selfcheck'\n entries ['tools/alpha/index.ts'] }\n" +
      " alpha = container 'alpha' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts'] } }\n" +
      " beta = container 'beta' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/beta'\n files ['tools/beta/*.ts'] } }\n }\n" +
      " specs = system 'specs' { description '规范系统；不定义架构' registry = container 'registry' { #doc\n description '规范登记；不定义实现架构'\n metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      ' app.alpha -> app.beta\n }\n'
    )
  }

  private static async model(this: void): Promise<void> {
    const declared: ArchitectureDeclarations = await ArchitectureRules.declarations(
      GovernanceTest.source(),
    )
    assert.equal(declared.sourceRelativePath, 'tools')
    assert.deepEqual(
      declared.elements
        .filter((element: ArchitectureElement): boolean => element.id === 'app')
        .flatMap((element: ArchitectureElement): string[] => element.entries),
      ['tools/alpha/index.ts'],
    )
    assert.deepEqual(declared.specRelativePaths, ['specs'])
    assert.equal(declared.containers.length, 2)
    assert.deepEqual(declared.codeTargets, ['app', 'app.alpha', 'app.beta'])
    assert.ok(
      declared
        .dependencyRules()
        .some((rule: IForbiddenRuleType): boolean => rule.name === 'app.beta-to-app.alpha'),
    )
    assert.ok(
      !declared
        .dependencyRules()
        .some((rule: IForbiddenRuleType): boolean => rule.name === 'app.alpha-to-app.beta'),
    )
    const rules: IForbiddenRuleType[] = declared.dependencyRules()
    rules.length = 0
    assert.ok(declared.dependencyRules().length > 0)
    const legacy: ArchitectureDeclarations = await ArchitectureRules.declarations(
      GovernanceTest.source()
        .replace(' element container\n tag tool\n tag doc\n tag acl', ' element container')
        .replaceAll(' #tool', '')
        .replaceAll(' #doc', ''),
    )
    assert.equal(legacy.containers.length, 2)
    await assert.rejects(
      ArchitectureRules.declarations(
        GovernanceTest.source().replace("sourcePath 'tools/beta'", "sourcePath 'tools/alpha'"),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        GovernanceTest.source().replace("sourcePath 'tools/beta'", "sourcePath '../beta'"),
      ),
    )
    await assert.rejects(ArchitectureRules.declarations('not a model'))
    await assert.rejects(
      ArchitectureRules.declarations(
        GovernanceTest.source().replace("specPath 'specs'", "specPath '../specs'"),
      ),
    )
    const guarded: ArchitectureDeclarations = await ArchitectureRules.declarations(
      GovernanceTest.source().replace(
        'specs = system',
        "shield = system 'shield' { description '依赖隔离；不承担业务' isolation = container 'iso' { #acl\n description '隔离外部依赖；不暴露内部类型'\n metadata { isolates ['pkg-x']\n files ['shared/*.ts'] } } }\n specs = system",
      ),
    )
    assert.ok(
      guarded
        .dependencyRules()
        .some((rule: IForbiddenRuleType): boolean => rule.name === 'isolated-pkg-x'),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        GovernanceTest.source().replace(
          'specs = system',
          "shield = system 'shield' { description '依赖隔离；不承担业务' isolation = container 'iso' { #acl\n description '隔离外部依赖；不暴露内部类型'\n metadata { isolates ['../evil']\n files ['shared/*.ts'] } } }\n specs = system",
        ),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        GovernanceTest.source().replace(
          'specs = system',
          "shield = system 'shield' { description '依赖隔离；不承担业务' isolation = container 'iso' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { isolates ['pkg-y']\n files ['shared/*.ts'] } } }\n specs = system",
        ),
      ),
      { message: 'shield.isolation: 防腐层用途与 isolates 登记必须成对出现' },
    )
  }

  private static async dependencies(this: void): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-architecture-'))
    try {
      mkdirSync(join(rootFullPath, 'tools/alpha'), { recursive: true })
      mkdirSync(join(rootFullPath, 'tools/beta'), { recursive: true })
      writeFileSync(join(rootFullPath, 'tools/alpha/index.ts'), 'export class Alpha {}')
      writeFileSync(
        join(rootFullPath, 'tools/beta/index.ts'),
        "import { Alpha } from '../alpha/index.ts'; export class Beta extends Alpha {}",
      )
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
        GovernanceTest.source(),
      )
      const rejected: IReporterOutput = await cruise(
        ['tools/alpha/index.ts', 'tools/beta/index.ts'],
        {
          baseDir: rootFullPath,
          ruleSet: { forbidden: model.dependencyRules() },
          outputType: 'err',
          validate: true,
        },
      )
      assert.notEqual(rejected.exitCode, 0)
      assert.ok(
        typeof rejected.output === 'string' && rejected.output.includes('app.beta-to-app.alpha'),
      )
      writeFileSync(join(rootFullPath, 'tools/beta/index.ts'), 'export class Beta {}')
      writeFileSync(
        join(rootFullPath, 'tools/alpha/index.ts'),
        "import { Beta } from '../beta/index.ts'; export class Alpha extends Beta {}",
      )
      const accepted: IReporterOutput = await cruise(
        ['tools/alpha/index.ts', 'tools/beta/index.ts'],
        {
          baseDir: rootFullPath,
          ruleSet: { forbidden: model.dependencyRules() },
          outputType: 'err',
          validate: true,
        },
      )
      assert.equal(accepted.exitCode, 0, JSON.stringify(accepted.output))
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  private static async staged(this: void): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-staged-'))
    try {
      Git.run(rootFullPath, ['init'])
      mkdirSync(join(rootFullPath, 'tools/alpha'), { recursive: true })
      writeFileSync(join(rootFullPath, 'tools/alpha/source.ts'), 'export class Source {}')
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
        GovernanceTest.source(),
      )
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(model, rootFullPath)
      }, /治理输入尚未全部暂存/)
      Git.run(rootFullPath, ['add', 'tools/alpha/source.ts'])
      writeFileSync(join(rootFullPath, 'tools/alpha/source.ts'), 'export class Changed {}')
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(model, rootFullPath)
      }, /治理输入尚未全部暂存/)
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  private static deadCode(this: void): void {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-dead-code-'))
    try {
      Git.run(rootFullPath, ['init'])
      mkdirSync(join(rootFullPath, 'tools/alpha'), { recursive: true })
      mkdirSync(join(rootFullPath, 'tools/beta'), { recursive: true })
      writeFileSync(join(rootFullPath, 'package.json'), '{"type":"module"}')
      writeFileSync(
        join(rootFullPath, 'tools/tsconfig.json'),
        '{"compilerOptions":{"module":"NodeNext","moduleResolution":"NodeNext"},"include":["**/*.ts"]}',
      )
      writeFileSync(
        join(rootFullPath, 'tools/alpha/index.ts'),
        "import { Beta } from '../beta/index.ts'; export class Alpha extends Beta {}",
      )
      writeFileSync(join(rootFullPath, 'tools/beta/index.ts'), 'export class Beta {}')
      writeFileSync(
        join(rootFullPath, 'knip.json'),
        JSON.stringify({
          entry: ['tools/alpha/index.ts'],
          project: ['tools/**/*.ts'],
        }),
      )
      const check: () => SpawnSyncReturns<string> = (): SpawnSyncReturns<string> =>
        spawnSync(
          process.execPath,
          [
            fileURLToPath(new URL('../bin/knip.js', import.meta.resolve('knip'))),
            '--directory',
            rootFullPath,
            '--include',
            'files,exports',
          ],
          { encoding: 'utf8', windowsHide: true },
        )
      const accepted: SpawnSyncReturns<string> = check()
      assert.equal(accepted.status, 0, accepted.stdout + accepted.stderr)
      writeFileSync(join(rootFullPath, 'tools/beta/unused.ts'), 'export class Unused {}')
      assert.notEqual(check().status, 0)
      rmSync(join(rootFullPath, 'tools/beta/unused.ts'))
      writeFileSync(
        join(rootFullPath, 'tools/beta/index.ts'),
        'export class Beta {}\nexport class Unused {}',
      )
      assert.notEqual(check().status, 0)
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  private static commitMessage(this: void): void {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-commit-message-'))
    try {
      mkdirSync(join(rootFullPath, '.git'))
      const messageFileRelativePath: string = '.git/COMMIT_EDITMSG'
      writeFileSync(
        join(rootFullPath, messageFileRelativePath),
        'fix(cli): resolve hook message from repository\n',
      )
      assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
      writeFileSync(
        join(rootFullPath, messageFileRelativePath),
        'fix(cli): crlf line ending tolerated\r\n\r\nbody line\r\n',
      )
      assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
      writeFileSync(join(rootFullPath, messageFileRelativePath), '131_第二阶段_tools.cli\n')
      assert.equal(CommitMessageGuard.run(rootFullPath, messageFileRelativePath), 0)
      writeFileSync(join(rootFullPath, messageFileRelativePath), 'x'.repeat(73) + '\n')
      assert.throws((): number => CommitMessageGuard.run(rootFullPath, messageFileRelativePath), {
        message: '提交标题须非空；每行最多 72 字符，总计最多 15 行',
      })
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  private static async edgeExemptions(this: void): Promise<void> {
    const tagged: string = GovernanceTest.source().replace(
      ' tag acl }',
      ' tag acl\n tag pending\n tag runtime }',
    )
    const plain: ArchitectureDeclarations = await ArchitectureRules.declarations(tagged)
    assert.deepEqual(plain.edges, [
      {
        source: 'app.alpha',
        target: 'app.beta',
        kind: 'plain',
        reason: Option.none(),
      },
    ])
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta { metadata { subscribe 'tools/alpha/index.ts>tools/beta/index.ts#Beta' } }",
        ),
      ),
      { message: 'app.alpha->app.beta: 非法依赖边 metadata 字段: subscribe' },
    )
    const pending: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged.replace(
        'app.alpha -> app.beta',
        "app.alpha -> app.beta #pending {\n metadata { pending 'Issue 115' }\n }",
      ),
    )
    assert.deepEqual(pending.edges, [
      {
        source: 'app.alpha',
        target: 'app.beta',
        kind: 'pending',
        reason: Option.some('Issue 115'),
      },
    ])
    const runtime: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged.replace(
        'app.alpha -> app.beta',
        "app.alpha -> app.beta #runtime {\n metadata { runtime 'stdio 进程边界' }\n }",
      ),
    )
    assert.deepEqual(runtime.edges, [
      {
        source: 'app.alpha',
        target: 'app.beta',
        kind: 'runtime',
        reason: Option.some('stdio 进程边界'),
      },
    ])
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace('app.alpha -> app.beta', 'app.alpha -> app.beta #pending'),
      ),
      { message: 'app.alpha->app.beta: #pending 豁免必须登记非空 pending 原因' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta #pending #runtime {\n metadata { pending 'a'\n runtime 'b' }\n }",
        ),
      ),
      { message: 'app.alpha->app.beta: #pending 与 #runtime 豁免不得同时声明' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta {\n metadata { pending 'a' }\n }",
        ),
      ),
      { message: 'app.alpha->app.beta: pending 原因必须与 #pending 标签成对声明' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta #pending {\n metadata { pending 'a' }\n }\n app.alpha -> app.beta",
        ),
      ),
      { message: 'app.alpha->app.beta: 同一边的多次声明必须保持一致的豁免状态' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        tagged.replace(
          'app.alpha -> app.beta',
          "app.alpha -> specs.registry #pending {\n metadata { pending 'a' }\n }",
        ),
      ),
      { message: 'app.alpha->specs.registry: 豁免标签只能声明在源码容器之间的依赖边上' },
    )
  }

  private static async runAligned(
    this: void,
    model: ArchitectureDeclarations,
    rootFullPath: string,
  ): Promise<{ readonly code: number; readonly output: string }> {
    const written: string[] = []
    const spy: MockInstance = vi
      .spyOn(process.stderr, 'write')
      .mockImplementation((chunk: unknown): boolean => {
        written.push(String(chunk))
        return true
      })
    try {
      return { code: await CheckArchitecture.run(model, rootFullPath), output: written.join('') }
    } finally {
      spy.mockRestore()
    }
  }

  private static async reverseAlignment(this: void): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-reverse-'))
    const tagged: string = GovernanceTest.source().replace(
      ' tag acl }',
      ' tag acl\n tag pending\n tag runtime }',
    )
    const plain: ArchitectureDeclarations = await ArchitectureRules.declarations(tagged)
    const pending: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged.replace(
        'app.alpha -> app.beta',
        "app.alpha -> app.beta #pending {\n metadata { pending 'Issue 115' }\n }",
      ),
    )
    const runtime: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged.replace(
        'app.alpha -> app.beta',
        "app.alpha -> app.beta #runtime {\n metadata { runtime 'stdio 进程边界' }\n }",
      ),
    )
    const exempted: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged.replace('app.alpha -> app.beta', 'app.alpha -> specs.registry'),
    )
    const todo: ArchitectureDeclarations = await ArchitectureRules.declarations(
      tagged
        .replace(' tag runtime }', ' tag runtime\n tag todo }')
        .replace("alpha = container 'alpha' { #tool", "alpha = container 'alpha' { #tool #todo")
        .replace(
          "sourcePath 'tools/alpha'",
          "sourcePath 'tools/alpha' todo '读取合同已声明，执行保持明确失败'",
        )
        .replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta #todo { metadata { todo '依赖合同已声明，执行保持明确失败' } }",
        ),
    )
    try {
      Git.run(rootFullPath, ['init'])
      mkdirSync(join(rootFullPath, 'tools/alpha'), { recursive: true })
      mkdirSync(join(rootFullPath, 'tools/beta'), { recursive: true })
      writeFileSync(
        join(rootFullPath, 'tools/tsconfig.json'),
        '{"compilerOptions":{"module":"NodeNext","moduleResolution":"NodeNext","allowImportingTsExtensions":true,"noEmit":true},"include":["**/*.ts"]}',
      )
      writeFileSync(join(rootFullPath, 'tools/alpha/index.ts'), 'export class Alpha {}')
      writeFileSync(join(rootFullPath, 'tools/beta/index.ts'), 'export class Beta {}')
      Git.run(rootFullPath, [
        'add',
        'tools/tsconfig.json',
        'tools/alpha/index.ts',
        'tools/beta/index.ts',
      ])
      const dangling: { readonly code: number; readonly output: string } =
        await GovernanceTest.runAligned(plain, rootFullPath)
      assert.equal(dangling.code, 1)
      assert.ok(dangling.output.includes('app.alpha->app.beta: 声明依赖边缺少真实代码依赖'))
      assert.equal((await GovernanceTest.runAligned(todo, rootFullPath)).code, 1)
      writeFileSync(
        join(rootFullPath, 'tools/alpha/index.ts'),
        "import { Beta } from '../beta/index.ts'; export class Alpha extends Beta {}",
      )
      Git.run(rootFullPath, ['add', 'tools/alpha/index.ts'])
      assert.equal((await GovernanceTest.runAligned(plain, rootFullPath)).code, 0)
      assert.equal((await GovernanceTest.runAligned(todo, rootFullPath)).code, 0)
      writeFileSync(
        join(rootFullPath, 'tools/beta/index.ts'),
        "import { Alpha } from '../alpha/index.ts'; export class Beta extends Alpha {}",
      )
      Git.run(rootFullPath, ['add', 'tools/beta/index.ts'])
      const reverseTodo: { readonly code: number; readonly output: string } =
        await GovernanceTest.runAligned(todo, rootFullPath)
      assert.equal(reverseTodo.code, 1)
      assert.ok(reverseTodo.output.includes('app.beta-to-app.alpha'))
      writeFileSync(join(rootFullPath, 'tools/beta/index.ts'), 'export class Beta {}')
      Git.run(rootFullPath, ['add', 'tools/beta/index.ts'])
      writeFileSync(join(rootFullPath, 'tools/alpha/index.ts'), 'export class Alpha {}')
      Git.run(rootFullPath, ['add', 'tools/alpha/index.ts'])
      assert.equal((await GovernanceTest.runAligned(pending, rootFullPath)).code, 1)
      const missingModule: ArchitectureDeclarations = {
        ...pending,
        containers: pending.containers.map(
          (
            container: ArchitectureDeclarations['containers'][number],
          ): ArchitectureDeclarations['containers'][number] =>
            container.id === 'app.beta'
              ? { ...container, files: ['tools/beta/missing.ts'] }
              : container,
        ),
      }
      const missing: { readonly code: number; readonly output: string } =
        await GovernanceTest.runAligned(missingModule, rootFullPath)
      assert.equal(missing.code, 1)
      assert.ok(missing.output.includes('目标源码声明未命中实际文件: tools/beta/missing.ts'))
      assert.equal((await GovernanceTest.runAligned(runtime, rootFullPath)).code, 0)
      assert.equal((await GovernanceTest.runAligned(exempted, rootFullPath)).code, 0)
      writeFileSync(
        join(rootFullPath, 'tools/alpha/index.ts'),
        "import { Beta } from '../beta/index.ts'; export class Alpha extends Beta {}",
      )
      Git.run(rootFullPath, ['add', 'tools/alpha/index.ts'])
      const stale: { readonly code: number; readonly output: string } =
        await GovernanceTest.runAligned(runtime, rootFullPath)
      assert.equal(stale.code, 1)
      assert.ok(stale.output.includes('app.alpha->app.beta: 已有静态依赖'))
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  static {
    test('架构模型派生单向约束并拒绝非法范围', GovernanceTest.model, 15000)
    test('依赖边豁免标签与理由成对校验并受放置规则约束', GovernanceTest.edgeExemptions, 15000)
    test('dependency-cruiser 对真实依赖执行正反例', GovernanceTest.dependencies)
    test('依赖边反向对齐：悬空报错、豁免与真实依赖互斥', GovernanceTest.reverseAlignment, 60000)
    test('未追踪或未暂存源码不得代替提交快照', GovernanceTest.staged)
    test('提交消息相对仓库读取，不使用检查进程工作目录', GovernanceTest.commitMessage)
    test('Knip 检查真实模块可达性并拒绝死文件与未使用导出', GovernanceTest.deadCode)
  }
}
