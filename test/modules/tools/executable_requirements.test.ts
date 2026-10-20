import { appendFile, mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import type { GherkinBlock } from '../../../tools/cli/ExecutableRequirements.ts'
import type { RequirementSteps } from '../../../tools/cli/RequirementsSteps.ts'
import { ExecutableRequirements } from '../../../tools/cli/ExecutableRequirements.ts'
import { RequirementsCli } from '../../../tools/cli/RequirementsCli.ts'

export class ExecutableRequirementsTests {
  private constructor() {}
  private static async fixture(
    this: void,
    source: string,
    check: (projectFullPath: string) => Promise<void>,
  ): Promise<void> {
    await mkdir(resolve('_build/tests'), { recursive: true })
    const projectFullPath: string = await mkdtemp(resolve('_build/tests/bdd-'))
    try {
      await mkdir(join(projectFullPath, 'requirements'))
      await writeFile(
        join(projectFullPath, 'requirements/conf.py'),
        [
          'extensions = ["sphinx_needs"]',
          'root_doc = "spec"',
          'needs_types = [{"directive": "design", "title": "Design", "prefix": "", "color": "#ffffff", "style": "node"}]',
          'needs_id_regex = "^[0-9a-f-]+$"',
          'def setup(app):',
          '    app.add_config_value("toolkit_sources_file", "", "env")',
        ].join('\n'),
      )
      await writeFile(join(projectFullPath, 'requirements/spec.rst'), source)
      await check(projectFullPath)
    } finally {
      await rm(projectFullPath, { recursive: true, force: true })
    }
  }

  private static design(this: void, source: string, language: string = 'gherkin'): string {
    return `.. design:: 测试\n   :id: 11111111-1111-4111-8111-111111111111\n\n   .. code-block:: ${language}\n\n${source
      .split('\n')
      .map((line: string): string => '      ' + line)
      .join('\n')}\n`
  }

  static {
    const repositoryFullPath: string = resolve('.')
    const needId: string = '11111111-1111-4111-8111-111111111111'
    const feature: string =
      '# language: zh-CN\n功能: 中文规范\n  场景: 校验路径\n    当 校验工程相对路径 "地图/123/code"\n    那么 路径校验成功并返回 "地图/123/code"'
    const fixture: typeof ExecutableRequirementsTests.fixture = ExecutableRequirementsTests.fixture
    const design: typeof ExecutableRequirementsTests.design = ExecutableRequirementsTests.design
    describe('RST executable requirements', (): void => {
      it('runs the normative scenarios through the real CLI and through Vitest', async (): Promise<void> => {
        expect(await RequirementsCli.run(repositoryFullPath, ['bdd', 'run'])).toBe(0)
      })

      it('preserves Chinese, CRLF, nested RST and multiple blocks with exact source locations', async (): Promise<void> => {
        const source: string =
          design(feature) +
          '\n' +
          design(feature).replace(needId, '22222222-2222-4222-8222-222222222222') +
          '\n.. note::\n\n' +
          design(feature)
            .replace(needId, '33333333-3333-4333-8333-333333333333')
            .split('\n')
            .map((line: string): string => '   ' + line)
            .join('\n')
        await fixture(
          source.replaceAll('\n', '\r\n'),
          async (projectFullPath: string): Promise<void> => {
            const { blocks }: { readonly blocks: readonly GherkinBlock[] } =
              await ExecutableRequirements.parseFiles(
                repositoryFullPath,
                ['requirements/spec.rst'],
                projectFullPath,
              )
            expect(blocks).toHaveLength(3)
            expect(blocks[0]?.needId).toBe(needId)
            expect(blocks[0]?.lines[0]).toEqual({ line: 6, column: 7 })
            expect(blocks[0]?.source).toBe(feature)
            expect(blocks[2]?.source).toBe(feature)
          },
        )
      })

      it.each(['python', 'typescript', 'Gherkin', ''])(
        'ignores the %s language',
        async (language: string): Promise<void> => {
          await fixture(
            design('这不是 Gherkin', language),
            async (projectFullPath: string): Promise<void> => {
              await appendFile(
                join(projectFullPath, 'requirements/conf.py'),
                '\nhighlight_language = "gherkin"\n',
              )
              expect(
                (
                  await ExecutableRequirements.parseFiles(
                    repositoryFullPath,
                    ['requirements/spec.rst'],
                    projectFullPath,
                  )
                ).blocks,
              ).toEqual([])
            },
          )
        },
      )

      it('ignores plain literal blocks', async (): Promise<void> => {
        await fixture(
          '普通文本::\n\n   # language: zh-CN\n   功能: 示例\n',
          async (projectFullPath: string): Promise<void> => {
            expect(
              (
                await ExecutableRequirements.parseFiles(
                  repositoryFullPath,
                  ['requirements/spec.rst'],
                  projectFullPath,
                )
              ).blocks,
            ).toEqual([])
          },
        )
      })

      it('rejects recognized blocks outside design', async (): Promise<void> => {
        await fixture(
          '.. code-block:: gherkin\n\n   # language: zh-CN\n',
          async (projectFullPath: string): Promise<void> => {
            await expect(
              ExecutableRequirements.parseFiles(
                repositoryFullPath,
                ['requirements/spec.rst'],
                projectFullPath,
              ),
            ).rejects.toThrow('必须位于 design')
          },
        )
      })

      it.each([
        '',
        '功能: 无方言',
        '# language: en\nFeature: wrong',
        '# language: zh-CN\n功能: 无场景',
      ])(
        'rejects empty, missing dialect and zero-scenario input: %s',
        async (source: string): Promise<void> => {
          await fixture(design(source), async (projectFullPath: string): Promise<void> => {
            await expect(
              ExecutableRequirements.parseFiles(
                repositoryFullPath,
                ['requirements/spec.rst'],
                projectFullPath,
              ),
            ).rejects.toThrow()
          })
        },
      )

      it('reports Gherkin grammar errors at their original RST line and column', async (): Promise<void> => {
        await fixture(
          design('# language: zh-CN\n功能: 错误\n  场景: 错误\n    那么 步骤\n功能: 重复功能'),
          async (projectFullPath: string): Promise<void> => {
            await expect(
              ExecutableRequirements.parseFiles(
                repositoryFullPath,
                ['requirements/spec.rst'],
                projectFullPath,
              ),
            ).rejects.toThrow('requirements/spec.rst:10:')
          },
        )
      })

      it('rejects invalid RST directive arguments', async (): Promise<void> => {
        await fixture(
          design(feature, 'gherkin extra'),
          async (projectFullPath: string): Promise<void> => {
            await expect(
              ExecutableRequirements.parseFiles(
                repositoryFullPath,
                ['requirements/spec.rst'],
                projectFullPath,
              ),
            ).rejects.toThrow()
          },
        )
      })

      it.each(['undefined', 'ambiguous', 'pending', 'skipped', 'assertion'])(
        'fails for Cucumber %s and reports the RST step',
        async (failure: string): Promise<void> => {
          await fixture(
            design('# language: zh-CN\n功能: 执行失败\n  场景: 实际失败\n    当 测试步骤'),
            async (projectFullPath: string): Promise<void> => {
              const register: (steps: RequirementSteps) => void = (
                steps: RequirementSteps,
              ): void => {
                if (failure === 'undefined') return
                if (failure === 'ambiguous') {
                  steps.When('测试步骤', (): void => {})
                  steps.When('测试步骤', (): void => {})
                  return
                }
                steps.When('测试步骤', (): string => {
                  if (failure === 'assertion') {
                    expect(1).toBe(2)
                  }
                  return failure
                })
              }
              await expect(
                ExecutableRequirements.run(
                  repositoryFullPath,
                  'run',
                  ['requirements/spec.rst'],
                  projectFullPath,
                  register,
                ),
              ).rejects.toThrow('requirements/spec.rst:9:11')
            },
          )
        },
      )

      it('fails when a real domain step produces a different result', async (): Promise<void> => {
        await fixture(
          design(feature.replace('返回 "地图/123/code"', '返回 "错误结果"')),
          async (projectFullPath: string): Promise<void> => {
            await expect(
              ExecutableRequirements.run(
                repositoryFullPath,
                'run',
                ['requirements/spec.rst'],
                projectFullPath,
              ),
            ).rejects.toThrow('FAILED')
          },
        )
      })

      it('executes standard Doc Strings and data tables', async (): Promise<void> => {
        const source: string =
          '# language: zh-CN\n功能: 参数\n  场景: 结构参数\n    当 文档\n      """\n      中文正文\n      """\n    那么 表格\n      | 名称 |\n      | 中文 |'
        await fixture(design(source), async (projectFullPath: string): Promise<void> => {
          expect(
            await ExecutableRequirements.run(
              repositoryFullPath,
              'run',
              ['requirements/spec.rst'],
              projectFullPath,
              (steps: RequirementSteps): void => {
                steps.When('文档', (text: string): void => {
                  expect(text).toBe('中文正文')
                })
                steps.Then('表格', (table: { raw(): string[][] }): void => {
                  expect(table.raw()).toEqual([['名称'], ['中文']])
                })
              },
            ),
          ).toBe(0)
        })
      })
    })
  }
}
