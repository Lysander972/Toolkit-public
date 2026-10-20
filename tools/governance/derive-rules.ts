import { LikeC4 } from 'likec4'
import { createLanguageServices } from '@likec4/language-server'
import { z } from 'zod'
import { Option } from 'effect'
import escapeStringRegexp from 'escape-string-regexp'
import type { IForbiddenRuleType } from 'dependency-cruiser'
import type { ElementModel, RelationshipModel } from 'likec4/model'
import type { SourceFragment as ReviewFragment, SourceRange as ReviewRange } from './source-text.ts'

/** @anchor architecture-metadata */
export interface ArchitectureElement {
  id: string
  parentId: Option.Option<string>
  kind: string
  description: Option.Option<string>
  purpose: ArchitecturePurpose
  files: string[]
  entries: string[]
  code: string[]
  textExempt: boolean
  testStrategy: Option.Option<TestStrategy>
  decision: Option.Option<string>
}

/** @anchor test-strategy-contract */
type TestStrategy = 'high' | 'standard' | 'boundary'

/** @anchor architecture-metadata */
type ArchitectureEdgeKind = 'plain' | 'pending' | 'runtime'

/** @anchor architecture-metadata */
type ExemptionKey = 'pending' | 'runtime'

/** @anchor architecture-metadata */
export interface ArchitectureEdge {
  readonly source: string
  readonly target: string
  readonly kind: ArchitectureEdgeKind
  readonly reason: Option.Option<string>
}

export interface ArchitectureSpecificationLink {
  readonly source: string
  readonly target: string
}

/** @anchor architecture-metadata */
export type ArchitecturePurpose = 'doc' | 'build' | 'test' | 'tool' | 'common' | 'acl' | ''

/** @anchor architecture-metadata */
export interface ArchitectureContainer extends ArchitectureElement {
  sourceRelativePath: string
}

/** @anchor architecture-metadata */
interface LegacyFreeze {
  readonly elementId: string
  readonly revision: string
}

/** @anchor architecture-metadata */
export interface ArchitectureDeclarations {
  gaps: readonly ArchitectureGap[]
  sourceRelativePath: string
  specRelativePaths: string[]
  elements: ArchitectureElement[]
  containers: ArchitectureContainer[]
  edges: readonly ArchitectureEdge[]
  specificationLinks: readonly ArchitectureSpecificationLink[]
  codeTargets: string[]
  legacyFreeze: Option.Option<LegacyFreeze>
  rootElementId: string
  /**
   * @anchor dependency-acl
   */
  dependencyRules(): IForbiddenRuleType[]
  guardedRelativePaths: string[]
  guardedElementIds: ReadonlySet<string>
  reviewTargets: ReadonlyMap<string, readonly ReviewFragment[]>
  reviewPaths: ReadonlyMap<string, string>
}

/** @anchor architecture-metadata */
export interface ArchitectureGap {
  readonly target: string
  readonly reason: string
  readonly kind: ArchitectureGapKind
}

/** @anchor architecture-metadata */
type ArchitectureGapKind = 'gap' | 'todo'

/** @anchor architecture-model */
interface RootSystem {
  id: string
  sourceRelativePath: string
}

/** @anchor review-architecture */
interface ParsedCst {
  readonly offset: number
  readonly end: number
}

/** @anchor review-architecture */
interface ParsedModelBlock {
  readonly elements: readonly unknown[]
}

/** @anchor review-architecture */
interface ParsedModelRoot {
  readonly models: readonly ParsedModelBlock[]
}

/** @anchor review-architecture */
interface ParsedElement {
  readonly name: string
  readonly $cstNode: ReviewRange
  readonly body: Option.Option<ParsedModelBlock>
}

/** @anchor review-architecture */
interface ParsedReferenceName {
  readonly $refText: string
}

/** @anchor review-architecture */
interface ParsedElementReference {
  readonly el: ParsedReferenceName
  readonly parent: Option.Option<unknown>
}

/** @anchor review-architecture */
interface ParsedExtension {
  readonly element: unknown
  readonly $cstNode: ReviewRange
  readonly body: ParsedModelBlock
}

/** @anchor architecture-model */
export class ArchitectureRules {
  private static readonly METADATA_NAMES: ReadonlySet<string> = new Set([
    'checks',
    'code',
    'entries',
    'files',
    'freeze',
    'isolates',
    'pending',
    'runtime',
    'sourcePath',
    'specPath',
    'textExempt',
    'gap',
    'todo',
    'testStrategy',
    'decision',
  ])
  private static readonly PURPOSES: readonly string[] = [
    'doc',
    'build',
    'test',
    'tool',
    'common',
    'acl',
  ]

  private static readonly relativePathSchema: z.ZodString = z
    .string()
    .min(1)
    .refine(
      (value: string): boolean =>
        !value.startsWith('/') &&
        !value.includes('\\') &&
        !value.includes(':') &&
        value
          .split('/')
          .every((part: string): boolean => part !== '..' && part !== '.' && part !== ''),
      '必须是规范的工程相对路径',
    )

  private static readonly fileSchema: z.ZodString = z
    .string()
    .min(1)
    .refine(
      (value: string): boolean =>
        ArchitectureRules.relativePathSchema.safeParse(
          value.endsWith('/') ? value.slice(0, -1) : value,
        ).success,
      '文件声明必须是工程相对路径、目录或 glob',
    )

  private static readonly packageNameSchema: z.ZodString = z
    .string()
    .min(1)
    .refine(
      (value: string): boolean =>
        !value.startsWith('/') &&
        !value.startsWith('@/') &&
        !value.includes('\\') &&
        !value.includes('..') &&
        !value.includes(' ') &&
        !value.endsWith('/'),
      '被接管包必须是裸包名',
    )

  private constructor() {}

  /** @anchor architecture-scope */
  private static checkSpecPathsOverlap(specRelativePaths: readonly string[]): void {
    if (new Set(specRelativePaths).size !== specRelativePaths.length) {
      throw new Error('规范范围不得重复声明')
    }
    const orderedSpecPaths: string[] = [...specRelativePaths].sort()
    for (const [index, previous] of orderedSpecPaths.entries()) {
      const current: Option.Option<string> = Option.fromNullishOr(orderedSpecPaths[index + 1])
      if (Option.isSome(current) && current.value.startsWith(previous + '/')) {
        throw new Error('规范范围不得重叠')
      }
    }
  }

  /** @anchor architecture-scope */
  private static forbidContainerPairs(
    containers: readonly ArchitectureContainer[],
    allowed: ReadonlySet<string>,
  ): IForbiddenRuleType[] {
    const forbidden: IForbiddenRuleType[] = []
    for (const from of containers) {
      for (const to of containers) {
        if (from.id === to.id) {
          continue
        }
        if (
          from.sourceRelativePath === to.sourceRelativePath ||
          from.sourceRelativePath.startsWith(to.sourceRelativePath + '/')
        ) {
          throw new Error('架构容器的源码范围不得重叠')
        }
        if (!allowed.has(`${from.id}->${to.id}`)) {
          forbidden.push({
            name: `${from.id}-to-${to.id}`,
            severity: 'error',
            from: { path: ArchitectureRules.pattern(from.sourceRelativePath) },
            to: { path: ArchitectureRules.pattern(to.sourceRelativePath) },
          })
        }
      }
      forbidden.push({
        name: `${from.id}-outside-system`,
        severity: 'error',
        from: { path: ArchitectureRules.pattern(from.sourceRelativePath) },
        to: {
          pathNot: containers
            .map((container: ArchitectureContainer): string =>
              ArchitectureRules.pattern(container.sourceRelativePath),
            )
            .join('|'),
          dependencyTypesNot: ['npm', 'core', 'npm-dev', 'npm-optional', 'npm-peer'],
        },
      })
    }
    return forbidden
  }

  /**
   * @anchor architecture-model
   * @fallback 组织元素不登记文件时按空集合处理，叶元素仍必须有文件声明。
   * @fallback 组织元素没有用途标签时使用空用途标识，文件元素在用途校验前已拒绝该状态。
   */
  public static async declarations(
    modelSource: string,
    modules: ReadonlyMap<string, string> = new Map(),
  ): Promise<ArchitectureDeclarations> {
    const sources: ReadonlyMap<string, string> = new Map([
      ['tools/model.c4', modelSource],
      ...modules,
    ])
    const instance: LikeC4 = await LikeC4.fromSource([...sources.values()].join('\n'), {
      logger: false,
      printErrors: false,
    })
    try {
      const errors: ReturnType<LikeC4['getErrors']> = instance.getErrors()
      if (errors.length > 0) {
        throw new Error(
          errors
            .map(
              (error: ReturnType<LikeC4['getErrors']>[number]): string =>
                `model.c4:${String(error.line)}: ${error.message}`,
            )
            .join('\n'),
        )
      }
      const model: Awaited<ReturnType<LikeC4['computedModel']>> = await instance.computedModel()
      const declaredTags: ReadonlySet<string> = new Set<string>(
        Object.keys(model.specification.tags),
      )
      const classified: boolean = ArchitectureRules.PURPOSES.some((purpose: string): boolean =>
        declaredTags.has(purpose),
      )
      const elements: ArchitectureElement[] = []
      const gaps: ArchitectureGap[] = []
      const containers: ArchitectureContainer[] = []
      const specRelativePaths: string[] = []
      const freezes: LegacyFreeze[] = []
      const roots: RootSystem[] = []
      const isolatedPackages: string[] = []
      for (const element of model.elements()) {
        gaps.push(
          ...ArchitectureRules.gap(
            element.id,
            'gap',
            element.isTagged('gap'),
            element.metadata['gap'],
          ),
          ...ArchitectureRules.gap(
            element.id,
            'todo',
            element.isTagged('todo'),
            element.metadata['todo'],
          ),
        )
        if (
          (element.kind === 'system' || element.kind === 'container') &&
          // eslint-disable-next-line no-restricted-properties -- 职责描述必须包含非空白正文，trim 只用于必填校验。
          (element.description.isEmpty || element.description.text.trim().length === 0)
        ) {
          throw new Error(`${element.id}: 必须通过 LikeC4 description 声明职责与边界`)
        }
        for (const name of Object.keys(element.metadata)) {
          if (!ArchitectureRules.METADATA_NAMES.has(name)) {
            throw new Error(`${element.id}: 非法架构 metadata 字段: ${name}`)
          }
        }
        const frozen: unknown = element.metadata['freeze']
        if (frozen !== undefined) {
          freezes.push({
            elementId: element.id,
            revision: z.literal('initial').parse(frozen),
          })
        }
        if (
          element.kind === 'document' &&
          (element.metadata['sourcePath'] !== undefined ||
            element.metadata['checks'] !== undefined ||
            element.metadata['specPath'] !== undefined)
        ) {
          throw new Error(`${element.id}: 文档元素不得声明源码或规范检查范围`)
        }
        const purposes: readonly string[] = element.tags.filter((tag: string): boolean =>
          ArchitectureRules.PURPOSES.includes(tag),
        )
        const declaration: ArchitectureElement = {
          id: element.id,
          parentId: Option.fromNullishOr(element.parent?.id),
          kind: element.kind,
          description: element.description.isEmpty
            ? Option.none()
            : Option.some(element.description.text),
          purpose: z
            .union([
              z.literal('doc'),
              z.literal('build'),
              z.literal('test'),
              z.literal('tool'),
              z.literal('common'),
              z.literal('acl'),
              z.literal(''),
            ])
            .parse(purposes[0] ?? ''),
          files:
            element.metadata['files'] === undefined
              ? []
              : z
                  .array(ArchitectureRules.fileSchema)
                  .min(1)
                  .parse([element.metadata['files']].flat()),
          textExempt: element.metadata['textExempt'] !== undefined,
          testStrategy: Option.map(
            Option.fromNullishOr(element.metadata['testStrategy']),
            (value: unknown): TestStrategy => z.enum(['high', 'standard', 'boundary']).parse(value),
          ),
          decision: Option.map(
            Option.fromNullishOr(element.metadata['decision']),
            (value: unknown): string => z.string().min(1).parse(value),
          ),
          entries: Option.toArray(Option.fromNullishOr(element.metadata['entries'])).flatMap(
            (entry: unknown): string[] =>
              z.array(ArchitectureRules.relativePathSchema).min(1).parse([entry].flat()),
          ),
          code: Option.toArray(Option.fromNullishOr(element.metadata['code'])).flatMap(
            (entry: unknown): string[] => z.array(z.string().min(1)).min(1).parse([entry].flat()),
          ),
        }
        if (
          element.isTagged('external') &&
          (element.kind !== 'system' ||
            Object.keys(element.metadata).length > 0 ||
            purposes.length > 0 ||
            element.children().size > 0)
        ) {
          throw new Error(`${element.id}: #external 只允许无仓库归属与子元素的外部 system`)
        }
        if (
          declaration.files.length === 0 &&
          element.children().size === 0 &&
          element.metadata['code'] === undefined &&
          !element.isTagged('external')
        ) {
          throw new Error(`${element.id}: 叶元素及待迁移元素必须直接登记文件`)
        }
        if (classified) {
          ArchitectureRules.purpose(element, declaration, purposes)
        }
        elements.push(declaration)
        const isolated: unknown = element.metadata['isolates']
        if (isolated !== undefined) {
          isolatedPackages.push(
            ...z.array(ArchitectureRules.packageNameSchema).min(1).parse([isolated].flat()),
          )
        }
        const declared: unknown = element.metadata['checks']
        if (declared !== undefined) {
          z.literal('selfcheck').parse(declared)
          roots.push({
            id: element.id,
            sourceRelativePath: ArchitectureRules.relativePathSchema.parse(
              element.metadata['sourcePath'],
            ),
          })
        } else if (element.metadata['sourcePath'] !== undefined) {
          const sourceRelativePath: string = ArchitectureRules.relativePathSchema.parse(
            element.metadata['sourcePath'],
          )
          if (
            declaration.files.length === 0 ||
            declaration.files.some(
              (fileRelativePath: string): boolean =>
                !fileRelativePath.startsWith(sourceRelativePath + '/'),
            )
          ) {
            throw new Error(`${element.id}: 源码容器必须直接登记自身范围内的文件`)
          }
          containers.push({ ...declaration, sourceRelativePath })
        }
        if (element.metadata['specPath'] !== undefined) {
          const declaredSpecPaths: string[] = z
            .array(ArchitectureRules.relativePathSchema)
            .min(1)
            .parse([element.metadata['specPath']].flat())
          if (
            declaration.files.some(
              (fileRelativePath: string): boolean =>
                fileRelativePath.endsWith('.md') &&
                !declaredSpecPaths.some((specRelativePath: string): boolean =>
                  fileRelativePath.startsWith(specRelativePath + '/'),
                ),
            )
          ) {
            throw new Error(`${element.id}: 规范文件登记必须位于其规范范围内`)
          }
          specRelativePaths.push(...declaredSpecPaths)
        }
      }
      if (freezes.length > 1) {
        throw new Error(
          '冻结基线至多声明一次: ' +
            freezes.map((entry: LegacyFreeze): string => entry.elementId).join(', '),
        )
      }
      const rootSystem: z.ZodSafeParseResult<[RootSystem]> = z
        .tuple([
          z.object({
            id: z.string(),
            sourceRelativePath: z.string(),
          }),
        ])
        .safeParse(roots)
      if (!rootSystem.success || containers.length === 0) {
        throw new Error('架构必须指定唯一自检系统与源码容器')
      }
      ArchitectureRules.checkSpecPathsOverlap(specRelativePaths)
      const [root]: [RootSystem] = rootSystem.data
      const containerIds: ReadonlySet<string> = new Set<string>(
        containers.map((container: ArchitectureContainer): string => container.id),
      )
      const allowed: Set<string> = new Set<string>()
      for (const relation of model.relationships()) {
        gaps.push(
          ...ArchitectureRules.gap(
            `${relation.source.id}->${relation.target.id}`,
            'gap',
            relation.isTagged('gap'),
            relation.metadata['gap'],
          ),
          ...ArchitectureRules.gap(
            `${relation.source.id}->${relation.target.id}`,
            'todo',
            relation.isTagged('todo'),
            relation.metadata['todo'],
          ),
        )
        allowed.add(`${relation.source.id}->${relation.target.id}`)
      }
      const edges: readonly ArchitectureEdge[] = ArchitectureRules.classifyEdges(
        model,
        new Set([
          ...containerIds,
          ...elements
            .filter((element: ArchitectureElement): boolean => element.code.length > 0)
            .map((element: ArchitectureElement): string => element.id),
        ]),
      )
      const forbidden: IForbiddenRuleType[] = [
        { name: 'unresolved', severity: 'error', from: {}, to: { couldNotResolve: true } },
        { name: 'circular', severity: 'error', from: {}, to: { circular: true } },
      ]
      for (const packageName of isolatedPackages) {
        forbidden.push({
          name: `isolated-${packageName}`,
          severity: 'error',
          from: {},
          to: { path: 'node_modules/' + escapeStringRegexp(packageName) },
        })
      }
      forbidden.push(...ArchitectureRules.forbidContainerPairs(containers, allowed))
      const guarded: Set<string> = new Set([
        root.id,
        ...containers.map((container: ArchitectureContainer): string => container.id),
      ])
      let previousSize: number = -1
      while (previousSize !== guarded.size) {
        previousSize = guarded.size
        for (const element of elements) {
          if (guarded.has(element.id) && Option.isSome(element.parentId)) {
            guarded.add(element.parentId.value)
          }
          if (Option.isSome(element.parentId) && guarded.has(element.parentId.value)) {
            guarded.add(element.id)
          }
        }
        for (const relation of model.relationships()) {
          if (guarded.has(relation.source.id)) {
            guarded.add(relation.target.id)
          }
        }
      }
      const guardedRelativePaths: string[] = elements
        .filter((element: ArchitectureElement): boolean => guarded.has(element.id))
        .flatMap((element: ArchitectureElement): string[] => element.files)
      const reviewTargets: Map<string, readonly ReviewFragment[]> = new Map()
      const reviewPaths: Map<string, string> = new Map()
      for (const [relativePath, source] of sources) {
        for (const [id, fragments] of await ArchitectureRules.fragments(source)) {
          if (reviewTargets.has(id)) {
            throw new Error(`${id}: 架构元素必须在唯一 C4 文件中定义`)
          }
          reviewTargets.set(id, fragments)
          reviewPaths.set(id, relativePath)
        }
      }
      return {
        gaps,
        sourceRelativePath: root.sourceRelativePath,
        specRelativePaths,
        elements,
        containers,
        edges,
        specificationLinks: [...model.relationships()]
          .filter((relation: RelationshipModel): boolean =>
            elements.some(
              (element: ArchitectureElement): boolean =>
                element.id === relation.target.id &&
                element.files.some((relativePath: string): boolean =>
                  specRelativePaths.some((specRelativePath: string): boolean =>
                    relativePath.startsWith(specRelativePath + '/'),
                  ),
                ),
            ),
          )
          .map((relation: RelationshipModel): ArchitectureSpecificationLink => ({
            source: relation.source.id,
            target: relation.target.id,
          })),
        codeTargets: [
          root.id,
          ...containers.map((container: ArchitectureContainer): string => container.id),
        ],
        legacyFreeze: Option.fromNullishOr(freezes[0]),
        rootElementId: root.id,
        guardedRelativePaths,
        guardedElementIds: guarded,
        reviewTargets,
        reviewPaths,
        dependencyRules(): IForbiddenRuleType[] {
          return structuredClone(forbidden)
        },
      }
    } finally {
      await instance.dispose()
    }
  }

  /** @anchor review-architecture */
  private static async fragments(source: string): Promise<Map<string, readonly ReviewFragment[]>> {
    const services: ReturnType<typeof createLanguageServices> = createLanguageServices()
    try {
      const parsed: ReturnType<typeof services.likec4.parser.LangiumParser.parse> =
        services.likec4.parser.LangiumParser.parse(source)
      if (parsed.parserErrors.length > 0 || parsed.lexerErrors.length > 0)
        throw new Error('架构源片段解析失败')
      const cstSchema: z.ZodType<ReviewRange> = z
        .object({ offset: z.number(), end: z.number() })
        .transform((cst: ParsedCst): ReviewRange => ({
          start: cst.offset,
          end: cst.end,
        }))
      const root: ParsedModelRoot = z
        .object({ models: z.array(z.object({ elements: z.array(z.unknown()) })) })
        .parse(parsed.value)
      const fragments: Map<string, readonly ReviewFragment[]> = new Map()
      const visit: (raw: unknown, parents: readonly string[]) => void = (
        raw: unknown,
        parents: readonly string[],
      ): void => {
        const kind: string = z
          .object({ $type: z.enum(['Element', 'ExtendElement', 'Relation']) })
          .parse(raw).$type
        if (kind === 'Relation') return
        if (kind === 'ExtendElement') {
          const extension: ParsedExtension = z
            .object({
              element: z.unknown(),
              $cstNode: cstSchema,
              body: z.object({ elements: z.array(z.unknown()) }),
            })
            .parse(raw)
          const referenceSchema: z.ZodType<ParsedElementReference> = z.object({
            el: z.object({ $refText: z.string() }),
            parent: z.unknown().optional().transform(Option.fromNullishOr),
          })
          let reference: Option.Option<unknown> = Option.some(extension.element)
          const names: string[] = []
          while (Option.isSome(reference)) {
            const part: ParsedElementReference = referenceSchema.parse(reference.value)
            names.unshift(part.el.$refText)
            reference = part.parent
          }
          const id: string = names.join('.')
          const existing: readonly ReviewFragment[] = Option.toArray(
            Option.fromNullishOr(fragments.get(id)),
          ).flat()
          fragments.set(id, [
            ...existing,
            {
              ...extension.$cstNode,
              content: source.slice(extension.$cstNode.start, extension.$cstNode.end),
            },
          ])
          for (const child of extension.body.elements) visit(child, names)
          return
        }
        const element: ParsedElement = z
          .object({
            name: z.string(),
            $cstNode: cstSchema,
            body: z
              .object({ elements: z.array(z.unknown()) })
              .optional()
              .transform(Option.fromNullishOr),
          })
          .parse(raw)
        const segments: readonly string[] = [...parents, element.name]
        const id: string = segments.join('.')
        const span: ReviewRange = element.$cstNode
        const existing: readonly ReviewFragment[] = Option.toArray(
          Option.fromNullishOr(fragments.get(id)),
        ).flat()
        fragments.set(id, [...existing, { ...span, content: source.slice(span.start, span.end) }])
        if (Option.isSome(element.body))
          for (const child of element.body.value.elements) visit(child, segments)
      }
      for (const model of root.models) for (const element of model.elements) visit(element, [])
      return new Map(
        [...fragments].map(
          ([id, entries]: [string, readonly ReviewFragment[]]): [
            string,
            readonly ReviewFragment[],
          ] => [
            id,
            entries.toSorted(
              (left: ReviewFragment, right: ReviewFragment): number => left.start - right.start,
            ),
          ],
        ),
      )
    } finally {
      await services.likec4.likec4.LanguageServices.dispose()
    }
  }

  /** @anchor edge-alignment */
  private static requirePairedExemption(relation: RelationshipModel, pair: string): void {
    for (const name of Object.keys(relation.metadata)) {
      if (!['pending', 'runtime', 'gap', 'todo'].includes(name)) {
        throw new Error(`${pair}: 非法依赖边 metadata 字段: ${name}`)
      }
    }
    const exemptionKeys: readonly ExemptionKey[] = ['pending', 'runtime']
    for (const key of exemptionKeys) {
      if (relation.metadata[key] !== undefined && !relation.isTagged(key)) {
        throw new Error(`${pair}: ${key} 原因必须与 #${key} 标签成对声明`)
      }
    }
  }

  /** @anchor architecture-metadata */
  private static gap(
    target: string,
    kind: ArchitectureGapKind,
    tagged: boolean,
    reason: unknown,
  ): ArchitectureGap[] {
    const declared: Option.Option<unknown> = Option.fromNullishOr(reason)
    if (tagged !== Option.isSome(declared)) {
      throw new Error(`${target}: #${kind} 与 metadata.${kind} 原因必须成对声明`)
    }
    return Option.toArray(declared).map((value: unknown): ArchitectureGap => ({
      target,
      kind,
      reason: z.string().min(1).parse(value),
    }))
  }

  /** @anchor edge-alignment */
  private static classifyRelation(
    relation: RelationshipModel,
    containerIds: ReadonlySet<string>,
  ): ArchitectureEdge {
    const pair: string = `${relation.source.id}->${relation.target.id}`
    const pending: boolean = relation.isTagged('pending')
    const runtime: boolean = relation.isTagged('runtime')
    ArchitectureRules.requirePairedExemption(relation, pair)
    if (pending && runtime) {
      throw new Error(`${pair}: #pending 与 #runtime 豁免不得同时声明`)
    }
    if (!pending && !runtime) {
      return {
        source: relation.source.id,
        target: relation.target.id,
        kind: 'plain',
        reason: Option.none(),
      }
    }
    const exemption: ExemptionKey = pending ? 'pending' : 'runtime'
    const declared: unknown = relation.metadata[exemption]
    if (typeof declared !== 'string' || declared.length === 0) {
      throw new Error(`${pair}: #${exemption} 豁免必须登记非空 ${exemption} 原因`)
    }
    if (!(containerIds.has(relation.source.id) && containerIds.has(relation.target.id))) {
      throw new Error(`${pair}: 豁免标签只能声明在源码容器之间的依赖边上`)
    }
    return {
      source: relation.source.id,
      target: relation.target.id,
      kind: exemption,
      reason: Option.some(declared),
    }
  }

  /** @anchor edge-alignment */
  private static classifyEdges(
    model: Awaited<ReturnType<LikeC4['computedModel']>>,
    containerIds: ReadonlySet<string>,
  ): readonly ArchitectureEdge[] {
    const edgeRecords: Map<string, ArchitectureEdge> = new Map<string, ArchitectureEdge>()
    for (const relation of model.relationships()) {
      const edge: ArchitectureEdge = ArchitectureRules.classifyRelation(relation, containerIds)
      if (!(containerIds.has(edge.source) && containerIds.has(edge.target))) {
        continue
      }
      const pair: string = `${edge.source}->${edge.target}`
      const previous: Option.Option<ArchitectureEdge> = Option.fromNullishOr(edgeRecords.get(pair))
      if (Option.isSome(previous)) {
        if (previous.value.kind !== edge.kind) {
          throw new Error(`${pair}: 同一边的多次声明必须保持一致的豁免状态`)
        }
      } else {
        edgeRecords.set(pair, edge)
      }
    }
    return [...edgeRecords.values()].toSorted(
      (left: ArchitectureEdge, right: ArchitectureEdge): number =>
        left.source === right.source
          ? left.target.localeCompare(right.target)
          : left.source.localeCompare(right.source),
    )
  }

  /** @anchor purpose-classification */
  private static purpose(
    element: ElementModel,
    declaration: ArchitectureElement,
    purposes: readonly string[],
  ): void {
    if (declaration.files.length > 0 && purposes.length !== 1) {
      throw new Error(
        `${element.id}: 登记文件的元素必须恰好携带一个用途标签（doc/build/test/tool/common/acl）`,
      )
    }
    if (declaration.files.length === 0 && purposes.length > 0) {
      throw new Error(`${element.id}: 未登记文件的组织元素不得携带用途标签`)
    }
    if (element.kind === 'document' && purposes[0] !== 'doc') {
      throw new Error(`${element.id}: 文档元素的用途必须是 doc`)
    }
    if ((purposes[0] === 'acl') !== (element.metadata['isolates'] !== undefined)) {
      throw new Error(`${element.id}: 防腐层用途与 isolates 登记必须成对出现`)
    }
  }

  private static pattern(relativePath: string): string {
    return '^' + escapeStringRegexp(relativePath) + '(/|$)'
  }
}
