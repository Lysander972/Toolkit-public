import { readFileSync } from 'node:fs'
import { mkdir } from 'node:fs/promises'
import { dirname, isAbsolute, join, posix } from 'node:path'
import { Option } from 'effect'
import writeFileAtomic from 'write-file-atomic'
import { z } from 'zod'
import { ArchitectureProject } from './ArchitectureProject.ts'
import { Git } from './Git.ts'
import { WorkspaceFiles } from './WorkspaceFiles.ts'
import { WorkspaceTree } from './WorkspaceTree.ts'
import { ArchitectureFiles } from '../governance/architecture-files.ts'
import type {
  ArchitectureContainer,
  ArchitectureEdge,
  ArchitectureElement,
  ArchitectureSpecificationLink,
} from '../governance/derive-rules.ts'
import type { WorkspaceTreeEntry } from './WorkspaceTree.ts'
import type { ArchitectureDeclarations } from '../governance/derive-rules.ts'
import type { SourceFragment } from '../governance/source-text.ts'

interface FileBinding {
  readonly kind: 'file'
  readonly relativePath: string
}

interface ArchitectureBinding {
  readonly kind: 'architecture'
  readonly element: string
}

type SourceBindingKind = 'file' | 'architecture'
type SourceNeedType = 'source' | 'design' | 'implementation' | 'verification'

interface SourceBinding {
  readonly kind: SourceBindingKind
  readonly target: string
}

interface SourceNeed {
  readonly id: string
  readonly type: SourceNeedType
  readonly title: string
  readonly status: string
  readonly implements: readonly string[]
  readonly content: string
}

interface BoundNeed extends SourceNeed {
  readonly bindings: readonly SourceBinding[]
}

interface BindingVersion {
  readonly needs: Readonly<Record<string, BoundNeed>>
}

interface BindingDocument {
  readonly current_version: string
  readonly versions: Readonly<Record<string, BindingVersion>>
}

interface RequirementsSubject {
  readonly relativePath: string
  readonly domains: readonly string[]
}

export interface RequirementsCoverage {
  readonly unbound: readonly string[]
  readonly unassigned: readonly string[]
  readonly pending: readonly string[]
  readonly subjects: readonly RequirementsSubject[]
}

/** @anchor cli-boundary */
export class RequirementsSources {
  private constructor() {}

  // @non-manual-path 正式 Sphinx/ubCode 项目的固定入口，不是治理源码扫描范围。
  public static readonly projectRelativePath: string = 'requirements'

  public static async coverage(
    repositoryFullPath: string,
    revision: Option.Option<string>,
  ): Promise<RequirementsCoverage> {
    const version: BindingVersion = RequirementsSources.version(repositoryFullPath, revision)
    const model: ArchitectureDeclarations = await ArchitectureProject.load(
      repositoryFullPath,
      revision,
    )
    const paths: readonly string[] = Option.isSome(revision)
      ? WorkspaceTree.list(repositoryFullPath, revision.value, []).map(
          (entry: WorkspaceTreeEntry): string => entry.relativePath,
        )
      : WorkspaceFiles.all(repositoryFullPath)
    const bound: ReadonlySet<string> = new Set(
      Object.values(version.needs).flatMap((need: BoundNeed): readonly string[] =>
        need.bindings
          .filter((binding: SourceBinding): boolean => binding.kind === 'file')
          .map((binding: SourceBinding): string => binding.target),
      ),
    )
    const domains: ReadonlySet<string> = new Set(
      model.elements
        .filter((element: ArchitectureElement): boolean =>
          element.files.some((relativePath: string): boolean =>
            relativePath.startsWith('requirements/sources/'),
          ),
        )
        .map((element: ArchitectureElement): string => element.id),
    )
    const required: Set<string> = new Set(
      model.specificationLinks
        .filter((edge: ArchitectureSpecificationLink): boolean => domains.has(edge.target))
        .map((edge: ArchitectureSpecificationLink): string => edge.source),
    )
    const pendingCode: Set<string> = new Set(
      model.specificationLinks
        .filter((edge: ArchitectureSpecificationLink): boolean => !domains.has(edge.target))
        .map((edge: ArchitectureSpecificationLink): string => edge.source),
    )
    let pendingSize: number = -1
    while (pendingSize !== pendingCode.size) {
      pendingSize = pendingCode.size
      for (const element of model.elements)
        if (Option.isSome(element.parentId) && pendingCode.has(element.parentId.value))
          pendingCode.add(element.id)
      for (const container of model.containers)
        if (
          container.sourceRelativePath.startsWith('test/') &&
          model.edges.some(
            (edge: ArchitectureEdge): boolean =>
              edge.source === container.id && pendingCode.has(edge.target),
          )
        )
          pendingCode.add(container.id)
    }
    let previous: number = -1
    while (previous !== required.size) {
      previous = required.size
      for (const element of model.elements) {
        if (Option.isSome(element.parentId) && required.has(element.parentId.value))
          required.add(element.id)
        if (
          (element.purpose === 'test' ||
            model.containers.some(
              (container: ArchitectureContainer): boolean =>
                container.id === element.id && container.sourceRelativePath.startsWith('test/'),
            )) &&
          !model.edges.some(
            (edge: ArchitectureEdge): boolean =>
              edge.source === element.id && pendingCode.has(edge.target),
          ) &&
          model.edges.some(
            (edge: ArchitectureEdge): boolean =>
              edge.source === element.id && required.has(edge.target),
          )
        )
          required.add(element.id)
      }
    }
    const declarations: readonly string[] = model.elements
      .filter((element: ArchitectureElement): boolean => required.has(element.id))
      .flatMap((element: ArchitectureElement): readonly string[] => element.files)
    const expected: readonly string[] = paths.filter(
      (relativePath: string): boolean =>
        (relativePath.startsWith('tools/') && relativePath.endsWith('.c4')) ||
        ((['.ts', '.tsx', '.mjs', '.cjs', '.js', '.py'].some((extension: string): boolean =>
          relativePath.endsWith(extension),
        ) ||
          relativePath.startsWith('tools/git-hooks/')) &&
          declarations.some((declaration: string): boolean =>
            ArchitectureFiles.matches(relativePath, declaration),
          )),
    )
    const unassigned: readonly string[] = model.containers
      .filter(
        (container: ArchitectureContainer): boolean =>
          !required.has(container.id) &&
          !pendingCode.has(container.id) &&
          paths.some(
            (relativePath: string): boolean =>
              (relativePath.endsWith('.ts') || relativePath.endsWith('.tsx')) &&
              relativePath.startsWith(container.sourceRelativePath + '/'),
          ),
      )
      .map((container: ArchitectureContainer): string => container.id)
    return {
      unbound: expected.filter((relativePath: string): boolean => !bound.has(relativePath)),
      unassigned,
      pending: model.elements.flatMap((element: ArchitectureElement): readonly string[] =>
        element.files.filter((relativePath: string): boolean =>
          relativePath.startsWith('openspec/specs/'),
        ),
      ),
      subjects: expected.map((relativePath: string): RequirementsSubject => {
        const owners: ArchitectureElement[] = model.elements.filter(
          (element: ArchitectureElement): boolean =>
            element.files.some((declaration: string): boolean =>
              ArchitectureFiles.matches(relativePath, declaration),
            ),
        )
        const ancestors: Set<string> = new Set(
          owners.map((element: ArchitectureElement): string => element.id),
        )
        for (const edge of model.edges)
          if (ancestors.has(edge.source) && required.has(edge.target)) ancestors.add(edge.target)
        let ancestorSize: number = -1
        while (ancestorSize !== ancestors.size) {
          ancestorSize = ancestors.size
          for (const element of model.elements)
            if (ancestors.has(element.id) && Option.isSome(element.parentId))
              ancestors.add(element.parentId.value)
        }
        const linked: ReadonlySet<string> = new Set(
          model.specificationLinks
            .filter(
              (edge: ArchitectureSpecificationLink): boolean =>
                ancestors.has(edge.source) && domains.has(edge.target),
            )
            .map((edge: ArchitectureSpecificationLink): string => edge.target),
        )
        return {
          relativePath,
          domains: model.elements
            .filter((element: ArchitectureElement): boolean => linked.has(element.id))
            .flatMap((element: ArchitectureElement): readonly string[] => element.files),
        }
      }),
    }
  }

  /** @anchor cli-boundary */
  public static async generate(
    repositoryFullPath: string,
    outputRelativePath: string,
    revision: Option.Option<string>,
  ): Promise<void> {
    const version: BindingVersion = RequirementsSources.version(repositoryFullPath, revision)
    const model: ArchitectureDeclarations = await ArchitectureProject.load(
      repositoryFullPath,
      revision,
    )
    const needs: Record<string, SourceNeed> = {}
    for (const [id, bound] of Object.entries(version.needs)) {
      if (id !== bound.id) throw new Error('需求绑定 key 与 id 不一致：' + id)
      const { bindings, ...need }: BoundNeed = bound
      needs[id] = {
        ...need,
        content:
          need.content +
          '\n\n' +
          bindings
            .map((binding: SourceBinding): string =>
              RequirementsSources.render(repositoryFullPath, binding, revision, model),
            )
            .join('\n\n'),
      }
    }
    await mkdir(dirname(join(repositoryFullPath, outputRelativePath)), { recursive: true })
    await writeFileAtomic(
      join(repositoryFullPath, outputRelativePath),
      JSON.stringify({ current_version: '1', versions: { '1': { needs } } }),
    )
  }

  /** @anchor cli-boundary */
  public static inputs(
    repositoryFullPath: string,
    revision: Option.Option<string>,
  ): readonly string[] {
    const version: BindingVersion = RequirementsSources.version(repositoryFullPath, revision)
    const files: readonly string[] = Option.match(revision, {
      onNone: (): readonly string[] => WorkspaceFiles.all(repositoryFullPath),
      onSome: (tree: string): readonly string[] =>
        WorkspaceTree.list(repositoryFullPath, tree, ['tools']).map(
          (entry: WorkspaceTreeEntry): string => entry.relativePath,
        ),
    })
    return [
      ...new Set([
        'requirements',
        ...files.filter(
          (relativePath: string): boolean =>
            relativePath.startsWith('tools/') && relativePath.endsWith('.c4'),
        ),
        ...Object.values(version.needs).flatMap((need: BoundNeed): readonly string[] =>
          need.bindings.flatMap((binding: SourceBinding): readonly string[] =>
            binding.kind === 'file' ? [binding.target] : [],
          ),
        ),
      ]),
    ]
  }

  private static version(
    repositoryFullPath: string,
    revision: Option.Option<string>,
  ): BindingVersion {
    const relativePathSchema: z.ZodType<string> = z
      .string()
      .min(1)
      .refine(
        (value: string): boolean =>
          !isAbsolute(value) &&
          !value.includes('\\') &&
          posix.normalize(value) === value &&
          value !== '..' &&
          !value.startsWith('../'),
      )
    const bindingSchema: z.ZodType<SourceBinding> = z.union([
      z
        .object({ kind: z.literal('file'), relativePath: relativePathSchema })
        .transform((binding: FileBinding): SourceBinding => ({
          kind: binding.kind,
          target: binding.relativePath,
        })),
      z
        .object({ kind: z.literal('architecture'), element: z.string().min(1) })
        .transform((binding: ArchitectureBinding): SourceBinding => ({
          kind: binding.kind,
          target: binding.element,
        })),
    ])
    const needSchema: z.ZodType<BoundNeed> = z.object({
      id: z.uuidv4(),
      type: z.enum(['source', 'design', 'implementation', 'verification']),
      title: z.string().min(1),
      status: z.string().min(1),
      implements: z.array(z.uuidv4()),
      content: z.string().min(1),
      bindings: z.array(bindingSchema).min(1),
    })
    const document: BindingDocument = z
      .object({
        current_version: z.string(),
        versions: z.record(z.string(), z.object({ needs: z.record(z.string(), needSchema) })),
      })
      .parse(
        JSON.parse(
          RequirementsSources.read(repositoryFullPath, 'requirements/bindings.json', revision),
        ),
      )
    return Option.getOrThrow(Option.fromNullishOr(document.versions[document.current_version]))
  }

  private static render(
    repositoryFullPath: string,
    binding: SourceBinding,
    revision: Option.Option<string>,
    model: ArchitectureDeclarations,
  ): string {
    if (binding.kind === 'file') {
      return (
        binding.target +
        '\n\n.. code-block:: text\n\n   ' +
        RequirementsSources.read(repositoryFullPath, binding.target, revision).replaceAll(
          '\n',
          '\n   ',
        )
      )
    }
    const fragments: readonly SourceFragment[] = Option.getOrThrow(
      Option.fromNullishOr(model.reviewTargets.get(binding.target)),
    )
    return (
      binding.target +
      '\n\n.. code-block:: text\n\n   ' +
      fragments
        .map((fragment: SourceFragment): string => fragment.content)
        .join('')
        .replaceAll('\n', '\n   ')
    )
  }

  private static read(
    repositoryFullPath: string,
    relativePath: string,
    revision: Option.Option<string>,
  ): string {
    return Option.match(revision, {
      onNone: (): string => readFileSync(join(repositoryFullPath, relativePath), 'utf8'),
      onSome: (tree: string): string =>
        Option.getOrThrow(
          Option.fromNullishOr(
            Git.blobs(repositoryFullPath, [tree + ':' + relativePath]).get(
              tree + ':' + relativePath,
            ),
          ),
        ),
    })
  }
}
