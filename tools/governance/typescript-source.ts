import ts from 'typescript'
import { Option } from 'effect'

export class TypeScriptSource {
  private constructor() {}

  public static isDocHost(node: ts.Node): boolean {
    return (
      ts.isClassDeclaration(node) ||
      ts.isInterfaceDeclaration(node) ||
      ts.isEnumDeclaration(node) ||
      ts.isTypeAliasDeclaration(node) ||
      ts.isMethodSignature(node) ||
      ts.isConstructorDeclaration(node) ||
      ((ts.isMethodDeclaration(node) ||
        ts.isGetAccessorDeclaration(node) ||
        ts.isSetAccessorDeclaration(node)) &&
        !ts.isObjectLiteralExpression(node.parent)) ||
      (ts.isFunctionDeclaration(node) && ts.isSourceFile(node.parent))
    )
  }

  public static declarations(content: string, relativePath: string): readonly string[] {
    const source: ts.SourceFile = ts.createSourceFile(
      relativePath,
      content,
      ts.ScriptTarget.Latest,
      true,
    )
    const declarations: string[] = []
    const visit: (node: ts.Node, owners: readonly string[]) => void = (
      node: ts.Node,
      owners: readonly string[],
    ): void => {
      if (
        ts.isClassDeclaration(node) ||
        ts.isInterfaceDeclaration(node) ||
        ts.isEnumDeclaration(node) ||
        ts.isTypeAliasDeclaration(node) ||
        ts.isMethodDeclaration(node) ||
        ts.isMethodSignature(node) ||
        ts.isGetAccessorDeclaration(node) ||
        ts.isSetAccessorDeclaration(node) ||
        ts.isConstructorDeclaration(node) ||
        ts.isFunctionDeclaration(node)
      ) {
        const name: Option.Option<string> = ts.isConstructorDeclaration(node)
          ? Option.some('constructor')
          : Option.map(
              Option.fromNullishOr(ts.getNameOfDeclaration(node)),
              (declaration: ts.DeclarationName): string => declaration.getText(source),
            )
        const names: readonly string[] = Option.match(name, {
          onNone: (): readonly string[] => owners,
          onSome: (value: string): readonly string[] => [...owners, value],
        })
        if (Option.isSome(name)) declarations.push(relativePath + '#/' + names.join('/'))
        ts.forEachChild(node, (child: ts.Node): void => {
          visit(child, names)
        })
      } else
        ts.forEachChild(node, (child: ts.Node): void => {
          visit(child, owners)
        })
    }
    visit(source, [])
    return declarations
  }
}
