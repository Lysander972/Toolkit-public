import { Option } from 'effect'
import type { LocalAstNode } from '../local_ast/LocalAst.ts'
import type { NodeSource, SourceField } from '../local_ast/SourceLayout.ts'
import { CallText } from './CallText.ts'
import { SourceWriter } from './SourceWriter.ts'
import { ArgumentWriter } from './ArgumentWriter.ts'

/** @anchor bescript-body-output */
export class BodyWriter {
  private readonly used: Set<number> = new Set()
  private readonly sources: ReadonlyMap<number, NodeSource>

  /** @anchor bescript-body-output */
  public constructor(sources: readonly NodeSource[]) {
    this.sources = new Map(
      sources.map((source: NodeSource): [number, NodeSource] => [source.id, source]),
    )
  }

  /** @anchor bescript-body-output */
  public write(nodes: readonly LocalAstNode[]): string {
    return nodes.map((node: LocalAstNode): string => this.node(node)).join('')
  }

  /**
   * @anchor bescript-body-output
   * @fallback sourceRef=None 明确标识新增节点，使用用户要求的生成格式。
   * @fallback 由非块节点编辑成块时原文没有块尾，必须生成新增的结束行。
   */
  private node(node: LocalAstNode): string {
    if (Option.isNone(node.sourceRef)) return this.generated(node)
    if (this.used.has(node.sourceRef.value))
      throw new Error('同一来源节点不能重复使用；新增节点必须显式使用 None')
    this.used.add(node.sourceRef.value)
    const source: NodeSource = Option.getOrThrow(
      Option.fromNullishOr(this.sources.get(node.sourceRef.value)),
    )
    const prefix: string =
      node.depth === source.depth
        ? this.prefix(node, source)
        : source.numberPrefix + (node.disabled ? '/*' : '') + ' '.repeat(node.depth * 2)
    const opening: string = source.leading + prefix + this.command(node, source) + source.suffix
    if (node.kind !== 'if' && node.kind !== 'while') return opening
    const children: string = this.write(node.children)
    if (Option.isNone(source.close)) return opening + children + ' '.repeat(node.depth * 2) + '}\n'
    const close: string =
      node.depth === source.depth
        ? source.close.value
        : source.closeNumberPrefix +
          (source.closeDisabled ? '/*' : '') +
          ' '.repeat(node.depth * 2) +
          source.close.value.slice(source.closePrefixLength)
    return opening + children + source.tail + close
  }

  /** @anchor bescript-body-output */
  private command(node: LocalAstNode, source: NodeSource): string {
    const values: Map<string, string> = new Map(CallText.fields(node))
    if (Option.isSome(node.call) && source.callKind === '') return CallText.command(node)
    if (Option.isNone(node.call) && source.callKind !== '') return CallText.command(node)
    if (Option.isSome(node.call)) {
      const emptyArguments: boolean =
        node.call.value.arguments.length === 0 && source.argumentCount === 0
      const region: SourceField = Option.getOrThrow(
        Option.fromNullishOr(
          source.fields.find((field: SourceField): boolean => field.key === 'arguments'),
        ),
      )
      values.set(
        'arguments',
        emptyArguments
          ? source.command.slice(region.start, region.end)
          : new ArgumentWriter(source).write(node.call.value.arguments),
      )
    }
    const changedSignature: boolean =
      Option.isSome(node.call) &&
      (CallText.signature(node.call.value).length === 1) !==
        (source.callKind === 'fun' || source.callKind === 'toolf')
    const leafReturn: boolean =
      source.hasReturn && Option.isSome(node.call) && Option.isSome(node.call.value.returnVariable)
    const fields: readonly SourceField[] = source.fields
      .filter((field: SourceField): boolean => {
        if (field.key === 'arguments') return true
        if (field.key === 'signature') return changedSignature
        if (field.key.startsWith('signature')) return !changedSignature
        if (field.key === 'return') return !leafReturn
        if (field.key === 'returnType' || field.key === 'returnName') return leafReturn
        if (field.key.startsWith('type') || field.key.startsWith('value')) return false
        return true
      })
      .toSorted((left: SourceField, right: SourceField): number =>
        left.start !== right.start ? left.start - right.start : left.end - right.end,
      )
      .map((field: SourceField): SourceField =>
        field.key === 'arguments'
          ? { ...field, value: source.command.slice(field.start, field.end) }
          : field,
      )
    return SourceWriter.rewrite(source.command, fields, values)
  }

  /** @anchor bescript-body-output */
  private generated(node: LocalAstNode): string {
    const indent: string = ' '.repeat(node.depth * 2)
    const opening: string = (node.disabled ? '/*' : '') + indent + CallText.command(node) + '\n'
    if (node.kind !== 'if' && node.kind !== 'while') return opening
    return opening + this.write(node.children) + indent + '}\n'
  }

  /** @anchor bescript-body-output */
  private prefix(node: LocalAstNode, source: NodeSource): string {
    if (node.disabled === source.disabled) return source.prefix
    return (
      source.prefix.slice(0, source.disabledStart) +
      (node.disabled ? '/*' : '') +
      source.prefix.slice(source.disabledStart + (source.disabled ? 2 : 0))
    )
  }
}
