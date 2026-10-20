import { Option } from 'effect'
import ts from 'typescript'
import type { BescriptDeviceContext, DeviceFunction } from '../device_context/DeviceContext.ts'
import type { ScriptGroup } from '../device_context/ScriptList.ts'
import type { CallKind, CallSignature } from '../local_ast/LocalAst.ts'
import type { ScriptParameter, VarTypeName } from '../vocabulary/Vocabulary.ts'

type ConversionGroupKind = 'custom' | 'event'
type ConstantKind = 'value' | 'empty' | 'canvas' | 'enum'

/** @anchor bescript-ts-basis */
interface DeviceIdentity {
  readonly deviceRelativePath: string
  readonly id: string
  readonly scope: string
  readonly namespaceName: string
  readonly className: string
  readonly baseClassName: string
  readonly internalCore: Option.Option<CoreIdentity>
}

/** @anchor bescript-ts-basis */
interface CoreIdentity {
  readonly scope: string
  readonly id: string
  readonly deviceType: string
  readonly logicName: Option.Option<string>
  readonly className: string
  readonly baseClassName: string
}

/** @anchor bescript-ts-basis */
export interface ScriptAssociation {
  readonly kind: ConversionGroupKind
  readonly eventName: Option.Option<string>
  readonly bindingName: string
  readonly scriptRelativePath: string
  readonly guid: Option.Option<string>
  readonly tsName: string
}

/** @anchor bescript-ts-basis */
interface PlatformType {
  readonly name: VarTypeName
  readonly tsName: string
  readonly bases: readonly VarTypeName[]
}

/** @anchor bescript-ts-basis */
export interface ParameterSemantics {
  readonly typeName: string
  readonly valueType: VarTypeName
  readonly constant: ConstantKind
  readonly enumName: Option.Option<string>
}

/** @anchor bescript-ts-basis */
export interface PlatformMethod {
  readonly kind: CallKind
  readonly ownerType: Option.Option<string>
  readonly besName: string
  readonly tsName: string
  readonly receiver: string
  readonly typeArguments: readonly VarTypeName[]
  readonly parameters: readonly ParameterSemantics[]
  readonly argumentOrder: readonly number[]
  readonly returnType: Option.Option<VarTypeName>
  readonly asynchronous: boolean
}

/** @anchor bescript-ts-basis */
export interface PlatformEvent {
  readonly name: string
  readonly parameters: readonly ScriptParameter[]
}

/** @anchor bescript-ts-basis */
export interface PlatformEnum {
  readonly name: string
  readonly tsName: string
  readonly values: readonly PlatformEnumValue[]
}

/** @anchor bescript-ts-basis */
export interface PlatformEnumValue {
  readonly value: string
  readonly tsName: string
}

/** @anchor bescript-ts-basis */
interface IdentifierMapping {
  readonly besName: string
  readonly tsName: string
}

/** @anchor bescript-ts-basis */
export interface TargetMethod {
  readonly besName: string
  readonly tsName: string
  readonly parameters: readonly ScriptParameter[]
  readonly returnType: Option.Option<VarTypeName>
  readonly isPrivate: boolean
  readonly asynchronous: boolean
}

/** @anchor bescript-ts-basis */
export interface ActTarget {
  readonly scope: string
  readonly id: string
  readonly deviceType: string
  readonly logicName: Option.Option<string>
  readonly className: string
  readonly methods: readonly TargetMethod[]
}

/** @anchor bescript-ts-basis */
export interface ConversionBasisData {
  readonly identity: DeviceIdentity
  readonly scripts: readonly ScriptAssociation[]
  readonly targets: readonly ActTarget[]
  readonly types: readonly PlatformType[]
  readonly methods: readonly PlatformMethod[]
  readonly events: readonly PlatformEvent[]
  readonly enums: readonly PlatformEnum[]
  readonly identifiers: readonly IdentifierMapping[]
}

/** @anchor bescript-ts-basis */
export class ConversionBasis {
  /** @anchor bescript-ts-basis */
  private constructor(public readonly data: ConversionBasisData) {
    for (const relativePath of [
      data.identity.deviceRelativePath,
      ...data.scripts.map((script: ScriptAssociation): string => script.scriptRelativePath),
    ]) {
      if (
        relativePath.length === 0 ||
        relativePath.includes('\\') ||
        relativePath.startsWith('/') ||
        relativePath.includes(':') ||
        relativePath.split('/').includes('..')
      )
        throw new Error('转换依据必须使用工程相对路径: ' + relativePath)
    }
    for (const method of data.methods) {
      if (
        method.argumentOrder.length !== method.parameters.length ||
        new Set(method.argumentOrder).size !== method.parameters.length ||
        method.argumentOrder.some(
          (index: number): boolean =>
            !Number.isInteger(index) || index < 0 || index >= method.parameters.length,
        )
      )
        throw new Error('平台参数映射必须是完整的双向排列: ' + method.besName)
    }
    ConversionBasis.unique(
      data.types.map((type: PlatformType): string => type.name),
      '平台类型',
    )
    ConversionBasis.unique(
      data.types.map((type: PlatformType): string => type.tsName),
      'TS 类型',
    )
    ConversionBasis.unique(
      data.methods.map((method: PlatformMethod): string =>
        JSON.stringify([method.kind, method.ownerType, method.besName]),
      ),
      'BES 方法',
    )
    ConversionBasis.unique(
      data.methods.map((method: PlatformMethod): string =>
        JSON.stringify([
          method.kind,
          method.ownerType,
          method.receiver,
          method.tsName,
          method.typeArguments,
        ]),
      ),
      'TS 方法',
    )
    ConversionBasis.unique(
      data.targets.map((target: ActTarget): string => JSON.stringify([target.scope, target.id])),
      'act 目标',
    )
    ConversionBasis.unique(
      data.targets.map((target: ActTarget): string => target.className),
      'act 类型',
    )
    for (const target of data.targets) {
      if (!ConversionBasis.decimal(target.id) || !ConversionBasis.decimal(target.deviceType))
        throw new Error('act ID 与类型必须为十进制数字')
    }
    ConversionBasis.unique(
      data.identifiers.map((name: IdentifierMapping): string => name.besName),
      'BES 标识符',
    )
    ConversionBasis.unique(
      data.identifiers.map((name: IdentifierMapping): string => name.tsName),
      'TS 标识符',
    )
  }

  /** @anchor bescript-ts-basis */
  public static create(data: ConversionBasisData): ConversionBasis {
    return new ConversionBasis(data)
  }

  /** @anchor bescript-ts-basis */
  public type(name: VarTypeName): string {
    return Option.getOrThrow(
      Option.fromNullishOr(
        this.data.types.find((type: PlatformType): boolean => type.name === name),
      ),
    ).tsName
  }

  /** @anchor bescript-ts-basis */
  public fromType(node: ts.TypeNode): VarTypeName {
    if (
      !ts.isTypeReferenceNode(node) ||
      !ts.isIdentifier(node.typeName) ||
      Option.isSome(Option.fromNullishOr(node.typeArguments))
    )
      throw new Error('必须使用明确的平台类型')
    const name: string = node.typeName.text
    return Option.getOrThrow(
      Option.fromNullishOr(
        this.data.types.find((type: PlatformType): boolean => type.tsName === name),
      ),
    ).name
  }

  /** @anchor bescript-ts-basis */
  public assignable(actual: VarTypeName, expected: VarTypeName): void {
    const pending: VarTypeName[] = [actual]
    const seen: Set<VarTypeName> = new Set()
    while (pending.length > 0) {
      const current: VarTypeName = Option.getOrThrow(Option.fromNullishOr(pending.pop()))
      if (current === expected) return
      if (seen.has(current)) continue
      seen.add(current)
      pending.push(
        ...Option.getOrThrow(
          Option.fromNullishOr(
            this.data.types.find((type: PlatformType): boolean => type.name === current),
          ),
        ).bases,
      )
    }
    throw new Error('平台类型不兼容: ' + actual + ' → ' + expected)
  }

  /**
   * @anchor bescript-ts-basis
   * @fallback 未登记的合法标识符保持原名，只有不满足官方 Identifier 语法时才失败。
   */
  public identifier(raw: string): string {
    const mapped: Option.Option<IdentifierMapping> = Option.fromNullishOr(
      this.data.identifiers.find((name: IdentifierMapping): boolean => name.besName === raw),
    )
    const name: string = Option.isSome(mapped) ? mapped.value.tsName : raw
    const scanner: ts.Scanner = ts.createScanner(
      ts.ScriptTarget.Latest,
      true,
      ts.LanguageVariant.Standard,
      name,
    )
    if (
      scanner.scan() !== ts.SyntaxKind.Identifier ||
      scanner.getTokenText() !== name ||
      scanner.scan() !== ts.SyntaxKind.EndOfFileToken
    )
      throw new Error('标识符需要显式 TS 映射: ' + raw)
    return name
  }

  /**
   * @anchor bescript-ts-basis
   * @fallback 未登记的 TS 标识符按原名作为 BES 标识符，避免凭空改写名称。
   */
  public rawIdentifier(name: string): string {
    const mapped: Option.Option<IdentifierMapping> = Option.fromNullishOr(
      this.data.identifiers.find((item: IdentifierMapping): boolean => item.tsName === name),
    )
    return Option.isSome(mapped) ? mapped.value.besName : name
  }

  /** @anchor bescript-ts-basis */
  public method(call: CallSignature): PlatformMethod {
    const matches: PlatformMethod[] = this.data.methods.filter(
      (method: PlatformMethod): boolean =>
        method.kind === call.kind &&
        method.besName === call.functionName &&
        Option.makeEquivalence<string>((left: string, right: string): boolean => left === right)(
          method.ownerType,
          call.ownerType,
        ),
    )
    if (matches.length !== 1)
      throw new Error('平台方法映射必须唯一: ' + call.kind + ' ' + call.saveName)
    return Option.getOrThrow(Option.fromNullishOr(matches[0]))
  }

  /**
   * @anchor bescript-ts-basis
   * @fallback 没有机械核心时当前目标采用零件身份；有核心时采用核心身份。
   */
  public isCurrent(target: ActTarget): boolean {
    return Option.match(this.data.identity.internalCore, {
      onNone: (): boolean =>
        target.scope === this.data.identity.scope && target.id === this.data.identity.id,
      onSome: (core: CoreIdentity): boolean => target.scope === core.scope && target.id === core.id,
    })
  }

  /**
   * @anchor bescript-ts-basis
   * @fallback 当前身份没有机械核心时使用零件所属作用域。
   * @fallback 当前身份有机械核心时使用核心所属作用域。
   */
  public target(call: CallSignature): ActTarget {
    const name: string = Option.getOrThrow(call.ownerName)
    const scope: string = Option.match(this.data.identity.internalCore, {
      onNone: (): string => this.data.identity.scope,
      onSome: (core: CoreIdentity): string => core.scope,
    })
    const ownId: string = Option.match(this.data.identity.internalCore, {
      onNone: (): string => this.data.identity.id,
      onSome: (core: CoreIdentity): string => core.id,
    })
    const candidates: ActTarget[] = this.data.targets.filter(
      (target: ActTarget): boolean =>
        target.scope === scope &&
        target.deviceType === Option.getOrThrow(call.ownerType) &&
        (name === 'this'
          ? target.id === ownId
          : call.ownerNameIsAlias
            ? Option.contains(name)(target.logicName)
            : target.id === name),
    )
    candidates.sort((left: ActTarget, right: ActTarget): number =>
      BigInt(left.id) < BigInt(right.id) ? -1 : BigInt(left.id) > BigInt(right.id) ? 1 : 0,
    )
    if (candidates.length === 0) throw new Error('缺少 act 目标闭包: ' + call.saveName)
    return Option.getOrThrow(Option.fromNullishOr(candidates[0]))
  }

  /**
   * @anchor bescript-ts-basis
   * @fallback 当前身份没有机械核心时使用零件作用域。
   * @fallback 当前身份有机械核心时使用核心作用域。
   * @fallback 当前身份没有机械核心时使用 Context 的零件类型。
   * @fallback 当前身份没有机械核心时使用 Context 的逻辑名。
   * @fallback 当前身份没有机械核心时使用零件 class 名。
   */
  public static currentTargets(
    context: BescriptDeviceContext,
    basis: ConversionBasisData,
  ): readonly ActTarget[] {
    const own: ActTarget = {
      scope: Option.match(basis.identity.internalCore, {
        onNone: (): string => basis.identity.scope,
        onSome: (core: CoreIdentity): string => core.scope,
      }),
      id: Option.match(basis.identity.internalCore, {
        onNone: (): string => basis.identity.id,
        onSome: (core: CoreIdentity): string => core.id,
      }),
      deviceType: Option.match(basis.identity.internalCore, {
        onNone: (): string => String(context.metadata.deviceType),
        onSome: (core: CoreIdentity): string => core.deviceType,
      }),
      logicName: Option.match(basis.identity.internalCore, {
        onNone: (): Option.Option<string> => context.metadata.logicName,
        onSome: (core: CoreIdentity): Option.Option<string> => core.logicName,
      }),
      className: Option.match(basis.identity.internalCore, {
        onNone: (): string => basis.identity.className,
        onSome: (core: CoreIdentity): string => core.className,
      }),
      methods: context.scriptGroups
        .filter((group: ScriptGroup<DeviceFunction>): boolean => group.kind === 'custom')
        .flatMap((group: ScriptGroup<DeviceFunction>): TargetMethod[] =>
          group.entries.map((item: DeviceFunction): TargetMethod => ({
            besName: item.binding.name,
            tsName: Option.getOrThrow(
              Option.fromNullishOr(
                basis.scripts.find(
                  (script: ScriptAssociation): boolean =>
                    script.kind === 'custom' && script.bindingName === item.binding.name,
                ),
              ),
            ).tsName,
            parameters: item.ast.header.parameters,
            returnType: Option.map(
              item.ast.header.returnParameter,
              (parameter: ScriptParameter): VarTypeName => parameter.typeName,
            ),
            isPrivate: item.ast.header.isPrivate || item.binding.isPrivate,
            asynchronous: false,
          })),
        ),
    }
    return [
      ...basis.targets.filter(
        (target: ActTarget): boolean => target.scope !== own.scope || target.id !== own.id,
      ),
      own,
    ]
  }

  /** @anchor bescript-ts-basis */
  private static decimal(value: string): boolean {
    return (
      value.length > 0 &&
      value.split('').every((character: string): boolean => character >= '0' && character <= '9')
    )
  }

  /** @anchor bescript-ts-basis */
  private static unique(names: readonly string[], label: string): void {
    if (new Set(names).size !== names.length) throw new Error(label + '映射重复')
  }
}
