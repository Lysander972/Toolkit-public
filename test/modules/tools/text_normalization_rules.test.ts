import { test } from 'vitest'
import assert from 'node:assert/strict'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { CheckTextNormalization } from '../../../tools/cli/CheckTextNormalization.ts'
import type { ArchitectureDeclarations } from '../../../tools/governance/derive-rules.ts'
import type { TextViolation } from '../../../tools/cli/CheckTextNormalization.ts'

/** @anchor text-convergence */
export class TextNormalizationTest {
  private constructor() {}

  private static source(): string {
    return (
      'specification { element system\n element container\n element document\n tag pending\n tag tool\n tag doc }\nmodel {\n' +
      " app = system 'app' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools'\n checks 'selfcheck'\n entries ['tools/model.c4']\n files ['tools/model.c4'] }\n" +
      " alpha = container 'alpha' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/alpha'\n files ['tools/alpha/*.ts'] } }\n" +
      " beta = container 'beta' { #tool\n description '治理实现；不承载产品运行状态'\n metadata { sourcePath 'tools/beta'\n files ['tools/beta/*.ts']\n textExempt '夹具保留原始字节' } }\n" +
      " doc = document 'doc' { #doc\n metadata { files ['docs/'] } } }\n" +
      " specs = system 'specs' { description '规范系统；不定义架构' registry = container 'registry' { #doc\n description '规范登记；不定义实现架构'\n metadata { specPath 'specs'\n files ['specs/*.md'] } } }\n" +
      ' app.alpha -> app.beta\n' +
      '\n}\n'
    )
  }

  /** @anchor text-convergence */
  public static async scope(this: void): Promise<void> {
    const model: ArchitectureDeclarations = await ArchitectureRules.declarations(
      TextNormalizationTest.source(),
    )
    assert.deepEqual(CheckTextNormalization.scope(model), ['tools/model.c4', 'tools/alpha/*.ts'])
  }

  /** @anchor text-convergence */
  public static verify(this: void): void {
    const count: (relativePath: string, text: string) => number = (
      relativePath: string,
      text: string,
    ): number => CheckTextNormalization.verify(relativePath, Buffer.from(text, 'utf8')).length
    assert.equal(count('sample.ts', 'export class Example {}\n'), 0)
    assert.equal(count('sample.ts', '\uFEFFexport class Example {}\n'), 1)
    assert.equal(count('sample.ts', 'export class Example {} \n'), 1)
    assert.equal(count('sample.ts', 'export class Example {}\t\n'), 1)
    assert.equal(count('sample.ts', 'export class Example {}\r\n'), 0)
    assert.equal(count('sample.json', '{"a": 1}'), 0)
    assert.equal(count('sample.json', '{"a": 1}\n'), 0)
    assert.equal(count('sample.ts', 'export class Example {}\n\n'), 0)
    assert.equal(count('sample.bin', 'value \u0000\n'), 0)
    const violations: TextViolation[] = CheckTextNormalization.verify(
      'sample.ts',
      Buffer.from('export class Example {}  \nexport class Other {}\t\n', 'utf8'),
    )
    assert.deepEqual(
      violations.map((violation: TextViolation): string => String(violation.line)),
      ['1', '2'],
    )
  }

  static {
    test('文本收敛判定范围由架构模型派生并排除文档与豁免元素', TextNormalizationTest.scope)
    test('文本收敛拒绝 BOM、尾随空白', TextNormalizationTest.verify)
  }
}
