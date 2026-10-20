import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import assert from 'node:assert/strict'
import { Option } from 'effect'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { ArchitectureFiles } from '../../../tools/governance/architecture-files.ts'
import { Git } from '../../../tools/cli/Git.ts'
import { WorkspaceFiles } from '../../../tools/cli/WorkspaceFiles.ts'
import { CheckArchitecture } from '../../../tools/cli/CheckArchitecture.ts'
import type {
  ArchitectureDeclarations,
  ArchitectureContainer,
  ArchitectureElement,
} from '../../../tools/governance/derive-rules.ts'
import type { ArchitectureFileReport } from '../../../tools/governance/architecture-files.ts'

/** @anchor architecture-files */
export class ArchitectureFilesTest {
  private static readonly files: string[] = [
    'tools/model.c4',
    'tools/alpha/first.ts',
    'tools/beta/second.ts',
    'specs/spec.md',
  ]

  private constructor() {}

  private static async responsibilities(this: void): Promise<void> {
    const source: string = ArchitectureFilesTest.source('')
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace("description 'Alpha 实现；不拥有 Beta 文件'", ''),
      ),
      { message: 'app.alpha: 必须通过 LikeC4 description 声明职责与边界' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace("description 'Alpha 实现；不拥有 Beta 文件'", "description '   '"),
      ),
      { message: 'app.alpha: 必须通过 LikeC4 description 声明职责与边界' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace(
          "sourcePath 'tools/alpha'",
          "sourcePath 'tools/alpha' responsibility '伪造字段'",
        ),
      ),
      { message: 'app.alpha: 非法架构 metadata 字段: responsibility' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace("sourcePath 'tools/alpha'", "sourcePath 'tools/alpha' budgetFake '1'"),
      ),
      { message: 'app.alpha: 非法架构 metadata 字段: budgetFake' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta { metadata { responsibility '伪造字段' } }",
        ),
      ),
      { message: 'app.alpha->app.beta: 非法依赖边 metadata 字段: responsibility' },
    )
  }

  private static async bindings(this: void): Promise<void> {
    const source: string = ArchitectureFilesTest.source(
      " detail = container 'detail' { description '引用 Alpha；不冒领其文件' metadata { code ['tools/alpha/first.ts#First'] } }",
    )
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(source)
    const sources: ReadonlyMap<string, string> = new Map([
      ['tools/alpha/first.ts', '/** @anchor first */\nexport class First {}'],
    ])
    assert.ok(
      ArchitectureFiles.codeReferences(model, sources).includes(
        'detail: 代码引用越出自身架构边界: tools/alpha/first.ts#First',
      ),
    )
    const owned: ArchitectureDeclarations = {
      ...model,
      elements: model.elements.map((element: ArchitectureElement): ArchitectureElement =>
        element.id === 'detail' ? { ...element, parentId: Option.some('app.alpha') } : element,
      ),
    }
    assert.deepEqual(ArchitectureFiles.codeReferences(owned, sources), [])
    const missingSymbol: ArchitectureDeclarations = {
      ...owned,
      elements: owned.elements.map((element: ArchitectureElement): ArchitectureElement =>
        element.id === 'detail' ? { ...element, code: ['tools/alpha/first.ts#Missing'] } : element,
      ),
    }
    assert.ok(
      ArchitectureFiles.codeReferences(missingSymbol, sources).some((message: string): boolean =>
        message.includes('代码对应不存在'),
      ),
    )
    const invalidEntries: ArchitectureDeclarations = {
      ...owned,
      elements: owned.elements.map((element: ArchitectureElement): ArchitectureElement =>
        element.id === 'app.alpha'
          ? { ...element, entries: ['tools/beta/second.ts', 'tools/alpha/missing.ts'] }
          : element,
      ),
    }
    const violations: string[] = ArchitectureFiles.check(
      invalidEntries,
      ArchitectureFilesTest.files,
    ).violations
    assert.ok(violations.includes('app.alpha: 入口文件越出自身文件边界: tools/beta/second.ts'))
    assert.ok(violations.includes('app.alpha: 入口文件不存在或未登记: tools/alpha/missing.ts'))
  }

  /** @anchor architecture-files */
  private static async externalSystems(this: void): Promise<void> {
    const source: string = ArchitectureFilesTest.source(
      " cloud = system 'Cloud' { #external description '外部协议边界，不拥有仓库文件' } app.alpha -> cloud",
    ).replace('tag doc }', 'tag doc\n tag external }')
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(source)
    assert.deepEqual(ArchitectureFiles.check(model, ArchitectureFilesTest.files).violations, [])
    for (const invalid of [
      source.replace("cloud = system 'Cloud'", "cloud = container 'Cloud'"),
      source.replace(
        '#external description',
        "#external metadata { files ['tools/alpha/*.ts'] } description",
      ),
      source.replace('#external description', '#external #doc description'),
      source.replace(
        "description '外部协议边界，不拥有仓库文件'",
        "description '外部协议边界，不拥有仓库文件' child = container 'child' { description '伪造内部源码' metadata { files ['tools/alpha/*.ts'] } }",
      ),
    ]) {
      await assert.rejects(ArchitectureRules.declarations(invalid), {
        message: 'cloud: #external 只允许无仓库归属与子元素的外部 system',
      })
    }
    await assert.rejects(ArchitectureRules.declarations(source.replace('#external ', '')), {
      message: 'cloud: 叶元素及待迁移元素必须直接登记文件',
    })
  }

  private static source(extra: string): string {
    return (
      'specification { element system\n element container\n element document\n tag tool\n tag doc }\nmodel {\n' +
      " app = system 'app' { #tool\n description '治理系统；不持有业务状态'\n metadata { sourcePath 'tools'\n checks 'selfcheck'\n entries ['tools/model.c4']\n files ['tools/model.c4'] }\n" +
      " alpha = container 'alpha' { #tool\n description 'Alpha 实现；不拥有 Beta 文件'\n metadata { sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts'] } }\n" +
      " beta = container 'beta' { #tool\n description 'Beta 实现；不拥有 Alpha 文件'\n metadata { sourcePath 'tools/beta'\n files ['tools/beta/*.ts'] } }\n }\n" +
      " specs = system 'specs' { description '约束实现；不定义架构' registry = container 'registry' { #doc\n description '保存规范；不承载业务'\n metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      ' app.alpha -> app.beta\n' +
      extra +
      '\n}\n'
    )
  }

  private static async coverage(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source(''),
    )
    assert.deepEqual(ArchitectureFiles.check(model, ArchitectureFilesTest.files), {
      violations: [],
    })
    const missing: ArchitectureFileReport = ArchitectureFiles.check(
      model,
      ArchitectureFilesTest.files.slice(1),
    )
    assert.ok(
      missing.violations.some((message: string): boolean =>
        message.includes('app: 文件声明未命中任何文件: tools/model.c4'),
      ),
    )
    const unexpected: ArchitectureFileReport = ArchitectureFiles.check(model, [
      ...ArchitectureFilesTest.files,
      'tools/alpha/nested/third.ts',
    ])
    assert.deepEqual(unexpected.violations, ['tools/alpha/nested/third.ts: 未在架构模型中登记'])
    const empty: ArchitectureFileReport = ArchitectureFiles.check(
      model,
      ArchitectureFilesTest.files.slice(0, 2),
    )
    assert.ok(
      empty.violations.some((message: string): boolean => message.includes('tools/beta/*.ts')),
    )
    assert.equal(ArchitectureFiles.check(model, []).violations.length, 5)
    const wildcardSource: string = ArchitectureFilesTest.source('')
      .replace("sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts']", "files ['**']")
      .replace("files ['tools/model.c4']", "files ['tools/model.c4', 'README.md', 'src/a.ts']")
    const wildcardModel: ArchitectureDeclarations =
      await ArchitectureRules.declarations(wildcardSource)
    const wildcardReport: ArchitectureFileReport = ArchitectureFiles.check(wildcardModel, [
      'tools/model.c4',
      'tools/beta/second.ts',
      'README.md',
      'src/a.ts',
    ])
    assert.ok(
      wildcardReport.violations.some((message: string): boolean =>
        message.includes('README.md: 重复归属'),
      ),
    )
    assert.ok(
      wildcardReport.violations.some((message: string): boolean =>
        message.includes('src/a.ts: 重复归属'),
      ),
    )
  }

  private static async duplicates(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source(
        " extra = container 'extra' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['tools/alpha/'] } }",
      ),
    )
    const report: ArchitectureFileReport = ArchitectureFiles.check(
      model,
      ArchitectureFilesTest.files,
    )
    assert.equal(report.violations.length, 1)
    assert.ok(
      report.violations.some(
        (message: string): boolean =>
          message.startsWith('tools/alpha/first.ts: 重复归属 ') &&
          message.includes('app.alpha') &&
          message.includes('extra'),
      ),
    )
    const conflictingOwner: ArchitectureDeclarations = {
      ...model,
      elements: model.elements.map((element: ArchitectureElement): ArchitectureElement =>
        element.id === 'app.alpha' ? { ...element, files: [] } : element,
      ),
    }
    assert.ok(
      ArchitectureFiles.check(conflictingOwner, ArchitectureFilesTest.files).violations.includes(
        'tools/alpha/first.ts: sourcePath 归属 app.alpha 与文件归属 extra 冲突',
      ),
    )
    const sameOwner: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source('').replace(
        "files ['tools/alpha/*.ts']",
        "files ['tools/alpha/*.ts', 'tools/alpha/first.ts']",
      ),
    )
    assert.equal(
      ArchitectureFiles.check(sameOwner, ArchitectureFilesTest.files).violations.length,
      1,
    )
  }

  private static async directories(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source(
        " assets = system 'assets' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['test/data/', 'docs/Registry'] } }",
      ),
    )
    const files: string[] = [
      ...ArchitectureFilesTest.files,
      'test/data/中文 空格/.hidden/file.guid',
      'docs/Registry',
    ]
    assert.deepEqual(ArchitectureFiles.check(model, files).violations, [])
    assert.ok(
      ArchitectureFiles.check(model, [...files, 'test/database/other']).violations.includes(
        'test/database/other: 未在架构模型中登记',
      ),
    )
    assert.ok(
      ArchitectureFiles.check(model, [...files, 'docs/Registry/source.cs']).violations.includes(
        'docs/Registry/source.cs: 未在架构模型中登记',
      ),
    )
  }

  private static async invalidModel(this: void): Promise<void> {
    await assert.rejects(
      ArchitectureRules.declarations(ArchitectureFilesTest.source(" empty = container 'empty'")),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source(" empty = container 'empty' { metadata { files [] } }"),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source(
          " invalid = container 'invalid' { metadata { files ['../outside'] } }",
        ),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(ArchitectureFilesTest.source(' app.alpha -> missing')),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source('').replace("checks 'selfcheck'", "checks 'unknown'"),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source('').replace(
          "files ['tools/alpha/*.ts']",
          "files ['other/*.ts']",
        ),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source('').replace(
          "container 'alpha' { #tool\n",
          "container 'alpha' {",
        ),
      ),
      {
        message:
          'app.alpha: 登记文件的元素必须恰好携带一个用途标签（doc/build/test/tool/common/acl）',
      },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source('').replace(
          "container 'alpha' { #tool\n",
          "container 'alpha' { #tool #doc\n",
        ),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        ArchitectureFilesTest.source(
          " old = document 'old' { #tool\n metadata { files ['old/'] } }",
        ),
      ),
      { message: 'old: 文档元素的用途必须是 doc' },
    )
  }

  private static async documents(this: void): Promise<void> {
    const source: string = ArchitectureFilesTest.source(
      " old = document 'old' { #doc\n metadata { files ['src/old/*.cs', 'legacy.json'] } }\n app -> old",
    )
    const regular: ArchitectureDeclarations = await ArchitectureRules.declarations(source)
    const files: string[] = [...ArchitectureFilesTest.files, 'src/old/a.cs', 'legacy.json']
    assert.ok(regular.guardedRelativePaths.includes('src/old/*.cs'))
    assert.ok(
      ArchitectureFiles.check(regular, files.slice(0, -1)).violations.some(
        (message: string): boolean => message.includes('legacy.json'),
      ),
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace("files ['src/old/*.cs'", "sourcePath 'src/old'\n files ['src/old/*.cs'"),
      ),
      { message: 'old: 文档元素不得声明源码或规范检查范围' },
    )
    await assert.rejects(
      ArchitectureRules.declarations(
        source.replace("files ['src/old/*.cs'", "checks 'selfcheck'\n files ['src/old/*.cs'"),
      ),
      { message: 'old: 文档元素不得声明源码或规范检查范围' },
    )
  }

  private static async guardedInputs(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source(
        " build = system 'build' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['package.json'] } }\n" +
          " lock = container 'lock' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['pnpm-lock.yaml'] } }\n app -> build\n build -> lock\n lock -> build",
      ),
    )
    assert.deepEqual(
      new Set(model.guardedRelativePaths),
      new Set([
        ...['tools/model.c4', 'tools/alpha/*.ts', 'tools/beta/*.ts'],
        'package.json',
        'pnpm-lock.yaml',
      ]),
    )
  }

  private static async lineTagScope(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      ArchitectureFilesTest.source(''),
    )
    assert.deepEqual(
      ArchitectureFiles.suspectedPaths(
        model,
        "// @non-manual-path 整个表达式\nconst value = join('tools/model.c4', 'tools/alpha')",
        'tools/alpha/first.ts',
      ),
      [],
    )
    assert.ok(
      ArchitectureFiles.suspectedPaths(
        model,
        "// @non-manual-path 非紧邻\n\nconst value = join('tools/model.c4', 'tools/alpha')",
        'tools/alpha/first.ts',
      ).length > 0,
    )
  }

  private static async snapshot(this: void): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-files-'))
    try {
      Git.run(rootFullPath, ['init'])
      mkdirSync(join(rootFullPath, 'tools/alpha'), { recursive: true })
      mkdirSync(join(rootFullPath, 'tools/beta'), { recursive: true })
      mkdirSync(join(rootFullPath, 'specs'), { recursive: true })
      const source: string = ArchitectureFilesTest.source('')
      writeFileSync(join(rootFullPath, 'tools/model.c4'), source)
      writeFileSync(join(rootFullPath, 'tools/alpha/first.ts'), 'export class First {}')
      writeFileSync(join(rootFullPath, 'specs/spec.md'), '# 规范\n')
      Git.run(rootFullPath, ['add', '.'])
      const before: string = Git.run(rootFullPath, ['write-tree'])
      writeFileSync(join(rootFullPath, 'tools/beta/second.ts'), 'export class Second {}')
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(source)
      const filesBefore: string[] = Git.run(rootFullPath, [
        'ls-tree',
        '-r',
        '--name-only',
        '-z',
        before,
      ])
        .split('\0')
        .filter(Boolean)
      assert.ok(
        ArchitectureFiles.check(model, filesBefore).violations.some((message: string): boolean =>
          message.includes('tools/beta/*.ts'),
        ),
      )
      Git.run(rootFullPath, ['add', '.'])
      const after: string = Git.run(rootFullPath, ['write-tree'])
      const filesAfter: string[] = Git.run(rootFullPath, [
        'ls-tree',
        '-r',
        '--name-only',
        '-z',
        after,
      ])
        .split('\0')
        .filter(Boolean)
      assert.notEqual(before, after)
      assert.deepEqual(ArchitectureFiles.check(model, filesAfter).violations, [])
      assert.equal(CheckArchitecture.files(model, rootFullPath), 0)
      const markedSource: string = source
        .replace('tag doc }', 'tag doc\n tag gap }')
        .replace("alpha = container 'alpha' { #tool", "alpha = container 'alpha' { #tool #gap")
        .replace("sourcePath 'tools/alpha'", "gap '未实现：读取入口缺失' sourcePath 'tools/alpha'")
        .replace(
          'app.alpha -> app.beta',
          "app.alpha -> app.beta #gap { metadata { gap '偏离：交互仍需收敛' } }",
        )
      const marked: ArchitectureDeclarations = await ArchitectureRules.declarations(markedSource)
      const messages: string[] = []
      const report: MockInstance = vi
        .spyOn(process.stderr, 'write')
        .mockImplementation((message: unknown): boolean => {
          messages.push(String(message))
          return true
        })
      try {
        assert.equal(CheckArchitecture.files(marked, rootFullPath), 1)
        assert.deepEqual(messages, [
          'app.alpha: 架构未收敛 #gap: 未实现：读取入口缺失\n',
          'app.alpha->app.beta: 架构未收敛 #gap: 偏离：交互仍需收敛\n',
        ])
      } finally {
        report.mockRestore()
      }
      const todoSource: string = markedSource
        .replace('tag gap }', 'tag gap\n tag todo }')
        .replaceAll('#gap', '#todo')
        .replaceAll("gap '", "todo '")
        .replace('偏离：交互仍需收敛', '未实现：具名读取合同的执行')
      const todo: ArchitectureDeclarations = await ArchitectureRules.declarations(todoSource)
      assert.equal(CheckArchitecture.files(todo, rootFullPath), 0)
      const missingTodoFile: ArchitectureDeclarations = await ArchitectureRules.declarations(
        todoSource.replace("files ['tools/beta/*.ts']", "files ['tools/beta/missing.ts']"),
      )
      assert.equal(CheckArchitecture.files(missingTodoFile, rootFullPath), 1)
      const danglingTodoReference: ArchitectureDeclarations = await ArchitectureRules.declarations(
        todoSource.replace(
          "sourcePath 'tools/alpha'",
          "sourcePath 'tools/alpha' code ['tools/alpha/first.ts#Missing']",
        ),
      )
      assert.equal(CheckArchitecture.files(danglingTodoReference, rootFullPath), 1)
      const unownedTodoFile: ArchitectureDeclarations = await ArchitectureRules.declarations(
        todoSource.replace("files ['tools/alpha/*.ts']", "files ['tools/alpha/other.ts']"),
      )
      assert.equal(CheckArchitecture.files(unownedTodoFile, rootFullPath), 1)
      assert.equal(
        CheckArchitecture.files(
          {
            ...todo,
            containers: todo.containers.map(
              (container: ArchitectureContainer): ArchitectureContainer => ({
                ...container,
                sourceRelativePath: container.sourceRelativePath + '/missing',
              }),
            ),
          },
          rootFullPath,
        ),
        1,
      )
      await assert.rejects(
        ArchitectureRules.declarations(todoSource.replace("todo '未实现：读取入口缺失' ", '')),
        { message: 'app.alpha: #todo 与 metadata.todo 原因必须成对声明' },
      )
      await assert.rejects(
        ArchitectureRules.declarations(markedSource.replace(' #tool #gap', ' #tool')),
        { message: 'app.alpha: #gap 与 metadata.gap 原因必须成对声明' },
      )
      await assert.rejects(
        ArchitectureRules.declarations(markedSource.replace("gap '未实现：读取入口缺失' ", '')),
        { message: 'app.alpha: #gap 与 metadata.gap 原因必须成对声明' },
      )
      await assert.rejects(
        ArchitectureRules.declarations(markedSource.replace('app.beta #gap', 'app.beta')),
        { message: 'app.alpha->app.beta: #gap 与 metadata.gap 原因必须成对声明' },
      )
      assert.equal(
        CheckArchitecture.files(
          {
            ...model,
            containers: model.containers.map(
              (container: ArchitectureContainer): ArchitectureContainer => ({
                ...container,
                sourceRelativePath: container.sourceRelativePath + '/missing',
              }),
            ),
          },
          rootFullPath,
        ),
        1,
      )
      WorkspaceFiles.requireStaged(model, rootFullPath)
      writeFileSync(join(rootFullPath, 'tools/beta/ignored.ts'), 'export class Ignored {}')
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(model, rootFullPath)
      }, /治理输入尚未全部暂存/)
      rmSync(join(rootFullPath, 'tools/beta/ignored.ts'))
      const buildSource: string = ArchitectureFilesTest.source(
        " build = container 'build' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['{package.json,pnpm-workspace.yaml}'] } }\n app -> build",
      )
      writeFileSync(join(rootFullPath, 'tools/model.c4'), buildSource)
      writeFileSync(join(rootFullPath, 'package.json'), 'original')
      writeFileSync(join(rootFullPath, 'pnpm-workspace.yaml'), 'original')
      Git.run(rootFullPath, ['add', '.'])
      const buildRevision: string = Git.run(rootFullPath, ['write-tree'])
      const buildModel: ArchitectureDeclarations = await ArchitectureRules.declarations(buildSource)
      WorkspaceFiles.requireStaged(buildModel, rootFullPath)
      writeFileSync(join(rootFullPath, 'package.json'), 'changed')
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(buildModel, rootFullPath)
      }, /治理输入尚未全部暂存/)
      writeFileSync(join(rootFullPath, 'package.json'), 'original')
      const commit: string = Git.run(rootFullPath, [
        '-c',
        'user.name=Architecture Test',
        '-c',
        'user.email=architecture@example.invalid',
        'commit-tree',
        buildRevision,
        '-m',
        'snapshot fixture',
      ])
      Git.run(rootFullPath, ['update-ref', 'HEAD', commit])
      rmSync(join(rootFullPath, 'package.json'))
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(buildModel, rootFullPath)
      }, /治理输入尚未全部暂存/)
      rmSync(join(rootFullPath, 'tools/beta/second.ts'))
      Git.run(rootFullPath, ['add', '-u'])
      const removed: string = Git.run(rootFullPath, ['write-tree'])
      const removedFiles: string[] = Git.run(rootFullPath, [
        'ls-tree',
        '-r',
        '--name-only',
        '-z',
        removed,
      ])
        .split('\0')
        .filter(Boolean)
      assert.ok(
        ArchitectureFiles.check(model, removedFiles).violations.some((message: string): boolean =>
          message.includes('tools/beta/*.ts'),
        ),
      )
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  private static async hiddenInputs(this: void): Promise<void> {
    mkdirSync(resolve('_build/tests'), { recursive: true })
    const rootFullPath: string = mkdtempSync(resolve('_build/tests', 'toolkit-hidden-'))
    try {
      Git.run(rootFullPath, ['init'])
      mkdirSync(join(rootFullPath, 'assets'))
      writeFileSync(join(rootFullPath, 'assets/input'), 'original')
      Git.run(rootFullPath, ['add', '.'])
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
        ArchitectureFilesTest.source(
          " assets = container 'assets' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { files ['assets/'] } }\n app -> assets",
        ),
      )
      WorkspaceFiles.requireStaged(model, rootFullPath)
      writeFileSync(join(rootFullPath, 'assets/.hidden'), 'new')
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(model, rootFullPath)
      }, /治理输入尚未全部暂存/)
      rmSync(join(rootFullPath, 'assets/.hidden'))
      mkdirSync(join(rootFullPath, 'assets/.nested'))
      writeFileSync(join(rootFullPath, 'assets/.nested/input'), 'new')
      assert.throws((): void => {
        WorkspaceFiles.requireStaged(model, rootFullPath)
      }, /治理输入尚未全部暂存/)
    } finally {
      rmSync(rootFullPath, { recursive: true })
    }
  }

  static {
    test(
      '外部系统不冒领仓库文件，现役模块不能借外部标记绕过归属',
      ArchitectureFilesTest.externalSystems,
    )
    test('原生职责描述必填，未知架构字段拒绝', ArchitectureFilesTest.responsibilities)
    test('入口与真实符号绑定遵循源码容器边界', ArchitectureFilesTest.bindings)
    test('文件登记双向覆盖，父级不隐含放行', ArchitectureFilesTest.coverage)
    test('跨元素与同元素重复登记均拒绝', ArchitectureFilesTest.duplicates)
    test('显式目录覆盖隐藏文件，gitlink 不展开', ArchitectureFilesTest.directories)
    test('模型拒绝悬空声明、非法路径和关联', ArchitectureFilesTest.invalidModel)
    test('旧代码按原路径登记为文档并保留模型关联', ArchitectureFilesTest.documents)
    test('受检输入由归属和依赖闭包派生', ArchitectureFilesTest.guardedInputs)
    test('行级路径豁免只覆盖紧邻下一行表达式', ArchitectureFilesTest.lineTagScope)
    test('文件增删以 Git 快照为准，未暂存文件不能补齐声明', ArchitectureFilesTest.snapshot)
    test('目录声明中的隐藏文件与隐藏目录同受快照保护', ArchitectureFilesTest.hiddenInputs)
  }
}
