import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { Option } from 'effect'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { CheckLegacyFreeze } from '../../../tools/cli/CheckLegacyFreeze.ts'
import { CommitCheck } from '../../../tools/cli/CommitCheck.ts'
import { Git } from '../../../tools/cli/Git.ts'
import { GitFixture } from '../../../tools/testing/GitFixture.ts'
import type { CommitGateWork } from '../../../tools/cli/CommitCheck.ts'
import type { ArchitectureDeclarations } from '../../../tools/governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'

/** @anchor legacy-freeze */
class PassingWork implements CommitGateWork {
  /** @anchor legacy-freeze */
  public run(): Promise<number> {
    return Promise.resolve(0)
  }
}

/** @anchor legacy-freeze */
export class LegacyFreezeRulesTest {
  private constructor() {}

  private static model(freeze: string): string {
    return (
      'specification { element system\n element container\n element document\n tag tool\n tag doc }\nmodel {\n' +
      " app = system 'app' { description '工具系统；不接管产品' metadata { sourcePath 'tools'\n checks 'selfcheck' }\n" +
      " code = container 'code' { #tool\n description '治理实现；不承载产品状态' metadata { sourcePath 'tools/code'\n files ['tools/code/*.ts'] } } }\n" +
      " specs = system 'specs' { description '约束实现；不定义架构' registry = container 'registry' { #doc\n description '规范登记；不定义实现架构' metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      " legacy = system '旧版参考区' { description '保存旧实现作为格式证据'" +
      (freeze.length > 0 ? " metadata { freeze '" + freeze + "' }" : '') +
      " renderer = document '旧渲染' { #doc\n description '旧版正式代码；只读参考' metadata { files ['legacy/*.ts'] } } }\n }\n"
    )
  }

  private static modelWithTwoFreezes(): string {
    return (
      'specification { element system\n element container\n element document\n tag tool\n tag doc }\nmodel {\n' +
      " app = system 'app' { description '工具系统；不接管产品' metadata { sourcePath 'tools'\n checks 'selfcheck'\n freeze 'initial' }\n" +
      " code = container 'code' { #tool\n description '治理实现；不承载产品状态' metadata { sourcePath 'tools/code'\n files ['tools/code/*.ts'] } } }\n" +
      " specs = system 'specs' { description '约束实现；不定义架构' registry = container 'registry' { #doc\n description '规范登记；不定义实现架构' metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      " legacy = system '旧版参考区' { description '保存旧实现作为格式证据' metadata { freeze 'initial' }\n" +
      " renderer = document '旧渲染' { #doc\n description '旧版正式代码；只读参考' metadata { files ['legacy/*.ts'] } } }\n }\n"
    )
  }

  private static fixtureModel(freeze: string): string {
    return (
      'specification { element system\n element container\n element document\n tag tool\n tag doc }\nmodel {\n' +
      " app = system 'app' { description '执行治理；不承载产品状态' metadata { sourcePath 'tools'\n checks 'selfcheck' }\n" +
      " code = container 'code' { #tool\n description '受审源码；不管理审计状态' metadata { sourcePath 'tools/code'\n files ['tools/code/*.ts'] } } }\n" +
      " specs = system 'specs' { description '约束实现；不定义架构' document = container 'document' { #doc\n description '保存规范正文；不承载源码' metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      " inputs = system 'inputs' { #doc\n description '保存治理输入' metadata { files ['tools/model.c4', '.gitignore'] } }\n" +
      " legacy = system '旧版参考区' { description '保存旧实现作为格式证据；永久冻结'" +
      (freeze.length > 0 ? " metadata { freeze '" + freeze + "' }" : '') +
      " canvas_render = document '旧渲染' { #doc\n description '旧版正式代码；只读参考' metadata { files ['legacy/canvas_render/*.ts'] } } }\n }\n"
    )
  }

  private static async parse(source: string): Promise<ReadonlyDeep<ArchitectureDeclarations>> {
    return ArchitectureRules.declarations(source)
  }

  private static async stderrOf(
    action: () => number | Promise<number>,
  ): Promise<readonly [number, string]> {
    const output: string[] = []
    const spy: MockInstance<typeof process.stderr.write> = vi
      .spyOn(process.stderr, 'write')
      .mockImplementation((value: string | Uint8Array): boolean => {
        output.push(value.toString())
        return true
      })
    try {
      return [await action(), output.join('')]
    } finally {
      spy.mockRestore()
    }
  }

  private static initRepository(rootFullPath: string): void {
    Git.run(rootFullPath, ['init', '--initial-branch=main'])
    Git.run(rootFullPath, ['config', 'user.name', 'Legacy Freeze Test'])
    Git.run(rootFullPath, ['config', 'user.email', 'freeze@example.invalid'])
    Git.run(rootFullPath, ['config', 'core.autocrlf', 'false'])
  }

  static {
    test('非首个节点首次引入参考区，以引入节点冻结并支持 index 检查', async (): Promise<void> => {
      const rootFullPath: string = mkdtempSync(join(tmpdir(), 'toolkit-legacy-introduction-'))
      try {
        GitFixture.create(rootFullPath)
        const frozen: ReadonlyDeep<ArchitectureDeclarations> = await LegacyFreezeRulesTest.parse(
          LegacyFreezeRulesTest.model('initial'),
        )
        mkdirSync(join(rootFullPath, 'legacy'))
        writeFileSync(join(rootFullPath, 'legacy/old.ts'), 'export {}\n')
        Git.run(rootFullPath, ['add', 'legacy'])
        const introducedTree: string = Git.run(rootFullPath, ['write-tree'])
        assert.equal(CheckLegacyFreeze.check(frozen, rootFullPath, introducedTree), 0)
        Git.run(rootFullPath, ['commit', '-m', 'introduce reference'])
        assert.equal(CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'), 0)
        writeFileSync(join(rootFullPath, 'legacy/old.ts'), 'export const changed = 1\n')
        Git.run(rootFullPath, ['add', 'legacy'])
        const changedTree: string = Git.run(rootFullPath, ['write-tree'])
        const [code, diagnostic]: readonly [number, string] = await LegacyFreezeRulesTest.stderrOf(
          (): number => CheckLegacyFreeze.check(frozen, rootFullPath, changedTree),
        )
        assert.equal(code, 1)
        assert.match(diagnostic, /legacy\/old\.ts: 旧版参考区已冻结，禁止修改/)
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
    test('提交门禁拦截 staged legacy 正文变化', async (): Promise<void> => {
      const rootFullPath: string = mkdtempSync(join(tmpdir(), 'toolkit-legacy-gate-'))
      try {
        GitFixture.create(rootFullPath)
        mkdirSync(join(rootFullPath, 'legacy/canvas_render'), { recursive: true })
        writeFileSync(join(rootFullPath, 'legacy/canvas_render/CanvasA.ts'), 'export {}\n')
        writeFileSync(join(rootFullPath, 'tools/model.c4'), LegacyFreezeRulesTest.fixtureModel(''))
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'test: legacy baseline'])
        writeFileSync(
          join(rootFullPath, 'tools/model.c4'),
          LegacyFreezeRulesTest.fixtureModel('initial'),
        )
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'test: declare freeze'])
        writeFileSync(
          join(rootFullPath, 'legacy/canvas_render/CanvasA.ts'),
          'export const changed = 1\n',
        )
        Git.run(rootFullPath, ['add', 'legacy'])
        const [code, diagnostic]: readonly [number, string] = await LegacyFreezeRulesTest.stderrOf(
          async (): Promise<number> => CommitCheck.index(rootFullPath),
        )
        assert.equal(code, 1)
        assert.match(diagnostic, /旧版参考区已冻结，禁止修改/)
        assert.equal(await CommitCheck.gate(rootFullPath, Option.some(new PassingWork())), 1)
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
    test('freeze 元数据解析：首次引入、缺失、多处与非法值', async (): Promise<void> => {
      const declared: ReadonlyDeep<ArchitectureDeclarations> = await LegacyFreezeRulesTest.parse(
        LegacyFreezeRulesTest.model('initial'),
      )
      const freeze: Option.Option<string> = Option.map(
        declared.legacyFreeze,
        (entry: { revision: string }): string => entry.revision,
      )
      assert.deepEqual(freeze, Option.some('initial'))
      const missing: ReadonlyDeep<ArchitectureDeclarations> = await LegacyFreezeRulesTest.parse(
        LegacyFreezeRulesTest.model(''),
      )
      assert.ok(Option.isNone(missing.legacyFreeze))
      await assert.rejects(
        LegacyFreezeRulesTest.parse(LegacyFreezeRulesTest.modelWithTwoFreezes()),
        /冻结基线至多声明一次/,
      )
      await assert.rejects(LegacyFreezeRulesTest.parse(LegacyFreezeRulesTest.model('z'.repeat(40))))
      await assert.rejects(LegacyFreezeRulesTest.parse(LegacyFreezeRulesTest.model('a'.repeat(39))))
    })

    test('首个提交包含参考区时通过，后续增删改拒绝', async (): Promise<void> => {
      const rootFullPath: string = mkdtempSync(join(tmpdir(), 'toolkit-legacy-freeze-'))
      try {
        mkdirSync(join(rootFullPath, 'legacy'), { recursive: true })
        writeFileSync(join(rootFullPath, 'legacy', 'old.ts'), 'export {}\n')
        LegacyFreezeRulesTest.initRepository(rootFullPath)
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'freeze baseline'])
        const frozen: ReadonlyDeep<ArchitectureDeclarations> = await LegacyFreezeRulesTest.parse(
          LegacyFreezeRulesTest.model('initial'),
        )
        assert.equal(CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'), 0)
        writeFileSync(join(rootFullPath, 'note.txt'), '非 legacy 变更\n')
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'unrelated change'])
        const [clean, cleanLog]: readonly [number, string] = await LegacyFreezeRulesTest.stderrOf(
          (): number => CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'),
        )
        assert.equal(clean, 0)
        assert.equal(cleanLog.length, 0)

        writeFileSync(join(rootFullPath, 'legacy', 'old.ts'), 'export { changed }\n')
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'modify legacy'])
        const [modified, modifiedLog]: readonly [number, string] =
          await LegacyFreezeRulesTest.stderrOf((): number =>
            CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'),
          )
        assert.equal(modified, 1)
        assert.match(modifiedLog, /legacy\/old\.ts: 旧版参考区已冻结，禁止修改（基线/)

        writeFileSync(join(rootFullPath, 'legacy', 'added.ts'), 'export {}\n')
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'add legacy'])
        const [added, addedLog]: readonly [number, string] = await LegacyFreezeRulesTest.stderrOf(
          (): number => CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'),
        )
        assert.equal(added, 1)
        assert.match(addedLog, /legacy\/added\.ts: 旧版参考区已冻结，禁止新增（基线/)

        Git.run(rootFullPath, ['rm', '-q', 'legacy/old.ts'])
        Git.run(rootFullPath, ['commit', '-m', 'remove legacy'])
        const [removed, removedLog]: readonly [number, string] =
          await LegacyFreezeRulesTest.stderrOf((): number =>
            CheckLegacyFreeze.check(frozen, rootFullPath, 'HEAD'),
          )
        assert.equal(removed, 1)
        assert.match(removedLog, /legacy\/old\.ts: 旧版参考区已冻结，禁止删除（基线/)

        const undeclared: ReadonlyDeep<ArchitectureDeclarations> =
          await LegacyFreezeRulesTest.parse(LegacyFreezeRulesTest.model(''))
        const [present, presentLog]: readonly [number, string] =
          await LegacyFreezeRulesTest.stderrOf((): number =>
            CheckLegacyFreeze.check(undeclared, rootFullPath, 'HEAD'),
          )
        assert.equal(present, 1)
        assert.match(presentLog, /受检树存在旧版参考区文件但模型未声明冻结基线/)

        writeFileSync(join(rootFullPath, 'note.txt'), '清空 legacy\n')
        Git.run(rootFullPath, ['rm', '-q', '-r', 'legacy'])
        Git.run(rootFullPath, ['add', '.'])
        Git.run(rootFullPath, ['commit', '-m', 'remove legacy tree'])
        const [absent, absentLog]: readonly [number, string] = await LegacyFreezeRulesTest.stderrOf(
          (): number => CheckLegacyFreeze.check(undeclared, rootFullPath, 'HEAD'),
        )
        assert.equal(absent, 0)
        assert.equal(absentLog.length, 0)
      } finally {
        rmSync(rootFullPath, { recursive: true, force: true })
      }
    })
  }
}
