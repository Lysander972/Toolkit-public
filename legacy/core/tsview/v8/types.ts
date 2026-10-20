// 输出结构类型定义 — 对齐 C# 侧 TsViewAstScanner 的反序列化

interface ScanResult {
  globals: ScannedGlobalVarJson[]
  methods: ScannedMethodJson[]
  eventGroups: ScannedEventGroupJson[]
  hasEventDecorator: boolean
  eventDecoratorValues: string[]
  sectionInfo: SectionInfo
}

interface ScanError {
  error: string
  errorLine?: number
}

interface SectionInfo {
  seenGlobals: boolean
  globalsBeginLine: number
  globalsEndLine: number
  seenScript: boolean
  scriptBeginLine: number
  scriptEndLine: number
}

interface ScannedGlobalVarJson {
  name: string
  rawName: string
  typeName: string
  comment: string | null
  lineNumber: number // 1-based
}

interface ScannedMethodJson {
  name: string
  rawName: string
  isPrivate: boolean
  isProtected: boolean
  isPublic: boolean
  returnType: string
  params: ParamJson[]
  bodyLines: BodyLineJson[]
  docStartLine: number // 1-based
  declLine: number // 1-based
  endLine: number // 1-based
  codeRepoGuid: string | null
  retVarFromDecorator: string | null
  retVarTypeFromDecorator: string | null
  hasBeScriptDecorator: boolean
  beScriptInfo: BeScriptInfoJson | null
}

interface ParamJson {
  name: string
  typeName: string
}

interface BodyLineJson {
  lineNumber: number // 1-based
  lineText: string
}

interface BeScriptInfoJson {
  name: string
  retVar: string | null
  retVarType: string | null
  debug: boolean | null
  skip: boolean | null
  isPrivate: boolean | null
  comment: string | null
  color: number[] | null
  rawParams: string | null
  rawRetVar: string | null
  repo: string | null
  isAsync: boolean
}

interface ScannedEventGroupJson {
  eventName: string
  members: ScannedMethodJson[]
}

interface CodeRepoViewInput {
  editorNamespace: string
  viewsPath: string
  codeExtension: string
  root: CodeRepoViewDir
}

interface CodeRepoViewDir {
  identifier: string
  directories: CodeRepoViewDir[]
  scripts: CodeRepoViewScript[]
}

interface CodeRepoViewScript {
  guid: string
  sourcePath: string
  identifier: string
  symbol: string
  commands: string[]
}

interface CodeRepoViewOutput {
  tsView: string
  scripts: CodeRepoViewScriptIndex[]
  error?: string
}

interface CodeRepoViewScriptIndex {
  guid: string
  sourceRelativePath: string
  symbol: string
  startLine: number
  endLine: number
}

interface CustomLogicDtsInput {
  namespaces: CustomLogicDtsNamespace[]
}

interface CustomLogicDtsNamespace {
  name: string
  devices: CustomLogicDtsDevice[]
}

interface CustomLogicDtsDevice {
  name: string
  baseTypeName: string
  methods: CustomLogicDtsMethod[]
  // 该零件实例具备的能力名（如 "Collidable"）。
  // 每个能力映射到 SDK d.ts 中的一个 I{Capability} 接口，由该零件 interface extends。
  capabilities: string[]
}

interface CustomLogicDtsMethod {
  name: string
  parameters: CustomLogicDtsParameter[]
  returnTypeName: string
  isAsync: boolean
  hasReturn: boolean
}

interface CustomLogicDtsParameter {
  name: string
  typeName: string
}

interface CustomLogicDtsOutput {
  dtsText: string
  error?: string
}

interface DeviceViewScriptIndexJson {
  methodName: string
  docStartLine: number
  endLine: number
  bodyLines: BodyLineJson[]
}

interface DeviceViewOutput {
  tsView: string
  scripts?: DeviceViewScriptIndexJson[]
  error?: string
}

interface DeviceViewGlobalVarInfo {
  originalName: string
  tsName: string
  beTypeName: string
  initValue: string
  comment: string | null
}

interface DeviceViewBescriptHeader {
  name: string
  comment: string | null
  debug: boolean
  skip: boolean
  private: boolean
  guid: string | null
  color: DeviceViewBescriptColor | null
  parameters: DeviceViewBescriptParameter[]
  returnParameter: DeviceViewBescriptParameter | null
}

interface DeviceViewBescriptParameter {
  typeName: string
  name: string
}

interface DeviceViewBescriptColor {
  r: number
  g: number
  b: number
}

interface DeviceViewParameterInfo {
  name: string
  typeName: string
}

interface DeviceViewEventMethodGroupInfo {
  eventName: string
  registrationEventName: string
  eventParams: DeviceViewEventParamInfo[]
  scripts: DeviceViewScriptMethodInfo[]
  summary: string
}

interface DeviceViewEventParamInfo {
  tsName: string
  beTypeName: string
  rawTypeName: string | null
  description: string | null
}

interface DeviceViewScriptBody {
  statements: DeviceViewStatement[]
  lines: BodyLineJson[]
}

interface DeviceViewStatement {
  kind: string
  isDisabled: boolean
  sourceLine: number | null
  expression: DeviceViewExpression | null
  thenStatement: DeviceViewStatement | null
  body: DeviceViewStatement | null
  statements: DeviceViewStatement[] | null
  text: string | null
  declarationList: DeviceViewVariableDeclarationList | null
}

interface DeviceViewVariableDeclarationList {
  variableKind: string
  declarations: DeviceViewVariableDeclaration[]
}

interface DeviceViewVariableDeclaration {
  name: string
  typeName: string | null
  initializer: DeviceViewExpression | null
}

interface DeviceViewExpression {
  kind: string
  text: string | null
  expression: DeviceViewExpression | null
  name: string | null
  arguments: DeviceViewExpression[] | null
  typeArguments: string[] | null
  left: DeviceViewExpression | null
  operator: string | null
  right: DeviceViewExpression | null
  argument: DeviceViewExpression | null
  typeName: string | null
  elements: DeviceViewExpression[] | null
  properties: DeviceViewObjectProperty[] | null
  isMultiLine: boolean
}

interface DeviceViewObjectProperty {
  name: string
  value: DeviceViewExpression
}
