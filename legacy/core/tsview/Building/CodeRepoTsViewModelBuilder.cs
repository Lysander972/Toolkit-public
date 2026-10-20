using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.TsView;

/// <summary>
/// CodeRepo TypeScript 视图业务模型构建器。
/// 将代码仓库脚本整理为官方 TypeScript 生成入口所需的业务输入。
/// 【仅限 CodeRepo】此处 //| 命令行格式仅适用于代码仓库视图，
/// DeviceTsView 使用受限 TS 视图语法，两者规则不同。
/// </summary>
internal static class CodeRepoTsViewModelBuilder
{
    /// <summary>
    /// 构建脚本树结构。
    /// </summary>
    public static CodeRepoDirNode BuildTree(IReadOnlyList<CodeRepoScriptEntry> scripts)
    {
        CodeRepoDirNode root = new() { OriginalName = string.Empty, Identifier = string.Empty };

        foreach (CodeRepoScriptEntry script in scripts)
        {
            CodeRepoDirNode current = root;
            foreach (string seg in script.DirSegments)
            {
                if (!current.SubDirsByOriginal.ContainsKey(seg))
                {
                    current.SubDirsByOriginal[seg] = new CodeRepoDirNode { OriginalName = seg };
                }
                current = current.SubDirsByOriginal[seg];
            }

            if (!current.ScriptsByOriginal.ContainsKey(script.FileStem))
            {
                current.ScriptsByOriginal[script.FileStem] = new CodeRepoScriptNode
                {
                    OriginalName = script.FileStem,
                    Script = script
                };
            }
        }

        return root;
    }

    /// <summary>
    /// 为目录和脚本节点分配唯一标识符。
    /// </summary>
    public static void AssignIdentifiers(CodeRepoDirNode node)
    {
        HashSet<string> used = new(StringComparer.Ordinal);

        foreach (CodeRepoDirNode? dir in node.SubDirsByOriginal.Values.OrderBy(static d => d.OriginalName, StringComparer.Ordinal))
        {
            dir.Identifier = TsViewNaming.EnsureUniqueIdentifier(TsViewIdentifierMap.ToTsIdentifier(dir.OriginalName), used);
        }

        foreach (CodeRepoScriptNode? script in node.ScriptsByOriginal.Values.OrderBy(static s => s.OriginalName, StringComparer.Ordinal))
        {
            script.Identifier = TsViewNaming.EnsureUniqueIdentifier(TsViewIdentifierMap.ToTsIdentifier(script.OriginalName), used);
        }

        foreach (CodeRepoDirNode dir in node.SubDirsByOriginal.Values)
        {
            AssignIdentifiers(dir);
        }
    }

    public static CodeRepoTsViewGeneratorInput BuildGeneratorInput(CodeRepoDirNode root)
    {
        return new CodeRepoTsViewGeneratorInput(
            ProjectEntityLayout.EditorNamespace,
            ScriptPathHelper.ViewsPath,
            ProjectEntityLayout.CodeExt,
            BuildDirectory(root, Array.Empty<string>()));
    }

#pragma warning disable TK006 // 符号名构建，含命名空间条件分支
    private static string BuildSymbol(string[] nsSegments, string functionName)
#pragma warning restore TK006
    {
        if (nsSegments.Length == 0)
        {
            return "CodeRepo." + functionName;
        }

        return "CodeRepo." + string.Join('.', nsSegments) + "." + functionName;
    }

    private static CodeRepoTsViewGeneratorDir BuildDirectory(CodeRepoDirNode node, string[] nsSegments)
    {
        CodeRepoTsViewGeneratorDir[] directories = node.SubDirsByOriginal.Values
            .OrderBy(static d => d.OriginalName, StringComparer.Ordinal)
            .Select(d => BuildDirectory(d, nsSegments.Concat([d.Identifier]).ToArray()))
            .ToArray();

        CodeRepoTsViewGeneratorScript[] scripts = node.ScriptsByOriginal.Values
            .OrderBy(static s => s.OriginalName, StringComparer.Ordinal)
            .Select(s => BuildScript(s, nsSegments))
            .ToArray();

        return new CodeRepoTsViewGeneratorDir(node.Identifier, directories, scripts);
    }

    private static CodeRepoTsViewGeneratorScript BuildScript(CodeRepoScriptNode scriptNode, string[] nsSegments)
    {
        CodeRepoScriptEntry script = scriptNode.Script;
        if (!GuidText.Validate(script.Guid))
        {
            throw new InvalidOperationException($"CodeRepo 脚本缺少 guid: {script.SourcePath}");
        }

        string symbol = BuildSymbol(nsSegments, scriptNode.Identifier);
        string[] commands = script.ScriptInfo.Ast.RootNodes
            .FlattenNodes()
            .Select(static n => n.Text)
            .ToArray();

        return new CodeRepoTsViewGeneratorScript(script.Guid, script.SourcePath, scriptNode.Identifier, symbol, commands);
    }
}

/// <summary>
/// CodeRepo 目录节点。
/// </summary>
internal sealed class CodeRepoDirNode
{
    public readonly Dictionary<string, CodeRepoDirNode> SubDirsByOriginal = new(StringComparer.Ordinal);
    public readonly Dictionary<string, CodeRepoScriptNode> ScriptsByOriginal = new(StringComparer.Ordinal);

    public string OriginalName { get; init; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
}

/// <summary>
/// CodeRepo 脚本节点。
/// </summary>
internal sealed class CodeRepoScriptNode
{
    public string OriginalName { get; init; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public required CodeRepoScriptEntry Script { get; init; }
}

/// <summary>
/// CodeRepo 脚本条目。
/// </summary>
internal sealed record CodeRepoScriptEntry(
    string? Guid,
    string SourcePath,
    string[] DirSegments,
    string FileStem,
    BescriptScriptInfo ScriptInfo
);

internal sealed record CodeRepoTsViewGeneratorInput(
    string EditorNamespace,
    string ViewsPath,
    string CodeExtension,
    CodeRepoTsViewGeneratorDir Root);

internal sealed record CodeRepoTsViewGeneratorDir(
    string Identifier,
    IReadOnlyList<CodeRepoTsViewGeneratorDir> Directories,
    IReadOnlyList<CodeRepoTsViewGeneratorScript> Scripts);

internal sealed record CodeRepoTsViewGeneratorScript(
    string Guid,
    string SourcePath,
    string Identifier,
    string Symbol,
    IReadOnlyList<string> Commands);
