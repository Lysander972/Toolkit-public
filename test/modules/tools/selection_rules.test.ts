import { test } from 'vitest'
import assert from 'node:assert/strict'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { WorkspaceFiles } from '../../../tools/cli/WorkspaceFiles.ts'
import type {
  ArchitectureDeclarations,
  ArchitectureElement,
} from '../../../tools/governance/derive-rules.ts'
import type { ReadonlyDeep } from 'type-fest'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { cruise } from 'dependency-cruiser'
import type { IReporterOutput } from 'dependency-cruiser'

/** @anchor selfcheck-gate */
export class SelectionTest {
  private static readonly productModel: string =
    'specification { element system\n element container\n element document\n tag tool\n tag common\n tag doc }\nmodel {\n' +
    " app = system 'tools' { description '工具系统；不接管产品' metadata { sourcePath 'tools'\n checks 'selfcheck' }\n" +
    " alpha = container 'alpha' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts'] } } }\n" +
    " product = system 'product' { description '产品系统；不接管治理'\n" +
    " model = container 'model' { #common\n description '纯计算合同；不读取工作区'\n metadata { sourcePath 'src/model'\n files ['src/model/*.ts'] } }\n" +
    " host = container 'host' { #common\n description '纯计算合同；不读取工作区'\n metadata { sourcePath 'src/host'\n files ['src/host/*.ts'] } } }\n" +
    " specs = system 'specs' { description '规范系统；不定义架构' contracts = container 'contracts' { #doc\n description '规范登记；不定义实现架构'\n metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
    " old = document 'old' { #doc\n metadata { files ['old/'] } }\n product.host -> product.model\n }"

  private constructor() {}
  static {
    test('只有声明源码容器的元素参与执行，产品文档和夹具不参与执行', async (): Promise<void> => {
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
        'specification { element system\n element container\n element document\n tag tool\n tag doc }\nmodel {\n' +
          " app = system 'app' { description '工具系统；不接管产品' metadata { sourcePath 'tools'\n checks 'selfcheck' }\n" +
          " alpha = container 'alpha' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts'] } }\n" +
          " old = document 'old' { #doc\n metadata { files ['old/'] } }\n }\n" +
          " specs = system 'specs' { description '规范系统；不定义架构' registry = container 'registry' { #doc\n description '规范登记；不定义实现架构'\n metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
          " fixtures = document 'fixtures' { #doc\n metadata { files ['test/data/'] } }\n app.alpha -> fixtures\n }",
      )
      assert.deepEqual(
        WorkspaceFiles.scope(model).map(
          (element: ReadonlyDeep<ArchitectureElement>): string => element.id,
        ),
        ['app', 'app.alpha'],
      )
    })

    test('工具和产品源码容器同时进入检查，文档保持只读参考', async (): Promise<void> => {
      const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
        SelectionTest.productModel,
      )
      assert.deepEqual(
        WorkspaceFiles.scope(model).map(
          (element: ReadonlyDeep<ArchitectureElement>): string => element.id,
        ),
        ['app', 'app.alpha', 'product.model', 'product.host'],
      )
      assert.ok(model.guardedElementIds.has('product'))
    })

    test('产品允许边可以导入，产品反向依赖工具被实际源码检查拒绝', async (): Promise<void> => {
      mkdirSync('_build/tests', { recursive: true })
      const rootFullPath: string = mkdtempSync(resolve('_build/tests/product-boundary-'))
      try {
        const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
          SelectionTest.productModel,
        )
        for (const relativePath of ['tools/alpha', 'src/model', 'src/host']) {
          mkdirSync(join(rootFullPath, relativePath), { recursive: true })
        }
        writeFileSync(
          join(rootFullPath, 'tsconfig.json'),
          JSON.stringify({
            compilerOptions: {
              module: 'NodeNext',
              moduleResolution: 'NodeNext',
              target: 'ES2022',
              allowImportingTsExtensions: true,
              noEmit: true,
            },
          }),
          'utf8',
        )
        writeFileSync(join(rootFullPath, 'tools/alpha/Tool.ts'), 'export class Tool {}', 'utf8')
        writeFileSync(join(rootFullPath, 'src/model/Model.ts'), 'export class Model {}', 'utf8')
        writeFileSync(
          join(rootFullPath, 'src/host/Host.ts'),
          "import { Model } from '../model/Model.ts'; export class Host extends Model {}",
          'utf8',
        )
        const check: () => Promise<IReporterOutput> = (): Promise<IReporterOutput> =>
          cruise(['src/host/Host.ts'], {
            baseDir: rootFullPath,
            tsPreCompilationDeps: true,
            tsConfig: { fileName: join(rootFullPath, 'tsconfig.json') },
            validate: true,
            outputType: 'err',
            ruleSet: { forbidden: model.dependencyRules() },
          })
        const accepted: IReporterOutput = await check()
        assert.equal(accepted.exitCode, 0, JSON.stringify(accepted.output))
        writeFileSync(
          join(rootFullPath, 'src/model/Model.ts'),
          "import { Tool } from '../../tools/alpha/Tool.ts'; export class Model extends Tool {}",
          'utf8',
        )
        const rejected: IReporterOutput = await check()
        assert.notEqual(rejected.exitCode, 0)
        assert.ok(typeof rejected.output === 'string')
        assert.ok(rejected.output.includes('product.model-to-app.alpha'))
      } finally {
        rmSync(rootFullPath, { recursive: true })
      }
    })
  }
}
