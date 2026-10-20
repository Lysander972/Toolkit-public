import assert from 'node:assert/strict'
import { test, vi } from 'vitest'
import type { MockInstance } from 'vitest'
import { Context, Effect, Layer, Option, Result, Scope } from 'effect'
import ts from 'typescript'
import { LogicalDevice } from '../../../src/toolkit/project_model/LogicalEntities.ts'
import { DeviceEntity } from '../../../src/toolkit/entities/DeviceEntity.ts'
import type { FormatEntity } from '../../../src/toolkit/entities/FormatEntity.ts'
import { TsViewEntity } from '../../../src/toolkit/entities/ToolkitEntity.ts'
import {
  ProjectEntitiesService,
  type ProjectEntities,
} from '../../../src/toolkit/entities/ProjectEntities.ts'
import { HtmlViewConversionService } from '../../../src/toolkit/conversion/html/HtmlViewConversion.ts'
import {
  TsViewConversionService,
  type TsViewConversion,
} from '../../../src/toolkit/conversion/ts/TsViewConversion.ts'
import {
  CanvasConversionService,
  type CanvasConversion,
} from '../../../src/toolkit/conversion/canvas/CanvasConversion.ts'
import {
  ProjectViewsService,
  UpdateTsView,
  RemoveTsView,
  type ProjectViews,
} from '../../../src/toolkit/views/ProjectViews.ts'
import {
  DeviceTsConverter,
  type DeviceConversionBasis,
} from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import type {
  BescriptDeviceContext,
  DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'
import type { ScriptGroup } from '../../../src/toolkit/script/device_context/ScriptList.ts'
import type {
  LocalAst,
  LocalAstNode,
  CallArgument,
  CallSignature,
} from '../../../src/toolkit/script/local_ast/LocalAst.ts'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasTestWorkspace } from '../toolkit-file-support/CanvasTestWorkspace.ts'
import { TsViewFixture } from '../toolkit-script-fixtures/TsViewFixture.ts'
import type { TsPrintState } from '../../../src/toolkit/script/ts_text/TsViewText.ts'

/**
 * @anchor ts-service-contract
 * @anchor ts-view-index-dts-convergence
 * @anchor ts-view-module-style-convergence
 */
export class TsConversionServiceTest {
  private constructor() {}

  /** @anchor ts-service-contract */
  private static device(
    context: BescriptDeviceContext,
    basis: DeviceConversionBasis,
  ): DeviceEntity {
    const files: readonly { readonly relativePath: string; readonly revision: string }[] =
      basis.scripts.map(
        (
          script: DeviceConversionBasis['scripts'][number],
        ): { readonly relativePath: string; readonly revision: string } => ({
          relativePath: script.scriptRelativePath,
          revision: 'baseline',
        }),
      )
    const declaration: LogicalDevice = new LogicalDevice({
      primaryRelativePath: basis.identity.deviceRelativePath,
      parentId: 'mech:机械/测试.mech',
      files,
      directories: [{ relativePath: basis.identity.deviceRelativePath, entries: ['scripts'] }],
      deviceId: 0,
      deviceType: context.metadata.deviceType,
      fields: context.metadata,
      variables: Option.none(),
    })
    const scripts: readonly DeviceFunction[] = context.scriptGroups.flatMap(
      (group: ScriptGroup<DeviceFunction>): readonly DeviceFunction[] => group.entries,
    )
    return new DeviceEntity(
      declaration,
      context,
      scripts.map((script: DeviceFunction, index: number): DeviceEntity['originals'][number] => ({
        fileRelativePath: Option.getOrThrow(Option.fromNullishOr(files[index])).relativePath,
        bytes: new TextEncoder().encode(BescriptConverter.serialize(script.ast)),
      })),
      basis,
      {
        files: [...files, { relativePath: '公共.code', revision: 'repository' }],
        directories: declaration.directories,
        fixedInputs: [{ name: 'platform', revision: 'platform-v1' }],
      },
    )
  }

  static {
    test('零件实体双向转换交付真实编辑并保留同一读取基准', async (): Promise<void> => {
      let released: Option.Option<TsViewConversion> = Option.none()
      let device: Option.Option<DeviceEntity> = Option.none()
      let printed: Option.Option<TsViewEntity> = Option.none()
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const canvas: CanvasConversion = fixture.service
            const built: Context.Context<TsViewConversionService> = yield* Layer.build(
              TsViewConversionService.layer(Layer.succeed(CanvasConversionService, canvas)),
            )
            const service: TsViewConversion = Context.get(built, TsViewConversionService)
            const baseline: DeviceEntity = TsConversionServiceTest.device(
              TsViewFixture.context('原值'),
              TsViewFixture.basis(),
            )
            released = Option.some(service)
            device = Option.some(baseline)
            const before: string = JSON.stringify(baseline)
            const view: TsViewEntity = yield* service.toTs(baseline, 'views/零件.ts')
            const entity: FormatEntity = view
            printed = Option.some(view)
            assert.equal(entity.id, 'views/零件.ts')
            assert.equal(entity.sourceRelativePath, 'views/零件.ts')
            assert.deepEqual(entity.baseline, baseline.baseline)
            assert.equal(view.sourceEntityId, baseline.id)
            assert.deepEqual(view.baseline, baseline.baseline)
            assert.equal(view.syntax.statements.length, 1)
            const namespace: ts.Statement = Option.getOrThrow(
              Option.fromNullishOr(view.syntax.statements[0]),
            )
            assert.ok(ts.isModuleDeclaration(namespace))
            const namespaceBody: NonNullable<ts.ModuleDeclaration['body']> = Option.getOrThrow(
              Option.fromNullishOr(namespace.body),
            )
            assert.ok(ts.isModuleBlock(namespaceBody))
            assert.ok(namespaceBody.statements.every(ts.isClassDeclaration))
            assert.equal(
              view.text,
              ts
                .createPrinter()
                .printFile(DeviceTsConverter.toTs(baseline.context, baseline.conversionBasis)),
            )
            assert.deepEqual(entity.encode(), [
              { fileRelativePath: 'views/零件.ts', bytes: new TextEncoder().encode(view.text) },
            ])
            const printState: TsPrintState = yield* service.print(
              baseline.context,
              baseline.conversionBasis,
            )
            assert.equal(printState.positions.length, baseline.conversionBasis.methods.length)
            assert.equal(entity.encode().length, 1)
            assert.deepEqual((yield* service.fromTs(view, baseline)).context, baseline.context)
            const transformed: ts.TransformationResult<ts.SourceFile> = ts.transform(view.syntax, [
              (context: ts.TransformationContext): ts.Transformer<ts.SourceFile> => {
                const visit: ts.Visitor = (node: ts.Node): ts.VisitResult<ts.Node> =>
                  ts.isStringLiteral(node) && node.text === '原值'
                    ? ts.factory.createStringLiteral('实际修改')
                    : ts.visitEachChild(node, visit, context)
                return (sourceFile: ts.SourceFile): ts.SourceFile =>
                  Option.getOrThrow(
                    Option.fromNullishOr(ts.visitNode(sourceFile, visit, ts.isSourceFile)),
                  )
              },
            ])
            try {
              const editedText: string = ts
                .createPrinter()
                .printFile(Option.getOrThrow(Option.fromNullishOr(transformed.transformed[0])))
              const edited: TsViewEntity = new TsViewEntity(
                view.sourceEntityId,
                view.viewRelativePath,
                view.baseline,
                editedText,
                view.syntax,
              )
              const restored: DeviceEntity = yield* service.fromTs(edited, baseline)
              assert.deepEqual(
                {
                  ...restored.context,
                  scriptGroups: restored.context.scriptGroups.map(
                    (group: ScriptGroup<DeviceFunction>): ScriptGroup<DeviceFunction> => ({
                      ...group,
                      entries: group.entries.map((entry: DeviceFunction): DeviceFunction => ({
                        ...entry,
                        ast: BescriptConverter.parse(BescriptConverter.serialize(entry.ast)),
                      })),
                    }),
                  ),
                },
                TsViewFixture.context('实际修改'),
              )
              assert.equal(JSON.stringify(baseline), before)
              assert.equal(restored.sourceRelativePath, baseline.sourceRelativePath)
              assert.equal(restored.baseline, baseline.baseline)
              assert.deepEqual(restored.originals, baseline.originals)
              assert.equal(
                BescriptConverter.serialize(
                  Option.getOrThrow(
                    Option.fromNullishOr(
                      Option.getOrThrow(Option.fromNullishOr(restored.context.scriptGroups[0]))
                        .entries[0],
                    ),
                  ).ast,
                ),
                '\nbegincodeV2\nname:first\nlines:\n12:fun echo(String 实际修改)\n/*if{fun predicate()\n  fun echo(String 启用)\n/*}\n// 注释  \nendcode\n\n',
              )
            } finally {
              transformed.dispose()
            }
            const foreign: TsViewEntity = new TsViewEntity(
              'device:其他',
              view.viewRelativePath,
              view.baseline,
              view.text,
              view.syntax,
            )
            assert.equal(
              Result.isFailure(yield* Effect.result(service.fromTs(foreign, baseline))),
              true,
            )
            const stale: TsViewEntity = new TsViewEntity(
              view.sourceEntityId,
              view.viewRelativePath,
              { files: [], directories: [], fixedInputs: [] },
              view.text,
              view.syntax,
            )
            assert.equal(
              Result.isFailure(yield* Effect.result(service.fromTs(stale, baseline))),
              true,
            )
          }),
        ),
      )
      await assert.rejects(
        Effect.runPromise(
          Option.getOrThrow(released).toTs(Option.getOrThrow(device), 'views/零件.ts'),
        ),
        /服务已关闭/u,
      )
      await assert.rejects(
        Effect.runPromise(
          Option.getOrThrow(released).fromTs(Option.getOrThrow(printed), Option.getOrThrow(device)),
        ),
        /服务已关闭/u,
      )
    })

    test('两种 Canvas 内联及禁用语句经共享服务往返，HTML 编辑改变真实 Canvas', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const canvas: CanvasConversion = fixture.service
            const built: Context.Context<TsViewConversionService> = yield* Layer.build(
              TsViewConversionService.layer(Layer.succeed(CanvasConversionService, canvas)),
            )
            const service: TsViewConversion = Context.get(built, TsViewConversionService)
            const html: string =
              '<BeCanvas name="界面" width="1080" height="2160"><BeUILabel name="文本" text="旧值"></BeUILabel></BeCanvas>'
            const changedHtml: string =
              '<BeCanvas name="界面" width="1080" height="2160"><BeUILabel name="文本" text="新值"></BeUILabel></BeCanvas>'
            const document: ReturnType<typeof CanvasConverter.fromHtml> =
              CanvasConverter.fromHtml(html)
            const basis: DeviceConversionBasis = {
              ...TsViewFixture.basis(),
              types: [
                ...TsViewFixture.basis().types,
                { name: 'Canvas', tsName: 'BeCanvas', bases: [] },
              ],
              methods: [
                ...TsViewFixture.basis().methods,
                {
                  kind: 'fun',
                  ownerType: Option.none(),
                  besName: 'canvas',
                  tsName: 'canvas',
                  receiver: 'G',
                  typeArguments: [],
                  parameters: [
                    {
                      typeName: 'Canvas',
                      valueType: 'Canvas',
                      constant: 'canvas',
                      enumName: Option.none(),
                    },
                  ],
                  argumentOrder: [0],
                  returnType: Option.none(),
                  asynchronous: false,
                },
              ],
            }
            for (const inline of [
              CanvasConverter.toInlineBase64(document),
              CanvasConverter.toInlineHjson(document),
            ]) {
              for (const disabled of [false, true]) {
                const parsed: LocalAst = BescriptConverter.parse(
                  'begincodeV2\nname:first\nlines:\n' +
                    (disabled ? '/*' : '') +
                    'fun canvas(Canvas )\nendcode\n',
                )
                const ast: LocalAst = {
                  ...parsed,
                  nodes: parsed.nodes.map((node: LocalAstNode): LocalAstNode => ({
                    ...node,
                    call: Option.map(node.call, (call: CallSignature): CallSignature => ({
                      ...call,
                      arguments: call.arguments.map((argument: CallArgument): CallArgument => ({
                        ...argument,
                        decodedText: inline,
                      })),
                    })),
                  })),
                }
                const original: BescriptDeviceContext = TsViewFixture.context('原值')
                const context: BescriptDeviceContext = {
                  ...original,
                  scriptGroups: original.scriptGroups.map(
                    (group: ScriptGroup<DeviceFunction>): ScriptGroup<DeviceFunction> => ({
                      ...group,
                      entries: group.entries.map((entry: DeviceFunction): DeviceFunction =>
                        entry.binding.name === 'first'
                          ? {
                              ...entry,
                              ast: BescriptConverter.parse(BescriptConverter.serialize(ast)),
                            }
                          : entry,
                      ),
                    }),
                  ),
                }
                const baseline: DeviceEntity = TsConversionServiceTest.device(context, basis)
                const view: TsViewEntity = yield* service.toTs(baseline, 'views/零件.ts')
                assert.equal(
                  view.text,
                  ts.createPrinter().printFile(DeviceTsConverter.toTs(context, basis)),
                )
                const expected: Error = new Error('共享 Canvas 转换失败')
                const invocation: MockInstance<CanvasConversion['base64ToHtml']> = vi.spyOn(
                  canvas,
                  inline.startsWith('canvas:') ? 'base64ToHtml' : 'hjsonToHtml',
                )
                try {
                  invocation.mockReturnValueOnce(Effect.fail(expected))
                  const failed: Result.Result<TsViewEntity, unknown> = yield* Effect.result(
                    service.toTs(baseline, 'views/零件.ts'),
                  )
                  assert.ok(Result.isFailure(failed))
                  assert.equal(failed.failure, expected)
                } finally {
                  invocation.mockRestore()
                }
                assert.equal((yield* service.print(context, basis)).printedFragments, 0)
                assert.deepEqual((yield* service.fromTs(view, baseline)).context, context)
                if (disabled) continue
                const transformed: ts.TransformationResult<ts.SourceFile> = ts.transform(
                  view.syntax,
                  [
                    (context: ts.TransformationContext): ts.Transformer<ts.SourceFile> => {
                      const visit: ts.Visitor = (node: ts.Node): ts.VisitResult<ts.Node> =>
                        ts.isNoSubstitutionTemplateLiteral(node)
                          ? ts.factory.createNoSubstitutionTemplateLiteral(changedHtml)
                          : ts.visitEachChild(node, visit, context)
                      return (sourceFile: ts.SourceFile): ts.SourceFile =>
                        Option.getOrThrow(
                          Option.fromNullishOr(ts.visitNode(sourceFile, visit, ts.isSourceFile)),
                        )
                    },
                  ],
                )
                try {
                  const text: string = ts
                    .createPrinter()
                    .printFile(Option.getOrThrow(Option.fromNullishOr(transformed.transformed[0])))
                  const restored: DeviceEntity = yield* service.fromTs(
                    new TsViewEntity(
                      view.sourceEntityId,
                      view.viewRelativePath,
                      view.baseline,
                      text,
                      view.syntax,
                    ),
                    baseline,
                  )
                  const script: DeviceFunction = Option.getOrThrow(
                    Option.fromNullishOr(
                      Option.getOrThrow(Option.fromNullishOr(restored.context.scriptGroups[0]))
                        .entries[0],
                    ),
                  )
                  const argument: CallArgument = Option.getOrThrow(
                    Option.fromNullishOr(
                      Option.getOrThrow(
                        Option.getOrThrow(Option.fromNullishOr(script.ast.nodes[0])).call,
                      ).arguments[0],
                    ),
                  )
                  assert.equal(
                    argument.decodedText.startsWith('canvas:'),
                    inline.startsWith('canvas:'),
                  )
                  assert.deepEqual(
                    CanvasConverter.toBinary(CanvasConverter.fromInline(argument.decodedText)),
                    CanvasConverter.toBinary(CanvasConverter.fromHtml(changedHtml)),
                  )
                  assert.deepEqual(baseline.context, context)
                } finally {
                  transformed.dispose()
                }
              }
            }
          }),
        ),
      )
    })

    test('视图框架统一处理更新与删除并共享同一转换缓存', async (): Promise<void> => {
      await Effect.runPromise(
        Effect.scoped(
          Effect.gen(function* (): Generator<
            Effect.Effect<unknown, unknown, Scope.Scope>,
            void,
            never
          > {
            const fixture: CanvasTestWorkspace = yield* CanvasTestWorkspace.make()
            const canvas: CanvasConversion = fixture.service
            const entities: ProjectEntities = fixture.entities
            const layer: Layer.Layer<TsViewConversionService, unknown> =
              TsViewConversionService.layer(Layer.succeed(CanvasConversionService, canvas))
            const built: Context.Context<TsViewConversionService | ProjectViewsService> =
              yield* Layer.build(
                Layer.merge(
                  layer,
                  ProjectViewsService.layer(
                    'memory',
                    Layer.succeed(ProjectEntitiesService, entities),
                    layer,
                    HtmlViewConversionService.layer(Layer.succeed(CanvasConversionService, canvas)),
                  ),
                ),
              )
            const service: TsViewConversion = Context.get(built, TsViewConversionService)
            const views: ProjectViews = Context.get(built, ProjectViewsService)
            const baseline: DeviceEntity = TsConversionServiceTest.device(
              TsViewFixture.context('原值'),
              TsViewFixture.basis(),
            )
            const view: TsViewEntity = Option.getOrThrow(
              yield* views.refreshTs(new UpdateTsView(baseline, 'views/零件.ts')),
            )
            assert.equal(
              view.text,
              ts
                .createPrinter()
                .printFile(DeviceTsConverter.toTs(baseline.context, baseline.conversionBasis)),
            )
            assert.equal(
              (yield* service.print(baseline.context, baseline.conversionBasis)).printedFragments,
              0,
            )
            assert.equal(
              Option.isNone(yield* views.refreshTs(new RemoveTsView(baseline.sourceRelativePath))),
              true,
            )
            assert.equal(
              (yield* service.print(baseline.context, baseline.conversionBasis)).printedFragments,
              2,
            )
          }),
        ),
      )
    })
  }
}
