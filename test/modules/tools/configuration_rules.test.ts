import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { z } from 'zod'
import { ArchitectureRules } from '../../../tools/governance/derive-rules.ts'
import { CheckConfiguration } from '../../../tools/cli/CheckConfiguration.ts'
import { Git } from '../../../tools/cli/Git.ts'
import type { ArchitectureDeclarations } from '../../../tools/governance/derive-rules.ts'

/** @anchor configuration-model */
export class ConfigurationRulesTest {
  private constructor() {}

  static {
    test('原生忽略只允许文档，现役实现和测试被忽略时报告具体路径', async (): Promise<void> => {
      mkdirSync(resolve('_build/tests'), { recursive: true })
      const repositoryFullPath: string = mkdtempSync(resolve('_build/tests/configuration-'))
      try {
        for (const directory of ['tools', 'src/toolkit', 'test/modules/tools', 'res', '.github'])
          mkdirSync(join(repositoryFullPath, directory), { recursive: true })
        writeFileSync(join(repositoryFullPath, 'README.md'), '# Project\n')
        writeFileSync(join(repositoryFullPath, '.github/pull_request_template.md'), '## 变更\n')
        const manifest: { scripts: Record<string, string> } = z
          .object({ scripts: z.record(z.string(), z.string()) })
          .parse(JSON.parse(readFileSync('package.json', 'utf8')))
        writeFileSync(
          join(repositoryFullPath, 'package.json'),
          JSON.stringify({ type: 'module', scripts: manifest.scripts }),
        )
        writeFileSync(join(repositoryFullPath, 'src/toolkit/sample.ts'), 'export class Sample {}\n')
        writeFileSync(join(repositoryFullPath, 'test/modules/tools/sample.test.ts'), 'export {}\n')
        writeFileSync(join(repositoryFullPath, 'res/reference.json'), '{"original":true}\n')
        writeFileSync(
          join(repositoryFullPath, 'tools/tsconfig.json'),
          JSON.stringify({
            files: [
              '../src/toolkit/sample.ts',
              '../test/modules/tools/sample.test.ts',
              '../knip.config.ts',
            ],
          }),
        )
        writeFileSync(
          join(repositoryFullPath, 'knip.config.ts'),
          'export default { workspaces: { ".": { entry: ["test/modules/tools/*.test.ts"], project: ["src/toolkit/*.ts", "test/modules/tools/*.test.ts"] } } }\n',
        )
        writeFileSync(
          join(repositoryFullPath, 'vitest.config.mjs'),
          'export default { test: { include: ["test/modules/tools/*.test.ts"] } }\n',
        )
        writeFileSync(
          join(repositoryFullPath, 'eslint.config.mjs'),
          'export default [{ files: ["src/toolkit/*.ts", "test/modules/tools/*.test.ts", "knip.config.ts"] }]\n',
        )
        writeFileSync(join(repositoryFullPath, '.prettierignore'), 'res/reference.json\n')
        Git.run(repositoryFullPath, ['init'])
        Git.run(repositoryFullPath, ['add', '.'])
        const model: ArchitectureDeclarations = await ArchitectureRules.declarations(`
specification { element system element container element document tag tool tag common tag test tag doc }
model {
  tools = system '检查' {
    #tool
    description '测试范围'
    metadata { sourcePath 'tools' checks 'selfcheck' specPath ['specs'] files ['tools/tsconfig.json'] }
    implementation = container '实现' {
      #common
      description '实际实现'
      metadata { sourcePath 'src/toolkit' files ['src/toolkit/*.ts'] }
    }
    tests = container '测试' {
      #test
      description '实际测试'
      metadata { sourcePath 'test/modules/tools' files ['test/modules/tools/*.test.ts'] }
    }
    reference = document '原文' {
      #doc
      metadata { files ['res/reference.json', 'README.md', '.github/*.md'] }
    }
  }
}`)
        const diagnostics: string[] = []
        const stderr: MockInstance = vi
          .spyOn(process.stderr, 'write')
          .mockImplementation((value: string | Uint8Array): boolean => {
            diagnostics.push(value.toString())
            return true
          })
        try {
          assert.equal(
            await CheckConfiguration.run(model, repositoryFullPath),
            0,
            diagnostics.join(''),
          )
          for (const relativePath of [
            'src/toolkit/sample.ts',
            'test/modules/tools/sample.test.ts',
          ]) {
            diagnostics.length = 0
            writeFileSync(
              join(repositoryFullPath, '.prettierignore'),
              `res/reference.json\n${relativePath}\n`,
            )
            assert.equal(await CheckConfiguration.run(model, repositoryFullPath), 1)
            assert.deepEqual(diagnostics, [`Prettier: 非文档文件不得忽略: ${relativePath}\n`])
          }
          diagnostics.length = 0
          writeFileSync(join(repositoryFullPath, '.prettierignore'), '')
          assert.equal(
            await CheckConfiguration.run(model, repositoryFullPath),
            0,
            diagnostics.join(''),
          )
        } finally {
          stderr.mockRestore()
        }
      } finally {
        rmSync(repositoryFullPath, { recursive: true })
      }
    })
  }
}
