using System.Text.Json;

using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// CodeRepo TsView 读取结果，包含生成的 TypeScript 视图和脚本索引。
/// </summary>
public sealed record CodeRepoTsViewReadData(
    string TsView,
    IReadOnlyList<CodeRepoTsViewScriptIndex> Scripts
);

/// <summary>
/// CodeRepo 脚本索引项，记录脚本在 TsView 中的位置信息。
/// </summary>
public sealed record CodeRepoTsViewScriptIndex(
    string? Guid,
    string SourceRelativePath,
    string Symbol,
    int StartLine,
    int EndLine
);

/// <summary>
/// CodeRepo TsView 写入结果，包含是否应用成功和受影响的路径。
/// </summary>
public sealed record CodeRepoTsViewWriteData(
    bool Applied,
    IReadOnlyList<string> AffectedRelativePaths
);

/// <summary>
/// CodeRepo TypeScript 视图生成器，将代码仓库脚本转换为 TypeScript 命名空间结构。
/// </summary>
/// <remarks>
/// <para><b>职责边界</b></para>
/// <para>读取非地图/机械目录下的 .code 脚本，生成 CodeRepo 命名空间视图。</para>
/// <para><b>视图写回规则</b></para>
/// <list type="bullet">
///   <item>CodeRepo.ts 绝对禁止任何直接改动，只负责被动跟随更新</item>
///   <item>写回已禁用，需使用重构命令（toRepository/linkRepository/deleteRepository）</item>
///   <item>零件视图（如 54.ts）是唯一允许 patch_mech_view 的改动点</item>
///   <item>零件视图改动会识别常规/CodeRepo 方法，决定检查和更新范围</item>
///   <item>最终统一强制刷新：CodeRepo.ts / 自定义逻辑索引 / 零件视图均同步</item>
/// </list>
/// <para><b>装饰器规则</b></para>
/// <list type="bullet">
///   <item>@CodeRepo 装饰器：自动更新，禁止 Agent 修改，仅维持 GUID 链接</item>
///   <item>BeScript/BeScriptAsync 调用：唯一约束来源，管 header（含返回变量名），各链接处重复声明</item>
///   <item>更新任意一处 BeScript 声明，其他链接处自动同步</item>
/// </list>
/// <para><b>方法体格式（仅限本类）</b></para>
/// <para>CodeRepo 视图中方法体使用 //| 原始命令行格式，不做 TS 语义转换。</para>
/// <para>此规则仅适用于 CodeRepo，不适用于 DeviceTsView（后者使用受限 TS 视图语法）。</para>
/// </remarks>
public sealed class CodeRepoTsView
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ProjectLogicContext _logic;

    public CodeRepoTsView(ProjectLogicContext logic)
    {
        _logic = logic;
    }

    public (CodeRepoTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Read(ProjectModel model)
    {
        return ReadCore(model);
    }

    public (CodeRepoTsViewWriteData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Write(
        string tsView)
    {
        return (null, [new ToolError(null, null, null, "CodeRepo.ts 禁止写回（请使用重构：script.toRepository / script.linkRepository / script.deleteRepository）")], Array.Empty<ToolError>());
    }

    private (CodeRepoTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ReadCore(
        ProjectModel model)
    {
        List<ToolError> warnings = [];
        List<CodeRepoScriptEntry> scripts = [];

        ScriptFileInfo[] scanned = _logic.GetCodeRepoScripts(warnings)
            .OrderBy(static s => s.RelativePath, StringComparer.Ordinal)
            .ToArray();
        foreach (ScriptFileInfo? script in scanned)
        {
            if (!GuidText.Validate(script.Guid))
            {
                continue;
            }

            scripts.Add(new CodeRepoScriptEntry(
                script.Guid,
                script.RelativePath,
                script.NamespaceDirSegments,
                script.NamespaceFileStem,
                script.ScriptInfo));
        }

        CodeRepoDirNode root = CodeRepoTsViewModelBuilder.BuildTree(scripts);
        CodeRepoTsViewModelBuilder.AssignIdentifiers(root);

        CodeRepoTsViewGeneratorInput input = CodeRepoTsViewModelBuilder.BuildGeneratorInput(root);
        string inputJson = JsonSerializer.Serialize(input, JsonOptions);
        string outputJson = TsCompilerHost.BuildCodeRepoView(inputJson);
        CodeRepoTsViewGeneratorOutput? output = JsonSerializer.Deserialize<CodeRepoTsViewGeneratorOutput>(outputJson, JsonOptions);
        if (output is null)
        {
            throw new InvalidOperationException("CodeRepo TS 视图生成返回空结果。");
        }
        if (output.Error is not null)
        {
            return (null, [new ToolError(null, null, null, output.Error)], Array.Empty<ToolError>());
        }

        return (new CodeRepoTsViewReadData(output.TsView, output.Scripts), Array.Empty<ToolError>(), warnings);
    }
}

internal sealed record CodeRepoTsViewGeneratorOutput(
    string TsView,
    IReadOnlyList<CodeRepoTsViewScriptIndex> Scripts,
    string? Error);
