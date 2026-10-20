using Toolkit.Core.Analysis;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// 自定义逻辑 TypeScript 声明库，提供 custom-logic.d.ts 文件内容。
/// 【职责边界】生成零件自定义逻辑的 TypeScript 类型声明。
/// </summary>
public static class CustomLogicTsViewDeclarationLibrary
{
    public const string DtsFileName = "custom-logic.d.ts";

    public static string GetDtsText(ProjectModel model, ProjectLogicContext? logic = null)
    {
        return DeviceTsView.BuildCustomLogicDts(model, logic?.ScriptAstCache);
    }
}
