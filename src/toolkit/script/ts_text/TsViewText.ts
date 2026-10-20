import { Option } from 'effect'
import ts from 'typescript'

/** @anchor bescript-ts-fragments */
export interface TsFragmentRevision {
  readonly id: string
  readonly revision: string
  readonly dependencies: string
}

/** @anchor bescript-ts-fragments */
export interface TsFragmentText extends TsFragmentRevision {
  readonly text: string
}

/** @anchor bescript-ts-positions */
export interface TsMethodPosition {
  readonly id: string
  readonly start: number
  readonly end: number
  readonly startLine: number
  readonly endLine: number
}

/** @anchor bescript-ts-fragments */
export interface TsPrintState {
  readonly shell: string
  readonly fragments: readonly TsFragmentText[]
  readonly text: string
  readonly positions: readonly TsMethodPosition[]
  readonly printedFragments: number
}

/** @anchor bescript-ts-text */
export interface TsParsedView {
  readonly sourceFile: ts.SourceFile
  readonly printed: TsPrintState
}

/** @anchor bescript-ts-fragments */
interface ViewStructure {
  readonly namespace: ts.ModuleDeclaration
  readonly block: ts.ModuleBlock
  readonly classes: readonly ts.ClassDeclaration[]
  readonly methods: readonly ViewMethod[]
}

/** @anchor bescript-ts-fragments */
interface ViewMethod {
  readonly declaration: ts.ClassDeclaration
  readonly method: ts.MethodDeclaration
  readonly body: ts.Block
}

/** @anchor bescript-ts-positions */
interface BodyRange {
  readonly start: number
  readonly end: number
  readonly methodStart: number
}

/**
 * @anchor bescript-ts-text
 * @anchor bescript-third-language-boundary
 */
export class TsViewText {
  private constructor() {}

  /** @anchor bescript-ts-text */
  public static parse(text: string): ts.SourceFile {
    const file: ts.SourceFile = ts.createSourceFile(
      'view.ts',
      text,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS,
    )
    const host: ts.CompilerHost = {
      getSourceFile: (): ts.SourceFile => file,
      getDefaultLibFileName: (): string => '',
      writeFile: (): void => {},
      getCurrentDirectory: (): string => '',
      getDirectories: (): string[] => [],
      fileExists: (name: string): boolean => name === 'view.ts',
      readFile: (): string => text,
      getCanonicalFileName: (name: string): string => name,
      useCaseSensitiveFileNames: (): boolean => true,
      getNewLine: (): string => '\n',
    }
    const program: ts.Program = ts.createProgram(
      ['view.ts'],
      { noLib: true, noResolve: true, types: [] },
      host,
    )
    const diagnostics: readonly ts.Diagnostic[] = program.getSyntacticDiagnostics(file)
    if (diagnostics.length > 0) throw new Error(ts.formatDiagnostics(diagnostics, host))
    return file
  }

  /** @anchor bescript-ts-text */
  public static read(text: string, revisions: readonly TsFragmentRevision[]): TsParsedView {
    const sourceFile: ts.SourceFile = TsViewText.parse(text)
    return { sourceFile, printed: TsViewText.print(sourceFile, revisions, Option.none()) }
  }

  /** @anchor bescript-ts-fragments */
  public static reusableMethods(
    sourceFile: ts.SourceFile,
    methodIds: readonly string[],
    reusableIds: ReadonlySet<string>,
  ): ReadonlyMap<string, ts.MethodDeclaration> {
    const methods: ts.MethodDeclaration[] = []
    const visit: (node: ts.Node) => void = (node: ts.Node): void => {
      if (ts.isMethodDeclaration(node)) methods.push(node)
      else ts.forEachChild(node, visit)
    }
    visit(sourceFile)
    const result: Map<string, ts.MethodDeclaration> = new Map()
    for (const [index, id] of methodIds.entries())
      if (reusableIds.has(id))
        result.set(id, Option.getOrThrow(Option.fromNullishOr(methods[index])))
    return result
  }

  /** @anchor bescript-ts-text */
  public static restore(original: TsParsedView, current: TsPrintState): string {
    const originalMethods: readonly ViewMethod[] = TsViewText.structure(original.sourceFile).methods
    const originals: ReadonlyMap<string, TsFragmentText> = new Map(
      original.printed.fragments.map(
        (fragment: TsFragmentText): readonly [string, TsFragmentText] => [fragment.id, fragment],
      ),
    )
    const sourceBodies: Map<string, string> = new Map()
    for (const [index, method] of originalMethods.entries()) {
      const fragment: TsFragmentText = Option.getOrThrow(
        Option.fromNullishOr(original.printed.fragments[index]),
      )
      sourceBodies.set(
        fragment.id,
        original.sourceFile.text.slice(method.body.getStart(original.sourceFile), method.body.end),
      )
    }
    if (
      current.shell === original.printed.shell &&
      current.fragments.length === original.printed.fragments.length &&
      current.fragments.every(
        (fragment: TsFragmentText, index: number): boolean =>
          fragment.id ===
          Option.getOrThrow(Option.fromNullishOr(original.printed.fragments[index])).id,
      )
    ) {
      const replacements: readonly TsFragmentText[] = current.fragments.map(
        (fragment: TsFragmentText): TsFragmentText => {
          const previous: TsFragmentText = Option.getOrThrow(
            Option.fromNullishOr(originals.get(fragment.id)),
          )
          return {
            ...fragment,
            text:
              previous.text === fragment.text
                ? Option.getOrThrow(Option.fromNullishOr(sourceBodies.get(fragment.id)))
                : fragment.text,
          }
        },
      )
      return TsViewText.assemble(
        original.sourceFile.text,
        TsViewText.ranges(original.sourceFile),
        replacements,
      ).text
    }
    const fragments: readonly TsFragmentText[] = current.fragments.map(
      (fragment: TsFragmentText): TsFragmentText => {
        const previous: Option.Option<TsFragmentText> = Option.fromNullishOr(
          originals.get(fragment.id),
        )
        return {
          ...fragment,
          text:
            Option.isSome(previous) && previous.value.text === fragment.text
              ? Option.getOrThrow(Option.fromNullishOr(sourceBodies.get(fragment.id)))
              : fragment.text,
        }
      },
    )
    return TsViewText.assemble(
      current.shell,
      TsViewText.ranges(TsViewText.parse(current.shell)),
      fragments,
    ).text
  }

  /**
   * @anchor bescript-ts-fragments
   * @fallback 没有上一轮打印状态时使用空片段缓存；冷启动必须打印全部当前方法体。
   */
  public static print(
    sourceFile: ts.SourceFile,
    revisions: readonly TsFragmentRevision[],
    previous: Option.Option<TsPrintState>,
  ): TsPrintState {
    const structure: ViewStructure = TsViewText.structure(sourceFile)
    if (
      structure.methods.length !== revisions.length ||
      new Set(revisions.map((revision: TsFragmentRevision): string => revision.id)).size !==
        revisions.length
    )
      throw new Error('方法体与唯一片段身份数量必须一致')
    const printer: ts.Printer = ts.createPrinter({ newLine: ts.NewLineKind.LineFeed })
    const cached: ReadonlyMap<string, TsFragmentText> = new Map(
      Option.isSome(previous)
        ? previous.value.fragments.map(
            (fragment: TsFragmentText): readonly [string, TsFragmentText] => [
              fragment.id,
              fragment,
            ],
          )
        : [],
    )
    const shell: string = printer.printFile(TsViewText.shell(sourceFile, structure))
    const shellRanges: readonly BodyRange[] = TsViewText.ranges(TsViewText.syntax(shell))
    let printedFragments: number = 0
    const fragments: readonly TsFragmentText[] = structure.methods.map(
      (method: ViewMethod, index: number): TsFragmentText => {
        const revision: TsFragmentRevision = Option.getOrThrow(
          Option.fromNullishOr(revisions[index]),
        )
        const old: Option.Option<TsFragmentText> = Option.fromNullishOr(cached.get(revision.id))
        if (
          Option.isSome(old) &&
          old.value.revision === revision.revision &&
          old.value.dependencies === revision.dependencies
        )
          return old.value
        printedFragments += 1
        const text: string = printer.printFile(TsViewText.isolate(sourceFile, structure, method))
        const parsed: ts.SourceFile = TsViewText.syntax(text)
        const range: BodyRange = Option.getOrThrow(
          Option.fromNullishOr(TsViewText.ranges(parsed)[0]),
        )
        return { ...revision, text: text.slice(range.start, range.end) }
      },
    )
    const assembled: TsPrintState = TsViewText.assemble(shell, shellRanges, fragments)
    return { ...assembled, printedFragments }
  }

  /** @anchor bescript-ts-fragments */
  private static structure(file: ts.SourceFile): ViewStructure {
    if (file.statements.length !== 1) throw new Error('TS 零件视图必须只有一个 namespace')
    const namespace: ts.Statement = Option.getOrThrow(Option.fromNullishOr(file.statements[0]))
    if (!ts.isModuleDeclaration(namespace)) throw new Error('TS 零件视图必须使用 namespace')
    const block: ts.ModuleBody = Option.getOrThrow(Option.fromNullishOr(namespace.body))
    if (!ts.isModuleBlock(block)) throw new Error('TS 零件视图 namespace 必须使用块')
    const classes: ts.ClassDeclaration[] = []
    const methods: ViewMethod[] = []
    for (const declaration of block.statements) {
      if (!ts.isClassDeclaration(declaration)) throw new Error('TS 零件视图 namespace 只允许 class')
      classes.push(declaration)
      for (const member of declaration.members) {
        if (ts.isMethodDeclaration(member))
          methods.push({
            declaration,
            method: member,
            body: Option.getOrThrow(Option.fromNullishOr(member.body)),
          })
        else if (!ts.isPropertyDeclaration(member))
          throw new Error('TS 零件 class 只允许字段和方法')
      }
    }
    return { namespace, block, classes, methods }
  }

  /** @anchor bescript-ts-fragments */
  private static shell(file: ts.SourceFile, structure: ViewStructure): ts.SourceFile {
    const classes: readonly ts.ClassDeclaration[] = structure.classes.map(
      (declaration: ts.ClassDeclaration): ts.ClassDeclaration =>
        ts.factory.updateClassDeclaration(
          declaration,
          declaration.modifiers,
          declaration.name,
          declaration.typeParameters,
          declaration.heritageClauses,
          declaration.members.map((member: ts.ClassElement): ts.ClassElement => {
            if (!ts.isMethodDeclaration(member)) return member
            const original: ts.Block = Option.getOrThrow(Option.fromNullishOr(member.body))
            const body: ts.Block = ts.setTextRange(ts.factory.createBlock([], true), original)
            ts.setEmitFlags(body, ts.EmitFlags.NoComments)
            return ts.factory.updateMethodDeclaration(
              member,
              member.modifiers,
              member.asteriskToken,
              member.name,
              member.questionToken,
              member.typeParameters,
              member.parameters,
              member.type,
              body,
            )
          }),
        ),
    )
    return TsViewText.wrap(file, structure, classes)
  }

  /** @anchor bescript-ts-fragments */
  private static isolate(
    file: ts.SourceFile,
    structure: ViewStructure,
    method: ViewMethod,
  ): ts.SourceFile {
    const declaration: ts.ClassDeclaration = method.declaration
    const isolated: ts.ClassDeclaration = ts.factory.updateClassDeclaration(
      declaration,
      declaration.modifiers,
      declaration.name,
      declaration.typeParameters,
      declaration.heritageClauses,
      [method.method],
    )
    return TsViewText.wrap(file, structure, [isolated])
  }

  /** @anchor bescript-ts-fragments */
  private static wrap(
    file: ts.SourceFile,
    structure: ViewStructure,
    classes: readonly ts.ClassDeclaration[],
  ): ts.SourceFile {
    return ts.factory.updateSourceFile(file, [
      ts.factory.updateModuleDeclaration(
        structure.namespace,
        structure.namespace.modifiers,
        structure.namespace.name,
        ts.factory.updateModuleBlock(structure.block, classes),
      ),
    ])
  }

  /** @anchor bescript-ts-positions */
  private static ranges(file: ts.SourceFile): readonly BodyRange[] {
    return TsViewText.structure(file).methods.map((method: ViewMethod): BodyRange => ({
      start: method.body.getStart(file),
      end: method.body.end,
      methodStart: method.method.getStart(file),
    }))
  }

  /** @anchor bescript-ts-positions */
  private static assemble(
    shell: string,
    ranges: readonly BodyRange[],
    fragments: readonly TsFragmentText[],
  ): TsPrintState {
    const pieces: string[] = []
    const offsets: BodyRange[] = []
    let cursor: number = 0
    let delta: number = 0
    for (const [index, range] of ranges.entries()) {
      const fragment: TsFragmentText = Option.getOrThrow(Option.fromNullishOr(fragments[index]))
      pieces.push(shell.slice(cursor, range.start), fragment.text)
      offsets.push({
        start: range.start + delta,
        end: range.start + delta + fragment.text.length,
        methodStart: range.methodStart + delta,
      })
      delta += fragment.text.length - (range.end - range.start)
      cursor = range.end
    }
    pieces.push(shell.slice(cursor))
    const text: string = pieces.join('')
    const lines: ts.SourceFileLike = {
      text,
      getLineAndCharacterOfPosition(position: number): ts.LineAndCharacter {
        return ts.getLineAndCharacterOfPosition(this, position)
      },
    }
    const positions: readonly TsMethodPosition[] = offsets.map(
      (range: BodyRange, index: number): TsMethodPosition => ({
        id: Option.getOrThrow(Option.fromNullishOr(fragments[index])).id,
        start: range.methodStart,
        end: range.end,
        startLine: lines.getLineAndCharacterOfPosition(range.methodStart).line + 1,
        endLine: lines.getLineAndCharacterOfPosition(range.end).line + 1,
      }),
    )
    return { shell, fragments, text, positions, printedFragments: 0 }
  }

  /** @anchor bescript-ts-text */
  private static syntax(text: string): ts.SourceFile {
    return ts.createSourceFile('view.ts', text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  }
}
