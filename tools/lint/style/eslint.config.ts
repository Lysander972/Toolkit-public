import tseslint from 'typescript-eslint'
import unicorn from 'eslint-plugin-unicorn'
import sonarjs from 'eslint-plugin-sonarjs'
import eslintComments from '@eslint-community/eslint-plugin-eslint-comments'
import { resolve } from 'node:path'
import type { Linter } from 'eslint'

const repositoryFullPath: string = resolve(import.meta.dirname, '../../..')

/** @anchor java-style-config */
export class CodebaseConfiguration {
  private static readonly restrictedGlobals: readonly string[] = [
    'require',
    'module',
    'exports',
    '__dirname',
    '__filename',
  ]

  private constructor() {}

  /** @anchor java-style-config */
  public static create(implementationRelativePaths: readonly string[]): Linter.Config[] {
    const rules: Linter.RulesRecord = {
      'no-var': 'error',
      'eslint-comments/no-use': ['error', { allow: ['eslint-disable-next-line'] }],
      'eslint-comments/no-restricted-disable': ['error', '*', '!no-restricted-properties'],
      'eslint-comments/require-description': 'error',
      'no-param-reassign': ['error', { props: true }],
      '@typescript-eslint/prefer-readonly': 'error',
      '@typescript-eslint/no-unused-vars': 'off',
      'no-eval': 'error',
      'no-constant-binary-expression': 'error',
      'no-duplicate-case': 'error',
      'no-fallthrough': 'error',
      'no-unsafe-finally': 'error',
      'no-unreachable': 'error',
      'no-unreachable-loop': 'error',
      'no-unsafe-negation': 'error',
      'no-self-assign': 'error',
      'no-self-compare': 'error',
      'no-sequences': 'error',
      'no-implicit-coercion': ['error', { boolean: false }],
      'no-new-wrappers': 'error',
      'no-promise-executor-return': 'error',
      'array-callback-return': 'error',
      'no-loss-of-precision': 'error',
      'no-compare-neg-zero': 'error',
      '@typescript-eslint/require-array-sort-compare': 'error',
      '@typescript-eslint/switch-exhaustiveness-check': 'error',
      '@typescript-eslint/no-unsafe-type-assertion': 'error',
      '@typescript-eslint/ban-ts-comment': [
        'error',
        { 'ts-expect-error': true, 'ts-ignore': true, 'ts-nocheck': true },
      ],
      'no-template-curly-in-string': 'error',
      eqeqeq: ['error', 'always'],
      '@typescript-eslint/explicit-member-accessibility': ['error', { accessibility: 'explicit' }],
      '@typescript-eslint/explicit-function-return-type': [
        'error',
        { allowExpressions: false, allowTypedFunctionExpressions: false },
      ],
      '@typescript-eslint/typedef': [
        'error',
        {
          parameter: true,
          arrowParameter: true,
          memberVariableDeclaration: true,
          variableDeclaration: true,
          arrayDestructuring: true,
          objectDestructuring: true,
        },
      ],
      '@typescript-eslint/no-extraneous-class': ['error', { allowStaticOnly: true }],
      '@typescript-eslint/no-invalid-void-type': ['error', { allowAsThisParameter: true }],
      'no-restricted-globals': ['error', ...CodebaseConfiguration.restrictedGlobals],
      '@typescript-eslint/strict-boolean-expressions': [
        'error',
        {
          allowAny: false,
          allowString: false,
          allowNumber: false,
          allowNullableObject: false,
          allowNullableBoolean: false,
          allowNullableString: false,
          allowNullableNumber: false,
        },
      ],
      '@typescript-eslint/no-unnecessary-condition': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
      'unicorn/no-useless-collection-argument': 'error',
      'unicorn/no-unnecessary-string-trim': 'error',
      'unicorn/prefer-string-slice': 'error',
      'unicorn/prefer-code-point': 'error',
      'unicorn/no-hex-escape': 'error',
      'unicorn/escape-case': 'error',
      'unicorn/throw-new-error': 'error',
      'unicorn/no-instanceof-array': 'error',
      'no-restricted-syntax': [
        'error',
        {
          selector: 'TSAsExpression',
          message: '禁止 as 断言；用显式类型、schema 收窄或受检工厂表达。',
        },
        {
          selector: 'TSTypePredicate',
          message: '禁止类型谓词 is；类型判断使用 instanceof 或结构化解析。',
        },
        {
          selector: 'Identifier[optional=true]',
          message: '禁止可选形参；缺省语义经 Option 或显式重载表达。',
        },
        {
          selector: 'TSPropertySignature[optional=true], TSMethodSignature[optional=true]',
          message: '禁止可选成员；可空语义经 Option 字段表达。',
        },
        {
          selector: 'TSUndefinedKeyword',
          message: '类型注解禁止 undefined；可空语义经 Option 包装表达。',
        },
        {
          selector:
            'PropertyDefinition[accessibility!="private"][readonly!=true], TSParameterProperty[accessibility!="private"][readonly!=true]',
          message: '可变状态必须私有；对外字段必须只读。',
        },
        {
          selector:
            ':matches(FunctionExpression, ArrowFunctionExpression, TSMethodSignature, TSFunctionType) > Identifier.params > TSTypeAnnotation > TSArrayType',
          message: '数组输入必须使用 readonly 数组。',
        },
        {
          selector:
            ':matches(FunctionExpression, ArrowFunctionExpression, TSMethodSignature, TSFunctionType) > Identifier.params > TSTypeAnnotation > TSTypeReference[typeName.name=/^(Array|Map|Set)$/]',
          message: '集合输入必须使用 ReadonlyArray、ReadonlyMap 或 ReadonlySet。',
        },
        {
          selector:
            'Program > :not(ImportDeclaration):not(ClassDeclaration):not(TSInterfaceDeclaration):not(TSEnumDeclaration):not(TSTypeAliasDeclaration):not(ExportNamedDeclaration):not(TSDeclareFunction[id.name="acquireVsCodeApi"]):not(VariableDeclaration:has(VariableDeclarator[id.name="piExtension"] > Identifier > TSTypeAnnotation > TSTypeReference[typeName.name="ExtensionFactory"])):not(ExportDefaultDeclaration[declaration.name="piExtension"])',
          message: '顶层只允许 import、class、interface、enum、type 别名。',
        },
        {
          selector:
            'ExportNamedDeclaration:not([declaration.type="ClassDeclaration"]):not([declaration.type="TSInterfaceDeclaration"]):not([declaration.type="TSEnumDeclaration"]):not([declaration.type="TSTypeAliasDeclaration"]):not(:has(VariableDeclarator[id.name="activate"])):not(:has(VariableDeclarator[id.name="deactivate"]))',
          message: '直接导出 class、interface、enum 或 type 别名。',
        },
        { selector: 'FunctionDeclaration', message: '函数行为放入类方法。' },
      ],
    }
    return [
      {
        linterOptions: {
          reportUnusedDisableDirectives: 'error',
          reportUnusedInlineConfigs: 'error',
        },
      },
      ...tseslint.configs.strictTypeChecked,
      {
        languageOptions: {
          parserOptions: {
            project: 'tools/tsconfig.json',
            tsconfigRootDir: repositoryFullPath,
          },
        },
      },
      {
        files: ['**/*.{ts,tsx}', 'knip.config.ts'],
        plugins: { unicorn, sonarjs, 'eslint-comments': eslintComments },
        rules,
      },
      CodebaseConfiguration.production(implementationRelativePaths),
      {
        files: ['*.config.ts'],
        rules: {
          'no-restricted-syntax': 'off',
        },
      },
      {
        files: ['src/preview/storybook/*.{ts,tsx}'],
        rules: { 'no-restricted-syntax': 'off' },
      },
    ]
  }

  /**
   * @anchor mature-rule-composition
   * @anchor fallback-diagnostics
   */
  private static production(implementationRelativePaths: readonly string[]): Linter.Config {
    return {
      files: [...implementationRelativePaths],
      rules: {
        'no-restricted-globals': [
          'error',
          ...CodebaseConfiguration.restrictedGlobals,
          { name: 'Reflect', message: '禁止反射绕过静态成员约束。' },
        ],
        'no-empty': ['error', { allowEmptyCatch: false }],
        'no-useless-catch': 'error',
        'preserve-caught-error': ['error', { requireCatchParameter: true }],
        'unicorn/no-useless-fallback-in-spread': 'error',
        'sonarjs/no-ignored-exceptions': 'error',
        'sonarjs/no-dead-store': 'error',
        'sonarjs/no-duplicated-branches': 'error',
        'sonarjs/no-all-duplicated-branches': 'error',
        'sonarjs/no-identical-functions': 'error',
        'no-restricted-properties': [
          'error',
          ...[
            'trim',
            'trimStart',
            'trimEnd',
            'toUpperCase',
            'toLowerCase',
            'toLocaleUpperCase',
            'toLocaleLowerCase',
          ].map((property: string) => ({
            property,
            message: '格式转换须有明确需求；必要调用用逐行 ESLint 豁免说明理由。',
          })),
        ],
      },
    }
  }
}
