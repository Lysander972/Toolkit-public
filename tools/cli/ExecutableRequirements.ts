import { execFile } from 'node:child_process'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { promisify } from 'node:util'
import { join, resolve } from 'node:path'
import { Writable } from 'node:stream'
import { EventEmitter } from 'node:events'
import { generateMessages } from '@cucumber/gherkin'
import { IdGenerator, SourceMediaType, TestStepResultStatus } from '@cucumber/messages'
import type { Envelope, Location, PickleStep, TestStep, Step } from '@cucumber/messages'
import { formatterHelpers, supportCodeLibraryBuilder } from '@cucumber/cucumber'
import { loadConfiguration, runCucumber } from '@cucumber/cucumber/api'
import type { IResolvedConfiguration, IRunResult } from '@cucumber/cucumber/api'
import { z } from 'zod'
import { Option } from 'effect'
import { RequirementsSteps } from './RequirementsSteps.ts'
import type { RequirementSteps } from './RequirementsSteps.ts'
import { Git } from './Git.ts'

interface SourcePosition {
  readonly line: number
  readonly column: number
}
export interface GherkinBlock {
  readonly fileRelativePath: string
  readonly needId: string
  readonly blockIndex: number
  readonly startLine: number
  readonly startColumn: number
  readonly endLine: number
  readonly endColumn: number
  readonly lines: readonly SourcePosition[]
  readonly source: string
}
interface ExtractedBlocks {
  readonly blocks: readonly GherkinBlock[]
}
export type RequirementsBddMode = 'lint' | 'run'
interface ProcessOutput {
  readonly stdout: string
}

export class ExecutableRequirements {
  private constructor() {}

  private static readonly positionSchema: z.ZodType<SourcePosition> = z.object({
    line: z.number().int().positive(),
    column: z.number().int().positive(),
  })
  private static readonly blockSchema: z.ZodType<GherkinBlock> = z.object({
    fileRelativePath: z.string(),
    needId: z.uuid(),
    blockIndex: z.number().int(),
    startLine: z.number().int().positive(),
    startColumn: z.number().int().positive(),
    endLine: z.number().int().positive(),
    endColumn: z.number().int().positive(),
    lines: z.array(ExecutableRequirements.positionSchema),
    source: z.string(),
  })

  public static async parseFiles(
    repositoryFullPath: string,
    fileRelativePaths: readonly string[],
    projectFullPath: string = repositoryFullPath,
    sourceRelativePath: string = '_build/requirements/sources.json',
  ): Promise<ExtractedBlocks> {
    await mkdir(join(repositoryFullPath, '_build/requirements/gherkin'), { recursive: true })
    const slotFullPath: string = await mkdtemp(
      join(repositoryFullPath, '_build/requirements/gherkin/ast-'),
    )
    try {
      const interpreter: string = process.platform === 'win32' ? 'Scripts/python.exe' : 'bin/python'
      // @non-manual-path 正式 CLI 内部受维护的 AST 适配器入口，不是源码扫描范围。
      const { stdout }: ProcessOutput = await promisify(execFile)(
        resolve(repositoryFullPath, 'requirements/.venv', interpreter),
        [
          '-X',
          'utf8',
          resolve(repositoryFullPath, 'requirements/gherkin_extract.py'),
          resolve(projectFullPath),
          slotFullPath,
          resolve(repositoryFullPath, sourceRelativePath),
          ...fileRelativePaths,
        ],
        {
          cwd: repositoryFullPath,
          env: Git.cleanEnv(),
          windowsHide: true,
          maxBuffer: 16 * 1024 * 1024,
        },
      )
      const result: ExtractedBlocks = z
        .object({ blocks: z.array(ExecutableRequirements.blockSchema) })
        .parse(JSON.parse(stdout))
      for (const block of result.blocks) ExecutableRequirements.lint(block)
      return result
    } finally {
      await rm(slotFullPath, { recursive: true, force: true })
    }
  }

  private static lint(block: GherkinBlock): void {
    if (block.source.split('\n')[0] !== '# language: zh-CN')
      throw new Error(
        `${ExecutableRequirements.location(block, { line: 1, column: 1 })} 必须使用 # language: zh-CN`,
      )
    const messages: readonly Envelope[] = generateMessages(
      block.source,
      block.fileRelativePath,
      SourceMediaType.TEXT_X_CUCUMBER_GHERKIN_PLAIN,
      {
        newId: IdGenerator.uuid(),
        includeGherkinDocument: true,
        includePickles: true,
        includeSource: false,
      },
    )
    for (const message of messages) {
      if (message.parseError !== undefined)
        throw new Error(
          `${ExecutableRequirements.location(block, ExecutableRequirements.positionSchema.parse(message.parseError.source.location))} ${message.parseError.message}`,
        )
    }
    if (!messages.some((message: Envelope): boolean => message.pickle !== undefined))
      throw new Error(
        `${block.fileRelativePath}:${String(block.startLine)}:${String(block.startColumn)} [${block.needId}] Gherkin 代码块不包含可执行场景`,
      )
  }

  public static async run(
    repositoryFullPath: string,
    mode: RequirementsBddMode,
    fileRelativePaths: readonly string[],
    projectFullPath: string = repositoryFullPath,
    register: (steps: RequirementSteps) => void = RequirementsSteps.register,
    sourceRelativePath: string = '_build/requirements/sources.json',
  ): Promise<number> {
    const { blocks }: ExtractedBlocks = await ExecutableRequirements.parseFiles(
      repositoryFullPath,
      fileRelativePaths,
      projectFullPath,
      sourceRelativePath,
    )
    if (mode === 'lint' || blocks.length === 0) return 0
    const slotFullPath: string = await mkdtemp(
      join(repositoryFullPath, '_build/requirements/gherkin/run-'),
    )
    try {
      const paths: string[] = []
      for (const [index, block] of blocks.entries()) {
        const featureFullPath: string = join(slotFullPath, `${String(index)}.feature`)
        await writeFile(featureFullPath, block.source, 'utf8')
        paths.push(featureFullPath)
      }
      const { runConfiguration }: IResolvedConfiguration = await loadConfiguration(
        {
          file: false,
          provided: {
            paths,
            strict: true,
            retry: 0,
            parallel: 0,
            format: ['message'],
            publish: false,
          },
        },
        { cwd: repositoryFullPath },
      )
      supportCodeLibraryBuilder.reset(repositoryFullPath, IdGenerator.uuid())
      register(supportCodeLibraryBuilder.methods)
      const support: ReturnType<typeof supportCodeLibraryBuilder.finalize> =
        supportCodeLibraryBuilder.finalize()
      const broadcaster: EventEmitter = new EventEmitter()
      const collector: InstanceType<typeof formatterHelpers.EventDataCollector> =
        new formatterHelpers.EventDataCollector(broadcaster)
      const output: Writable = new Writable({
        write(_chunk: Buffer, _encoding: BufferEncoding, callback: () => void): void {
          callback()
        },
      })
      const result: IRunResult = await runCucumber(
        { ...runConfiguration, support },
        { cwd: repositoryFullPath, stdout: output },
        (message: Envelope): void => {
          broadcaster.emit('envelope', message)
        },
      )
      const failures: string[] = ExecutableRequirements.failures(
        repositoryFullPath,
        blocks,
        paths,
        collector,
      )
      if (!result.success || failures.length > 0) throw new Error(failures.join('\n'))
      process.stdout.write(`${String(blocks.length)} 个 RST BDD 块执行通过\n`)
      return 0
    } finally {
      await rm(slotFullPath, { recursive: true, force: true })
    }
  }

  private static failures(
    repositoryFullPath: string,
    blocks: readonly GherkinBlock[],
    paths: readonly string[],
    collector: InstanceType<typeof formatterHelpers.EventDataCollector>,
  ): string[] {
    const failures: string[] = []
    for (const attempt of collector.getTestCaseAttempts()) {
      const block: GherkinBlock = ExecutableRequirements.blockSchema.parse(
        blocks[
          paths.findIndex(
            (path: string): boolean => resolve(repositoryFullPath, attempt.pickle.uri) === path,
          )
        ],
      )
      const steps: Record<string, Step> = formatterHelpers.GherkinDocumentParser.getGherkinStepMap(
        attempt.gherkinDocument,
      )
      const scenarios: Record<string, Location> =
        formatterHelpers.GherkinDocumentParser.getGherkinScenarioLocationMap(
          attempt.gherkinDocument,
        )
      for (const [id, result] of Object.entries(attempt.stepResults)) {
        if (result.status === TestStepResultStatus.PASSED) continue
        const testStep: TestStep = Option.getOrThrow(
          Option.fromNullishOr(
            attempt.testCase.testSteps.find((entry: TestStep): boolean => entry.id === id),
          ),
        )
        const pickleStep: Option.Option<PickleStep> = Option.fromNullishOr(
          attempt.pickle.steps.find(
            (entry: PickleStep): boolean => entry.id === testStep.pickleStepId,
          ),
        )
        const location: SourcePosition = ExecutableRequirements.positionSchema.parse(
          Option.match(pickleStep, {
            onNone: (): Location =>
              Option.getOrThrow(
                Option.fromNullishOr(scenarios[z.string().parse(attempt.pickle.astNodeIds[0])]),
              ),
            onSome: (step: PickleStep): Location =>
              Option.getOrThrow(Option.fromNullishOr(steps[z.string().parse(step.astNodeIds[0])]))
                .location,
          }),
        )
        failures.push(
          `${ExecutableRequirements.location(block, location)} ${attempt.pickle.name} / ${Option.match(pickleStep, { onNone: (): string => 'hook', onSome: (step: PickleStep): string => step.text })}: ${result.status} ${result.message === undefined ? '' : result.message}`,
        )
      }
    }
    return failures
  }

  private static location(block: GherkinBlock, location: SourcePosition): string {
    const original: SourcePosition = ExecutableRequirements.positionSchema.parse(
      block.lines[location.line - 1],
    )
    return `${block.fileRelativePath}:${String(original.line)}:${String(original.column + location.column - 1)} [${block.needId}]`
  }
}
