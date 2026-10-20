import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import { resolve } from 'node:path'
import { Option } from 'effect'
import { test } from 'vitest'
import { z } from 'zod'

interface LanguageContribution {
  readonly id: string
  readonly extensions: Option.Option<readonly string[]>
  readonly filenames: Option.Option<readonly string[]>
}

interface GrammarContribution {
  readonly language: string
  readonly scopeName: string
}

interface SemanticTokenType {
  readonly id: string
}

interface SemanticTokenScope {
  readonly language: string
  readonly scopes: Readonly<Record<string, readonly string[]>>
}

interface ExtensionManifest {
  readonly contributes: {
    readonly languages: readonly LanguageContribution[]
    readonly grammars: readonly GrammarContribution[]
    readonly semanticTokenTypes: readonly SemanticTokenType[]
    readonly semanticTokenScopes: readonly SemanticTokenScope[]
    readonly configurationDefaults: {
      readonly 'files.associations': Readonly<Record<string, string>>
    }
  }
}

interface TextMateGrammar {
  readonly scopeName: string
  readonly patterns: readonly unknown[]
  readonly repository: Readonly<Record<string, unknown>>
}

/** @anchor frontend-lsp-syntax-hjson-convergence */
export class LanguageAssetsTest {
  private constructor() {}

  private static readonly languageSchema: z.ZodType<LanguageContribution> = z.object({
    id: z.string(),
    extensions: z.array(z.string()).optional().transform(Option.fromNullishOr),
    filenames: z.array(z.string()).optional().transform(Option.fromNullishOr),
  })

  private static readonly manifestSchema: z.ZodType<ExtensionManifest> = z.object({
    contributes: z.object({
      languages: z.array(LanguageAssetsTest.languageSchema),
      grammars: z.array(z.object({ language: z.string(), scopeName: z.string() })),
      semanticTokenTypes: z.array(z.object({ id: z.string() })),
      semanticTokenScopes: z.array(
        z.object({
          language: z.string(),
          scopes: z.record(z.string(), z.array(z.string())),
        }),
      ),
      configurationDefaults: z.object({
        'files.associations': z.record(z.string(), z.string()),
      }),
    }),
  })

  private static readonly grammarSchema: z.ZodType<TextMateGrammar> = z.object({
    scopeName: z.string(),
    patterns: z.array(z.unknown()),
    repository: z.record(z.string(), z.unknown()),
  })

  static {
    test('扩展清单登记语言资源并保留 LSP 与 BEScript 未接入边界', async (): Promise<void> => {
      const manifest: ExtensionManifest = LanguageAssetsTest.manifestSchema.parse(
        JSON.parse(await readFile(resolve('src/frontend/package.json'), 'utf8')),
      )
      const bescript: Option.Option<LanguageContribution> = Option.fromNullishOr(
        manifest.contributes.languages.find(
          (language: LanguageContribution): boolean => language.id === 'bescript',
        ),
      )
      const hjson: Option.Option<LanguageContribution> = Option.fromNullishOr(
        manifest.contributes.languages.find(
          (language: LanguageContribution): boolean => language.id === 'mech-hjson',
        ),
      )
      assert.deepEqual(
        Option.map(
          bescript,
          (language: LanguageContribution): Option.Option<readonly string[]> => language.extensions,
        ),
        Option.some(Option.some(['.code'])),
      )
      assert.deepEqual(
        Option.map(
          hjson,
          (language: LanguageContribution): Option.Option<readonly string[]> => language.filenames,
        ),
        Option.some(
          Option.some(['device.json', 'list.json', 'mech.json', 'map.json', 'setting.json']),
        ),
      )
      assert.deepEqual(manifest.contributes.configurationDefaults['files.associations'], {
        '**/机械/**/*.json': 'mech-hjson',
        '**/*.mech_/**/*.json': 'mech-hjson',
        '**/*.map_/**/*.json': 'mech-hjson',
      })
      assert.deepEqual(
        manifest.contributes.grammars.map((grammar: GrammarContribution): GrammarContribution => ({
          language: grammar.language,
          scopeName: grammar.scopeName,
        })),
        [
          { language: 'bescript', scopeName: 'source.bescript' },
          { language: 'mech-hjson', scopeName: 'source.mech-hjson' },
        ],
      )
      const bescriptGrammar: TextMateGrammar = LanguageAssetsTest.grammarSchema.parse(
        JSON.parse(
          await readFile(resolve('res/frontend/syntaxes/bescript.tmLanguage.json'), 'utf8'),
        ),
      )
      const hjsonGrammar: TextMateGrammar = LanguageAssetsTest.grammarSchema.parse(
        JSON.parse(
          await readFile(resolve('res/frontend/syntaxes/mech-hjson.tmLanguage.json'), 'utf8'),
        ),
      )
      assert.equal(bescriptGrammar.scopeName, 'source.bescript')
      assert.deepEqual(bescriptGrammar.patterns, [])
      assert.deepEqual(bescriptGrammar.repository, {})
      assert.equal(hjsonGrammar.scopeName, 'source.mech-hjson')
      assert.ok(hjsonGrammar.patterns.length > 0)
      assert.ok(Object.keys(hjsonGrammar.repository).length > 0)
      assert.ok(
        manifest.contributes.semanticTokenTypes.some(
          (entry: SemanticTokenType): boolean => entry.id === 'function',
        ),
      )
      const bescriptScopes: Option.Option<SemanticTokenScope> = Option.fromNullishOr(
        manifest.contributes.semanticTokenScopes.find(
          (entry: SemanticTokenScope): boolean => entry.language === 'bescript',
        ),
      )
      assert.equal(
        Option.getOrThrow(
          Option.flatMap(
            bescriptScopes,
            (entry: SemanticTokenScope): Option.Option<readonly string[]> =>
              Option.fromNullishOr(entry.scopes['function']),
          ),
        )[0],
        'entity.name.function',
      )
    })
  }
}
