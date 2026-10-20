// =============================================================================
// 核心设计警告 / CORE DESIGN WARNING
// =============================================================================
//
// 本文件包含文本处理工具。这些是 internal 类型，因为工作区应通过
// 实体模型操作文件内容，这些工具仅用于实体内部的文本处理。
//
// 禁止：
// 1. 将这些类型改为 public
// 2. 在其他模块直接使用这些工具处理文件内容
//
// 正确做法：
//   var model = ProjectModel.Load();
//   if (model.Find<CodeRepoScriptEntity>("repository/test.code") is CodeRepoScriptEntity script)
//   {
//       script.SourceText = "新内容";  // 实体内部使用这些工具
//       model.SaveChanges(script);
//   }
//
// =============================================================================

using System.Text;

/// <summary>
/// 通用文本替换辅助：按倒序应用去重后的替换片段。
/// 上游扫描器必须保证位置有效，否则自然抛出异常。
/// </summary>
internal static class TextReplacementApplier
{
    /// <summary>
    /// 通用文本替换辅助：按倒序应用去重后的替换片段。
    /// 上游扫描器必须保证位置有效，否则自然抛出异常。
    /// </summary>
    public static string ApplyReplacements(string line, List<(int Start, int Length, string Text)> replacements)
    {
        (int Start, int Length, string Text)[] ordered = replacements
            .DistinctBy(static r => (r.Start, r.Length))
            .OrderByDescending(static r => r.Start)
            .ToArray();

        StringBuilder sb = new(line);
        foreach ((int Start, int Length, string Text) r in ordered)
        {
            sb.Remove(r.Start, r.Length);
            sb.Insert(r.Start, r.Text);
        }

        return sb.ToString();
    }
}
