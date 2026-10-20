using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Lint;

public static partial class LintEngine
{
    /// <summary>
    /// 需要校验文件名参数的内置方法集合。
    /// Key: (Category, OwnerType, MethodName, ParamIndex) 元组。
    /// 这些方法的指定参数位置上期望传入工程中存在的资源文件名。
    /// </summary>
    private static readonly HashSet<(MethodCategory Category, string OwnerType, string MethodName, int ParamIndex)>
        FilenameParamMethodKeys = new()
    {
        // DeviceAction
        (MethodCategory.DeviceAction, "Device",               "setDynamicMatPic",   1),
        (MethodCategory.DeviceAction, "SpriteDevice",         "setTexture",         0),
        (MethodCategory.DeviceAction, "FlareDevice",          "setTexture",         0),
        (MethodCategory.DeviceAction, "DecalDevice",          "setTexture",         0),
        (MethodCategory.DeviceAction, "ObjDevice",            "setDynamicMatPic2",  1),
        (MethodCategory.DeviceAction, "PlanetDevice",         "setMainTex",         0),
        (MethodCategory.DeviceAction, "SunDevice",            "setMainTex",         0),
        (MethodCategory.DeviceAction, "GasPlanetDevice",      "setMainTex",         0),

        // VarFunc
        (MethodCategory.VarFunc,      "Device",              "setDynamicMatPic",    1),
        (MethodCategory.VarFunc,      "UIRawImage",          "loadPic",             0),
        (MethodCategory.VarFunc,      "UIRawImage",          "loadPicAsync",        0),
        (MethodCategory.VarFunc,      "UIRawImage",          "LoadUserPic",         0),
        (MethodCategory.VarFunc,      "UIRect",              "loadPic",             0),
        (MethodCategory.VarFunc,      "UIRect",              "loadPicAsync",        0),

        // EditorFunc
        (MethodCategory.EditorFunc,   "Device",              "setPic",              0),
    };

    /// <summary>
    /// 验证零件中所有脚本的文件名参数是否能在工程资源中找到对应文件。
    /// </summary>
    public static IReadOnlyList<LintDiagnostic> ValidateFilenameParams(
        DeviceEntityBase device,
        ScriptAnalysisService analysisService,
        ProjectModel model)
    {
        List<LintDiagnostic> diagnostics = [];

        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        HashSet<string> workspaceFilenames = BuildWorkspaceFilenameSet(model);

        foreach (ScriptGroupEntityBase group in device.ScriptGroups)
        {
            foreach (ScriptEntryInfo entry in group.ScriptEntries)
            {
                string? scriptPath = ResolveScriptPath(entry, group, model);
                if (scriptPath is null)
                {
                    diagnostics.Add(CreateLintEngineErrorDiagnostic(
                        $"脚本清单与实体不一致：'{entry.Name}' 在脚本组中无对应实体，已跳过",
                        new LintLocation(group.listJson_完整相对路径, 1)));
                    continue;
                }
                BescriptParseData parseData = analysisService.ParseScript(scriptPath)
                    ?? throw new InvalidOperationException($"脚本解析失败: {scriptPath}");

                foreach (BescriptAstNode node in parseData.Ast.RootNodes)
                {
                    CollectFilenameParamDiagnostics(node, diagnostics, registry, scriptPath, workspaceFilenames);
                }
            }
        }

        return diagnostics;
    }

    private static HashSet<string> BuildWorkspaceFilenameSet(ProjectModel model)
    {
        HashSet<string> filenames = new(StringComparer.Ordinal);
        foreach (ResourceEntity resource in model.List<ResourceEntity>())
        {
            string relativePath = resource.实体_完整相对路径标识;
            string fileName = System.IO.Path.GetFileName(relativePath);
            filenames.Add(fileName);
        }

        return filenames;
    }

    private static void CollectFilenameParamDiagnostics(
        BescriptAstNode node,
        List<LintDiagnostic> diagnostics,
        BuiltinMethodRegistry registry,
        string scriptPath,
        HashSet<string> workspaceFilenames)
    {
        if (node.Kind == BescriptStatementKind.Call && node is IHasCall { Call: var call })
        {
            CheckFilenameParam(node, call, diagnostics, registry, scriptPath, workspaceFilenames);
        }

        foreach (BescriptAstNode child in node.Children)
        {
            CollectFilenameParamDiagnostics(child, diagnostics, registry, scriptPath, workspaceFilenames);
        }
    }

    private static void CheckFilenameParam(
        BescriptAstNode node,
        BescriptCallSignature call,
        List<LintDiagnostic> diagnostics,
        BuiltinMethodRegistry registry,
        string scriptPath,
        HashSet<string> workspaceFilenames)
    {
        if (call.Kind is BescriptCallKind.None or BescriptCallKind.Toolf)
            return;

        if (TryResolveBuiltinMethodSignature(registry, call) is not { } resolved)
            return;

        MethodSignature sig = resolved.Sig;
        if (sig.OwnerType is not string ownerType)
            return;

        for (int i = 0; i < call.Arguments.Count; i++)
        {
            BescriptCallArgument arg = call.Arguments[i];
            if (arg.IsVariable)
                continue;

            // 仅检查 String 类型常量
            if (BescriptVarTypeHelper.TryParse(arg.TypeName) is not BescriptVarType type || type != BescriptVarType.String)
                continue;

            string value = arg.DecodedText;

            (MethodCategory, string, string, int) key = (sig.Category, ownerType, sig.MethodName, i);
            if (!FilenameParamMethodKeys.Contains(key))
                continue;

            if (!workspaceFilenames.Contains(value))
            {
                diagnostics.Add(CreateCallFilenameNotFoundDiagnostic(
                    resolved.Path, i, sig.Parameters[i].EffectiveName, value, scriptPath, node.Line));
            }
        }
    }
}
