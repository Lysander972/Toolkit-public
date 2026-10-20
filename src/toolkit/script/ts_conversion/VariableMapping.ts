import { Option } from 'effect'
import ts from 'typescript'
import { LocalAstSchema, type LocalAst, type VariableBinding } from '../local_ast/LocalAst.ts'
import type { GlobalVariable } from '../device_context/GlobalVariables.ts'
import type { ScriptParameter, VarTypeName } from '../vocabulary/Vocabulary.ts'
import { ConversionBasis } from './ConversionBasis.ts'
import { TsSyntax } from './TsSyntax.ts'

/** @anchor bescript-ts-bindings */
export class VariableMapping {
  private readonly locals: Map<string, VarTypeName> = new Map()
  private readonly globals: Map<string, VarTypeName>
  private readonly emitted: Set<string> = new Set()
  /** @anchor bescript-ts-bindings */
  public constructor(
    private readonly basis: ConversionBasis,
    globals: readonly GlobalVariable[],
    parameters: readonly ScriptParameter[],
  ) {
    this.globals = new Map(
      globals.map((variable: GlobalVariable): [string, VarTypeName] => [
        variable.name,
        variable.typeName,
      ]),
    )
    for (const parameter of parameters) {
      this.locals.set(parameter.name, parameter.typeName)
      this.emitted.add(parameter.name)
    }
  }

  /** @anchor bescript-ts-bindings */
  public collect(ast: LocalAst): void {
    const parameters: Set<string> = new Set(
      ast.header.parameters.map((parameter: ScriptParameter): string => parameter.name),
    )
    for (const binding of LocalAstSchema.bindings(ast)) {
      if (parameters.has(binding.name) || !this.globals.has(binding.name))
        this.locals.set(binding.name, binding.typeName)
    }
    if (
      Option.isSome(ast.header.returnParameter) &&
      !this.locals.has(ast.header.returnParameter.value.name) &&
      !this.globals.has(ast.header.returnParameter.value.name)
    )
      this.locals.set(
        ast.header.returnParameter.value.name,
        ast.header.returnParameter.value.typeName,
      )
  }

  /** @anchor bescript-ts-bindings */
  public expression(name: string): ts.Expression {
    if (this.locals.has(name)) return ts.factory.createIdentifier(this.basis.identifier(name))
    if (this.globals.has(name)) return TsSyntax.access(ts.factory.createThis(), name)
    throw new Error('变量没有声明来源: ' + name)
  }

  /** @anchor bescript-ts-bindings */
  public fromExpression(expression: ts.Expression): ScriptParameter {
    const node: ts.Expression = TsSyntax.unwrap(expression)
    if (ts.isIdentifier(node)) {
      const name: string = this.basis.rawIdentifier(node.text)
      return { name, typeName: Option.getOrThrow(Option.fromNullishOr(this.locals.get(name))) }
    }
    if (
      (ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) &&
      node.expression.kind === ts.SyntaxKind.ThisKeyword
    ) {
      const name: string = ts.isPropertyAccessExpression(node)
        ? node.name.text
        : TsSyntax.name(node.argumentExpression)
      return { name, typeName: Option.getOrThrow(Option.fromNullishOr(this.globals.get(name))) }
    }
    throw new Error('不支持的变量引用')
  }

  /** @anchor bescript-ts-bindings */
  public type(name: string): VarTypeName {
    return Option.getOrThrow(
      Option.fromNullishOr(this.locals.has(name) ? this.locals.get(name) : this.globals.get(name)),
    )
  }

  /** @anchor bescript-ts-bindings */
  public declare(parameter: ScriptParameter): void {
    if (this.locals.has(parameter.name) && this.locals.get(parameter.name) !== parameter.typeName)
      throw new Error('局部绑定类型冲突: ' + parameter.name)
    if (this.emitted.has(parameter.name)) throw new Error('局部绑定重复声明: ' + parameter.name)
    this.locals.set(parameter.name, parameter.typeName)
    this.emitted.add(parameter.name)
  }

  /** @anchor bescript-ts-bindings */
  public declaration(name: string): Option.Option<ts.VariableStatement> {
    if (!this.locals.has(name) || this.emitted.has(name)) return Option.none()
    this.emitted.add(name)
    return Option.some(
      ts.factory.createVariableStatement(
        undefined,
        ts.factory.createVariableDeclarationList(
          [
            ts.factory.createVariableDeclaration(
              this.basis.identifier(name),
              undefined,
              ts.factory.createTypeReferenceNode(this.basis.type(this.type(name))),
              undefined,
            ),
          ],
          ts.NodeFlags.None,
        ),
      ),
    )
  }

  /** @anchor bescript-ts-bindings */
  public bindings(): readonly VariableBinding[] {
    return Array.from(this.locals, ([name, typeName]: [string, VarTypeName]): VariableBinding => ({
      name,
      typeName,
    }))
  }
}
