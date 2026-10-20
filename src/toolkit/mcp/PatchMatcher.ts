/** @anchor patch-matching-contract */
export interface PatchEdit {
  readonly oldText: string
  readonly newText: string
  readonly replaceAll: boolean
}

/** @anchor patch-matching-contract */
export class PatchMatcher {
  private constructor() {}

  /** @anchor patch-matching-contract */
  public static apply(_originalText: string, _edits: readonly PatchEdit[]): string {
    throw new Error('TODO: 按唯一精确匹配与顺序编辑合同生成完整更新文本')
  }
}
