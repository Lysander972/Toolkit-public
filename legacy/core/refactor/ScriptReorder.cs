using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

/// <summary>
/// 脚本大纲条目
/// </summary>
public sealed record ScriptOutlineEntry(
    string Name,
    string Type, // "inline" | "codeRepo"
    string? Guid,
    bool Private);

/// <summary>
/// 脚本组大纲
/// </summary>
public sealed record ScriptGroupOutline(
    string GroupName,
    string? EventName,
    IReadOnlyList<ScriptOutlineEntry> Scripts);

/// <summary>
/// 零件脚本大纲（含全局变量）
/// </summary>
public sealed record DeviceScriptOutline(
    IReadOnlyList<ScriptGroupOutline> ScriptGroups,
    IReadOnlyList<string> GlobalVariables);

/// <summary>
/// 重排序请求中的单组排序
/// </summary>
public sealed record ScriptGroupOrder(
    string GroupName,
    IReadOnlyList<string> OrderedNames);

/// <summary>
/// 重排序结果
/// </summary>
public sealed record ScriptReorderData(
    string DeviceRelativePath,
    bool Applied,
    IReadOnlyList<string> AffectedRelativePaths);

/// <summary>
/// 零件脚本大纲读取 + 组内重排序。
/// 脚本组间相对顺序不可调整。
/// </summary>
public sealed class ScriptReorder
{
    private readonly ProjectModel _model;

    public ScriptReorder(ProjectModel model)
    {
        _model = model;
    }

    /// <summary>
    /// 读取零件脚本大纲（含全局变量）。
    /// </summary>
    public (DeviceScriptOutline? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ReadOutline(
        string deviceRelativePath)
    {
        (string resolvedPath1, string? pathError) = WorkspacePath.TryRequireRelativePath(deviceRelativePath);
        if (pathError != null)
            return FailOutline(pathError);
        deviceRelativePath = resolvedPath1;

        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity device)
            return FailOutline($"零件实体未找到: {deviceRelativePath}");

        List<ScriptGroupOutline> groups = new(device.ScriptGroups.Count);
        foreach (ScriptGroupEntityBase sg in device.ScriptGroups)
        {
            List<ScriptOutlineEntry> entries = new(sg.ScriptEntries.Count);
            foreach (ScriptEntryInfo se in sg.ScriptEntries)
            {
                string scriptType;
                if (se.IsCodeRepo)
                    scriptType = "codeRepo";
                else
                    scriptType = "inline";

                entries.Add(new ScriptOutlineEntry(
                    se.Name,
                    scriptType,
                    se.Guid,
                    se.IsPrivate));
            }

            groups.Add(new ScriptGroupOutline(
                sg.实体_无相对路径标识符,
                sg.EventName,
                entries));
        }

        List<string> globalVars = device.Vars.Variables
            .Select(static v => v.Name)
            .ToList();

        return (new DeviceScriptOutline(groups, globalVars), Array.Empty<ToolError>(), Array.Empty<ToolError>());
    }

    /// <summary>
    /// 重排序零件内脚本和全局变量。
    /// orders 和 globalVarOrder 同时为空时，全部按名称升序自动排序。
    /// 非空时内部必须完整（集合相等校验）。无实质修改时警告但仍为成功。
    /// </summary>
    public (ScriptReorderData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Reorder(
        string deviceRelativePath,
        IReadOnlyList<ScriptGroupOrder>? orders,
        IReadOnlyList<string>? globalVarOrder)
    {
        (string resolvedPath2, string? pathError) = WorkspacePath.TryRequireRelativePath(deviceRelativePath);
        if (pathError != null)
            return FailReorder(pathError);
        deviceRelativePath = resolvedPath2;

        bool autoReorder = (orders == null || orders.Count == 0) && globalVarOrder == null;

        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity device)
            return FailReorder($"零件实体未找到: {deviceRelativePath}");

        // 自动排序模式：构建排序请求后走同一条执行路径
        if (autoReorder)
        {
            List<ScriptGroupOrder> autoOrders = new(device.ScriptGroups.Count);
            foreach (ScriptGroupEntityBase sg in device.ScriptGroups)
            {
                if (sg.ScriptEntries.Count <= 1) continue;
                autoOrders.Add(new ScriptGroupOrder(
                    sg.实体_无相对路径标识符,
                    sg.ScriptEntries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray()));
            }

            orders = autoOrders;
            if (device.Vars.Variables.Count > 1)
            {
                globalVarOrder = device.Vars.Variables.Select(v => v.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            }
        }

        // 手动排序模式：校验 → 收集变更 → 单次持久化
        List<ToolError> warnings = [];
        Dictionary<string, List<ScriptEntryInfo>> scriptGroupChanges = new(StringComparer.Ordinal);
        List<string> affectedPaths = [];
        List<VarInfo>? varChange = null;

        // --- 校验脚本组 ---
        if (orders != null && orders.Count > 0)
        {
            Dictionary<string, ScriptGroupEntityBase> groupMap = new(StringComparer.Ordinal);
            foreach (ScriptGroupEntityBase sg in device.ScriptGroups)
                groupMap[sg.实体_无相对路径标识符] = sg;

            HashSet<string> processedGroups = new(StringComparer.Ordinal);

            foreach (ScriptGroupOrder order in orders)
            {
                if (!processedGroups.Add(order.GroupName))
                    return FailReorder($"重复的 group_name: {order.GroupName}");

                if (!groupMap.TryGetValue(order.GroupName, out ScriptGroupEntityBase? scriptGroup))
                    return FailReorder($"脚本组不存在: {order.GroupName}");

                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach (string name in order.OrderedNames)
                {
                    if (!seen.Add(name))
                        return FailReorder($"脚本组 {order.GroupName} 中脚本名重复: {name}");
                }

                HashSet<string> existingNames = new(scriptGroup.ScriptEntries.Select(e => e.Name), StringComparer.Ordinal);
                HashSet<string> orderedSet = new(order.OrderedNames, StringComparer.Ordinal);

                string[] missing = existingNames.Except(orderedSet, StringComparer.Ordinal).ToArray();
                if (missing.Length > 0)
                    return FailReorder($"脚本组 {order.GroupName} 缺少脚本: {string.Join(", ", missing)}");

                string[] extra = orderedSet.Except(existingNames, StringComparer.Ordinal).ToArray();
                if (extra.Length > 0)
                    return FailReorder($"脚本组 {order.GroupName} 包含不存在的脚本: {string.Join(", ", extra)}");

                bool hasChange;
                IReadOnlyList<ScriptEntryInfo> currentEntries = scriptGroup.ScriptEntries;
                if (currentEntries.Count != order.OrderedNames.Count)
                {
                    hasChange = true;
                }
                else
                {
                    hasChange = false;
                    for (int i = 0; i < currentEntries.Count; i++)
                    {
                        if (!string.Equals(currentEntries[i].Name, order.OrderedNames[i], StringComparison.Ordinal))
                        {
                            hasChange = true;
                            break;
                        }
                    }
                }

                if (!hasChange)
                {
                    warnings.Add(new ToolError(deviceRelativePath, null, null,
                        $"脚本组 {order.GroupName} 顺序无实质变更", ToolDiagnosticSeverity.Warning));
                    continue;
                }

                Dictionary<string, ScriptEntryInfo> entryByName = new(StringComparer.Ordinal);
                foreach (ScriptEntryInfo entry in scriptGroup.ScriptEntries)
                    entryByName[entry.Name] = entry;

                List<ScriptEntryInfo> reordered = new(order.OrderedNames.Count);
                foreach (string name in order.OrderedNames)
                    reordered.Add(entryByName[name]);

                scriptGroupChanges[order.GroupName] = reordered;
                affectedPaths.Add(scriptGroup.listJson_完整相对路径);
            }
        }

        // --- 校验全局变量 ---
        if (globalVarOrder != null)
        {
            if (device.Vars.Variables.Count == 0)
                return FailReorder("该零件无全局变量，无法重排");

            if (globalVarOrder.Count == 0)
                return FailReorder("global_var_order 不能为空数组");

            HashSet<string> varSeen = new(StringComparer.Ordinal);
            foreach (string name in globalVarOrder)
            {
                if (!varSeen.Add(name))
                    return FailReorder($"全局变量名重复: {name}");
            }

            HashSet<string> existingVarNames = new(device.Vars.Variables.Select(v => v.Name), StringComparer.Ordinal);
            HashSet<string> orderedVarSet = new(globalVarOrder, StringComparer.Ordinal);

            string[] varMissing = existingVarNames.Except(orderedVarSet, StringComparer.Ordinal).ToArray();
            if (varMissing.Length > 0)
                return FailReorder($"全局变量缺少: {string.Join(", ", varMissing)}");

            string[] varExtra = orderedVarSet.Except(existingVarNames, StringComparer.Ordinal).ToArray();
            if (varExtra.Length > 0)
                return FailReorder($"不存在的全局变量: {string.Join(", ", varExtra)}");

            bool varHasChange;
            IReadOnlyList<VarInfo> currentVars = device.Vars.Variables;
            if (currentVars.Count != globalVarOrder.Count)
            {
                varHasChange = true;
            }
            else
            {
                varHasChange = false;
                for (int i = 0; i < currentVars.Count; i++)
                {
                    if (!string.Equals(currentVars[i].Name, globalVarOrder[i], StringComparison.Ordinal))
                    {
                        varHasChange = true;
                        break;
                    }
                }
            }

            if (!varHasChange)
            {
                warnings.Add(new ToolError(deviceRelativePath, null, null,
                    "全局变量顺序无实质变更", ToolDiagnosticSeverity.Warning));
            }
            else
            {
                Dictionary<string, VarInfo> varByName = new(StringComparer.Ordinal);
                foreach (VarInfo v in device.Vars.Variables)
                    varByName[v.Name] = v;

                varChange = new List<VarInfo>(globalVarOrder.Count);
                foreach (string name in globalVarOrder)
                    varChange.Add(varByName[name]);

                affectedPaths.Add(device.vars配置_完整相对路径);
            }
        }

        // --- 单次持久化 ---
        if (affectedPaths.Count > 0)
        {
            EntityWriteScope.ApplyContentChange<DeviceEntity, WritableDeviceEntity>(
                _model,
                device,
                d => d.ToWritable(),
                writableDevice =>
                {
                    foreach (ScriptGroupEntityBase sg in writableDevice.ScriptGroups)
                    {
                        if (sg is WritableScriptGroupEntity writableSg
                            && scriptGroupChanges.TryGetValue(sg.实体_无相对路径标识符, out List<ScriptEntryInfo>? reordered))
                        {
                            writableSg.ScriptEntries = reordered;
                        }
                    }
                    if (varChange != null)
                        writableDevice.Vars = new VarsConfig { Variables = varChange };
                });
        }

        bool applied = affectedPaths.Count > 0;
        return (new ScriptReorderData(deviceRelativePath, applied, affectedPaths),
            Array.Empty<ToolError>(), warnings);
    }

    private static (DeviceScriptOutline? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) FailOutline(string message)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }

    private static (ScriptReorderData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) FailReorder(string message)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>());
    }
}
