// 类成员扫描（替代 TsViewClassScanner）
// 全局 ts 命名空间由 typescript.d.ts 提供类型，运行时由 typescript.js 注入

// ============================================================
// 入口：ClearScript 从 C# 调用
// ============================================================

function __scanClassMembers(sourceText: string, className: string, entryClassName: string | null): string {
  try {
    const text = typeof sourceText === 'string' ? sourceText : '' + sourceText
    return JSON.stringify(scanClassMembersImpl(text, className, entryClassName))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({ error: msg } satisfies ScanError)
  }
}

// ============================================================
// 类成员扫描（替代 TsViewClassScanner）
// ============================================================

function scanClassMembersImpl(
  sourceText: string,
  className: string,
  entryClassName: string | null,
): ScanResult | ScanError {
  const sf = ts.createSourceFile('input.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)

  const targetClass = findClassByName(sf, className)
  if (!targetClass) {
    return { error: `未找到目标类: ${className}` }
  }

  if (entryClassName === null && !hasModifier(targetClass, ts.SyntaxKind.ExportKeyword)) {
    return {
      error: '未找到 export class 声明',
      errorLine: lineOf(sf, targetClass) + 1,
    }
  }

  if (entryClassName !== null) {
    const entryError = validateBirthBuildingEntryClass(sf, entryClassName)
    if (entryError) return entryError
  }

  if (!hasExtendsClause(targetClass)) {
    return {
      error: '类定义不合规：必须有显式的继承关系。',
      errorLine: lineOf(sf, targetClass) + 1,
    }
  }

  const sectionInfo = scanSectionMarkers(sf, targetClass)
  if ('error' in sectionInfo) return sectionInfo

  const globals: ScannedGlobalVarJson[] = []
  const methods: ScannedMethodJson[] = []
  const eventGroups: ScannedEventGroupJson[] = []
  const eventRegistrationBlocks: ts.ClassStaticBlockDeclaration[] = []

  for (const member of targetClass.members) {
    const memberLine = lineOf(sf, member)

    if (ts.isPropertyDeclaration(member)) {
      const r = scanProperty(sf, member, memberLine)
      if ('error' in r) return r
      if (r.kind === 'global') globals.push(r.data as ScannedGlobalVarJson)
      else if (r.kind === 'method') methods.push(r.data as ScannedMethodJson)
      continue
    }

    if (ts.isMethodDeclaration(member)) {
      const r = scanMethod(sf, member, memberLine)
      if ('error' in r) return r
      if (r.data) methods.push(r.data)
      continue
    }

    if (ts.isClassStaticBlockDeclaration(member)) {
      eventRegistrationBlocks.push(member)
      continue
    }
  }

  if (eventRegistrationBlocks.length > 1) {
    return {
      error: '事件区只允许一个静态注册块。',
      errorLine: lineOf(sf, eventRegistrationBlocks[1]) + 1,
    }
  }

  for (const block of eventRegistrationBlocks) {
    const r = scanEventRegistrationBlock(sf, block)
    if ('error' in r) return r
    for (const group of r.groups) {
      eventGroups.push(group)
    }
  }

  const eventDecorator = findDecoratorCall(targetClass, 'events')
  const eventDecoratorValues: string[] = []
  if (eventDecorator) {
    for (const arg of eventDecorator.arguments) {
      if (!ts.isStringLiteral(arg)) {
        return {
          error: '@events 参数必须是字符串字面量。',
          errorLine: lineOf(sf, arg) + 1,
        }
      }
      eventDecoratorValues.push(arg.text)
    }
  }

  return {
    globals,
    methods,
    eventGroups,
    hasEventDecorator: eventDecorator !== null,
    eventDecoratorValues,
    sectionInfo: sectionInfo.info,
  }
}

function __parseMethodBody(bodyText: string, contextJson: string): string {
  try {
    const context = JSON.parse(contextJson) as BodyParseContext
    return JSON.stringify(parseMethodBodyImpl(bodyText, context))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({ error: msg } satisfies BodyParseError)
  }
}

function __parseParameterList(parameterListText: string): string {
  const sourceText = `function __tsview_parameters__(${parameterListText}) {}`
  const sf = ts.createSourceFile('parameters.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length !== 0) {
    return JSON.stringify({
      parameters: [],
      error: flattenDiagnostic(parseDiagnostics[0].messageText),
    })
  }

  const fn = sf.statements.find(ts.isFunctionDeclaration)
  if (!fn) return JSON.stringify({ parameters: [], error: '参数列表解析失败。' })

  const parameters: { name: string; typeName: string }[] = []
  for (const parameter of fn.parameters) {
    if (!ts.isIdentifier(parameter.name)) {
      return JSON.stringify({
        parameters: [],
        error: `参数名不合法: ${parameter.name.getText(sf)}`,
      })
    }
    if (parameter.questionToken) {
      return JSON.stringify({
        parameters: [],
        error: '事件参数不允许可选标记 ?',
      })
    }
    if (!parameter.type) {
      return JSON.stringify({
        parameters: [],
        error: '参数声明必须为 name: BeType',
      })
    }
    parameters.push({
      name: parameter.name.text,
      typeName: parameter.type.getText(sf),
    })
  }
  return JSON.stringify({ parameters, error: null })
}

function __isValidIdentifier(identifier: string): boolean {
  if (typeof identifier !== 'string') return false
  const sf = ts.createSourceFile(
    'identifier.ts',
    `class __tsview_identifier__ { ${identifier}() {} }`,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS,
  )
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length !== 0 || sf.statements.length !== 1) return false

  const statement = sf.statements[0]
  if (!ts.isClassDeclaration(statement) || statement.members.length !== 1) return false
  return ts.isMethodDeclaration(statement.members[0]) && statement.members[0].name?.getText(sf) === identifier
}

function __parseBeEnumTypeArgument(typeText: string): string {
  const sourceText = `type __tsview_enum__ = ${typeText}`
  const sf = ts.createSourceFile('enum-type.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length !== 0) return JSON.stringify({ enumTypeName: null })

  const alias = sf.statements.find(ts.isTypeAliasDeclaration)
  const type = alias?.type
  if (
    !type ||
    !ts.isTypeReferenceNode(type) ||
    !ts.isIdentifier(type.typeName) ||
    type.typeName.text !== 'BeEnum' ||
    type.typeArguments?.length !== 1
  ) {
    return JSON.stringify({ enumTypeName: null })
  }

  const argument = type.typeArguments[0]
  const enumTypeName =
    ts.isTypeReferenceNode(argument) && ts.isIdentifier(argument.typeName) && !argument.typeArguments
      ? argument.typeName.text
      : null
  return JSON.stringify({ enumTypeName })
}

function __formatMethodBody(bodyText: string): string {
  try {
    return JSON.stringify(formatMethodBodyImpl(bodyText))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({
      text: '',
      error: msg,
    } satisfies MethodBodyFormatOutput)
  }
}

function __buildCodeRepoView(inputJson: string): string {
  try {
    const input = JSON.parse(inputJson) as CodeRepoViewInput
    return JSON.stringify(buildCodeRepoViewImpl(input))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({
      tsView: '',
      scripts: [],
      error: msg,
    } satisfies CodeRepoViewOutput)
  }
}

function __buildCustomLogicDts(inputJson: string): string {
  try {
    const input = JSON.parse(inputJson) as CustomLogicDtsInput
    return JSON.stringify(buildCustomLogicDtsImpl(input))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    return JSON.stringify({
      dtsText: '',
      error: msg,
    } satisfies CustomLogicDtsOutput)
  }
}

function __buildDeviceView(inputJson: string): string {
  try {
    const input = JSON.parse(inputJson) as DeviceViewInput
    return JSON.stringify(buildDeviceViewIncremental(input))
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : String(e)
    const context = currentDeviceViewBuildContext.length > 0 ? `${currentDeviceViewBuildContext}: ` : ''
    return JSON.stringify({
      tsView: '',
      error: context + msg,
    } satisfies DeviceViewOutput)
  }
}

// ============================================================
// AST 工具函数
// ============================================================

function lineOf(sf: ts.SourceFile, node: ts.Node): number {
  return sf.getLineAndCharacterOfPosition(node.getStart(sf)).line
}

function endLineOf(sf: ts.SourceFile, node: ts.Node): number {
  return sf.getLineAndCharacterOfPosition(node.getEnd()).line
}

function findClassByName(root: ts.Node, className: string): ts.ClassDeclaration | null {
  let result: ts.ClassDeclaration | null = null
  function visit(node: ts.Node): void {
    if (result) return
    if (ts.isClassDeclaration(node) && node.name?.text === className) {
      result = node
      return
    }
    ts.forEachChild(node, visit)
  }
  visit(root)
  return result
}

function validateBirthBuildingEntryClass(sf: ts.SourceFile, entryClassName: string): ScanError | null {
  const entryClass = findClassByName(sf, entryClassName)
  if (!entryClass || !hasModifier(entryClass, ts.SyntaxKind.ExportKeyword)) {
    return { error: '未找到外部调用入口类（export class）' }
  }

  for (const member of entryClass.members) {
    if (ts.isPropertyDeclaration(member) && ts.isIdentifier(member.name) && member.name.text === '__concreteBrand')
      continue
    return {
      error: '外部调用入口类必须为空，禁止添加任何内容',
      errorLine: lineOf(sf, member) + 1,
    }
  }

  return null
}

function hasExtendsClause(cls: ts.ClassDeclaration): boolean {
  return cls.heritageClauses?.some((h: ts.HeritageClause) => h.token === ts.SyntaxKind.ExtendsKeyword) ?? false
}

function getDecorators(node: ts.HasDecorators): readonly ts.Decorator[] {
  return ts.getDecorators(node) ?? []
}

function findDecoratorCall(node: ts.HasDecorators, name: string): ts.CallExpression | null {
  for (const d of getDecorators(node)) {
    if (
      ts.isCallExpression(d.expression) &&
      ts.isIdentifier(d.expression.expression) &&
      d.expression.expression.text === name
    ) {
      return d.expression
    }
  }
  return null
}

function hasModifier(node: ts.Node, kind: ts.SyntaxKind): boolean {
  const mods = ts.canHaveModifiers(node) ? ts.getModifiers(node) : undefined
  return mods?.some((m: ts.ModifierLike) => m.kind === kind) ?? false
}

function parseObjectLiteral(node: ts.Expression): Record<string, unknown> | null {
  if (!ts.isObjectLiteralExpression(node)) return null
  const result: Record<string, unknown> = {}
  for (const prop of node.properties) {
    if (ts.isPropertyAssignment(prop) && ts.isIdentifier(prop.name)) {
      result[prop.name.text] = parseLiteralValue(prop.initializer)
    }
  }
  return result
}

function parseLiteralValue(node: ts.Expression): unknown {
  if (ts.isStringLiteral(node)) return node.text
  if (ts.isNumericLiteral(node)) return Number(node.text)
  if (node.kind === ts.SyntaxKind.TrueKeyword) return true
  if (node.kind === ts.SyntaxKind.FalseKeyword) return false
  if (ts.isArrayLiteralExpression(node)) return node.elements.map(parseLiteralValue)
  if (ts.isObjectLiteralExpression(node)) return parseObjectLiteral(node)
  return null
}

function parseBeScriptInfo(callExpr: ts.CallExpression): BeScriptInfoJson | null {
  if (callExpr.arguments.length < 1) return null
  const arg = callExpr.arguments[0]
  const obj = parseObjectLiteral(arg)
  if (!obj || typeof obj.name !== 'string') return null

  const calleeName = ts.isIdentifier(callExpr.expression) ? callExpr.expression.text : ''

  // repo 字段是标识符引用（guid_XXXX），不是字符串字面量
  let repo: string | null = null
  if (ts.isObjectLiteralExpression(arg)) {
    for (const prop of arg.properties) {
      if (
        ts.isPropertyAssignment(prop) &&
        ts.isIdentifier(prop.name) &&
        prop.name.text === 'repo' &&
        ts.isIdentifier(prop.initializer)
      ) {
        const ident = prop.initializer.text
        if (ident.startsWith('guid_') && ident.length === 37) {
          repo = ident.slice(5)
        }
      }
    }
  }

  return {
    name: obj.name as string,
    retVar: (obj.retVar as string) ?? null,
    retVarType: (obj.retVarType as string) ?? null,
    debug: obj.debug != null ? (obj.debug as boolean) : null,
    skip: obj.skip != null ? (obj.skip as boolean) : null,
    isPrivate: obj['private'] != null ? (obj['private'] as boolean) : null,
    comment: (obj.comment as string) ?? null,
    color: (obj.color as number[]) ?? null,
    rawParams: (obj.rawParams as string) ?? null,
    rawRetVar: (obj.rawRetVar as string) ?? null,
    repo,
    isAsync: calleeName === 'BeScriptAsync',
  }
}

// ============================================================
// 区段标记扫描
// ============================================================

type SectionResult = { info: SectionInfo } | ScanError

function scanSectionMarkers(sf: ts.SourceFile, cls: ts.ClassDeclaration): SectionResult {
  const text = sf.text
  const classStart = cls.getStart(sf)
  const classEnd = cls.getEnd()
  const baseLineInFile = lineOf(sf, cls)

  const lines = text.substring(classStart, classEnd).split('\n')
  const info: SectionInfo = {
    seenGlobals: false,
    globalsBeginLine: -1,
    globalsEndLine: -1,
    seenScript: false,
    scriptBeginLine: -1,
    scriptEndLine: -1,
  }

  for (let i = 0; i < lines.length; i++) {
    const trimmed = lines[i].trim()
    const fileLine = baseLineInFile + i // 0-based

    // 非注释形式的区段标记
    if (!trimmed.startsWith('//') && (trimmed.startsWith('[Globals]') || trimmed.startsWith('[Script]'))) {
      return {
        error: '区段标记必须以 // 开头，只允许 // [Globals] BEGIN/END、// [Script] BEGIN/END。',
        errorLine: fileLine + 1,
      }
    }

    if (trimmed === '// [Globals] BEGIN') {
      if (info.seenGlobals) return { error: 'Globals 区段重复出现。', errorLine: fileLine + 1 }
      if (info.seenScript)
        return {
          error: 'Globals 区段必须位于最上方。',
          errorLine: fileLine + 1,
        }
      info.seenGlobals = true
      info.globalsBeginLine = fileLine
    } else if (trimmed === '// [Globals] END') {
      if (info.globalsBeginLine < 0)
        return {
          error: 'Globals 区段结束标记不匹配。',
          errorLine: fileLine + 1,
        }
      info.globalsEndLine = fileLine
    } else if (trimmed === '// [Script] BEGIN') {
      if (info.seenScript) return { error: '脚本区段重复出现。', errorLine: fileLine + 1 }
      info.seenScript = true
      info.scriptBeginLine = fileLine
    } else if (trimmed === '// [Script] END') {
      if (info.scriptBeginLine < 0)
        return {
          error: '缺少脚本区段起始标记或结束标记不匹配。',
          errorLine: fileLine + 1,
        }
      info.scriptEndLine = fileLine
    }
  }

  return { info }
}

// ============================================================
// 属性扫描（全局变量 / 属性方法）
// ============================================================

type PropertyScanResult =
  | { kind: 'global'; data: ScannedGlobalVarJson }
  | { kind: 'method'; data: ScannedMethodJson }
  | { kind: 'skip' }
  | ScanError

function scanProperty(sf: ts.SourceFile, member: ts.PropertyDeclaration, memberLine: number): PropertyScanResult {
  // @bsVar 全局变量
  const bsVarCall = findDecoratorCall(member, 'bsVar')
  if (bsVarCall) return scanGlobalVar(sf, member, bsVarCall, memberLine)

  if (!member.initializer) return { kind: 'skip' }

  // BeScript 柯里化属性方法
  if (isBeScriptCurriedInit(member.initializer)) {
    return scanPropertyMethod(sf, member, member.initializer as ts.CallExpression, memberLine)
  }

  return { kind: 'skip' }
}

function scanGlobalVar(
  sf: ts.SourceFile,
  member: ts.PropertyDeclaration,
  call: ts.CallExpression,
  memberLine: number,
): PropertyScanResult {
  const obj = call.arguments.length > 0 ? parseObjectLiteral(call.arguments[0]) : null
  if (!obj || typeof obj.name !== 'string') return { error: '@bsVar 缺少 name', errorLine: memberLine + 1 }

  const fieldName = ts.isIdentifier(member.name) ? member.name.text : ''
  const typeName = member.type ? member.type.getText(sf) : ''

  return {
    kind: 'global',
    data: {
      name: fieldName,
      rawName: obj.name as string,
      typeName,
      comment: (obj.comment as string) ?? null,
      lineNumber: memberLine + 1,
    },
  }
}

function isBeScriptCurriedInit(node: ts.Expression): boolean {
  if (!ts.isCallExpression(node)) return false
  const inner = node.expression
  if (!ts.isCallExpression(inner) || !ts.isIdentifier(inner.expression)) return false
  const name = inner.expression.text
  return name === 'BeScript' || name === 'BeScriptAsync'
}

function scanPropertyMethod(
  sf: ts.SourceFile,
  member: ts.PropertyDeclaration,
  init: ts.CallExpression,
  memberLine: number,
): PropertyScanResult {
  const propName = ts.isIdentifier(member.name) ? member.name.text : ''
  const r = parseBeScriptCurried(sf, init, propName, memberLine, false)
  if ('error' in r) return r
  r.isPrivate = hasModifier(member, ts.SyntaxKind.PrivateKeyword)
  r.isProtected = hasModifier(member, ts.SyntaxKind.ProtectedKeyword)
  r.isPublic = hasModifier(member, ts.SyntaxKind.PublicKeyword)
  return { kind: 'method', data: r }
}

function parseBeScriptCurried(
  sf: ts.SourceFile,
  outerCall: ts.CallExpression,
  memberName: string,
  memberLine: number,
  isEventMember: boolean,
): ScannedMethodJson | ScanError {
  const innerCall = outerCall.expression as ts.CallExpression
  const bsInfo = parseBeScriptInfo(innerCall)
  if (!bsInfo) return { error: 'BeScript 参数解析失败', errorLine: memberLine + 1 }

  const scriptFn = outerCall.arguments.length > 0 ? outerCall.arguments[0] : undefined
  let params: ParamJson[] = []
  let returnType = 'void'
  let bodyLines: BodyLineJson[] = []
  let declLine = memberLine + 1
  let endLine = memberLine + 1

  if (!scriptFn || (!ts.isFunctionExpression(scriptFn) && !ts.isArrowFunction(scriptFn))) {
    return {
      error: 'BeScript 脚本体必须使用 function 或箭头函数表达式。',
      errorLine: memberLine + 1,
    }
  }
  if (!ts.isBlock(scriptFn.body)) {
    return {
      error: 'BeScript 脚本体必须使用块语句。',
      errorLine: memberLine + 1,
    }
  }

  params = scriptFn.parameters.map((p: ts.ParameterDeclaration) => ({
    name: ts.isIdentifier(p.name) ? p.name.text : '',
    typeName: p.type ? p.type.getText(sf) : '',
  }))
  if (scriptFn.type) returnType = scriptFn.type.getText(sf)
  bodyLines = extractBodyLines(sf, scriptFn.body)
  declLine = lineOf(sf, scriptFn) + 1
  endLine = endLineOf(sf, scriptFn.body) + 1

  return {
    name: memberName,
    rawName: bsInfo.name,
    isPrivate: isEventMember,
    isProtected: false,
    isPublic: false,
    returnType,
    params,
    bodyLines,
    docStartLine: memberLine + 1,
    declLine,
    endLine,
    codeRepoGuid: bsInfo.repo,
    retVarFromDecorator: bsInfo.retVar,
    retVarTypeFromDecorator: bsInfo.retVarType,
    hasBeScriptDecorator: true,
    beScriptInfo: bsInfo,
  }
}

// ============================================================
// 事件注册静态块扫描
// ============================================================

type EventRegistrationScanResult = { groups: ScannedEventGroupJson[] } | ScanError

function scanEventRegistrationBlock(
  sf: ts.SourceFile,
  block: ts.ClassStaticBlockDeclaration,
): EventRegistrationScanResult {
  const groups: ScannedEventGroupJson[] = []

  for (const statement of block.body.statements) {
    const line = lineOf(sf, statement)
    if (!ts.isExpressionStatement(statement) || !ts.isCallExpression(statement.expression)) {
      return {
        error: '事件静态注册块只允许事件注册调用。',
        errorLine: line + 1,
      }
    }

    const r = scanEventRegistrationCall(sf, statement.expression, line)
    if ('error' in r) return r
    groups.push(r.group)
  }

  return { groups }
}

type EventRegistrationCallResult = { group: ScannedEventGroupJson } | ScanError

function scanEventRegistrationCall(
  sf: ts.SourceFile,
  call: ts.CallExpression,
  line: number,
): EventRegistrationCallResult {
  if (
    !ts.isPropertyAccessExpression(call.expression) ||
    call.expression.name.text.indexOf('registerEvent_') !== 0 ||
    call.arguments.length !== 2 ||
    call.arguments[0].kind !== ts.SyntaxKind.ThisKeyword ||
    !ts.isObjectLiteralExpression(call.arguments[1])
  ) {
    return { error: '事件注册调用格式不正确。', errorLine: line + 1 }
  }

  const eventName = call.expression.name.text.substring('registerEvent_'.length)
  const memberMethods: ScannedMethodJson[] = []
  const memberObject = call.arguments[1]
  for (const prop of memberObject.properties) {
    if (!ts.isPropertyAssignment(prop) || !ts.isIdentifier(prop.name)) {
      return {
        error: '事件注册成员必须使用属性赋值。',
        errorLine: lineOf(sf, prop) + 1,
      }
    }

    const memberName = prop.name.text
    if (!isBeScriptCurriedInit(prop.initializer)) {
      return {
        error: '事件注册成员必须是 BeScript/BeScriptAsync 脚本。',
        errorLine: lineOf(sf, prop) + 1,
      }
    }
    const method = parseBeScriptCurried(sf, prop.initializer as ts.CallExpression, memberName, lineOf(sf, prop), true)
    if ('error' in method) return method
    memberMethods.push(method)
  }

  return { group: { eventName, members: memberMethods } }
}

// ============================================================
// 方法声明扫描
// ============================================================

type MethodScanResult = { data: ScannedMethodJson | null } | ScanError

function scanMethod(sf: ts.SourceFile, member: ts.MethodDeclaration, memberLine: number): MethodScanResult {
  let bsCall = findDecoratorCall(member, 'BeScript')
  let isAsync = false
  if (!bsCall) {
    bsCall = findDecoratorCall(member, 'BeScriptAsync')
    isAsync = true
  }
  if (!bsCall) return { data: null }

  const bsInfo = parseBeScriptInfo(bsCall)
  if (!bsInfo) return { error: 'BeScript 参数解析失败', errorLine: memberLine + 1 }
  if (isAsync) bsInfo.isAsync = true

  const methodName = member.name && ts.isIdentifier(member.name) ? member.name.text : ''
  let returnType = member.type ? member.type.getText(sf) : 'void'

  const params: ParamJson[] = member.parameters.map((p: ts.ParameterDeclaration) => ({
    name: ts.isIdentifier(p.name) ? p.name.text : '',
    typeName: p.type ? p.type.getText(sf) : '',
  }))

  let bodyLines: BodyLineJson[] = []
  let endLine = memberLine + 1
  if (member.body && ts.isBlock(member.body)) {
    bodyLines = extractBodyLines(sf, member.body)
    endLine = endLineOf(sf, member.body) + 1
  }

  // JSDoc 起始行
  let docStartLine = memberLine + 1
  const jsDocs = ts.getJSDocCommentsAndTags(member)
  if (jsDocs.length > 0) {
    docStartLine = lineOf(sf, jsDocs[0]) + 1
  }

  return {
    data: {
      name: methodName,
      rawName: bsInfo.name,
      isPrivate: hasModifier(member, ts.SyntaxKind.PrivateKeyword),
      isProtected: hasModifier(member, ts.SyntaxKind.ProtectedKeyword),
      isPublic: hasModifier(member, ts.SyntaxKind.PublicKeyword),
      returnType,
      params,
      bodyLines,
      docStartLine,
      declLine: memberLine + 1,
      endLine,
      codeRepoGuid: bsInfo.repo,
      retVarFromDecorator: bsInfo.retVar,
      retVarTypeFromDecorator: bsInfo.retVarType,
      hasBeScriptDecorator: true,
      beScriptInfo: bsInfo,
    },
  }
}

// ============================================================
// 方法体行提取
// ============================================================

function extractBodyLines(sf: ts.SourceFile, block: ts.Block): BodyLineJson[] {
  const text = sf.text
  const openBraceLine = lineOf(sf, block)
  const closeBraceLine = endLineOf(sf, block)
  const lineStarts = sf.getLineStarts()
  const result: BodyLineJson[] = []

  for (let li = openBraceLine + 1; li < closeBraceLine; li++) {
    if (li >= lineStarts.length) break
    const start = lineStarts[li]
    const end = li + 1 < lineStarts.length ? lineStarts[li + 1] : text.length
    let lineText = text.substring(start, end)
    if (lineText.endsWith('\r\n')) lineText = lineText.slice(0, -2)
    else if (lineText.endsWith('\n')) lineText = lineText.slice(0, -1)
    result.push({ lineNumber: li + 1, lineText })
  }
  return result
}

// ============================================================
// 方法体解析：官方 TypeScript AST -> 写回数据 JSON
// ============================================================

interface BodyParseResult {
  statements: unknown[]
  normalizedLines: BodyLineJson[]
  rewriteWarnings: BodyRewriteWarningJson[]
  asAssertionLines: number[]
  inlineCommentLines: number[]
  insertedLeadingEmptyComment: boolean
}

interface BodyParseError {
  error: string
  errorLine?: number
}

function isBodyParseError(value: unknown): value is BodyParseError {
  return typeof value === 'object' && value !== null && 'error' in value
}

interface BodyParseContext {
  expectedRetVar: string | null
  normalizeImplicitReturn?: boolean
}

interface BodyLineJson {
  lineNumber: number
  lineText: string
}

interface BodyRewriteWarningJson {
  message: string
}

interface BodyWritebackNode extends Record<string, unknown> {
  kind: string
}

interface MethodBodyFormatOutput {
  text: string
  error?: string
  errorLine?: number
}

const methodBodyEndSentinel = '__tsview_body_end_sentinel__'
const blankLineMarker = '__TSVIEW_BLANK_LINE__'

function parseMethodBodyImpl(bodyText: string, context: BodyParseContext): BodyParseResult | BodyParseError {
  const sourceText = `async function __tsview_body__() {\n${bodyText}\n${methodBodyEndSentinel};\n}`
  const sf = ts.createSourceFile('method-body.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length > 0) {
    const diag = parseDiagnostics[0]
    const pos = diag.start ?? 0
    const lc = sf.getLineAndCharacterOfPosition(pos)
    return {
      error: flattenDiagnostic(diag.messageText),
      errorLine: Math.max(1, lc.line),
    }
  }

  const diagnostics =
    ts.transpileModule(sourceText, {
      compilerOptions: { target: ts.ScriptTarget.Latest },
      fileName: 'method-body.ts',
      reportDiagnostics: true,
    }).diagnostics ?? []
  if (diagnostics.length > 0) {
    const diag = diagnostics[0]
    const pos = diag.start ?? 0
    const tempSf = ts.createSourceFile('method-body.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
    const lc = tempSf.getLineAndCharacterOfPosition(pos)
    return {
      error: flattenDiagnostic(diag.messageText),
      errorLine: Math.max(1, lc.line),
    }
  }

  const fn = sf.statements.find(ts.isFunctionDeclaration)
  if (!fn || !fn.body) {
    return { error: '方法体解析失败：未找到包装函数。', errorLine: 1 }
  }
  const bodyStatements: ts.Statement[] = Array.from(fn.body.statements)
  const endStatement: ts.Statement | undefined = bodyStatements[bodyStatements.length - 1]
  if (
    !endStatement ||
    !ts.isExpressionStatement(endStatement) ||
    !ts.isIdentifier(endStatement.expression) ||
    endStatement.expression.text !== methodBodyEndSentinel
  ) {
    return {
      error: '方法体代码块未闭合。',
      errorLine: Math.max(1, bodyText.split(/\r?\n/).length),
    }
  }

  const statements: unknown[] = []
  const bodyLines = buildBodyLineJson(bodyText)
  const rawStatements = collectBodyStatementsWithComments(sf, fn.body)
  rawStatements.pop()
  for (let i = 0; i < rawStatements.length; i++) {
    const item = rawStatements[i]
    if (item.commentText != null) {
      const disabledBlock = mapDisabledCommentBlock(sf, rawStatements, i)
      if (isBodyParseError(disabledBlock)) return disabledBlock
      const mappedComments = disabledBlock !== null ? disabledBlock.nodes : mapCommentItem(item)
      if ((mappedComments as BodyParseError).error !== undefined) return mappedComments as BodyParseError
      statements.push(...(mappedComments as BodyWritebackNode[]))
      if (disabledBlock !== null) i = disabledBlock.endIndex
      continue
    }

    const mapped = mapStatement(sf, item.statement!, item.lineNumber)
    if ((mapped as BodyParseError).error !== undefined) return mapped as BodyParseError
    statements.push(mapped)
  }

  const normalized =
    context.normalizeImplicitReturn === true
      ? ensureImplicitReturn(statements, bodyLines, context.expectedRetVar ?? null)
      : { lines: bodyLines, insertedLeadingEmptyComment: false }
  const rewriteWarnings: BodyRewriteWarningJson[] = []
  const rewrittenStatements = statements.map((s) => rewriteMappedNode(s as BodyWritebackNode, rewriteWarnings))
  return {
    statements: rewrittenStatements,
    normalizedLines: normalized.lines,
    rewriteWarnings,
    asAssertionLines: collectAsAssertionLines(sf, fn.body),
    inlineCommentLines: collectInlineCommentLines(bodyText),
    insertedLeadingEmptyComment: normalized.insertedLeadingEmptyComment,
  }
}

function collectAsAssertionLines(sf: ts.SourceFile, body: ts.Block): number[] {
  const lines = new Set<number>()
  function visit(node: ts.Node): void {
    if (ts.isAsExpression(node)) lines.add(lineOf(sf, node))
    ts.forEachChild(node, visit)
  }
  visit(body)
  return Array.from(lines)
}

function collectInlineCommentLines(text: string): number[] {
  const lines = new Set<number>()
  const scanner = ts.createScanner(ts.ScriptTarget.Latest, false, ts.LanguageVariant.Standard, text)
  for (let kind = scanner.scan(); kind !== ts.SyntaxKind.EndOfFileToken; kind = scanner.scan()) {
    if (kind !== ts.SyntaxKind.SingleLineCommentTrivia && kind !== ts.SyntaxKind.MultiLineCommentTrivia) continue
    const commentStart = scanner.getTokenPos()
    const lineStart = text.lastIndexOf('\n', commentStart - 1) + 1
    if (text.slice(lineStart, commentStart).trim().length !== 0) {
      lines.add(text.slice(0, commentStart).split('\n').length)
    }
  }
  return Array.from(lines)
}

function formatMethodBodyImpl(bodyText: string): MethodBodyFormatOutput {
  const sourceText = `async function __tsview_format_body__() {\n${bodyText}\n${methodBodyEndSentinel};\n}`
  const sf = ts.createSourceFile('method-body-format.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length > 0) {
    const diag = parseDiagnostics[0]
    const pos = diag.start ?? 0
    const lc = sf.getLineAndCharacterOfPosition(pos)
    return {
      text: '',
      error: flattenDiagnostic(diag.messageText),
      errorLine: Math.max(1, lc.line),
    }
  }

  const diagnostics =
    ts.transpileModule(sourceText, {
      compilerOptions: { target: ts.ScriptTarget.Latest },
      fileName: 'method-body-format.ts',
      reportDiagnostics: true,
    }).diagnostics ?? []
  if (diagnostics.length > 0) {
    const diag = diagnostics[0]
    const pos = diag.start ?? 0
    const tempSf = ts.createSourceFile(
      'method-body-format.ts',
      sourceText,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TS,
    )
    const lc = tempSf.getLineAndCharacterOfPosition(pos)
    return {
      text: '',
      error: flattenDiagnostic(diag.messageText),
      errorLine: Math.max(1, lc.line),
    }
  }

  const fn = sf.statements.find(ts.isFunctionDeclaration)
  if (!fn || !fn.body) {
    return {
      text: '',
      error: '方法体解析失败：未找到包装函数。',
      errorLine: 1,
    }
  }
  const bodyStatements: ts.Statement[] = Array.from(fn.body.statements)
  const endStatement: ts.Statement | undefined = bodyStatements[bodyStatements.length - 1]
  if (
    !endStatement ||
    !ts.isExpressionStatement(endStatement) ||
    !ts.isIdentifier(endStatement.expression) ||
    endStatement.expression.text !== methodBodyEndSentinel
  ) {
    return {
      text: '',
      error: '方法体代码块未闭合。',
      errorLine: Math.max(1, bodyText.split(/\r?\n/).length),
    }
  }

  const bodySourceFile = ts.factory.createSourceFile(
    preserveBlankLinesBetweenStatements(
      bodyStatements.slice(0, -1),
      buildBodyLineJson(bodyText),
      (statement) => sourceLineOf(sf, statement),
      (statement) => sourceEndLineOf(sf, statement),
    ),
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  return { text: printFileWithTwoSpaceIndent(bodySourceFile) }
}

function buildBodyLineJson(bodyText: string): BodyLineJson[] {
  return bodyText.split('\n').map((lineText, index) => ({ lineNumber: index + 1, lineText }))
}

function ensureImplicitReturn(
  statements: unknown[],
  bodyLines: BodyLineJson[],
  expectedRetVar: string | null,
): { lines: BodyLineJson[]; insertedLeadingEmptyComment: boolean } {
  const expectedReturnStmt = expectedRetVar === null ? 'return;' : `return ${expectedRetVar};`
  const lastLine = bodyLines[bodyLines.length - 1]
  if (lastLine.lineText.trim() === expectedReturnStmt) {
    if (bodyLines.length === 1) {
      const indent = /^\s*/.exec(lastLine.lineText)?.[0] ?? ''
      statements.unshift(mapCommentStatement('', 1))
      return {
        lines: [
          { lineNumber: 1, lineText: `${indent}//` },
          { lineNumber: 2, lineText: lastLine.lineText },
        ],
        insertedLeadingEmptyComment: true,
      }
    }
    return { lines: bodyLines, insertedLeadingEmptyComment: false }
  }

  statements.push(
    withSourceLine(
      {
        kind: 'returnStatement',
        expression: expectedRetVar === null ? null : { kind: 'identifier', text: expectedRetVar },
      },
      bodyLines.length + 1,
    ),
  )
  return {
    lines: bodyLines.concat([
      {
        lineNumber: bodyLines.length + 1,
        lineText: `    ${expectedReturnStmt}`,
      },
    ]),
    insertedLeadingEmptyComment: false,
  }
}

function flattenDiagnostic(message: string | ts.DiagnosticMessageChain): string {
  if (typeof message === 'string') return message
  const parts: string[] = []
  let current: ts.DiagnosticMessageChain | undefined = message
  while (current) {
    parts.push(current.messageText)
    current = current.next?.[0]
  }
  return parts.join(' ')
}

type BodyRawStatement =
  | {
      statement: ts.Statement
      lineNumber: number
      commentText?: never
      disabled?: never
    }
  | {
      commentText: string
      lineNumber: number
      commentKind: 'singleLineComment' | 'multiLineComment'
      disabled: boolean
      statement?: never
    }

type DisabledCommentBlock = { nodes: BodyWritebackNode[]; endIndex: number }

function collectBodyStatementsWithComments(sf: ts.SourceFile, block: ts.Block): BodyRawStatement[] {
  const statementItems = block.statements.map((s: ts.Statement) => ({
    statement: s,
    lineNumber: lineOf(sf, s),
    pos: s.getFullStart(),
  }))
  const comments = collectFullLineComments(sf, block)
  const items: BodyRawStatement[] = []
  let statementIndex = 0
  let commentIndex = 0
  while (statementIndex < statementItems.length || commentIndex < comments.length) {
    const nextStatement = statementItems[statementIndex]
    const nextComment = comments[commentIndex]
    if (nextComment && (!nextStatement || nextComment.lineNumber <= nextStatement.lineNumber)) {
      items.push(nextComment)
      commentIndex++
      continue
    }
    items.push({
      statement: nextStatement.statement,
      lineNumber: nextStatement.lineNumber,
    })
    statementIndex++
  }
  return items
}

function collectFullLineComments(sf: ts.SourceFile, block: ts.Block): BodyRawStatement[] {
  const text = sf.text
  const result: BodyRawStatement[] = []
  const lineStarts = sf.getLineStarts()
  const openLine = lineOf(sf, block)
  const closeLine = endLineOf(sf, block)
  for (let line = openLine + 1; line < closeLine; line++) {
    if (isInsideNestedDirectStatementBlock(sf, block, line)) continue
    const start = lineStarts[line]
    const end = line + 1 < lineStarts.length ? lineStarts[line + 1] : text.length
    const raw = text.substring(start, end).replace(/\r?\n$/, '')
    const content = raw.replace(/^\s+/, '')
    if (content.startsWith('//')) {
      result.push({
        commentText: content.slice(2),
        lineNumber: line,
        commentKind: 'singleLineComment',
        disabled: false,
      })
      continue
    }
    if (content.startsWith('/*')) {
      const commentStart = start + raw.length - content.length
      const close = text.indexOf('*/', commentStart + 2)
      if (close < 0) continue
      const closeLine = sf.getLineAndCharacterOfPosition(close).line
      const closeLineEnd = closeLine + 1 < lineStarts.length ? lineStarts[closeLine + 1] : text.length
      if (text.substring(close + 2, closeLineEnd).trim().length !== 0) continue
      const inner = text.substring(commentStart + 2, close)
      result.push({
        commentText: inner,
        lineNumber: line,
        commentKind: 'multiLineComment',
        disabled: true,
      })
      line = closeLine
    }
  }
  return result
}

function isInsideNestedDirectStatementBlock(sf: ts.SourceFile, block: ts.Block, line: number): boolean {
  for (const statement of block.statements) {
    if (!ts.isIfStatement(statement) && !ts.isWhileStatement(statement)) continue
    const start = lineOf(sf, statement)
    const end = endLineOf(sf, statement)
    if (line > start && line < end) return true
  }
  return false
}

function mapCommentStatement(text: string, lineNumber: number): BodyWritebackNode {
  return withSourceLine({ kind: 'singleLineComment', text }, lineNumber)
}

function mapCommentItem(item: BodyRawStatement): BodyWritebackNode[] | BodyParseError {
  if (item.commentText == null) return []
  if (!item.disabled) {
    return [
      item.commentKind === 'multiLineComment'
        ? withSourceLine({ kind: 'multiLineComment', text: item.commentText }, item.lineNumber)
        : mapCommentStatement(item.commentText, item.lineNumber),
    ]
  }

  return mapDisabledCommentStatements(item.commentText, item.lineNumber)
}

function mapDisabledCommentBlock(
  sf: ts.SourceFile,
  items: BodyRawStatement[],
  startIndex: number,
): DisabledCommentBlock | BodyParseError | null {
  const first = items[startIndex]
  if (first.commentText == null || !first.disabled) return null

  // Disabled control shells come from BeScript disabled if/while lines; mapper output may keep active statements inside.
  let braceDepth = getDisabledCommentBlockDelta(first.commentText)
  if (braceDepth <= 0) return null

  let endIndex = startIndex
  while (braceDepth > 0 && endIndex + 1 < items.length) {
    endIndex++
    const item = items[endIndex]
    if (item.commentText != null && item.disabled) {
      braceDepth += getDisabledCommentBlockDelta(item.commentText)
    }
  }

  if (braceDepth !== 0) {
    const unclosed = mapDisabledCommentStatements(first.commentText, first.lineNumber)
    if (isBodyParseError(unclosed)) return unclosed
    return { nodes: unclosed, endIndex }
  }

  const shell = mapDisabledCommentStatements(first.commentText + '\n}', first.lineNumber)
  if (isBodyParseError(shell)) return shell

  const shellNodes = shell as BodyWritebackNode[]
  if (shellNodes.length !== 1) {
    return {
      error: '禁用块注释解析失败：结构壳必须只包含一个语句。',
      errorLine: first.lineNumber,
    }
  }

  const children = mapRawStatementItems(sf, items, startIndex + 1, endIndex)
  if ((children as BodyParseError).error !== undefined) return children as BodyParseError

  const shellNode = shellNodes[0]
  if (shellNode.kind === 'ifStatement') {
    const thenStatement = shellNode.thenStatement as BodyWritebackNode
    return {
      nodes: [
        {
          ...shellNode,
          thenStatement: {
            ...thenStatement,
            statements: children as BodyWritebackNode[],
          },
        },
      ],
      endIndex,
    }
  }

  if (shellNode.kind === 'whileStatement') {
    const body = shellNode.body as BodyWritebackNode
    return {
      nodes: [
        {
          ...shellNode,
          body: { ...body, statements: children as BodyWritebackNode[] },
        },
      ],
      endIndex,
    }
  }

  return {
    error: '禁用块注释解析失败：结构壳只支持 if/while。',
    errorLine: first.lineNumber,
  }
}

function mapRawStatementItems(
  sf: ts.SourceFile,
  items: BodyRawStatement[],
  startIndex: number,
  endIndexExclusive: number,
): BodyWritebackNode[] | BodyParseError {
  const result: BodyWritebackNode[] = []
  for (let i = startIndex; i < endIndexExclusive; i++) {
    const item = items[i]
    if (item.commentText != null) {
      const disabledBlock = mapDisabledCommentBlock(sf, items, i)
      if (isBodyParseError(disabledBlock)) return disabledBlock
      const mappedComments = disabledBlock !== null ? disabledBlock.nodes : mapCommentItem(item)
      if ((mappedComments as BodyParseError).error !== undefined) return mappedComments as BodyParseError
      result.push(...(mappedComments as BodyWritebackNode[]))
      if (disabledBlock !== null) i = disabledBlock.endIndex
      continue
    }

    const mapped = mapStatement(sf, item.statement!, item.lineNumber)
    if ((mapped as BodyParseError).error !== undefined) return mapped as BodyParseError
    result.push(mapped as BodyWritebackNode)
  }
  return result
}

function getDisabledCommentBlockDelta(text: string): number {
  const trimmed = text.trim()
  if (trimmed === '}') return -1
  if (isDisabledControlShellStart(trimmed)) return 1
  return 0
}

function isDisabledControlShellStart(text: string): boolean {
  const sourceText = `async function __tsview_disabled_shell__() {\n${text}\n}\n}`
  const sf = ts.createSourceFile(
    'disabled-comment-shell.ts',
    sourceText,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS,
  )
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length !== 0) return false

  const fn = sf.statements.find(ts.isFunctionDeclaration)
  if (!fn?.body || fn.body.statements.length !== 1) return false

  const statement = fn.body.statements[0]
  return (
    (ts.isIfStatement(statement) && ts.isBlock(statement.thenStatement)) ||
    (ts.isWhileStatement(statement) && ts.isBlock(statement.statement))
  )
}

function mapDisabledCommentStatements(text: string, lineNumber: number): BodyWritebackNode[] | BodyParseError {
  const disabledCommentText = text.trim()
  if (disabledCommentText.startsWith('//')) {
    return [
      withSourceLine(
        {
          kind: 'singleLineComment',
          text: disabledCommentText.slice(2),
          isDisabled: true,
        },
        lineNumber,
      ),
    ]
  }

  const sourceText = `async function __tsview_disabled__() {\n${text}\n}`
  const sf = ts.createSourceFile('disabled-comment.ts', sourceText, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const parseDiagnostics = (sf as ts.SourceFile & { parseDiagnostics: readonly ts.Diagnostic[] }).parseDiagnostics
  if (parseDiagnostics.length > 0) {
    const diag = parseDiagnostics[0]
    const pos = diag.start ?? 0
    const lc = sf.getLineAndCharacterOfPosition(pos)
    return {
      error: flattenDiagnostic(diag.messageText),
      errorLine: lineNumber + Math.max(0, lc.line - 1),
    }
  }

  const diagnostics =
    ts.transpileModule(sourceText, {
      compilerOptions: { target: ts.ScriptTarget.Latest },
      fileName: 'disabled-comment.ts',
      reportDiagnostics: true,
    }).diagnostics ?? []
  if (diagnostics.length > 0) {
    const diag = diagnostics[0]
    const pos = diag.start ?? 0
    const lc = sf.getLineAndCharacterOfPosition(pos)
    return {
      error: flattenDiagnostic(diag.messageText),
      errorLine: lineNumber + Math.max(0, lc.line - 1),
    }
  }

  const fn = sf.statements.find(ts.isFunctionDeclaration)
  if (!fn || !fn.body) {
    return {
      error: '禁用块注释解析失败：未找到包装函数。',
      errorLine: lineNumber,
    }
  }
  const rawStatements = collectBodyStatementsWithComments(sf, fn.body)
  if (rawStatements.length === 0) {
    return { error: '禁用块注释未包含可写回 TS 语句。', errorLine: lineNumber }
  }

  const result: BodyWritebackNode[] = []
  for (const item of rawStatements) {
    if (item.commentText != null) {
      const mappedComments = mapCommentItem({
        ...item,
        lineNumber: lineNumber + item.lineNumber - 1,
      })
      if ((mappedComments as BodyParseError).error !== undefined) return mappedComments as BodyParseError
      for (const mappedComment of mappedComments as BodyWritebackNode[]) {
        result.push(markDisabled(mappedComment))
      }
      continue
    }

    const statement = item.statement!
    const mapped = mapStatement(sf, statement, lineNumber + lineOf(sf, statement) - 1)
    if ((mapped as BodyParseError).error !== undefined) return mapped as BodyParseError
    result.push(markDisabled(mapped as BodyWritebackNode))
  }
  return result
}

function markDisabled(node: BodyWritebackNode): BodyWritebackNode {
  switch (node.kind) {
    case 'ifStatement':
    case 'whileStatement':
    case 'variableStatement':
    case 'expressionStatement':
    case 'returnStatement':
    case 'breakStatement':
    case 'continueStatement':
    case 'singleLineComment':
    case 'multiLineComment':
      return { ...node, isDisabled: true }
    default:
      return node
  }
}

function dedentTemplateLiteral(text: string): string {
  if (!text.includes('\n')) return text

  const lines = text.split('\n')
  let minIndent = Number.MAX_SAFE_INTEGER
  for (let i = 1; i < lines.length; i++) {
    const match = /^ */.exec(lines[i])
    const spaces = match ? match[0].length : 0
    if (spaces < minIndent) minIndent = spaces
  }

  if (minIndent === 0 || minIndent === Number.MAX_SAFE_INTEGER) return text

  let result = lines[0]
  for (let i = 1; i < lines.length; i++) {
    result += '\n'
    result += lines[i].length >= minIndent ? lines[i].slice(minIndent) : lines[i]
  }
  return result
}

function mapStatement(sf: ts.SourceFile, stmt: ts.Statement, lineNumber: number): BodyWritebackNode | BodyParseError {
  if (ts.isEmptyStatement(stmt)) return withSourceLine({ kind: 'emptyStatement' }, lineNumber)
  if (ts.isVariableStatement(stmt)) return withSourceLine(mapVariableStatement(sf, stmt), lineNumber)
  if (ts.isExpressionStatement(stmt))
    return withSourceLine(
      {
        kind: 'expressionStatement',
        expression: mapExpression(sf, stmt.expression),
      },
      lineNumber,
    )
  if (ts.isIfStatement(stmt)) {
    return withSourceLine(
      {
        kind: 'ifStatement',
        condition: mapExpression(sf, stmt.expression),
        thenStatement: mapStatementAsBlock(sf, stmt.thenStatement),
        elseStatement: stmt.elseStatement ? mapStatementAsBlock(sf, stmt.elseStatement) : null,
      },
      lineNumber,
    )
  }
  if (ts.isWhileStatement(stmt)) {
    return withSourceLine(
      {
        kind: 'whileStatement',
        condition: mapExpression(sf, stmt.expression),
        body: mapStatementAsBlock(sf, stmt.statement),
      },
      lineNumber,
    )
  }
  if (ts.isReturnStatement(stmt))
    return withSourceLine(
      {
        kind: 'returnStatement',
        expression: stmt.expression ? mapExpression(sf, stmt.expression) : null,
      },
      lineNumber,
    )
  if (ts.isBreakStatement(stmt)) return withSourceLine({ kind: 'breakStatement' }, lineNumber)
  if (ts.isContinueStatement(stmt)) return withSourceLine({ kind: 'continueStatement' }, lineNumber)
  return {
    error: `不支持的 TS 语句: ${ts.SyntaxKind[stmt.kind]}`,
    errorLine: lineNumber,
  }
}

function mapStatementAsBlock(sf: ts.SourceFile, stmt: ts.Statement): BodyWritebackNode {
  if (ts.isBlock(stmt)) {
    const statements: BodyWritebackNode[] = []
    const rawStatements = collectBodyStatementsWithComments(sf, stmt)
    for (let i = 0; i < rawStatements.length; i++) {
      const item = rawStatements[i]
      if (item.commentText != null) {
        const disabledBlock = mapDisabledCommentBlock(sf, rawStatements, i)
        if (isBodyParseError(disabledBlock)) throw new Error(disabledBlock.error)
        const mappedComments = disabledBlock !== null ? disabledBlock.nodes : mapCommentItem(item)
        if ((mappedComments as BodyParseError).error !== undefined)
          throw new Error((mappedComments as BodyParseError).error)
        statements.push(...(mappedComments as BodyWritebackNode[]))
        if (disabledBlock !== null) i = disabledBlock.endIndex
        continue
      }

      const mapped = mapStatement(sf, item.statement!, item.lineNumber)
      if ((mapped as BodyParseError).error !== undefined) throw new Error((mapped as BodyParseError).error)
      statements.push(mapped as BodyWritebackNode)
    }
    return {
      kind: 'block',
      statements,
    }
  }
  return {
    kind: 'block',
    statements: [mapStatement(sf, stmt, lineOf(sf, stmt))],
  }
}

function mapVariableStatement(sf: ts.SourceFile, stmt: ts.VariableStatement): BodyWritebackNode {
  return {
    kind: 'variableStatement',
    declarationList: {
      kind: 'variableDeclarationList',
      variableKind: mapVariableKind(stmt.declarationList.flags),
      declarations: stmt.declarationList.declarations.map((d: ts.VariableDeclaration) => ({
        kind: 'variableDeclaration',
        name: mapBindingName(d.name),
        type: d.type ? mapTypeNode(sf, d.type) : null,
        initializer: d.initializer ? mapExpression(sf, d.initializer) : null,
      })),
    },
  }
}

function mapVariableKind(flags: ts.NodeFlags): string {
  if ((flags & ts.NodeFlags.Const) !== 0) return 'const'
  if ((flags & ts.NodeFlags.Let) !== 0) return 'let'
  return 'var'
}

function mapBindingName(name: ts.BindingName): BodyWritebackNode {
  if (ts.isIdentifier(name)) return { kind: 'identifier', text: name.text }
  throw new Error('TS 视图不支持解构声明。')
}

function mapExpression(sf: ts.SourceFile, expr: ts.Expression): BodyWritebackNode {
  if (ts.isIdentifier(expr)) return { kind: 'identifier', text: expr.text }
  if (expr.kind === ts.SyntaxKind.ThisKeyword) return { kind: 'thisExpression' }
  if (expr.kind === ts.SyntaxKind.TrueKeyword) return { kind: 'trueLiteral' }
  if (expr.kind === ts.SyntaxKind.FalseKeyword) return { kind: 'falseLiteral' }
  if (ts.isStringLiteral(expr)) return { kind: 'stringLiteral', text: expr.text }
  if (ts.isNumericLiteral(expr)) return { kind: 'numericLiteral', text: expr.text }
  if (ts.isNoSubstitutionTemplateLiteral(expr))
    return { kind: 'templateLiteral', text: dedentTemplateLiteral(expr.text) }
  if (ts.isPropertyAccessExpression(expr))
    return {
      kind: 'propertyAccess',
      expression: mapExpression(sf, expr.expression),
      name: { kind: 'identifier', text: expr.name.text },
    }
  if (ts.isCallExpression(expr))
    return {
      kind: 'call',
      expression: mapExpression(sf, expr.expression),
      arguments: expr.arguments.map((a: ts.Expression) => mapExpression(sf, a)),
      typeArguments: expr.typeArguments?.map((t: ts.TypeNode) => mapTypeNode(sf, t)) ?? null,
    }
  if (ts.isExpressionWithTypeArguments(expr))
    return {
      kind: 'typeReference',
      typeName: mapExpression(sf, expr.expression),
      typeArguments: expr.typeArguments?.map((t: ts.TypeNode) => mapTypeNode(sf, t)) ?? null,
    }
  if (ts.isNewExpression(expr))
    return {
      kind: 'newExpression',
      expression: mapExpression(sf, expr.expression),
      arguments: expr.arguments?.map((a: ts.Expression) => mapExpression(sf, a)) ?? [],
    }
  if (ts.isPrefixUnaryExpression(expr) && expr.operator === ts.SyntaxKind.ExclamationToken)
    return { kind: 'notExpression', operand: mapExpression(sf, expr.operand) }
  if (ts.isBinaryExpression(expr))
    return {
      kind: 'binaryExpression',
      left: mapExpression(sf, expr.left),
      operator: mapBinaryOperator(expr.operatorToken.kind),
      right: mapExpression(sf, expr.right),
    }
  if (ts.isParenthesizedExpression(expr))
    return {
      kind: 'parenthesizedExpression',
      expression: mapExpression(sf, expr.expression),
    }
  if (ts.isAsExpression(expr))
    return {
      kind: 'asExpression',
      expression: mapExpression(sf, expr.expression),
      type: mapTypeNode(sf, expr.type),
    }
  if (ts.isArrayLiteralExpression(expr))
    return {
      kind: 'arrayLiteral',
      elements: expr.elements.map((e: ts.Expression) => mapExpression(sf, e)),
    }
  if (ts.isObjectLiteralExpression(expr)) {
    return {
      kind: 'objectLiteral',
      properties: expr.properties.map((p: ts.ObjectLiteralElementLike) => {
        if (!ts.isPropertyAssignment(p)) throw new Error('TS 视图对象字面量只支持属性赋值。')
        return {
          kind: 'objectProperty',
          name: p.name.getText(sf),
          value: mapExpression(sf, p.initializer),
          jsDoc: null,
        }
      }),
      isMultiLine: false,
    }
  }
  if (ts.isAwaitExpression(expr))
    return {
      kind: 'awaitExpression',
      argument: mapExpression(sf, expr.expression),
    }
  if (ts.isVoidExpression(expr))
    return {
      kind: 'voidExpression',
      argument: mapExpression(sf, expr.expression),
    }
  throw new Error(`不支持的 TS 表达式: ${ts.SyntaxKind[expr.kind]}`)
}

const downgradableCreateBeTypeName: Record<string, string> = {
  'create.float': 'Float',
  'create.long': 'Long',
  'create.bool': 'Bool',
  'create.string': 'String',
  'create.color': 'Color',
  'creatVariable.Vector3': 'Vector3',
}

const anyPositionNonEmptyConstTypes = new Set(['Bool', 'Float', 'Long', 'Color', 'Vector3', 'String'])

function rewriteMappedNode(node: BodyWritebackNode, warnings: BodyRewriteWarningJson[]): BodyWritebackNode {
  const withChildren = rewriteMappedChildren(node, warnings)
  if (withChildren.kind === 'call') {
    const stripped = tryStripRedundantCreateBool(withChildren, warnings)
    if (stripped !== null) return stripped
  }
  return withChildren
}

function rewriteMappedChildren(node: BodyWritebackNode, warnings: BodyRewriteWarningJson[]): BodyWritebackNode {
  switch (node.kind) {
    case 'block':
      return {
        ...node,
        statements: (node.statements as BodyWritebackNode[]).map((s) => rewriteMappedNode(s, warnings)),
      }
    case 'variableStatement': {
      const list = node.declarationList as BodyWritebackNode
      return {
        ...node,
        declarationList: {
          ...list,
          declarations: (list.declarations as BodyWritebackNode[]).map((d) => ({
            ...d,
            initializer: d.initializer ? rewriteMappedNode(d.initializer as BodyWritebackNode, warnings) : null,
          })),
        },
      }
    }
    case 'expressionStatement':
      return {
        ...node,
        expression: rewriteMappedNode(node.expression as BodyWritebackNode, warnings),
      }
    case 'ifStatement':
      return {
        ...node,
        condition: rewriteMappedNode(node.condition as BodyWritebackNode, warnings),
        thenStatement: rewriteMappedNode(node.thenStatement as BodyWritebackNode, warnings),
        elseStatement: node.elseStatement ? rewriteMappedNode(node.elseStatement as BodyWritebackNode, warnings) : null,
      }
    case 'whileStatement':
      return {
        ...node,
        condition: rewriteMappedNode(node.condition as BodyWritebackNode, warnings),
        body: rewriteMappedNode(node.body as BodyWritebackNode, warnings),
      }
    case 'returnStatement':
      return {
        ...node,
        expression: node.expression ? rewriteMappedNode(node.expression as BodyWritebackNode, warnings) : null,
      }
    case 'call': {
      const expression = rewriteMappedNode(node.expression as BodyWritebackNode, warnings)
      const allowDowngrade = getMappedPropertyPathText(node.expression as BodyWritebackNode) !== 'delay'
      const args = (node.arguments as BodyWritebackNode[]).map((arg) => {
        const rewrittenArg = rewriteMappedNode(arg, warnings)
        if (!allowDowngrade) return rewrittenArg
        const downgraded = tryDowngradeNestedCreate(rewrittenArg, warnings)
        return downgraded ?? rewrittenArg
      })
      return { ...node, expression, arguments: args }
    }
    case 'propertyAccess':
      return {
        ...node,
        expression: rewriteMappedNode(node.expression as BodyWritebackNode, warnings),
      }
    case 'notExpression':
      return {
        ...node,
        operand: rewriteMappedNode(node.operand as BodyWritebackNode, warnings),
      }
    case 'binaryExpression':
      return {
        ...node,
        left: rewriteMappedNode(node.left as BodyWritebackNode, warnings),
        right: rewriteMappedNode(node.right as BodyWritebackNode, warnings),
      }
    case 'parenthesizedExpression':
      return {
        ...node,
        expression: rewriteMappedNode(node.expression as BodyWritebackNode, warnings),
      }
    case 'asExpression':
      return {
        ...node,
        expression: rewriteMappedNode(node.expression as BodyWritebackNode, warnings),
      }
    case 'awaitExpression':
      return {
        ...node,
        argument: rewriteMappedNode(node.argument as BodyWritebackNode, warnings),
      }
    case 'voidExpression':
      return {
        ...node,
        argument: rewriteMappedNode(node.argument as BodyWritebackNode, warnings),
      }
    case 'newExpression':
      return {
        ...node,
        expression: node.expression ? rewriteMappedNode(node.expression as BodyWritebackNode, warnings) : null,
        arguments: (node.arguments as BodyWritebackNode[]).map((a) => rewriteMappedNode(a, warnings)),
      }
    case 'objectLiteral':
      return {
        ...node,
        properties: (node.properties as BodyWritebackNode[]).map((p) => ({
          ...p,
          value: rewriteMappedNode(p.value as BodyWritebackNode, warnings),
        })),
      }
    case 'arrayLiteral':
      return {
        ...node,
        elements: (node.elements as BodyWritebackNode[]).map((e) => rewriteMappedNode(e, warnings)),
      }
    default:
      return node
  }
}

function tryStripRedundantCreateBool(
  call: BodyWritebackNode,
  warnings: BodyRewriteWarningJson[],
): BodyWritebackNode | null {
  if (getMappedPropertyPathText(call.expression as BodyWritebackNode) !== 'G.create.bool') return null
  const args = call.arguments as BodyWritebackNode[]
  if (args.length !== 1 || args[0].kind !== 'call') return null
  const innerCall = args[0]
  if (tryParseMappedConstValue(innerCall) !== null) return null
  const innerPath = getMappedPropertyPathText(innerCall.expression as BodyWritebackNode)
  const dot = innerPath.indexOf('.')
  const innerMethod = dot >= 0 ? innerPath.slice(dot + 1) : ''
  if (downgradableCreateBeTypeName[innerMethod] !== undefined) return null
  warnings.push({
    message: `G.create.bool(${innerPath}(...)) 冗余包装。create.bool 参数位已是返回 Bool 的调用表达式，已自动剥离。正确写法应直接使用内层调用。`,
  })
  return innerCall
}

function tryDowngradeNestedCreate(
  node: BodyWritebackNode,
  warnings: BodyRewriteWarningJson[],
): BodyWritebackNode | null {
  if (node.kind !== 'call') return null
  const pathText = getMappedPropertyPathText(node.expression as BodyWritebackNode)
  const firstDot = pathText.indexOf('.')
  if (firstDot < 0 || pathText.indexOf('.', firstDot + 1) < 0 || pathText.slice(0, firstDot) !== 'G') return null
  const methodPath = pathText.slice(firstDot + 1)
  const beTypeName = downgradableCreateBeTypeName[methodPath]
  if (beTypeName === undefined) return null
  const downgraded = downgradeCreateCall(node, methodPath)
  if (downgraded === null) return null
  warnings.push({
    message: `G.${methodPath} 嵌套常量是错误写法。已自动改写为 Be${beTypeName}.fromBeConst 形式。正确写法应直接使用常量形式。`,
  })
  return downgraded
}

function downgradeCreateCall(call: BodyWritebackNode, methodPath: string): BodyWritebackNode | null {
  const args = call.arguments as BodyWritebackNode[]
  if (methodPath === 'creatVariable.Vector3') {
    if (args.length !== 3) return null
    const floatValues: string[] = []
    for (const arg of args) {
      const parsed = tryParseMappedConstValue(arg)
      if (parsed === null || parsed.typeName !== 'Float') return null
      floatValues.push(parsed.value)
    }
    return createMappedBeConstCall('Vector3', stringLiteralNode(floatValues.join(',')))
  }
  if (args.length !== 1) return null
  const parsed = tryParseMappedConstValue(args[0])
  if (parsed === null) return null
  if (methodPath === 'create.string' && anyPositionNonEmptyConstTypes.has(parsed.typeName)) {
    return createMappedBeConstCall('String', stringLiteralNode(parsed.value))
  }
  return args[0]
}

function tryParseMappedConstValue(node: BodyWritebackNode): { typeName: string; value: string } | null {
  if (node.kind !== 'call') return null
  const path = getMappedPropertyPathText(node.expression as BodyWritebackNode)
  const dot = path.indexOf('.')
  if (dot < 0) return null
  const typePart = path.slice(0, dot)
  const methodName = path.slice(dot + 1)
  if (!typePart.startsWith('Be')) return null
  const typeName = typePart.slice(2)
  const args = node.arguments as BodyWritebackNode[]
  if (args.length === 0) {
    if (typeName === 'Datetime') return { typeName, value: '0' }
    if (typeName === 'Number') return { typeName, value: '?' }
    return { typeName, value: '' }
  }
  if (
    methodName !== 'fromBeConst' &&
    methodName !== 'fromBase64Const' &&
    methodName !== 'fromNodeConst' &&
    methodName !== 'fromGuidConst' &&
    methodName !== 'fromGuidConstLegacy'
  ) {
    return null
  }
  const first = args[0]
  if (first.kind === 'stringLiteral' || first.kind === 'templateLiteral') {
    return { typeName, value: first.text as string }
  }
  return null
}

function createMappedBeConstCall(typeName: string, arg: BodyWritebackNode): BodyWritebackNode {
  return {
    kind: 'call',
    expression: {
      kind: 'propertyAccess',
      expression: { kind: 'identifier', text: 'Be' + typeName },
      name: { kind: 'identifier', text: 'fromBeConst' },
    },
    arguments: [arg],
    typeArguments: null,
  }
}

function stringLiteralNode(text: string): BodyWritebackNode {
  return { kind: 'stringLiteral', text }
}

function getMappedPropertyPathText(node: BodyWritebackNode): string {
  switch (node.kind) {
    case 'identifier':
      return node.text as string
    case 'thisExpression':
      return 'this'
    case 'propertyAccess':
      return `${getMappedPropertyPathText(node.expression as BodyWritebackNode)}.${getMappedPropertyPathText(node.name as BodyWritebackNode)}`
    case 'parenthesizedExpression':
      return getMappedPropertyPathText(node.expression as BodyWritebackNode)
    case 'asExpression':
      return getMappedPropertyPathText(node.expression as BodyWritebackNode)
    case 'typeReference':
      return getMappedPropertyPathText(node.typeName as BodyWritebackNode)
    default:
      return ''
  }
}

function mapTypeNode(sf: ts.SourceFile, node: ts.TypeNode): BodyWritebackNode {
  if (ts.isTypeReferenceNode(node)) {
    return {
      kind: 'typeReference',
      typeName: mapEntityName(node.typeName),
      typeArguments: node.typeArguments?.map((t: ts.TypeNode) => mapTypeNode(sf, t)) ?? null,
    }
  }
  if (ts.isArrayTypeNode(node))
    return {
      kind: 'arrayType',
      elementType: mapTypeNode(sf, node.elementType),
    }
  if (ts.isTupleTypeNode(node))
    return {
      kind: 'tupleType',
      elements: node.elements.map((e: ts.TypeNode) => mapTypeNode(sf, e)),
    }
  if (ts.isUnionTypeNode(node))
    return {
      kind: 'unionType',
      types: node.types.map((t: ts.TypeNode) => mapTypeNode(sf, t)),
    }
  if (node.kind === ts.SyntaxKind.VoidKeyword) return { kind: 'voidType' }
  if (ts.isLiteralTypeNode(node) && ts.isStringLiteral(node.literal))
    return { kind: 'stringLiteral', text: node.literal.text }
  return { kind: 'identifier', text: node.getText(sf) }
}

function mapEntityName(name: ts.EntityName): BodyWritebackNode {
  if (ts.isIdentifier(name)) return { kind: 'identifier', text: name.text }
  return {
    kind: 'propertyAccess',
    expression: mapEntityName(name.left),
    name: { kind: 'identifier', text: name.right.text },
  }
}

// ============================================================
// Device 主视图生成：业务 DTO -> 官方 TypeScript AST -> 文本
// ============================================================

const deviceFactory = ts.factory as unknown as Record<string, (...args: unknown[]) => unknown>

let currentDeviceViewBuildContext = ''

// ============================================================
// Device 视图增量序列化：方法级片段缓存 + 骨架装配
// 骨架与片段同源（同一 printer + 同一后处理管线），格式天然一致。
// ============================================================

const deviceFragmentMarkerPrefix = '__TSVIEW_FRAGMENT_'
// 片段缓存按时间失效：5 分钟后视为 miss，兜底生成器代码变更与长生命周期引擎的陈旧。
const DEVICE_FRAGMENT_TTL_MS = 5 * 60 * 1000

interface DeviceViewFragmentInfo {
  text: string
  bodyLines?: BodyLineJson[]
  createdAtMs: number
}

const deviceViewFragmentCache = new Map<string, DeviceViewFragmentInfo>()

// 缓存键 = 零件id + 方法名 + 内容指纹：身份定位、内容指纹钉内容变更（同方法改体→指纹变→miss 重算）。
// isBirthBuilding 影响方法 this 类型，随内容指纹一并计入。
function deviceFragmentCacheKey(input: DeviceViewInput, memberName: string, contentFingerprint: string): string {
  return `${input.deviceClassName}|${memberName}|${contentFingerprint}`
}

// 内容指纹：FNV-1a 32 位确定性哈希 → base36。仅用于缓存键，非安全用途。
function deviceContentFingerprint(...parts: string[]): string {
  let h = 0x811c9dc5
  for (const part of parts) {
    for (let i = 0; i < part.length; i++) {
      h ^= part.charCodeAt(i)
      h = Math.imul(h, 0x01000193)
    }
  }
  return (h >>> 0).toString(36)
}

function deviceFragmentKey(kind: string, index: number, groupIndex?: number): string {
  return groupIndex === undefined ? `${kind}_${index}` : `${kind}_${groupIndex}_${index}`
}

function createDeviceFragmentMarker(key: string): ts.ClassElement {
  return ts.addSyntheticLeadingComment(
    createNotEmitted(),
    ts.SyntaxKind.SingleLineCommentTrivia,
    deviceFragmentMarkerPrefix + key + '__',
    true,
  ) as unknown as ts.ClassElement
}

function createDeviceFragmentMarkerProperty(key: string): ts.ObjectLiteralElementLike {
  return f<ts.ObjectLiteralElementLike>(
    'createPropertyAssignment',
    deviceFragmentMarkerPrefix + key + '__',
    ts.factory.createNull(),
  )
}

function createDeviceEventMemberProperty(
  input: DeviceViewInput,
  script: DeviceViewScriptMethodInfo,
  eventParams: DeviceViewEventParamInfo[],
): ts.ObjectLiteralElementLike {
  let property = f<ts.ObjectLiteralElementLike>(
    'createPropertyAssignment',
    script.methodName,
    f<ts.Expression>('createCallExpression', createDeviceBeScriptCall(script), undefined, [
      createDeviceScriptFunctionExpression(input, script, true, eventParams),
    ]),
  )
  property = ts.addSyntheticLeadingComment(
    property,
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildJsDocText(createDeviceScriptDocLines(script)),
    true,
  )
  return property
}

function printDeviceMemberFragment(node: ts.Node): string {
  const fragmentSourceFile = ts.createSourceFile('fragment.ts', '', ts.ScriptTarget.Latest, false, ts.ScriptKind.TS)
  const printer = ts.createPrinter({ newLine: ts.NewLineKind.LineFeed, removeComments: false })
  const text = printer.printNode(ts.EmitHint.Unspecified, node, fragmentSourceFile)
  return decodeReadableUnicodeEscapesInStringLiterals(
    removeBlankLinePlaceholders(convertPrinterIndentToTwoSpaces(stripFinalLineBreak(text))),
  )
}

function getOrBuildDeviceFragment(
  cacheKey: string,
  build: () => Omit<DeviceViewFragmentInfo, 'createdAtMs'>,
): DeviceViewFragmentInfo {
  const cached = deviceViewFragmentCache.get(cacheKey)
  if (cached !== undefined && Date.now() - cached.createdAtMs < DEVICE_FRAGMENT_TTL_MS) return cached
  const info: DeviceViewFragmentInfo = { ...build(), createdAtMs: Date.now() }
  deviceViewFragmentCache.set(cacheKey, info)
  return info
}

// 方法体行提取：独立打印 body Block，去掉首行 `{` 和末行 `}`
function extractDeviceMethodBodyLines(script: DeviceViewScriptMethodInfo): BodyLineJson[] {
  const block = createDeviceViewBlock(script.body)
  const lines = printDeviceMemberFragment(block).split('\n')
  const result: BodyLineJson[] = []
  for (let i = 1; i < lines.length - 1; i++) {
    result.push({ lineNumber: i, lineText: lines[i] })
  }
  return result
}

function createDeviceClassMembersWithMarkers(input: DeviceViewInput): ts.ClassElement[] {
  const members: ts.ClassElement[] = [
    createDeviceNotEmittedClassElement(),
    createDeviceSingleLineComment(' [Globals] BEGIN') as unknown as ts.ClassElement,
  ]
  for (let i = 0; i < input.globalVars.length; i++) {
    members.push(createDeviceFragmentMarker(deviceFragmentKey('globalVar', i)))
  }
  members.push(createDeviceSingleLineComment(' [Globals] END') as unknown as ts.ClassElement)
  members.push(createDeviceNotEmittedClassElement())
  members.push(createDeviceSingleLineComment(' [Script] BEGIN') as unknown as ts.ClassElement)
  for (let i = 0; i < input.scriptMethodInfos.length; i++) {
    members.push(createDeviceFragmentMarker(deviceFragmentKey('scriptMethod', i)))
  }
  members.push(createDeviceSingleLineComment(' [Script] END') as unknown as ts.ClassElement)
  if (input.eventMethodInfos.length > 0) {
    members.push(createDeviceNotEmittedClassElement())
    members.push(createDeviceSingleLineComment(' [Events] BEGIN') as unknown as ts.ClassElement)
    members.push(createDeviceSkeletonEventBlock(input))
    members.push(createDeviceSingleLineComment(' [Events] END') as unknown as ts.ClassElement)
  }
  return members
}

function createDeviceSkeletonEventBlock(input: DeviceViewInput): ts.ClassElement {
  const statements: ts.Statement[] = []
  for (let g = 0; g < input.eventMethodInfos.length; g++) {
    const eventInfo = input.eventMethodInfos[g]
    if (g > 0) statements.push(createBlankLinePlaceholder())
    const markerMembers = eventInfo.scripts.map((_, s) =>
      createDeviceFragmentMarkerProperty(deviceFragmentKey('eventMethod', s, g)),
    )
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
          [f<ts.Expression>('createThis'), f<ts.Expression>('createObjectLiteralExpression', markerMembers, true)],
        ),
      ),
    )
  }
  return f<ts.ClassElement>('createClassStaticBlockDeclaration', ts.factory.createBlock(statements, true))
}

function buildDeviceViewIncremental(input: DeviceViewInput): DeviceViewOutput {
  // 1. 构建所有成员片段（带缓存）
  const fragmentInfos = new Map<string, DeviceViewFragmentInfo>()

  for (let i = 0; i < input.globalVars.length; i++) {
    const globalVar = input.globalVars[i]
    const key = deviceFragmentKey('globalVar', i)
    const cacheKey = deviceFragmentCacheKey(
      input,
      globalVar.tsName,
      deviceContentFingerprint(JSON.stringify(globalVar)),
    )
    fragmentInfos.set(
      key,
      getOrBuildDeviceFragment(cacheKey, () => ({
        text: printDeviceMemberFragment(createDeviceGlobalVarProperty(globalVar)),
      })),
    )
  }

  for (let i = 0; i < input.scriptMethodInfos.length; i++) {
    const script = input.scriptMethodInfos[i]
    const key = deviceFragmentKey('scriptMethod', i)
    const cacheKey = deviceFragmentCacheKey(
      input,
      script.methodName,
      deviceContentFingerprint(String(input.isBirthBuilding), JSON.stringify(script)),
    )
    fragmentInfos.set(
      key,
      getOrBuildDeviceFragment(cacheKey, () => ({
        text: printDeviceMemberFragment(createDeviceScriptProperty(input, script, false, [])),
        bodyLines: extractDeviceMethodBodyLines(script),
      })),
    )
  }

  for (let g = 0; g < input.eventMethodInfos.length; g++) {
    const eventInfo = input.eventMethodInfos[g]
    for (let s = 0; s < eventInfo.scripts.length; s++) {
      const script = eventInfo.scripts[s]
      const key = deviceFragmentKey('eventMethod', s, g)
      const cacheKey = deviceFragmentCacheKey(
        input,
        script.methodName,
        deviceContentFingerprint(
          String(input.isBirthBuilding),
          JSON.stringify(script),
          JSON.stringify(eventInfo.eventParams),
        ),
      )
      fragmentInfos.set(
        key,
        getOrBuildDeviceFragment(cacheKey, () => ({
          text: printDeviceMemberFragment(createDeviceEventMemberProperty(input, script, eventInfo.eventParams)),
          bodyLines: extractDeviceMethodBodyLines(script),
        })),
      )
    }
  }

  // 2. 骨架打印（成员位置为片段标记行）
  currentDeviceViewBuildContext = 'createSourceFile'
  const sourceFile = ts.factory.createSourceFile(
    createDeviceViewStatements(input, createDeviceClassMembersWithMarkers(input)),
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  currentDeviceViewBuildContext = `assemble after ${currentDeviceViewBuildContext}`
  const skeletonText = printFileWithTwoSpaceIndent(sourceFile)

  // 3. 标记替换 + 脚本行号累计
  const markerRe = /^(\s*)(?:\/\/)?__TSVIEW_FRAGMENT_([A-Za-z]+(?:_\d+)+)__(?:: null,?)?$/
  const skeletonLines = skeletonText.split('\n')
  const resultLines: string[] = []
  const scripts: DeviceViewScriptIndexJson[] = []

  // fragmentKey → methodName 映射（用于行号索引）
  const methodNameByKey = new Map<string, string>()
  for (let i = 0; i < input.scriptMethodInfos.length; i++) {
    methodNameByKey.set(deviceFragmentKey('scriptMethod', i), input.scriptMethodInfos[i].methodName)
  }
  for (let g = 0; g < input.eventMethodInfos.length; g++) {
    for (let s = 0; s < input.eventMethodInfos[g].scripts.length; s++) {
      methodNameByKey.set(deviceFragmentKey('eventMethod', s, g), input.eventMethodInfos[g].scripts[s].methodName)
    }
  }

  for (const line of skeletonLines) {
    const match = markerRe.exec(line)
    if (match === null) {
      resultLines.push(line)
      continue
    }
    const [, indent, key] = match
    const fragmentInfo = fragmentInfos.get(key)
    if (fragmentInfo === undefined) {
      throw new Error(`Device 视图装配缺少片段: ${key}`)
    }
    // 事件属性在对象字面量内有 trailing comma；独立 printNode 不带逗号，装配时补上
    const hasTrailingComma = line.trimEnd().endsWith(',')
    const fragmentLines = fragmentInfo.text.split('\n')
    if (hasTrailingComma && fragmentLines.length > 0) {
      fragmentLines[fragmentLines.length - 1] += ','
    }
    const startLine = resultLines.length + 1
    for (const fl of fragmentLines) {
      resultLines.push(fl.length > 0 ? indent + fl : '')
    }
    const endLine = resultLines.length

    const methodName = methodNameByKey.get(key)
    if (methodName !== undefined) {
      // bodyLines 来自片段独立打印（相对行号），按片段在装配结果中的落点换算成绝对行号：
      // 片段末行是方法闭合 `}`（事件属性可能带逗号），方法体即闭合括号前的 bodyCount 行。
      const bodyLines = fragmentInfo.bodyLines ?? []
      const bodyStartFragIdx = fragmentLines.length - 1 - bodyLines.length
      scripts.push({
        methodName,
        docStartLine: startLine,
        endLine,
        bodyLines: bodyLines.map((bl, i) => ({
          lineNumber: startLine + bodyStartFragIdx + i,
          lineText: bl.lineText,
        })),
      })
    }
  }

  currentDeviceViewBuildContext = ''
  return { tsView: resultLines.join('\n'), scripts }
}

function createDeviceViewStatements(input: DeviceViewInput, members: ts.ClassElement[]): ts.Statement[] {
  const statements: ts.Statement[] = []
  statements.push(...createDeviceHeaderStatements(input.referencePaths, input.commentLines, input.extraCommentLines))
  statements.push(createBlankLinePlaceholder())
  statements.push(createDeviceViewNamespace(input, members))
  return statements
}

function createDeviceHeaderStatements(
  referencePaths: string[],
  commentLines: string[],
  extraCommentLines: string[],
): ts.Statement[] {
  const statements: ts.Statement[] = []
  for (const referencePath of referencePaths) {
    statements.push(createDeviceTripleSlashReference(referencePath))
  }

  const lines = [...commentLines]
  if (extraCommentLines.length > 0) {
    lines.push(...extraCommentLines)
  }
  if (lines.length > 0) {
    statements.push(createDeviceJsDocStatement(lines))
  }
  return statements
}

function createDeviceViewNamespace(input: DeviceViewInput, members: ts.ClassElement[]): ts.Statement {
  const statements: ts.Statement[] = []

  if (input.isBirthBuilding) {
    statements.push(
      createDeviceSingleLineComment('════════════════════════════════════════════════════════════════════'),
    )
    statements.push(createDeviceSingleLineComment('外部调用入口类（地图内其他零件调用此零件时使用此类型）'))
    statements.push(createDeviceSingleLineComment('此类继承自 DeviceType_1110，仅提供1110类型的固有Act方法'))
    statements.push(createDeviceSingleLineComment('禁止在此类内部添加任何内容'))
    statements.push(
      createDeviceSingleLineComment('════════════════════════════════════════════════════════════════════'),
    )
    statements.push(createDeviceViewClass(input.deviceClassName, 'DeviceType_1110', true, [], []))
    statements.push(createBlankLinePlaceholder())
    statements.push(
      createDeviceSingleLineComment('════════════════════════════════════════════════════════════════════'),
    )
    statements.push(createDeviceSingleLineComment('虚拟核心类（运行时注入目标机械核心的额外逻辑）'))
    statements.push(createDeviceSingleLineComment("此类表示机械产生器作为'虚拟核心'的内部视角"))
    statements.push(createDeviceSingleLineComment('脚本在运行时会合并注入到生成的机械实例的核心零件中'))
    statements.push(
      createDeviceSingleLineComment('════════════════════════════════════════════════════════════════════'),
    )
    statements.push(
      createDeviceViewClass(
        input.deviceClassName + '_VirtualCore',
        input.baseTypeName,
        false,
        input.allowedEvents,
        members,
      ),
    )
  } else {
    statements.push(
      createDeviceViewClass(input.deviceClassName, input.baseTypeName, true, input.allowedEvents, members),
    )
  }

  return ts.factory.createModuleDeclaration(
    undefined,
    ts.factory.createIdentifier(input.outerNamespace),
    ts.factory.createModuleBlock(statements),
    ts.NodeFlags.Namespace,
  )
}

function createDeviceViewClass(
  name: string,
  baseTypeName: string,
  exported: boolean,
  events: string[],
  members: ts.ClassElement[],
): ts.ClassDeclaration {
  const modifiers: ts.ModifierLike[] = []
  if (events.length > 0) modifiers.push(createDeviceEventsDecorator(events))
  if (exported) modifiers.push(ts.factory.createModifier(ts.SyntaxKind.ExportKeyword))
  const classMembers = [createDeviceConcreteBrandMember(), ...members]
  return f<ts.ClassDeclaration>(
    'createClassDeclaration',
    modifiers,
    name,
    undefined,
    [
      ts.factory.createHeritageClause(ts.SyntaxKind.ExtendsKeyword, [
        ts.factory.createExpressionWithTypeArguments(createDeviceTypeExpression(baseTypeName), undefined),
      ]),
    ],
    classMembers,
  )
}

function createDeviceGlobalVarProperty(globalVar: DeviceViewGlobalVarInfo): ts.ClassElement {
  let property = f<ts.ClassElement>(
    'createPropertyDeclaration',
    [
      f(
        'createDecorator',
        f<ts.Expression>('createCallExpression', ts.factory.createIdentifier('bsVar'), undefined, [
          createDeviceObjectLiteral([
            ['name', ts.factory.createStringLiteral(globalVar.originalName)],
            ...(globalVar.comment !== null
              ? [['comment', ts.factory.createStringLiteral(globalVar.comment)] as [string, ts.Expression]]
              : []),
          ]),
        ]),
      ),
      f<ts.Modifier>('createModifier', ts.SyntaxKind.PrivateKeyword),
    ],
    globalVar.tsName,
    undefined,
    createDeviceTypeReference(globalVar.beTypeName),
    f<ts.Expression>(
      'createCallExpression',
      f<ts.Expression>(
        'createPropertyAccessExpression',
        ts.factory.createIdentifier(globalVar.beTypeName),
        'GlobalVarInitialize',
      ),
      undefined,
      [],
    ),
  )
  return property
}

function createDeviceScriptProperty(
  input: DeviceViewInput,
  script: DeviceViewScriptMethodInfo,
  isEvent: boolean,
  eventParams: DeviceViewEventParamInfo[],
): ts.ClassElement {
  let property = f<ts.ClassElement>(
    'createPropertyDeclaration',
    isEvent
      ? undefined
      : [
          f<ts.Modifier>(
            'createModifier',
            script.isPrivate ? ts.SyntaxKind.ProtectedKeyword : ts.SyntaxKind.PublicKeyword,
          ),
        ],
    script.methodName,
    undefined,
    undefined,
    f<ts.Expression>('createCallExpression', createDeviceBeScriptCall(script), undefined, [
      createDeviceScriptFunctionExpression(input, script, isEvent, eventParams),
    ]),
  )

  property = ts.addSyntheticLeadingComment(
    property,
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildJsDocText(createDeviceScriptDocLines(script)),
    true,
  )
  return property
}

function createDeviceScriptFunctionExpression(
  input: DeviceViewInput,
  script: DeviceViewScriptMethodInfo,
  isEvent: boolean,
  eventParams: DeviceViewEventParamInfo[],
): ts.Expression {
  currentDeviceViewBuildContext = `method ${script.methodName}`
  const parameters = isEvent
    ? [
        f<ts.ParameterDeclaration>(
          'createParameterDeclaration',
          undefined,
          undefined,
          'this',
          undefined,
          createDeviceTypeReference(
            input.isBirthBuilding ? input.deviceClassName + '_VirtualCore' : input.deviceClassName,
          ),
          undefined,
        ),
        ...eventParams.map((p) => createDeviceParameterFromNames(p.tsName, p.beTypeName)),
      ]
    : script.parameters.map((p) => createDeviceParameterFromNames(p.name, p.typeName))

  const body = createDeviceViewBlock(script.body)
  return isEvent
    ? f<ts.Expression>(
        'createFunctionExpression',
        script.isAsync ? [f<ts.Modifier>('createModifier', ts.SyntaxKind.AsyncKeyword)] : undefined,
        undefined,
        undefined,
        undefined,
        parameters,
        undefined,
        body,
      )
    : f<ts.Expression>(
        'createArrowFunction',
        script.isAsync ? [f<ts.Modifier>('createModifier', ts.SyntaxKind.AsyncKeyword)] : undefined,
        undefined,
        parameters,
        undefined,
        undefined,
        body,
      )
}

function createDeviceViewBlock(body: DeviceViewScriptBody): ts.Block {
  const statements: ts.Statement[] = []
  let previousSourceEndLine: number | null = null
  for (const statement of body.statements) {
    if (statement.sourceLine !== null) {
      if (previousSourceEndLine !== null) {
        for (const line of body.lines) {
          if (
            line.lineNumber > previousSourceEndLine &&
            line.lineNumber < statement.sourceLine &&
            line.lineText.length === 0
          ) {
            statements.push(createBlankLinePlaceholder())
          }
        }
      }
      previousSourceEndLine = sourceEndLineOfDeviceViewStatement(statement)
    }
    statements.push(...createDeviceViewBodyStatements(statement))
  }
  return ts.factory.createBlock(statements, true)
}

function sourceEndLineOfDeviceViewStatement(statement: DeviceViewStatement): number | null {
  let endLine = statement.sourceLine
  for (const child of childrenOfDeviceViewStatement(statement)) {
    const childEndLine = sourceEndLineOfDeviceViewStatement(child)
    if (childEndLine !== null && (endLine === null || childEndLine > endLine)) {
      endLine = childEndLine
    }
  }
  if ((statement.kind === 'if' || statement.kind === 'while' || statement.kind === 'block') && endLine !== null) {
    endLine++
  }
  return endLine
}

function childrenOfDeviceViewStatement(statement: DeviceViewStatement): DeviceViewStatement[] {
  if (statement.kind === 'block') return statement.statements ?? []
  if (statement.kind === 'if' && statement.thenStatement !== null) return [statement.thenStatement]
  if (statement.kind === 'while' && statement.body !== null) return [statement.body]
  return []
}

function createDeviceViewBodyStatements(node: DeviceViewStatement, emitDisabledComment = true): ts.Statement[] {
  if (emitDisabledComment && node.isDisabled) {
    if (node.kind === 'if') return createDisabledIfViewStatements(node)
    if (node.kind === 'while') return createDisabledWhileViewStatements(node)
    const statement = createDeviceViewStatementCore(node, false)
    return [createDisabledStatementComment(buildDisabledStatementCommentText(statement))]
  }
  return [createDeviceViewStatementCore(node, emitDisabledComment)]
}

function createDeviceViewStatement(node: DeviceViewStatement, emitDisabledComment = true): ts.Statement {
  return createDeviceViewStatementCore(node, emitDisabledComment)
}

function createDeviceViewStatementCore(node: DeviceViewStatement, emitDisabledComment: boolean): ts.Statement {
  currentDeviceViewBuildContext = `statement ${node.kind}`
  switch (node.kind) {
    case 'block': {
      const statements: ts.Statement[] = []
      for (const statement of node.statements ?? []) {
        statements.push(...createDeviceViewBodyStatements(statement, emitDisabledComment))
      }
      return ts.factory.createBlock(statements, true)
    }
    case 'expression':
      return f<ts.Statement>(
        'createExpressionStatement',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
      )
    case 'if':
      return f<ts.Statement>(
        'createIfStatement',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
        createDeviceViewStatement(node.thenStatement as DeviceViewStatement, emitDisabledComment),
        undefined,
      )
    case 'while':
      return f<ts.Statement>(
        'createWhileStatement',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
        createDeviceViewStatement(node.body as DeviceViewStatement, emitDisabledComment),
      )
    case 'return':
      return ts.factory.createReturnStatement(node.expression ? createDeviceViewExpression(node.expression) : undefined)
    case 'break':
      return f<ts.Statement>('createBreakStatement')
    case 'continue':
      return f<ts.Statement>('createContinueStatement')
    case 'singleLineComment':
      if (emitDisabledComment && node.text === '') return createBlankLinePlaceholder()
      return ts.addSyntheticLeadingComment(
        createNotEmitted(),
        ts.SyntaxKind.SingleLineCommentTrivia,
        node.text as string,
        true,
      )
    case 'multiLineComment':
      return ts.addSyntheticLeadingComment(
        createNotEmitted(),
        ts.SyntaxKind.MultiLineCommentTrivia,
        ' ' + (node.text as string) + ' ',
        true,
      )
    case 'variable':
      return createDeviceViewVariableStatement(node.declarationList as DeviceViewVariableDeclarationList)
    default:
      throw new Error(`不支持的 Device 视图语句: ${node.kind}`)
  }
}

function buildDisabledStatementCommentText(statement: ts.Statement): string {
  const sourceFile = ts.factory.createSourceFile(
    [statement],
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  return printFileWithTwoSpaceIndent(sourceFile)
}

function createDisabledIfViewStatements(node: DeviceViewStatement): ts.Statement[] {
  const statements = [
    createDisabledStatementComment(`if (${buildDeviceViewExpressionText(node.expression as DeviceViewExpression)}) {`),
  ]
  const thenStatement = node.thenStatement as DeviceViewStatement
  if (thenStatement.kind === 'block') {
    for (const statement of thenStatement.statements ?? []) {
      statements.push(...createDeviceViewBodyStatements(statement))
    }
  } else {
    statements.push(...createDeviceViewBodyStatements(thenStatement))
  }
  statements.push(createDisabledStatementComment('}'))
  return statements
}

function createDisabledWhileViewStatements(node: DeviceViewStatement): ts.Statement[] {
  const statements = [
    createDisabledStatementComment(
      `while (${buildDeviceViewExpressionText(node.expression as DeviceViewExpression)}) {`,
    ),
  ]
  const body = node.body as DeviceViewStatement
  if (body.kind === 'block') {
    for (const statement of body.statements ?? []) {
      statements.push(...createDeviceViewBodyStatements(statement))
    }
  } else {
    statements.push(...createDeviceViewBodyStatements(body))
  }
  statements.push(createDisabledStatementComment('}'))
  return statements
}

function createDisabledStatementComment(text: string): ts.Statement {
  return ts.addSyntheticLeadingComment(createNotEmitted(), ts.SyntaxKind.MultiLineCommentTrivia, ' ' + text + ' ', true)
}

function buildDeviceViewExpressionText(expression: DeviceViewExpression): string {
  const sourceFile = ts.factory.createSourceFile(
    [f<ts.Statement>('createExpressionStatement', createDeviceViewExpression(expression))],
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  const statementText = printFileWithTwoSpaceIndent(sourceFile)
  return statementText.endsWith(';') ? statementText.slice(0, -1) : statementText
}

function createDeviceViewVariableStatement(list: DeviceViewVariableDeclarationList): ts.Statement {
  return ts.factory.createVariableStatement(
    undefined,
    ts.factory.createVariableDeclarationList(
      list.declarations.map(createDeviceViewVariableDeclaration),
      createDeviceNodeFlags(list.variableKind),
    ),
  )
}

function createDeviceViewVariableDeclaration(node: DeviceViewVariableDeclaration): ts.VariableDeclaration {
  return f<ts.VariableDeclaration>(
    'createVariableDeclaration',
    node.name,
    undefined,
    node.typeName ? createDeviceTypeReference(node.typeName) : undefined,
    node.initializer ? createDeviceViewExpression(node.initializer) : undefined,
  )
}

function createDeviceViewExpression(node: DeviceViewExpression): ts.Expression {
  currentDeviceViewBuildContext = `expression ${node.kind} ${node.text ?? node.name ?? ''}`
  switch (node.kind) {
    case 'identifier':
      return ts.factory.createIdentifier(node.text as string)
    case 'string':
      return ts.factory.createStringLiteral(node.text as string)
    case 'number':
      return f<ts.Expression>('createNumericLiteral', node.text as string)
    case 'template':
      return f<ts.Expression>('createNoSubstitutionTemplateLiteral', node.text as string)
    case 'true':
      return f<ts.Expression>('createTrue')
    case 'false':
      return f<ts.Expression>('createFalse')
    case 'this':
      return f<ts.Expression>('createThis')
    case 'propertyAccess':
      return f<ts.Expression>(
        'createPropertyAccessExpression',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
        node.name as string,
      )
    case 'call':
      return f<ts.Expression>(
        'createCallExpression',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
        node.typeArguments ? node.typeArguments.map(createDeviceTypeReference) : undefined,
        (node.arguments ?? []).map(createDeviceViewExpression),
      )
    case 'array':
      return f<ts.Expression>(
        'createArrayLiteralExpression',
        (node.elements ?? []).map(createDeviceViewExpression),
        false,
      )
    case 'object':
      return f<ts.Expression>(
        'createObjectLiteralExpression',
        (node.properties ?? []).map(createDeviceViewObjectProperty),
        node.isMultiLine,
      )
    case 'await':
      return f<ts.Expression>(
        'createAwaitExpression',
        createDeviceViewExpression(node.argument as DeviceViewExpression),
      )
    case 'void':
      return f<ts.Expression>('createVoidExpression', createDeviceViewExpression(node.argument as DeviceViewExpression))
    case 'binary':
      return f<ts.Expression>(
        'createBinaryExpression',
        createDeviceViewExpression(node.left as DeviceViewExpression),
        createDeviceBinaryOperator(node.operator as string),
        createDeviceViewExpression(node.right as DeviceViewExpression),
      )
    case 'as':
      return f<ts.Expression>(
        'createAsExpression',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
        createDeviceTypeReference(node.typeName as string),
      )
    case 'parenthesized':
      return f<ts.Expression>(
        'createParenthesizedExpression',
        createDeviceViewExpression(node.expression as DeviceViewExpression),
      )
    default:
      throw new Error(`不支持的 Device 视图表达式: ${node.kind}`)
  }
}

function createDeviceViewObjectProperty(node: DeviceViewObjectProperty): ts.ObjectLiteralElementLike {
  return f<ts.ObjectLiteralElementLike>('createPropertyAssignment', node.name, createDeviceViewExpression(node.value))
}

function createDeviceBeScriptCall(script: DeviceViewScriptMethodInfo): ts.Expression {
  const props: ts.ObjectLiteralElementLike[] = [
    f<ts.ObjectLiteralElementLike>(
      'createPropertyAssignment',
      'name',
      ts.factory.createStringLiteral(script.originalName),
    ),
  ]
  if (script.isCodeRepo) {
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'repo',
        ts.factory.createIdentifier('guid_' + script.guid),
      ),
    )
  }
  if (script.header.color !== null) {
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'color',
        f<ts.Expression>('createArrayLiteralExpression', [
          f<ts.Expression>('createNumericLiteral', String(script.header.color.r)),
          f<ts.Expression>('createNumericLiteral', String(script.header.color.g)),
          f<ts.Expression>('createNumericLiteral', String(script.header.color.b)),
        ]),
      ),
    )
  }
  if (script.header.debug)
    props.push(f<ts.ObjectLiteralElementLike>('createPropertyAssignment', 'debug', f<ts.Expression>('createTrue')))
  if (script.header.skip)
    props.push(f<ts.ObjectLiteralElementLike>('createPropertyAssignment', 'skip', f<ts.Expression>('createTrue')))
  if (script.header.private)
    props.push(f<ts.ObjectLiteralElementLike>('createPropertyAssignment', 'private', f<ts.Expression>('createTrue')))
  if (script.header.comment !== null)
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'comment',
        ts.factory.createStringLiteral(script.header.comment),
      ),
    )
  if (script.header.returnParameter !== null) {
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'retVar',
        ts.factory.createStringLiteral(script.header.returnParameter.name),
      ),
    )
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'retVarType',
        ts.factory.createStringLiteral(script.header.returnParameter.typeName),
      ),
    )
  }
  if (script.rawParameters !== null && script.rawParameters.length > 0) {
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'rawParams',
        ts.factory.createStringLiteral(script.rawParameters.map((p) => `${p.name}: ${p.typeName}`).join(', ')),
      ),
    )
  }
  if (script.rawRetVar !== null) {
    props.push(
      f<ts.ObjectLiteralElementLike>(
        'createPropertyAssignment',
        'rawRetVar',
        ts.factory.createStringLiteral(script.rawRetVar),
      ),
    )
  }
  return f<ts.Expression>(
    'createCallExpression',
    ts.factory.createIdentifier(script.isAsync ? 'BeScriptAsync' : 'BeScript'),
    undefined,
    [f<ts.Expression>('createObjectLiteralExpression', props, false)],
  )
}

function createDeviceScriptDocLines(script: DeviceViewScriptMethodInfo): string[] {
  const lines = [`name: ${script.originalName}`]
  if (script.sourcePath !== null) {
    lines.push(`sourcePath: ${script.sourcePath}`)
    lines.push(`potentialPreviewPath: temp/toolkit/views/${script.sourcePath.slice(0, -5)}_inline_{N}.png`)
  } else if (script.isCodeRepo) {
    lines.push(`guid: ${script.guid}`)
  }
  return lines
}

function createDeviceConcreteBrandMember(): ts.ClassElement {
  return f<ts.ClassElement>(
    'createPropertyDeclaration',
    [
      f<ts.Modifier>('createModifier', ts.SyntaxKind.PublicKeyword),
      ts.factory.createModifier(ts.SyntaxKind.DeclareKeyword),
      f<ts.Modifier>('createModifier', ts.SyntaxKind.ReadonlyKeyword),
    ],
    '__concreteBrand',
    undefined,
    ts.factory.createLiteralTypeNode(ts.factory.createTrue()),
    undefined,
  )
}

function createDeviceEventsDecorator(events: string[]): ts.Decorator {
  return f<ts.Decorator>(
    'createDecorator',
    f<ts.Expression>(
      'createCallExpression',
      ts.factory.createIdentifier('events'),
      undefined,
      events.map((e) => ts.factory.createStringLiteral(e)),
    ),
  )
}

function createDeviceParameterFromNames(name: string, typeName: string): ts.ParameterDeclaration {
  return f<ts.ParameterDeclaration>(
    'createParameterDeclaration',
    undefined,
    undefined,
    name,
    undefined,
    createDeviceTypeReference(typeName),
    undefined,
  )
}

function createDeviceTypeReference(typeName: string): ts.TypeNode {
  return ts.factory.createTypeReferenceNode(createDeviceEntityNameFromText(typeName), undefined)
}

function createDeviceTypeExpression(typeName: string): ts.Expression {
  const segments = typeName.split('.')
  let expression: ts.Expression = ts.factory.createIdentifier(segments[0])
  for (let i = 1; i < segments.length; i++) {
    expression = f<ts.Expression>('createPropertyAccessExpression', expression, segments[i])
  }
  return expression
}

function createDeviceEntityNameFromText(typeName: string): ts.EntityName {
  const segments = typeName.split('.')
  let name: ts.EntityName = ts.factory.createIdentifier(segments[0])
  for (let i = 1; i < segments.length; i++) {
    name = f<ts.EntityName>('createQualifiedName', name, segments[i])
  }
  return name
}

function createDeviceObjectLiteral(properties: [string, ts.Expression][]): ts.ObjectLiteralExpression {
  return f<ts.ObjectLiteralExpression>(
    'createObjectLiteralExpression',
    properties.map(([name, value]) => f<ts.ObjectLiteralElementLike>('createPropertyAssignment', name, value)),
    false,
  )
}

function createDeviceSingleLineComment(text: string): ts.Statement {
  return ts.addSyntheticLeadingComment(createNotEmitted(), ts.SyntaxKind.SingleLineCommentTrivia, text, true)
}

function createDeviceTripleSlashReference(path: string): ts.Statement {
  return ts.addSyntheticLeadingComment(
    createNotEmitted(),
    ts.SyntaxKind.SingleLineCommentTrivia,
    `/ <reference path="${path}" />`,
    true,
  )
}

function createDeviceJsDocStatement(lines: string[]): ts.Statement {
  return ts.addSyntheticLeadingComment(
    createNotEmitted(),
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildJsDocText(lines),
    true,
  )
}

function createDeviceNotEmittedClassElement(): ts.ClassElement {
  return createBlankLinePlaceholder() as unknown as ts.ClassElement
}

function createDeviceNodeFlags(kind: string): ts.NodeFlags {
  if (kind === 'let') return ts.NodeFlags.Let
  if (kind === 'const') return ts.NodeFlags.Const
  return ts.NodeFlags.None
}

function createDeviceBinaryOperator(operator: string): unknown {
  const kind = (
    {
      equals: ts.SyntaxKind.EqualsToken,
      plusEquals: ts.SyntaxKind.PlusEqualsToken,
      minusEquals: ts.SyntaxKind.MinusEqualsToken,
      equalsEquals: ts.SyntaxKind.EqualsEqualsToken,
      equalsEqualsEquals: ts.SyntaxKind.EqualsEqualsEqualsToken,
      exclamationEquals: ts.SyntaxKind.ExclamationEqualsToken,
      exclamationEqualsEquals: ts.SyntaxKind.ExclamationEqualsEqualsToken,
      lessThan: ts.SyntaxKind.LessThanToken,
      lessThanEquals: ts.SyntaxKind.LessThanEqualsToken,
      greaterThan: ts.SyntaxKind.GreaterThanToken,
      greaterThanEquals: ts.SyntaxKind.GreaterThanEqualsToken,
      plus: ts.SyntaxKind.PlusToken,
      minus: ts.SyntaxKind.MinusToken,
      asterisk: ts.SyntaxKind.AsteriskToken,
      slash: ts.SyntaxKind.SlashToken,
      percent: ts.SyntaxKind.PercentToken,
      ampersandAmpersand: ts.SyntaxKind.AmpersandAmpersandToken,
      barBar: ts.SyntaxKind.BarBarToken,
    } as Record<string, number>
  )[operator]
  return f('createToken', kind)
}

function createNotEmitted(): ts.Statement {
  return f<ts.Statement>('createNotEmittedStatement', undefined)
}

function preserveBlankLinesBetweenStatements(
  statements: readonly ts.Statement[],
  lines: readonly BodyLineJson[],
  getStartLine: (statement: ts.Statement) => number | null,
  getEndLine: (statement: ts.Statement) => number | null,
): ts.Statement[] {
  const result: ts.Statement[] = []
  let previousEndLine = 0
  for (const statement of statements) {
    const startLine = getStartLine(statement)
    if (startLine !== null) {
      for (const line of lines) {
        if (line.lineNumber > previousEndLine && line.lineNumber < startLine && line.lineText.length === 0) {
          result.push(createBlankLinePlaceholder())
        }
      }
    }

    result.push(statement)
    const endLine = getEndLine(statement)
    if (endLine !== null) {
      previousEndLine = endLine
    }
  }
  return result
}

function sourceLineOf(sourceFile: ts.SourceFile, node: ts.Node): number {
  return sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line
}

function sourceEndLineOf(sourceFile: ts.SourceFile, node: ts.Node): number {
  return sourceFile.getLineAndCharacterOfPosition(node.getEnd()).line
}

function createBlankLinePlaceholder(): ts.Statement {
  return ts.addSyntheticLeadingComment(createNotEmitted(), ts.SyntaxKind.SingleLineCommentTrivia, blankLineMarker, true)
}

function f<T>(name: string, ...args: unknown[]): T {
  const fn = deviceFactory[name]
  if (!fn) throw new Error(`当前 TypeScript API 缺少 ${name}`)
  return fn(...args) as T
}

function mapBinaryOperator(kind: ts.SyntaxKind): string {
  switch (kind) {
    case ts.SyntaxKind.EqualsToken:
      return 'equals'
    case ts.SyntaxKind.EqualsEqualsToken:
      return 'equalsEquals'
    case ts.SyntaxKind.EqualsEqualsEqualsToken:
      return 'equalsEqualsEquals'
    case ts.SyntaxKind.ExclamationEqualsToken:
      return 'exclamationEquals'
    case ts.SyntaxKind.ExclamationEqualsEqualsToken:
      return 'exclamationEqualsEquals'
    case ts.SyntaxKind.LessThanToken:
      return 'lessThan'
    case ts.SyntaxKind.LessThanEqualsToken:
      return 'lessThanEquals'
    case ts.SyntaxKind.GreaterThanToken:
      return 'greaterThan'
    case ts.SyntaxKind.GreaterThanEqualsToken:
      return 'greaterThanEquals'
    case ts.SyntaxKind.PlusToken:
      return 'plus'
    case ts.SyntaxKind.MinusToken:
      return 'minus'
    case ts.SyntaxKind.AsteriskToken:
      return 'asterisk'
    case ts.SyntaxKind.SlashToken:
      return 'slash'
    case ts.SyntaxKind.PercentToken:
      return 'percent'
    case ts.SyntaxKind.AmpersandAmpersandToken:
      return 'ampersandAmpersand'
    case ts.SyntaxKind.BarBarToken:
      return 'barBar'
    default:
      throw new Error(`不支持的二元运算符: ${ts.SyntaxKind[kind]}`)
  }
}

function withSourceLine<T extends Record<string, unknown>>(node: T, lineNumber: number): T {
  return { ...node, sourceLine: lineNumber } as T
}

// ============================================================
// CodeRepo 视图生成：业务 DTO -> 官方 TypeScript AST -> 文本
// ============================================================

const codeRepoHeaderReferences = ['./sdk.ts', './custom-logic.d.ts']

const codeRepoHeaderLines = [
  'CodeRepo.ts（临时视图）',
  '- 每个方法对应工程中非地图/机械目录下的一个 .code 脚本文件',
  '- 若脚本包含内联 canvas，则其预览图按源码出现顺序命名为 temp/toolkit/views/.../{脚本名}_inline_{N}.png',
  '- 方法体内为原版 BeScript 命令行（以 `//|` 记录），不做 TS 语义转换',
  '- 写回时仅替换源文件的命令区块（lines: ... endcode 之间）',
  '- 此文件内容固定，如需修改需要使用 MCP 提供的 refactor 工具',
]

function buildCodeRepoViewImpl(input: CodeRepoViewInput): CodeRepoViewOutput {
  const scriptsInEmitOrder: CodeRepoViewScript[] = []
  const rootStatements = buildCodeRepoDirStatements(input, input.root, [], scriptsInEmitOrder)
  let rootNamespace = createCodeRepoNamespace('CodeRepo', rootStatements, false)
  rootNamespace = addCodeRepoHeader(rootNamespace)

  const statements: ts.Statement[] = [rootNamespace]
  for (const script of scriptsInEmitOrder) {
    const guidNoHyphens = script.guid.replaceAll('-', '')
    statements.push(createCodeRepoGuidDeclaration(guidNoHyphens))
  }

  const sourceFile = ts.factory.createSourceFile(
    statements,
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  const tsView = printFileWithTwoSpaceIndent(sourceFile)
  const scripts = buildCodeRepoScriptIndexes(tsView, scriptsInEmitOrder)
  return { tsView, scripts }
}

function buildCodeRepoDirStatements(
  input: CodeRepoViewInput,
  dir: CodeRepoViewDir,
  nsSegments: string[],
  scriptsInEmitOrder: CodeRepoViewScript[],
): ts.Statement[] {
  const statements: ts.Statement[] = []

  for (const child of dir.directories) {
    const childSegments = nsSegments.concat(child.identifier)
    const childStatements = buildCodeRepoDirStatements(input, child, childSegments, scriptsInEmitOrder)
    statements.push(
      createCodeRepoNamespace(
        child.identifier,
        childStatements,
        !isCodeRepoInternalNamespace(input, nsSegments, child.identifier),
      ),
    )
  }

  for (const script of dir.scripts) {
    scriptsInEmitOrder.push(script)
    statements.push(createCodeRepoFunction(input, script))
  }

  return statements
}

function createCodeRepoNamespace(name: string, statements: ts.Statement[], exported: boolean): ts.ModuleDeclaration {
  const modifiers = exported ? [ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)] : undefined
  return ts.factory.createModuleDeclaration(
    modifiers,
    ts.factory.createIdentifier(name),
    ts.factory.createModuleBlock(statements),
    ts.NodeFlags.Namespace,
  )
}

function createCodeRepoFunction(input: CodeRepoViewInput, script: CodeRepoViewScript): ts.FunctionDeclaration {
  const bodyStatements: ts.Statement[] = []
  let returnStatement = ts.factory.createReturnStatement()
  for (const command of script.commands) {
    returnStatement = ts.addSyntheticLeadingComment(
      returnStatement,
      ts.SyntaxKind.SingleLineCommentTrivia,
      '|' + command,
      true,
    )
  }
  bodyStatements.push(returnStatement)

  let fn = ts.factory.createFunctionDeclaration(
    [ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)],
    undefined,
    script.identifier,
    undefined,
    [],
    ts.factory.createKeywordTypeNode(ts.SyntaxKind.VoidKeyword),
    ts.factory.createBlock(bodyStatements, true),
  )

  fn = ts.addSyntheticLeadingComment(
    fn,
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildCodeRepoFunctionDoc(input, script),
    true,
  )
  return fn
}

function buildCodeRepoFunctionDoc(input: CodeRepoViewInput, script: CodeRepoViewScript): string {
  const sourceWithoutCodeExt = script.sourcePath.substring(0, script.sourcePath.length - input.codeExtension.length)
  return buildJsDocText([
    `guid: ${script.guid}`,
    `sourcePath: ${script.sourcePath}`,
    `potentialPreviewPath: ${input.viewsPath}/${sourceWithoutCodeExt}_inline_{N}.png`,
    '@remarks 如需修改签名，请找到任意零件中对此方法的链接处进行修改。',
  ])
}

function createCodeRepoGuidDeclaration(guidNoHyphens: string): ts.VariableStatement {
  return ts.factory.createVariableStatement(
    [ts.factory.createModifier(ts.SyntaxKind.DeclareKeyword)],
    ts.factory.createVariableDeclarationList(
      [
        ts.factory.createVariableDeclaration(
          ts.factory.createIdentifier('guid_' + guidNoHyphens),
          undefined,
          ts.factory.createTypeReferenceNode('BrandedRepo', [
            ts.factory.createLiteralTypeNode(ts.factory.createStringLiteral(guidNoHyphens)),
          ]),
          undefined,
        ),
      ],
      ts.NodeFlags.Const,
    ),
  )
}

function addCodeRepoHeader(statement: ts.ModuleDeclaration): ts.ModuleDeclaration {
  let result = statement
  for (const reference of codeRepoHeaderReferences) {
    result = ts.addSyntheticLeadingComment(
      result,
      ts.SyntaxKind.SingleLineCommentTrivia,
      `/ <reference path="${reference}" />`,
      true,
    )
  }
  result = ts.addSyntheticLeadingComment(
    result,
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildJsDocText(codeRepoHeaderLines),
    true,
  )
  return result
}

function buildJsDocText(lines: string[]): string {
  let text = '*\n'
  for (const line of lines) {
    text += ' * ' + line + '\n'
  }
  text += ' '
  return text
}

function isCodeRepoInternalNamespace(input: CodeRepoViewInput, nsSegments: string[], currentName: string): boolean {
  if (nsSegments.length === 0) {
    return currentName === input.editorNamespace
  }

  return nsSegments[0] === input.editorNamespace
}

function buildCodeRepoScriptIndexes(
  tsView: string,
  scriptsInEmitOrder: CodeRepoViewScript[],
): CodeRepoViewScriptIndex[] {
  const lines = tsView.split('\n')
  const result: CodeRepoViewScriptIndex[] = []
  let cursor = 0
  for (const script of scriptsInEmitOrder) {
    const signature = `export function ${script.identifier}(): void {`
    let startLine = -1
    for (let i = cursor; i < lines.length; i++) {
      if (lines[i].trim() === signature) {
        startLine = i + 1
        cursor = i + 1
        break
      }
    }
    if (startLine < 0) {
      throw new Error(`CodeRepo 视图生成后未找到函数: ${script.symbol}`)
    }

    let endLine = -1
    for (let i = cursor; i < lines.length; i++) {
      if (lines[i].trim() === '}') {
        endLine = i + 1
        cursor = i + 1
        break
      }
    }
    if (endLine < 0) {
      throw new Error(`CodeRepo 视图生成后未找到函数结束: ${script.symbol}`)
    }

    result.push({
      guid: script.guid,
      sourceRelativePath: script.sourcePath,
      symbol: script.symbol,
      startLine,
      endLine,
    })
  }
  return result
}

function stripFinalLineBreak(text: string): string {
  if (text.endsWith('\r\n')) return text.slice(0, -2)
  if (text.endsWith('\n')) return text.slice(0, -1)
  return text
}

function printFileWithTwoSpaceIndent(sourceFile: ts.SourceFile): string {
  const printer = ts.createPrinter({
    newLine: ts.NewLineKind.LineFeed,
    removeComments: false,
  })
  return decodeReadableUnicodeEscapesInStringLiterals(
    removeBlankLinePlaceholders(convertPrinterIndentToTwoSpaces(stripFinalLineBreak(printer.printFile(sourceFile)))),
  )
}

function convertPrinterIndentToTwoSpaces(text: string): string {
  const lines = text.split('\n')
  let inTemplateLiteral = false
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    lines[i] = inTemplateLiteral ? line : line.replace(/^(?: {4})+/, (spaces) => '  '.repeat(spaces.length / 4))
    inTemplateLiteral = scanTemplateLiteralState(lines[i], inTemplateLiteral)
  }
  return lines.join('\n')
}

function removeBlankLinePlaceholders(text: string): string {
  return text
    .split('\n')
    .map((line) => (line.trim() === `//${blankLineMarker}` ? '' : line))
    .join('\n')
}

function decodeReadableUnicodeEscapesInStringLiterals(text: string): string {
  let result = ''
  let quote: "'" | '"' | null = null
  let inLineComment = false
  let inBlockComment = false
  let inTemplateLiteral = false

  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    const next = text[i + 1]

    if (inLineComment) {
      result += ch
      if (ch === '\n') inLineComment = false
      continue
    }
    if (inBlockComment) {
      result += ch
      if (ch === '*' && next === '/') {
        result += next
        i++
        inBlockComment = false
      }
      continue
    }
    if (inTemplateLiteral) {
      result += ch
      if (ch === '`' && !isEscapedAt(text, i)) inTemplateLiteral = false
      continue
    }
    if (quote !== null) {
      if (ch === '\\') {
        const decoded = tryDecodeReadableUnicodeEscape(text, i)
        if (decoded !== null) {
          result += decoded.text
          i += decoded.length - 1
        } else {
          result += ch
          if (next !== undefined) {
            result += next
            i++
          }
        }
        continue
      }
      result += ch
      if (ch === quote) quote = null
      continue
    }

    if (ch === '/' && next === '/') {
      result += ch + next
      i++
      inLineComment = true
      continue
    }
    if (ch === '/' && next === '*') {
      result += ch + next
      i++
      inBlockComment = true
      continue
    }
    if (ch === '`') {
      result += ch
      inTemplateLiteral = true
      continue
    }
    if (ch === "'" || ch === '"') {
      result += ch
      quote = ch
      continue
    }
    result += ch
  }

  return result
}

function tryDecodeReadableUnicodeEscape(text: string, index: number): { text: string; length: number } | null {
  if (text[index] !== '\\' || text[index + 1] !== 'u') return null
  if (text[index + 2] === '{') {
    const closeIndex = text.indexOf('}', index + 3)
    if (closeIndex < 0) return null
    const hex = text.slice(index + 3, closeIndex)
    if (!/^[0-9a-fA-F]+$/.test(hex)) return null
    const codePoint = Number.parseInt(hex, 16)
    return decodeReadableCodePoint(codePoint, closeIndex - index + 1)
  }

  const hex = text.slice(index + 2, index + 6)
  if (!/^[0-9a-fA-F]{4}$/.test(hex)) return null
  const codePoint = Number.parseInt(hex, 16)
  return decodeReadableCodePoint(codePoint, 6)
}

function decodeReadableCodePoint(codePoint: number, length: number): { text: string; length: number } | null {
  if (!isReadableEscapedCodePoint(codePoint)) return null
  return { text: String.fromCodePoint(codePoint), length }
}

function isReadableEscapedCodePoint(codePoint: number): boolean {
  if (codePoint < 0x80 || codePoint > 0x10ffff) return false
  if (codePoint >= 0xd800 && codePoint <= 0xdfff) return false
  if (codePoint >= 0x7f && codePoint <= 0x9f) return false
  if (codePoint === 0x2028 || codePoint === 0x2029) return false
  if (codePoint === 0x22 || codePoint === 0x27 || codePoint === 0x5c || codePoint === 0x60) return false
  if (codePoint === 0x201c || codePoint === 0x201d) return false
  return true
}

function scanTemplateLiteralState(line: string, initialState: boolean): boolean {
  let inTemplateLiteral = initialState
  for (let i = 0; i < line.length; i++) {
    if (line[i] === '`' && !isEscapedAt(line, i)) {
      inTemplateLiteral = !inTemplateLiteral
    }
  }
  return inTemplateLiteral
}

function isEscapedAt(line: string, index: number): boolean {
  let slashCount = 0
  for (let i = index - 1; i >= 0 && line[i] === '\\'; i--) {
    slashCount++
  }
  return slashCount % 2 === 1
}

// ============================================================
// custom-logic.d.ts 生成：业务 DTO -> 官方 TypeScript AST -> 文本
// ============================================================

const customLogicHeaderReferences = ['./sdk.ts']

const customLogicHeaderLines = [
  '全工程自定义逻辑声明（只读索引，禁止手动修改）',
  '此文件呈现的是经 BeScript/BeScriptAsync 转换后的最终对外签名：',
  '  同步无返回值 → void',
  '  同步有返回值 → T',
  '  异步无返回值 → FireAndForget（调用方无需 await）',
  '  异步有返回值 → Promise<T>（调用方必须 await）',
  '  只有异步有返回值才会阻塞调用方(即与调用方是否接收无关)',
]

function buildCustomLogicDtsImpl(input: CustomLogicDtsInput): CustomLogicDtsOutput {
  const statements: ts.Statement[] = input.namespaces.map(createCustomLogicNamespace)
  statements[0] = addCustomLogicHeader(statements[0])

  const sourceFile = ts.factory.createSourceFile(
    statements,
    ts.factory.createToken(ts.SyntaxKind.EndOfFileToken),
    ts.NodeFlags.None,
  )
  return { dtsText: printFileWithTwoSpaceIndent(sourceFile) }
}

function createCustomLogicNamespace(ns: CustomLogicDtsNamespace): ts.ModuleDeclaration {
  const devices = ns.devices.map(createCustomLogicInterface)
  return ts.factory.createModuleDeclaration(
    [ts.factory.createModifier(ts.SyntaxKind.DeclareKeyword)],
    ts.factory.createIdentifier(ns.name),
    ts.factory.createModuleBlock(devices),
    ts.NodeFlags.Namespace,
  )
}

function createCustomLogicInterface(device: CustomLogicDtsDevice): ts.InterfaceDeclaration {
  // 零件实例 interface 的 extends 子句：先 DeviceActions<OwnerType> 基类，
  // 再按 capabilities 顺序追加 I{Capability} 接口（如 ICollidable）。
  // TS 类型层按零件实例能力限定可用方法。
  const extendsTargets: ts.ExpressionWithTypeArguments[] = [
    ts.factory.createExpressionWithTypeArguments(ts.factory.createIdentifier(device.baseTypeName), undefined),
  ]
  for (const cap of device.capabilities) {
    extendsTargets.push(ts.factory.createExpressionWithTypeArguments(ts.factory.createIdentifier('I' + cap), undefined))
  }
  return ts.factory.createInterfaceDeclaration(
    [ts.factory.createModifier(ts.SyntaxKind.ExportKeyword)],
    device.name,
    undefined,
    [ts.factory.createHeritageClause(ts.SyntaxKind.ExtendsKeyword, extendsTargets)],
    device.methods.map(createCustomLogicMethod),
  )
}

function createCustomLogicMethod(method: CustomLogicDtsMethod): ts.MethodSignature {
  return ts.factory.createMethodSignature(
    undefined,
    method.name,
    undefined,
    undefined,
    method.parameters.map(createCustomLogicParameter),
    createCustomLogicReturnType(method),
  )
}

function createCustomLogicParameter(parameter: CustomLogicDtsParameter): ts.ParameterDeclaration {
  return ts.factory.createParameterDeclaration(
    undefined,
    undefined,
    parameter.name,
    undefined,
    ts.factory.createUnionTypeNode([
      createCustomLogicTypeReference(parameter.typeName),
      ts.factory.createTypeReferenceNode('Const', [createCustomLogicTypeReference(parameter.typeName)]),
    ]),
    undefined,
  )
}

function createCustomLogicReturnType(method: CustomLogicDtsMethod): ts.TypeNode {
  if (method.isAsync) {
    if (method.hasReturn) {
      return ts.factory.createTypeReferenceNode('Promise', [createCustomLogicTypeReference(method.returnTypeName)])
    }

    return ts.factory.createTypeReferenceNode('FireAndForget', undefined)
  }

  return createCustomLogicTypeReference(method.returnTypeName)
}

function createCustomLogicTypeReference(typeName: string): ts.TypeNode {
  if (typeName === 'void') {
    return ts.factory.createKeywordTypeNode(ts.SyntaxKind.VoidKeyword)
  }

  return ts.factory.createTypeReferenceNode(typeName, undefined)
}

function addCustomLogicHeader(statement: ts.Statement): ts.Statement {
  let result = statement
  for (const reference of customLogicHeaderReferences) {
    result = ts.addSyntheticLeadingComment(
      result,
      ts.SyntaxKind.SingleLineCommentTrivia,
      `/ <reference path="${reference}" />`,
      true,
    )
  }
  result = ts.addSyntheticLeadingComment(
    result,
    ts.SyntaxKind.MultiLineCommentTrivia,
    buildJsDocText(customLogicHeaderLines),
    true,
  )
  return result
}
