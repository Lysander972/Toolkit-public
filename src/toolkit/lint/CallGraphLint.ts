import type { CallGraph } from '@toolkit/analysis/call-graph'
import type { LintDiagnostic } from './ScriptLint.ts'

/** @anchor lint-layered-pipeline */
export class CallGraphLint {
  private constructor() {}

  /** @anchor lint-layered-pipeline */
  public static inspect(_analysis: CallGraph): readonly LintDiagnostic[] {
    throw new Error('TODO: 按完整调用图执行一次跨方法静态检查')
  }
}
