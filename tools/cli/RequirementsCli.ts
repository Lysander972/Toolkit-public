import { createHash } from 'node:crypto'
import { existsSync, globSync, readFileSync } from 'node:fs'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { basename, dirname, join, relative } from 'node:path'
import writeFileAtomic from 'write-file-atomic'
import { z } from 'zod'
import { parse } from 'smol-toml'
import { Option } from 'effect'
import { HostProcess } from './HostProcess.ts'
import { Git } from './Git.ts'
import { RequirementsSources } from './RequirementsSources.ts'
import type { RequirementsCoverage } from './RequirementsSources.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'
import type { RequirementsBddMode } from './ExecutableRequirements.ts'
import type {
  ReviewSnapshot,
  RequirementsReviewStore as ReviewStore,
} from './RequirementsReviewStore.ts'

type RequirementsCommand =
  | 'prepare'
  | 'check'
  | 'build'
  | 'diff'
  | 'verify'
  | 'ubc'
  | 'review'
  | 'coverage'
  | 'rewrite-start'
  | 'bdd'
type RequirementsPlatform = 'win32-x64' | 'linux-x64'

interface RequirementsDistribution {
  readonly file: string
  readonly sha256: string
}

interface VerdictScore {
  readonly score: number
  readonly reason: string
  readonly suggestion: Option.Option<string>
}

interface RequirementsQuality {
  readonly criteria_file: string
}

interface RequirementsConfiguration {
  readonly quality: RequirementsQuality
}

interface RequirementsSourceConfiguration {
  readonly source: RequirementsSourceDirectory
}

interface RequirementsSourceDirectory {
  readonly dir: string
  readonly include: readonly string[]
}

interface ReviewCriterion {
  readonly id: string
  readonly max_score: number
}

interface ReviewCriteria {
  readonly criterion: readonly ReviewCriterion[]
}

interface StoredVerdict {
  readonly need: string
  readonly axes: Readonly<Record<string, VerdictScore>>
}

/** @anchor cli-boundary */
export class RequirementsCli {
  private constructor() {}

  /** @anchor cli-boundary */
  public static async run(repositoryFullPath: string, args: readonly string[]): Promise<number> {
    const command: RequirementsCommand = z
      .enum([
        'prepare',
        'check',
        'build',
        'diff',
        'verify',
        'ubc',
        'review',
        'coverage',
        'rewrite-start',
        'bdd',
      ])
      .parse(args[0])
    if (command === 'rewrite-start') {
      if (args.length > 2) throw new Error('rewrite-start 仅接受待重写分支')
      const ref: string = args.length === 2 ? z.string().min(1).parse(args[1]) : 'HEAD'
      const { RequirementsReviewStore }: typeof import('./RequirementsReviewStore.ts') =
        await import('./RequirementsReviewStore.ts')
      const store: ReviewStore = new RequirementsReviewStore(repositoryFullPath)
      try {
        store.startRewrite(
          Git.run(repositoryFullPath, ['rev-parse', '--verify', ref + '^{commit}']),
        )
      } finally {
        store.close()
      }
      return 0
    }
    if (command === 'bdd' && (args.length < 2 || args.length > 3))
      throw new Error('bdd 需要 lint 或 run，可选 HEAD 或 index')
    const reviewSnapshot: Option.Option<ReviewSnapshot> =
      command === 'review'
        ? Option.some(
            (await import('./RequirementsReviewProtocol.ts')).RequirementsReview.snapshot(
              repositoryFullPath,
            ),
          )
        : Option.none()
    if (command === 'diff') {
      return HostProcess.run(
        {
          file: 'git',
          args: ['--no-pager', 'diff', ...args.slice(1), '--', 'requirements'],
          windowsHide: true,
        },
        repositoryFullPath,
      )
    }
    if (command === 'prepare') {
      if (args.length !== 1) throw new Error('prepare 不接受额外参数')
      return RequirementsCli.prepare(repositoryFullPath)
    }
    const selected: Option.Option<string> =
      command === 'review'
        ? Option.some('index')
        : command === 'bdd' && args.length === 3
          ? Option.some(z.enum(['HEAD', 'index']).parse(args[2]))
          : command === 'verify' && args.length === 2
            ? Option.some(z.enum(['HEAD', 'index']).parse(args[1]))
            : Option.none()
    const revision: Option.Option<string> = Option.map(selected, (input: string): string =>
      Git.run(
        repositoryFullPath,
        input === 'index' ? ['write-tree'] : ['rev-parse', 'HEAD^{tree}'],
      ),
    )
    if (
      command === 'coverage' ||
      command === 'check' ||
      command === 'verify' ||
      command === 'review'
    ) {
      const coverage: RequirementsCoverage = await RequirementsSources.coverage(
        repositoryFullPath,
        revision,
      )
      if (command === 'coverage') {
        process.stdout.write(JSON.stringify(coverage, null, 2) + '\n')
        return coverage.unbound.length === 0 && coverage.unassigned.length === 0 ? 0 : 1
      }
      if (coverage.unassigned.length > 0)
        throw new Error('现役源码未声明规范归属：\n' + coverage.unassigned.join('\n'))
      if (coverage.unbound.length > 0)
        throw new Error('已迁移范围的正文未绑定：\n' + coverage.unbound.join('\n'))
      if (coverage.pending.length > 0)
        process.stderr.write('尚未迁移的规范范围：' + coverage.pending.join(', ') + '\n')
    }
    await mkdir(join(repositoryFullPath, '_build/requirements/inputs'), { recursive: true })
    const directoryFullPath: string = await mkdtemp(
      join(repositoryFullPath, '_build/requirements/inputs/sources-'),
    )
    const sourceRelativePath: string =
      '_build/requirements/inputs/' + basename(directoryFullPath) + '/needs.json'
    try {
      await RequirementsSources.generate(repositoryFullPath, sourceRelativePath, revision)
      const projectFullPath: string = Option.isSome(revision)
        ? join(directoryFullPath, 'project')
        : repositoryFullPath
      if (Option.isSome(revision)) {
        const entries: readonly WorkspaceTreeEntry[] = WorkspaceTree.list(
          repositoryFullPath,
          revision.value,
          [RequirementsSources.projectRelativePath],
        )
        const contents: ReadonlyMap<string, string> = Git.blobs(
          repositoryFullPath,
          entries.map(
            (entry: WorkspaceTreeEntry): string => revision.value + ':' + entry.relativePath,
          ),
        )
        for (const entry of entries) {
          const destinationFullPath: string = join(projectFullPath, entry.relativePath)
          await mkdir(dirname(destinationFullPath), { recursive: true })
          await writeFile(
            destinationFullPath,
            Option.getOrThrow(
              Option.fromNullishOr(contents.get(revision.value + ':' + entry.relativePath)),
            ),
          )
        }
      }
      const result: number = await RequirementsCli.execute(
        repositoryFullPath,
        args,
        command,
        sourceRelativePath,
        projectFullPath,
        reviewSnapshot,
      )
      if (Option.isSome(revision)) {
        const after: string = Git.run(
          repositoryFullPath,
          Option.getOrThrow(selected) === 'index' ? ['write-tree'] : ['rev-parse', 'HEAD^{tree}'],
        )
        if (after !== revision.value) throw new Error('检视期间受检 Git tree 已变化')
      }
      return result
    } finally {
      await rm(directoryFullPath, { recursive: true })
    }
  }

  private static async execute(
    repositoryFullPath: string,
    args: readonly string[],
    command: RequirementsCommand,
    sourceRelativePath: string,
    projectFullPath: string,
    reviewSnapshot: Option.Option<ReviewSnapshot>,
  ): Promise<number> {
    if (command === 'bdd' || command === 'check') {
      const result: number = await RequirementsCli.bdd(
        repositoryFullPath,
        projectFullPath,
        sourceRelativePath,
        command === 'bdd' ? z.enum(['lint', 'run']).parse(args[1]) : 'lint',
      )
      if (command === 'bdd' || result !== 0) return result
    }
    const sourceOverride: string =
      'needs.import_keys.toolkit_sources=' +
      JSON.stringify(
        relative(
          join(projectFullPath, RequirementsSources.projectRelativePath),
          join(repositoryFullPath, sourceRelativePath),
        ).replaceAll('\\', '/'),
      )
    if (command === 'build') {
      const slot: string = z
        .string()
        .min(1)
        .refine(
          (value: string): boolean => basename(value) === value && value !== '.' && value !== '..',
        )
        .parse(args[1])
      if (args.length !== 2) throw new Error('build 需要构建分组名')
      const executable: string =
        process.platform === 'win32' ? 'Scripts/sphinx-build.exe' : 'bin/sphinx-build'
      // @non-manual-path uv 项目原生虚拟环境的 Sphinx 入口，不是源码枚举范围。
      return HostProcess.run(
        {
          file: join(repositoryFullPath, 'requirements/.venv', executable),
          args: [
            '-W',
            '--keep-going',
            '-b',
            'html',
            '-D',
            'toolkit_sources_file=../' + sourceRelativePath,
            '-d',
            '_build/requirements/sphinx/' + slot + '/doctrees',
            'requirements',
            '_build/requirements/sphinx/' + slot + '/html',
          ],
          windowsHide: true,
        },
        repositoryFullPath,
      )
    }
    const distribution: RequirementsDistribution = RequirementsCli.distribution()
    const file: string = join(repositoryFullPath, '_build/requirements', distribution.file)
    // @non-manual-path 固定的 Sphinx 与 ubCode 项目入口，不是源码枚举范围
    const cwd: string = join(projectFullPath, 'requirements')
    if (command === 'review') {
      const { RequirementsReview }: typeof import('./RequirementsReviewProtocol.ts') =
        await import('./RequirementsReviewProtocol.ts')
      return RequirementsReview.run(
        repositoryFullPath,
        cwd,
        file,
        sourceOverride,
        args.slice(1),
        Option.getOrThrow(reviewSnapshot),
      )
    }
    if (command === 'verify') {
      if (args.length > 2) throw new Error('verify 仅接受 HEAD 或 index')
      const result: number = await HostProcess.run(
        {
          file,
          args: ['agent', 'gaps', '-c', sourceOverride],
          windowsHide: true,
        },
        cwd,
      )
      if (result !== 0) return result
      const verdicts: number = await HostProcess.run(
        { file, args: ['agent', 'verdict-check', '-c', sourceOverride], windowsHide: true },
        cwd,
      )
      if (verdicts !== 0) return verdicts
      await RequirementsCli.bdd(repositoryFullPath, projectFullPath, sourceRelativePath, 'lint')
      const { RequirementsReview }: typeof import('./RequirementsReviewProtocol.ts') =
        await import('./RequirementsReviewProtocol.ts')
      RequirementsReview.verify(cwd, file, sourceOverride)
      for (const warning of RequirementsCli.warnings(cwd)) process.stderr.write(warning + '\n')
      return 0
    }
    if (command === 'ubc') {
      if (args.length < 2) throw new Error('ubc 需要正式子命令')
      if ((args.length === 2 && args[1] === '--help') || args[1] === 'help')
        return HostProcess.run(
          { file, args: [...args.slice(1)], windowsHide: true },
          repositoryFullPath,
        )
      if (
        args[1] === 'license' &&
        ['show', 'config-file', 'clean'].includes(z.string().parse(args[2]))
      )
        return HostProcess.run(
          { file, args: [...args.slice(1)], windowsHide: true },
          repositoryFullPath,
        )
      const readCommands: readonly string[] = [
        'context',
        'audit',
        'impact',
        'next',
        'status',
        'gaps',
        'trace',
        'review-brief',
        'verdict-check',
        'config-validate',
        'doctor',
        'prompt',
      ]
      if (args[1] !== 'agent' || !readCommands.includes(z.string().parse(args[2])))
        throw new Error('ubc 仅透传只读查询；登记使用 requirements review record --stdin')
      return HostProcess.run(
        { file, args: [...args.slice(1), '-c', sourceOverride], windowsHide: true },
        cwd,
      )
    }
    if (args.length !== 1) throw new Error('check 不接受额外参数')
    const checked: number = await HostProcess.run(
      { file, args: ['check', '--deny', 'warning', '-c', sourceOverride], windowsHide: true },
      cwd,
    )
    if (checked !== 0) return checked
    return HostProcess.run(
      { file, args: ['agent', 'doctor', '-c', sourceOverride], windowsHide: true },
      cwd,
    )
  }

  private static async bdd(
    repositoryFullPath: string,
    projectFullPath: string,
    sourceRelativePath: string,
    mode: RequirementsBddMode,
  ): Promise<number> {
    const directoryFullPath: string = join(projectFullPath, RequirementsSources.projectRelativePath)
    const configuration: RequirementsSourceConfiguration = z
      .object({ source: z.object({ dir: z.string(), include: z.array(z.string()) }) })
      .parse(parse(readFileSync(join(directoryFullPath, 'ubproject.toml'), 'utf8')))
    const files: readonly string[] = globSync([...configuration.source.include], {
      cwd: join(directoryFullPath, configuration.source.dir),
    })
      .sort()
      .map((file: string): string =>
        join(RequirementsSources.projectRelativePath, configuration.source.dir, file).replaceAll(
          '\\',
          '/',
        ),
      )
    const { ExecutableRequirements }: typeof import('./ExecutableRequirements.ts') =
      await import('./ExecutableRequirements.ts')
    return ExecutableRequirements.run(
      repositoryFullPath,
      mode,
      files,
      projectFullPath,
      (await import('./RequirementsSteps.ts')).RequirementsSteps.register,
      sourceRelativePath,
    )
  }

  /** @anchor cli-boundary */
  public static warnings(projectFullPath: string): readonly string[] {
    const configuration: RequirementsConfiguration = z
      .object({ quality: z.object({ criteria_file: z.string() }) })
      .parse(parse(readFileSync(join(projectFullPath, 'ubproject.toml'), 'utf8')))
    const criteria: ReviewCriteria = z
      .object({ criterion: z.array(z.object({ id: z.string(), max_score: z.number().int() })) })
      .parse(
        parse(readFileSync(join(projectFullPath, configuration.quality.criteria_file), 'utf8')),
      )
    const warnings: string[] = []
    for (const relativePath of globSync('.pharaoh/verdicts/*.json', {
      cwd: projectFullPath,
    }).sort()) {
      const verdict: StoredVerdict = z
        .object({
          need: z.string(),
          axes: z.record(
            z.string(),
            z.object({
              score: z.number().int(),
              reason: z.string(),
              suggestion: z.string().optional().transform(Option.fromNullishOr),
            }),
          ),
        })
        .parse(JSON.parse(readFileSync(join(projectFullPath, relativePath), 'utf8')))
      for (const criterion of criteria.criterion) {
        const axis: VerdictScore = Option.getOrThrow(
          Option.fromNullishOr(verdict.axes[criterion.id]),
        )
        if (axis.score < criterion.max_score) {
          warnings.push(
            'WARN ' +
              verdict.need +
              '/' +
              criterion.id +
              ' ' +
              String(axis.score) +
              '/' +
              String(criterion.max_score) +
              '：' +
              axis.reason +
              Option.match(axis.suggestion, {
                onNone: (): string => '',
                onSome: (suggestion: string): string => '；建议：' + suggestion,
              }),
          )
        }
      }
    }
    return warnings
  }

  private static distribution(): RequirementsDistribution {
    const platform: RequirementsPlatform = z
      .enum(['win32-x64', 'linux-x64'])
      .parse(process.platform + '-' + process.arch)
    return {
      'win32-x64': {
        file: 'ubc-windows-x64-0.37.0.exe',
        sha256: 'c7ab5ac2efbf27458c1479a7002e1d18dcbb7bdbeb92b70dea1ce60301b1dc0e',
      },
      'linux-x64': {
        file: 'ubc-linux-x64-0.37.0',
        sha256: '5ee1869538dcecc9984fe15ae771125b5afd4a94a7ff090f70a178bd67403e67',
      },
    }[platform]
  }

  private static async prepare(repositoryFullPath: string): Promise<number> {
    const distribution: RequirementsDistribution = RequirementsCli.distribution()
    const target: string = join(repositoryFullPath, '_build/requirements', distribution.file)
    await mkdir(join(repositoryFullPath, '_build/requirements'), { recursive: true })
    if (!existsSync(target)) {
      const response: Response = await fetch(
        'https://download.useblocks.com/ubc/0.37.0/' + distribution.file,
      )
      if (!response.ok) throw new Error('ubc 下载失败：' + String(response.status))
      const data: Buffer = Buffer.from(await response.arrayBuffer())
      if (createHash('sha256').update(data).digest('hex') !== distribution.sha256)
        throw new Error('ubc 下载摘要与锁定版本不符')
      await writeFileAtomic(target, data, { mode: 0o755 })
    }
    if (createHash('sha256').update(readFileSync(target)).digest('hex') !== distribution.sha256)
      throw new Error('ubc 文件摘要与锁定版本不符：' + target)
    const result: number = await HostProcess.run(
      {
        file: 'uv',
        args: [
          'sync',
          '--no-config',
          '--default-index',
          'https://pypi.org/simple',
          '--project',
          'requirements',
          '--locked',
          '--python',
          '3.13.12',
          '--managed-python',
        ],
        windowsHide: true,
      },
      repositoryFullPath,
    )
    if (result !== 0) return result
    await RequirementsSources.generate(
      repositoryFullPath,
      '_build/requirements/sources.json',
      Option.none(),
    )
    return 0
  }
}
