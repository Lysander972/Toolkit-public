import type { Option } from 'effect'
import type { LocalAst } from '@toolkit/script/local_ast'
import type { BescriptDeviceContext } from '@toolkit/script/device_context'
import type { CallGraph } from '@toolkit/analysis/call-graph'

/** @anchor script-lint-contract */
export type LintSeverity = 'info' | 'warning' | 'error' | 'fatal'

/** @anchor script-lint-contract */
export interface ScriptLintInput {
  readonly methodKey: string
  readonly ast: LocalAst
  readonly context: BescriptDeviceContext
  readonly analysis: CallGraph
}

/** @anchor script-lint-contract */
interface LintLocation {
  readonly sourceRelativePath: string
  readonly line: Option.Option<number>
  readonly sourceRef: Option.Option<number>
}

/** @anchor script-lint-contract */
export interface LintDiagnostic {
  readonly ruleId: string
  readonly severity: LintSeverity
  readonly message: string
  readonly methodKey: string
  readonly location: LintLocation
}

/** @anchor script-lint-contract */
export class ScriptLint {
  private constructor() {}

  /** @anchor script-lint-contract */
  public static inspect(_input: ScriptLintInput): readonly LintDiagnostic[] {
    throw new Error('TODO: 基于 LocalAst、完整 Context 与调用分析的脚本静态检查')
  }
}
