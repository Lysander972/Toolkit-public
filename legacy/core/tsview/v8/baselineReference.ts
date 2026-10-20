// ============================================================
// Device 视图全量 AST 参考实现（仅测试）。
// 不进生产 bundle：tsview/v8/tsconfig.json 的 files 列表不含本文件。
// 由 tsview/v8/tsconfig.test.json 连同生产源码一起编译进测试 superset bundle，
// 供 TsViewIncrementalEquivalenceTests 钉死“增量转译 vs 直接全 AST 官方序列化”的字节级等价性。
// 复用生产 scanClassMembers.ts 的全局 helper（createDeviceViewStatements/printFileWithTwoSpaceIndent 等），
// 故必须与生产源码同编，不能独立编译。
// ============================================================

// 全量基线入口（仅测试用）：不经增量序列化，直接全 AST 官方 printer 打印。
function __buildDeviceViewBaseline(inputJson: string): string {
  try {
    const input = JSON.parse(inputJson) as DeviceViewInput
    return JSON.stringify(buildDeviceViewBaseline(input))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({ tsView: '', error: msg } satisfies DeviceViewOutput)
  }
}

// 全量整文件打印参考路径：一次性把全部成员建成 AST 再整体打印。
function buildDeviceViewBaseline(input: DeviceViewInput): DeviceViewOutput {
  currentDeviceViewBuildContext = 'createSourceFile'
  const sourceFile = ts.factory.createSourceFile(
    createDeviceViewStatements(input, createDeviceClassMembers(input)),
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  currentDeviceViewBuildContext = `printFile after ${currentDeviceViewBuildContext}`
  const tsView = printFileWithTwoSpaceIndent(sourceFile)
  currentDeviceViewBuildContext = ''
  return { tsView }
}

// 全量成员装配（直接建每个成员的真实 AST，不经片段缓存/骨架）。
function createDeviceClassMembers(input: DeviceViewInput): ts.ClassElement[] {
  const members: ts.ClassElement[] = [
    createDeviceNotEmittedClassElement(),
    createDeviceSingleLineComment(' [Globals] BEGIN') as unknown as ts.ClassElement,
  ]

  for (const globalVar of input.globalVars) {
    members.push(createDeviceGlobalVarProperty(globalVar))
  }

  members.push(createDeviceSingleLineComment(' [Globals] END') as unknown as ts.ClassElement)
  members.push(createDeviceNotEmittedClassElement())
  members.push(createDeviceSingleLineComment(' [Script] BEGIN') as unknown as ts.ClassElement)

  for (const script of input.scriptMethodInfos) {
    members.push(createDeviceScriptProperty(input, script, false, []))
  }

  members.push(createDeviceSingleLineComment(' [Script] END') as unknown as ts.ClassElement)

  if (input.eventMethodInfos.length > 0) {
    members.push(createDeviceNotEmittedClassElement())
    members.push(createDeviceSingleLineComment(' [Events] BEGIN') as unknown as ts.ClassElement)
    members.push(createDeviceEventRegistrationBlock(input))
    members.push(createDeviceSingleLineComment(' [Events] END') as unknown as ts.ClassElement)
  }

  return members
}

function createDeviceEventRegistrationBlock(input: DeviceViewInput): ts.ClassElement {
  const statements: ts.Statement[] = []
  for (let i = 0; i < input.eventMethodInfos.length; i++) {
    const eventInfo = input.eventMethodInfos[i]
    if (i > 0) statements.push(createBlankLinePlaceholder())
    const members = eventInfo.scripts.map((script) => {
      let property = f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        script.methodName,
        f<ts.Expression>('createCallExpression', createDeviceBeScriptCall(script), undefined, [
          createDeviceScriptFunctionExpression(input, script, true, eventInfo.eventParams),
        ]),
      )
      property = ts.addSyntheticLeadingComment(
        property,
        ts.SyntaxKind.MultiLineCommentTrivia,
        buildJsDocText(createDeviceScriptDocLines(script)),
        true,
      )
      return property
    })
    statements.push(createDeviceJsDocStatement([`event: ${eventInfo.eventName}`, eventInfo.summary]))
    statements.push(
      f<ts.Statement>(
        'createExpressionStatement',
        f<ts.Expression>(
          'createCallExpression',
          f<ts.Expression>(
            'createPropertyAccessExpression',
            f<ts.Expression>('createThis'),
            'registerEvent_' + eventInfo.registrationEventName,
          ),
          undefined,
          [f<ts.Expression>('createThis'), f<ts.Expression>('createObjectLiteralExpression', members, true)],
        ),
      ),
    )
  }
  return f<ts.ClassElement>('createClassStaticBlockDeclaration', ts.factory.createBlock(statements, true))
}
