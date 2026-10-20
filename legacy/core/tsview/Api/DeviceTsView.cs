using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.TsView;

/// <summary>
/// 零件 TypeScript 视图生成器。
/// 【核心区别】Device 视图使用受限 TS 视图语法（变量声明、赋值、调用、控制流），
/// TS 语法解析与文本生成必须走官方 TypeScript API，C# 只负责 BeScript 业务映射与写回调度。
/// 【易混淆点】CodeRepo 视图使用 //| 原始命令行格式；Device 视图方法体使用普通 // 注释，两者规则完全不同，不可混淆。
/// </summary>
public sealed partial class DeviceTsView
{
    private readonly ProjectModel _model;
    private readonly ProjectLogicContext _logic;

    public DeviceTsView(ProjectModel model, ProjectLogicContext logic)
    {
        _model = model;
        _logic = logic;
    }

    public (DeviceTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Read(
        string deviceRelativePath)
    {
        return ReadCore(deviceRelativePath);
    }

    public (DeviceTsViewPatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Patch(
        string deviceRelativePath,
        TsViewPatch[] patches,
        string? baseTsView = null,
        string? baseTsViewFileRelativePath = null,
        bool forceRemoval = false,
        bool persistChanges = true)
    {
        return PatchCore(deviceRelativePath, patches, baseTsView, baseTsViewFileRelativePath, forceRemoval, persistChanges);
    }

    private (DeviceTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ReadCore(
        string deviceRelativePath)
    {
        (string resolvedPath, string? pathError) = WorkspacePath.TryRequireRelativePath(deviceRelativePath);
        if (pathError != null)
        {
            return FailRead(pathError);
        }
        deviceRelativePath = resolvedPath;

        _model.EnterReadLock();
        try
        {
            // 通过模型检查零件是否存在，禁止直接读取文件系统
            if (_model.Find<DeviceEntity>(deviceRelativePath) == null)
            {
                return FailRead($"零件实体未找到: {deviceRelativePath}");
            }

            List<ToolError> warnings = [];
            DeviceTsViewBuildResult buildResult = BuildDeviceTsView(_model, deviceRelativePath, warnings, cache: _logic.ScriptAstCache);

            TsViewEditAllowance editAllowance = buildResult.EditAllowance;
            DeviceTsViewReadData readData = new(
                deviceRelativePath,
                buildResult.TsView,
                buildResult.ReferencedDeviceRelativePaths,
                buildResult.ScriptIndexes,
                editAllowance);

            return (readData, Array.Empty<ToolError>(), warnings);
        }
        finally
        {
            _model.ExitReadLock();
        }
    }

    /// <summary>
    /// 零件 TS 视图补丁核心逻辑。
    /// 委托给 TsViewWritebackOrchestrator 执行三层转换架构。
    /// </summary>
#pragma warning disable TK006 // API 层委托入口，隔离上层调用与编排器实现
    private (DeviceTsViewPatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) PatchCore(
        string deviceRelativePath,
        TsViewPatch[] patches,
        string? baseTsView,
        string? baseTsViewFileRelativePath,
        bool forceRemoval = false,
        bool persistChanges = true)
    {
        // Patch 会进入 SaveChanges；写时序只能由保存入口统一管理，不能在这里持有模型读锁。
        TsViewWritebackOrchestrator orchestrator = new(_model, _logic);
        return orchestrator.Execute(deviceRelativePath, patches, baseTsView, baseTsViewFileRelativePath, forceRemoval, persistChanges);
    }
#pragma warning restore TK006

    // ═══════════════════════════════════════════════════════════════════════════════
    // 脚本删除检测（供 Orchestrator 调用）
    // ═══════════════════════════════════════════════════════════════════════════════

    internal static List<ScriptRemoval> BuildScriptRemovals(
        IReadOnlyDictionary<string, List<DesiredScriptListEntry>> baseLists,
        IReadOnlyDictionary<string, List<DesiredScriptListEntry>> desiredLists,
        string devicePath)
    {
        HashSet<string> listKeys = new(baseLists.Keys, StringComparer.Ordinal);
        foreach (string key in desiredLists.Keys)
        {
            listKeys.Add(key);
        }

        List<ScriptRemoval> removals = [];
        foreach (string listPath in listKeys)
        {
            HashSet<string> baseNames = GetLocalScriptNames(baseLists, listPath);
            HashSet<string> desiredNames = GetLocalScriptNames(desiredLists, listPath);
            foreach (string name in baseNames)
            {
                if (desiredNames.Contains(name))
                {
                    continue;
                }

                string baseDir = ScriptPathHelper.BuildScriptDirectoryFromListPath(listPath);
                string scriptFile = name + ProjectEntityLayout.CodeExt;
                string scriptPath = RelativePathUtils.Combine(baseDir, scriptFile);
                removals.Add(new ScriptRemoval(name, scriptPath));
            }
        }

        return removals;
    }

    private static HashSet<string> GetLocalScriptNames(
        IReadOnlyDictionary<string, List<DesiredScriptListEntry>> lists,
        string listPath)
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        if (!lists.TryGetValue(listPath, out List<DesiredScriptListEntry>? items))
        {
            return set;
        }

        foreach (DesiredScriptListEntry item in items)
        {
            if (GuidText.Validate(item.Guid))
            {
                continue;
            }

            if (!item.Name.AsSpan().IsWhiteSpace())
            {
                set.Add(item.Name);
            }
        }

        return set;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 脚本删除/更名校验（供 Orchestrator 调用）
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 校验脚本消失操作（更名或删除不做区分）。
    /// </summary>
    /// <remarks>
    /// <para>为确保引用者同步更新，patch_mech_view 仅允许在无外部引用者的状态下移除脚本。</para>
    /// <para>- 更名脚本：推荐使用 refactor_rename_script（可自动级联更新引用者）</para>
    /// <para>- 删除脚本：应先到引用者处逐步删除引用，最后再 patch_mech_view 整体删除</para>
    /// 为了确保避免误判,一次修改内同时完成"移除引用"+"移除定义"也可放行,因为现在已经让当前零件按patch后计算(其他零件外部引用仍然按patch前算,暂时不管coderepo相关的复杂度)
    /// </remarks>
    internal static IReadOnlyList<ToolError> ValidateRemovalsForApplyPatch(
        ProjectLogicContext logic,
        IReadOnlyList<ScriptRemoval> removals,
        IReadOnlyList<ScriptUpdate>? patchedScriptUpdates)
    {
        if (removals.Count == 0)
            return Array.Empty<ToolError>();

        ProjectModel model = logic.Model;
        List<ToolError> blocked = [];
        HashSet<string> patchedPaths = new(StringComparer.Ordinal);
        if (patchedScriptUpdates is not null)
        {
            foreach (ScriptUpdate update in patchedScriptUpdates)
                patchedPaths.Add(WorkspacePath.RequireRelativePath(update.SourcePath));
        }

        foreach (ScriptRemoval removal in removals)
        {
            string targetPath = WorkspacePath.RequireRelativePath(removal.ScriptPath);
            string targetDeviceDir = RelativePathUtils.GetParentDirectory(RelativePathUtils.GetParentDirectory(targetPath));
            int count = 0;

            // 1. 统计其他零件的现有引用（不在patch范围内）
            foreach (SymbolReference reference in logic.FindSymbolReferences(removal.ScriptName))
            {
                string refPath = WorkspacePath.RequireRelativePath(reference.ScriptRelativePath);
                if (patchedPaths.Contains(refPath) || string.Equals(refPath, targetPath, StringComparison.Ordinal) || reference.CallKind != BescriptCallKind.Act)
                    continue;

                string refDeviceDir = RelativePathUtils.GetParentDirectory(RelativePathUtils.GetParentDirectory(refPath));
                if (model.Find<DeviceEntity>(refDeviceDir) is DeviceEntity callerDevice)
                {
                    string? resolved = RefactorHelper.ResolveActTargetDevicePath(reference.OwnerName, reference.OwnerNameIsAlias, callerDevice, model);
                    if (resolved != null && !string.Equals(resolved, targetDeviceDir, StringComparison.Ordinal))
                        continue;
                }
                count++;
            }

            // 2. 统计patch后当前零件内的引用
            if (patchedScriptUpdates != null)
            {
                foreach (ScriptUpdate update in patchedScriptUpdates)
                {
                    string refPath = WorkspacePath.RequireRelativePath(update.SourcePath);
                    if (string.Equals(refPath, targetPath, StringComparison.Ordinal))
                        continue;

                    string refDeviceDir = RelativePathUtils.GetParentDirectory(RelativePathUtils.GetParentDirectory(refPath));
                    DeviceEntity? callerDevice = model.Find<DeviceEntity>(refDeviceDir);
                    count += CountActCalls(update.BodyAst.RootNodes, removal.ScriptName, callerDevice, model, targetDeviceDir);
                }
            }

            if (count > 0)
            {
                blocked.Add(new ToolError(removal.ScriptPath, null, null,
                    $"脚本 {removal.ScriptName} 在 patch 后仍有 {count} 处旧引用，不允许移除或更名。" +
                    $"若要删除：请先在同一次 patch 中删光所有旧调用，再删除脚本定义；" +
                    $"若要更名：请改用 refactor_rename_script 工具（可自动级联更新所有引用者）。"));
            }
        }

        if (blocked.Count > 0)
        {
            return blocked;
        }
        return Array.Empty<ToolError>();
    }

    private static int CountActCalls(IReadOnlyList<BescriptAstNode> nodes, string scriptName, DeviceEntity? callerDevice, ProjectModel model, string targetDeviceDir)
    {
        int count = 0;
        foreach (BescriptAstNode node in nodes)
        {
            if (node.Kind == BescriptStatementKind.Call &&
                ((IHasCall)node).Call is ActCallSignature act &&
                string.Equals(act.FunctionName, scriptName, StringComparison.Ordinal))
            {
                if (callerDevice is null)
                {
                    count++;
                    continue;
                }

                if (string.Equals(RefactorHelper.ResolveActTargetDevicePath(act.OwnerName, act.OwnerNameIsAlias, callerDevice, model), targetDeviceDir, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            if (node.Children.Count > 0)
                count += CountActCalls(node.Children, scriptName, callerDevice, model, targetDeviceDir);
        }
        return count;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════════════════════════

    private static (DeviceTsViewReadData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) FailRead(string message)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }
}
