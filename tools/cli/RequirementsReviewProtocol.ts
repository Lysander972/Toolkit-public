import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, globSync, readFileSync, unlinkSync } from 'node:fs'
import { mkdir } from 'node:fs/promises'
import { join, basename } from 'node:path'
import { isDeepStrictEqual, parseArgs } from 'node:util'
import { createInterface } from 'node:readline'
import { Ajv } from 'ajv'
import type { ValidateFunction } from 'ajv'
import { Option } from 'effect'
import writeFileAtomic from 'write-file-atomic'
import { z } from 'zod'
import { countTokens } from 'gpt-tokenizer/encoding/cl100k_base'
import type { JsonObject } from 'type-fest'
import { Git } from './Git.ts'
import { RequirementsSources } from './RequirementsSources.ts'
import { RequirementsReviewStore } from './RequirementsReviewStore.ts'
import type {
  ReviewPublication,
  ReviewSnapshot,
  ReviewSubject,
  ReviewTicket,
} from './RequirementsReviewStore.ts'

interface NativeNeed {
  readonly id: string
  readonly type: string
  readonly title: string
  readonly body: string
}

interface RelatedNeed extends NativeNeed {
  readonly relation: string
}

interface NativeContext {
  readonly anchor: NativeNeed
  readonly fingerprint: string
  readonly related: readonly RelatedNeed[]
  readonly links: Readonly<Record<string, readonly string[]>>
}

interface ReviewCriterion {
  readonly key: string
  readonly max_score: number
}

interface ReviewCriteria {
  readonly pack: string
  readonly fingerprint: string
  readonly axes: readonly ReviewCriterion[]
}

interface NativeBrief {
  readonly review_needs: readonly string[]
  readonly fingerprints: Readonly<Record<string, string>>
  readonly criteria: ReviewCriteria
  readonly verdict_schema: JsonObject
}

interface NativeBriefEntry {
  readonly review_brief: NativeBrief
}

interface NativeBriefDocument {
  readonly briefs: readonly NativeBriefEntry[]
}

interface ReviewSubmission {
  readonly id: string
  readonly token: string
  readonly verdict: JsonObject
}

interface ReviewAxis {
  readonly score: number
  readonly suggestion: Option.Option<string>
}

interface ReviewChange {
  readonly id: string
  readonly relation: string
  readonly change: string
}

interface ReviewState {
  readonly valid: boolean
  readonly reason: string
  readonly changes: readonly ReviewChange[]
}

interface ReviewInput {
  readonly context: NativeContext
  readonly brief: NativeBrief
  readonly coverage: string
  readonly subjects: readonly ReviewSubject[]
}

export class RequirementsReview {
  private constructor() {}

  private static readonly contextSchema: z.ZodType<NativeContext> = z.object({
    anchor: z.object({ id: z.uuidv4(), type: z.string(), title: z.string(), body: z.string() }),
    fingerprint: z.string(),
    related: z.array(
      z.object({
        id: z.uuidv4(),
        type: z.string(),
        title: z.string(),
        body: z.string(),
        relation: z.string(),
      }),
    ),
    links: z.record(z.string(), z.array(z.string())),
  })

  private static readonly briefSchema: z.ZodType<NativeBriefDocument> = z.object({
    briefs: z.array(
      z.object({
        review_brief: z.looseObject({
          review_needs: z.array(z.uuidv4()),
          fingerprints: z.record(z.string(), z.string()),
          criteria: z.looseObject({
            pack: z.string(),
            fingerprint: z.string(),
            axes: z.array(z.looseObject({ key: z.string(), max_score: z.number().int() })),
          }),
          verdict_schema: z.record(z.string(), z.json()),
        }),
      }),
    ),
  })

  public static async run(
    repositoryFullPath: string,
    projectFullPath: string,
    executableFullPath: string,
    override: string,
    args: readonly string[],
    before: ReviewSnapshot,
  ): Promise<number> {
    const parsed: ReturnType<typeof parseArgs> = parseArgs({
      args: [...args],
      allowPositionals: true,
      options: {
        id: { type: 'string' },
        all: { type: 'boolean' },
        pending: { type: 'boolean' },
        stdin: { type: 'boolean' },
        rewrite: { type: 'boolean' },
      },
    })
    const action: string = z
      .enum(['audit', 'record', 'status', 'publish', 'reconcile'])
      .parse(parsed.positionals[0])
    if (parsed.positionals.length !== 1) throw new Error('review 不接受额外位置参数')
    if (action === 'publish') {
      if (Object.keys(parsed.values).length !== 0) throw new Error('publish 不接受参数')
      await RequirementsReview.publish(
        repositoryFullPath,
        join(repositoryFullPath, RequirementsSources.projectRelativePath),
      )
      return 0
    }
    if (action === 'record') {
      if (parsed.values.stdin !== true || Object.keys(parsed.values).length !== 1)
        throw new Error('record 需要 --stdin JSON 数组')
      await RequirementsReview.record(
        repositoryFullPath,
        projectFullPath,
        executableFullPath,
        override,
        before,
      )
      return 0
    }
    if (action === 'reconcile') {
      if (Object.keys(parsed.values).some((key: string): boolean => key !== 'rewrite'))
        throw new Error('reconcile 仅接受 --rewrite')
      await RequirementsReview.reconcile(
        repositoryFullPath,
        projectFullPath,
        executableFullPath,
        override,
        before,
        parsed.values.rewrite === true,
      )
      return 0
    }
    const selectors: number =
      Number(parsed.values.id !== undefined) +
      Number(parsed.values.all === true) +
      Number(parsed.values.pending === true)
    if (selectors !== 1 || Object.keys(parsed.values).length !== 1)
      throw new Error('audit/status 需要互斥的 --id、--all 或 --pending')
    const ids: readonly string[] =
      typeof parsed.values.id === 'string'
        ? [z.uuidv4().parse(parsed.values.id)]
        : RequirementsReview.ids(projectFullPath, executableFullPath, override)
    const inputs: ReviewInput[] = ids.map((id: string): ReviewInput =>
      RequirementsReview.input(projectFullPath, executableFullPath, override, id),
    )
    const selected: ReviewInput[] =
      parsed.values.pending === true
        ? inputs.filter(
            (input: ReviewInput): boolean =>
              !RequirementsReview.state(projectFullPath, input).valid,
          )
        : inputs
    if (selected.length === 0) {
      process.stdout.write('没有待审条目\n')
      return 0
    }
    if (action === 'status') {
      process.stdout.write(
        JSON.stringify(
          selected.map((input: ReviewInput): unknown => ({
            id: input.context.anchor.id,
            ...RequirementsReview.state(projectFullPath, input),
          })),
          null,
          2,
        ) + '\n',
      )
      return 0
    }
    RequirementsReview.requireSnapshot(repositoryFullPath, before)
    for (const input of selected) {
      const text: string = [
        input.context.anchor.body,
        ...input.context.related.map((need: RelatedNeed): string => need.body),
      ].join('\n')
      const size: number = countTokens(text, { disallowedSpecial: new Set() })
      if (size > 5000)
        process.stderr.write(
          input.context.anchor.id +
            ': 完整审计正文 ' +
            String(size) +
            ' tokens（cl100k_base），超过 5000，请拆分检视范围\n',
        )
    }
    const store: RequirementsReviewStore = new RequirementsReviewStore(repositoryFullPath)
    try {
      const tickets: readonly ReviewTicket[] = store.issue(
        selected.map((input: ReviewInput): Omit<ReviewTicket, 'token'> => ({
          id: input.context.anchor.id,
          snapshot: before,
          fingerprint: input.context.fingerprint,
          criteria: input.brief.criteria.fingerprint,
          coverage: input.coverage,
          subjects: input.subjects,
        })),
      )
      RequirementsReview.requireSnapshot(repositoryFullPath, before)
      process.stdout.write(
        JSON.stringify(
          {
            snapshot: before,
            reviews: selected.map((input: ReviewInput, index: number): unknown => ({
              context: input.context,
              criteria: input.brief.criteria,
              verdict_schema: input.brief.verdict_schema,
              ticket: tickets[index],
            })),
          },
          null,
          2,
        ) + '\n',
      )
    } finally {
      store.close()
    }
    return 0
  }

  private static async record(
    repositoryFullPath: string,
    projectFullPath: string,
    executableFullPath: string,
    override: string,
    before: ReviewSnapshot,
  ): Promise<void> {
    const submissions: ReviewSubmission[] = z
      .array(
        z.object({
          id: z.uuidv4(),
          token: z.hex().length(64),
          verdict: z.record(z.string(), z.json()),
        }),
      )
      .min(1)
      .parse(JSON.parse(readFileSync(0, 'utf8')))
    const store: RequirementsReviewStore = new RequirementsReviewStore(repositoryFullPath)
    try {
      const publications: ReviewPublication[] = submissions.map(
        (submission: ReviewSubmission): ReviewPublication => {
          const ticket: ReviewTicket = store.ticket(submission.id, submission.token)
          const input: ReviewInput = RequirementsReview.input(
            projectFullPath,
            executableFullPath,
            override,
            submission.id,
          )
          if (
            ticket.fingerprint !== input.context.fingerprint ||
            ticket.criteria !== input.brief.criteria.fingerprint ||
            ticket.coverage !== input.coverage
          )
            throw new Error(submission.id + ': 正文、覆盖或评分标准已变化，请重新领取')
          const verdict: JsonObject = {
            ...submission.verdict,
            schema: 2,
            need: ticket.id,
            criteria: input.brief.criteria.pack,
            criteria_fingerprint: ticket.criteria,
            reviewed_fingerprint: ticket.fingerprint,
          }
          const validator: ValidateFunction = new Ajv({ allErrors: true }).compile(
            input.brief.verdict_schema,
          )
          if (!validator(verdict)) throw new Error(JSON.stringify(validator.errors))
          const axes: Record<string, ReviewAxis> = z
            .record(
              z.string(),
              z.object({
                score: z.number().int(),
                suggestion: z.string().min(1).optional().transform(Option.fromNullishOr),
              }),
            )
            .parse(verdict.axes)
          for (const criterion of input.brief.criteria.axes) {
            const axis: ReviewAxis = Option.getOrThrow(Option.fromNullishOr(axes[criterion.key]))
            if (axis.score < criterion.max_score && Option.isNone(axis.suggestion))
              throw new Error(criterion.key + ': 未满分必须提供建议')
          }
          return {
            ticket,
            verdict: JSON.stringify(verdict, null, 2) + '\n',
            recordedAt: new Date().toISOString(),
          }
        },
      )
      RequirementsReview.requireSnapshot(repositoryFullPath, before)
      store.record(publications, before)
    } finally {
      store.close()
    }
    await RequirementsReview.publish(
      repositoryFullPath,
      join(repositoryFullPath, RequirementsSources.projectRelativePath),
    )
    process.stdout.write('检视意见已登记\n')
  }

  private static native<Payload>(
    projectFullPath: string,
    executableFullPath: string,
    override: string,
    args: readonly string[],
    schema: z.ZodType<Payload>,
  ): Payload {
    return schema.parse(
      JSON.parse(
        execFileSync(executableFullPath, [...args, '-c', override], {
          cwd: projectFullPath,
          env: Git.cleanEnv(false),
          windowsHide: true,
          encoding: 'utf8',
          maxBuffer: 64 * 1024 * 1024,
        }),
      ),
    )
  }

  private static input(
    projectFullPath: string,
    executableFullPath: string,
    override: string,
    id: string,
  ): ReviewInput {
    const context: NativeContext = RequirementsReview.native(
      projectFullPath,
      executableFullPath,
      override,
      ['agent', 'context', '--id', id],
      RequirementsReview.contextSchema,
    )
    const briefs: NativeBriefDocument = RequirementsReview.native(
      projectFullPath,
      executableFullPath,
      override,
      ['agent', 'review-brief', '--ids', id, '--all'],
      RequirementsReview.briefSchema,
    )
    const brief: NativeBrief = Option.getOrThrow(
      Option.fromNullishOr(briefs.briefs[0]),
    ).review_brief
    if (brief.fingerprints[id] !== context.fingerprint) throw new Error('收集全文期间输入已变化')
    const coverage: string = createHash('sha256')
      .update(
        JSON.stringify([
          context.anchor.id,
          context.related
            .map((need: RelatedNeed): readonly string[] => [need.id, need.type, need.relation])
            .sort((left: readonly string[], right: readonly string[]): number =>
              JSON.stringify(left).localeCompare(JSON.stringify(right), 'en'),
            ),
          Object.entries(context.links).sort(
            ([left]: [string, readonly string[]], [right]: [string, readonly string[]]): number =>
              left.localeCompare(right, 'en'),
          ),
        ]),
      )
      .digest('hex')
    const subjects: ReviewSubject[] = [
      { ...context.anchor, relation: 'anchor' },
      ...context.related,
    ].map((need: RelatedNeed): ReviewSubject => ({
      id: need.id,
      type: need.type,
      relation: need.relation,
      hash: createHash('sha256').update(need.body).digest('hex'),
    }))
    return { context, brief, coverage, subjects }
  }

  private static ids(
    projectFullPath: string,
    executableFullPath: string,
    override: string,
  ): readonly string[] {
    const ids: Set<string> = new Set()
    for (const type of ['interpretation', 'decision', 'design', 'implementation', 'verification']) {
      const briefs: NativeBriefDocument = RequirementsReview.native(
        projectFullPath,
        executableFullPath,
        override,
        ['agent', 'review-brief', type, '--all'],
        RequirementsReview.briefSchema,
      )
      for (const brief of briefs.briefs)
        for (const id of brief.review_brief.review_needs) ids.add(id)
    }
    return [...ids]
  }

  public static snapshot(repositoryFullPath: string): ReviewSnapshot {
    const head: string = Git.run(repositoryFullPath, ['rev-parse', 'HEAD'])
    // @non-environment-variable MERGE_HEAD 是 Git 原生合并状态文件名。
    const mergeFullPath: string = Git.privatePath(repositoryFullPath, 'MERGE_HEAD')
    return {
      head,
      index: Git.indexFingerprint(repositoryFullPath),
      tree: Git.run(repositoryFullPath, ['write-tree']),
      parents: existsSync(mergeFullPath)
        ? [head, ...readFileSync(mergeFullPath, 'utf8').split('\n').filter(Boolean)]
        : [head],
    }
  }

  private static requireSnapshot(repositoryFullPath: string, expected: ReviewSnapshot): void {
    if (
      JSON.stringify(RequirementsReview.snapshot(repositoryFullPath)) !== JSON.stringify(expected)
    )
      throw new Error('受审 HEAD、index 或合并上下文已变化')
  }

  private static state(projectFullPath: string, input: ReviewInput): ReviewState {
    const proofFullPath: string = join(
      projectFullPath,
      '.pharaoh/receipts',
      input.context.anchor.id + '.json',
    )
    const verdictFullPath: string = join(
      projectFullPath,
      '.pharaoh/verdicts',
      input.context.anchor.id + '.json',
    )
    if (!existsSync(proofFullPath) || !existsSync(verdictFullPath))
      return {
        valid: false,
        reason: '首次审计：缺少领取证明或正式意见',
        changes: input.subjects.map((subject: ReviewSubject): ReviewChange => ({
          id: subject.id,
          relation: subject.relation,
          change: 'added',
        })),
      }
    const proof: ReviewPublication = RequirementsReviewStore.publicationSchema.parse(
      JSON.parse(readFileSync(proofFullPath, 'utf8')),
    )
    const valid: boolean =
      proof.ticket.id === input.context.anchor.id &&
      proof.ticket.fingerprint === input.context.fingerprint &&
      proof.ticket.criteria === input.brief.criteria.fingerprint &&
      proof.ticket.coverage === input.coverage &&
      proof.verdict === readFileSync(verdictFullPath, 'utf8')
    const changes: ReviewChange[] = []
    for (const current of input.subjects) {
      const prior: Option.Option<ReviewSubject> = Option.fromNullishOr(
        proof.ticket.subjects.find(
          (subject: ReviewSubject): boolean =>
            subject.id === current.id && subject.relation === current.relation,
        ),
      )
      if (
        Option.isNone(prior) ||
        prior.value.hash !== current.hash ||
        prior.value.type !== current.type
      )
        changes.push({
          id: current.id,
          relation: current.relation,
          change: Option.isNone(prior) ? 'added' : 'modified',
        })
    }
    for (const previous of proof.ticket.subjects)
      if (
        !input.subjects.some(
          (subject: ReviewSubject): boolean =>
            subject.id === previous.id && subject.relation === previous.relation,
        )
      )
        changes.push({ id: previous.id, relation: previous.relation, change: 'removed' })
    const reasons: string[] = []
    if (proof.ticket.fingerprint !== input.context.fingerprint) reasons.push('关联正文变化')
    if (proof.ticket.coverage !== input.coverage) reasons.push('主体覆盖变化')
    if (proof.ticket.criteria !== input.brief.criteria.fingerprint) reasons.push('评分标准变化')
    if (
      proof.ticket.id !== input.context.anchor.id ||
      proof.verdict !== readFileSync(verdictFullPath, 'utf8')
    )
      reasons.push('领取证明与正式意见失配')
    return { valid, reason: valid ? '正文、覆盖、标准和领取证明匹配' : reasons.join('；'), changes }
  }

  public static verify(
    projectFullPath: string,
    executableFullPath: string,
    override: string,
  ): void {
    const ids: readonly string[] = RequirementsReview.ids(
      projectFullPath,
      executableFullPath,
      override,
    )
    const failures: string[] = []
    for (const id of ids) {
      const state: ReviewState = RequirementsReview.state(
        projectFullPath,
        RequirementsReview.input(projectFullPath, executableFullPath, override, id),
      )
      if (!state.valid) failures.push(id + ': ' + state.reason)
    }
    for (const relativePath of globSync('.pharaoh/{verdicts,receipts}/*.json', {
      cwd: projectFullPath,
    })) {
      const id: string = basename(relativePath, '.json')
      if (!ids.includes(id)) failures.push(id + ': 保留已删除或不再受审条目的证明')
    }
    if (failures.length > 0) throw new Error(failures.join('\n'))
  }

  private static async reconcile(
    repositoryFullPath: string,
    projectFullPath: string,
    executableFullPath: string,
    override: string,
    before: ReviewSnapshot,
    rewrite: boolean,
  ): Promise<void> {
    const store: RequirementsReviewStore = new RequirementsReviewStore(repositoryFullPath)
    try {
      const source: Option.Option<string> = rewrite ? store.rewriteHead() : Option.none()
      if (rewrite) {
        if (Option.isNone(source)) throw new Error('缺少本次 rebase 输入')
        const mapping: string[] = []
        for await (const line of createInterface({ input: process.stdin, crlfDelay: Infinity }))
          mapping.push(line)
        if (!mapping.some((line: string): boolean => line === source.value + ' ' + before.head))
          throw new Error(
            '重写映射与本次 rebase 输入及 HEAD 不匹配：' +
              JSON.stringify({ source: source.value, head: before.head, mapping }),
          )
      }
      const parents: readonly string[] = Git.run(repositoryFullPath, [
        'rev-list',
        '--parents',
        '-n',
        '1',
        before.head,
      ])
        .split(' ')
        .slice(1)
      const revisions: readonly string[] = [
        ...new Set([...before.parents, ...parents, ...Option.toArray(source)]),
      ]
      const inherited: ReviewPublication[] = []
      const pending: string[] = []
      const ids: readonly string[] = RequirementsReview.ids(
        projectFullPath,
        executableFullPath,
        override,
      )
      for (const id of ids) {
        const input: ReviewInput = RequirementsReview.input(
          projectFullPath,
          executableFullPath,
          override,
          id,
        )
        if (RequirementsReview.state(projectFullPath, input).valid) continue
        const paths: readonly [string, string] = [
          'requirements/.pharaoh/receipts/' + id + '.json',
          'requirements/.pharaoh/verdicts/' + id + '.json',
        ]
        const candidates: ReviewPublication[] = []
        for (const revision of revisions) {
          if (
            !paths.every((relativePath: string): boolean =>
              Git.succeeds(repositoryFullPath, ['cat-file', '-e', revision + ':' + relativePath]),
            )
          )
            continue
          const objects: readonly string[] = paths.map(
            (relativePath: string): string => revision + ':' + relativePath,
          )
          const contents: ReadonlyMap<string, string> = Git.blobs(repositoryFullPath, objects)
          const publication: ReviewPublication = RequirementsReviewStore.publicationSchema.parse(
            JSON.parse(
              Option.getOrThrow(Option.fromNullishOr(contents.get(revision + ':' + paths[0]))),
            ),
          )
          if (
            publication.ticket.id === id &&
            publication.ticket.fingerprint === input.context.fingerprint &&
            publication.ticket.coverage === input.coverage &&
            publication.ticket.criteria === input.brief.criteria.fingerprint &&
            publication.verdict === contents.get(revision + ':' + paths[1])
          )
            candidates.push(publication)
        }
        const opinions: ReadonlySet<string> = new Set(
          candidates.map((candidate: ReviewPublication): string => candidate.verdict),
        )
        if (opinions.size !== 1) {
          pending.push(
            id +
              (opinions.size === 0 ? ': 有限输入没有匹配证明' : ': 有限输入意见冲突，需重新检视'),
          )
          continue
        }
        inherited.push(Option.getOrThrow(Option.fromNullishOr(candidates[0])))
      }
      RequirementsReview.requireSnapshot(repositoryFullPath, before)
      const destinationFullPath: string = join(
        repositoryFullPath,
        RequirementsSources.projectRelativePath,
      )
      await mkdir(join(destinationFullPath, '.pharaoh/verdicts'), { recursive: true })
      await mkdir(join(destinationFullPath, '.pharaoh/receipts'), { recursive: true })
      for (const publication of inherited) {
        const files: readonly (readonly [string, string])[] = [
          ['verdicts', publication.verdict],
          ['receipts', JSON.stringify(publication, null, 2) + '\n'],
        ]
        for (const [directory, body] of files) {
          const relativePath: string =
            '.pharaoh/' + directory + '/' + publication.ticket.id + '.json'
          const destination: string = join(destinationFullPath, relativePath)
          const inspected: string = join(projectFullPath, relativePath)
          if (
            existsSync(destination) !== existsSync(inspected) ||
            (existsSync(destination) &&
              readFileSync(destination, 'utf8') !== readFileSync(inspected, 'utf8'))
          )
            throw new Error(relativePath + ': 工作树意见与受检输入不同')
          writeFileAtomic.sync(destination, body)
        }
      }
      const removed: string[] = []
      for (const relativePath of globSync('.pharaoh/{verdicts,receipts}/*.json', {
        cwd: projectFullPath,
      })) {
        if (ids.includes(basename(relativePath, '.json'))) continue
        const destination: string = join(destinationFullPath, relativePath)
        if (
          !existsSync(destination) ||
          readFileSync(destination, 'utf8') !==
            readFileSync(join(projectFullPath, relativePath), 'utf8')
        )
          throw new Error(relativePath + ': 工作树意见与受检输入不同')
        unlinkSync(destination)
        removed.push(relativePath.replaceAll('\\', '/'))
      }
      store.retain(ids)
      if (Option.isSome(source)) store.finishRewrite(source.value)
      process.stdout.write(
        JSON.stringify(
          {
            inherited: inherited.map(
              (publication: ReviewPublication): string => publication.ticket.id,
            ),
            pending,
            removed,
            sources: revisions,
          },
          null,
          2,
        ) + '\n',
      )
    } finally {
      store.close()
    }
  }

  private static async publish(repositoryFullPath: string, projectFullPath: string): Promise<void> {
    const store: RequirementsReviewStore = new RequirementsReviewStore(repositoryFullPath)
    try {
      await mkdir(join(projectFullPath, '.pharaoh/verdicts'), { recursive: true })
      await mkdir(join(projectFullPath, '.pharaoh/receipts'), { recursive: true })
      const current: ReviewSnapshot = RequirementsReview.snapshot(repositoryFullPath)
      store.publish((publication: ReviewPublication): boolean => {
        if (!isDeepStrictEqual(current, publication.ticket.snapshot)) {
          process.stderr.write(
            publication.ticket.id + ': 待发布输入已变化，须重新领取；保留待发布记录\n',
          )
          return false
        }
        RequirementsReview.requireSnapshot(repositoryFullPath, current)
        writeFileAtomic.sync(
          join(projectFullPath, '.pharaoh/verdicts', publication.ticket.id + '.json'),
          publication.verdict,
        )
        writeFileAtomic.sync(
          join(projectFullPath, '.pharaoh/receipts', publication.ticket.id + '.json'),
          JSON.stringify(publication, null, 2) + '\n',
        )
        RequirementsReview.requireSnapshot(repositoryFullPath, current)
        return true
      })
    } finally {
      store.close()
    }
  }
}
