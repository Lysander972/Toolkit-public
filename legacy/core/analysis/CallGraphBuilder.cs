// 调用图构建器 - Logic 层（逻辑/行为层）
// 职责：从 ProjectModel 构建全工程调用图，识别入口点，标记死代码候选
// 数据来源：Persistence 层提供的 ProjectModel（只读）+ ScriptAnalysisService（AST 解析）

using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Analysis;

// =============================================================================
// 数据模型
// =============================================================================

/// <summary>
/// 调用图节点 - 表示一个具体的脚本方法。
/// </summary>
public sealed record CallGraphNode(
    string MethodName,
    string DevicePath,
    int DeviceId,
    int DeviceType,
    string? DeviceLogicName,
    string ScriptPath,
    string? EventName,
    bool IsPrivate
);

/// <summary>
/// 调用边类型
/// </summary>
public enum CallEdgeType
{
    /// <summary>静态 act 调用 - 目标在作用域内可精确解析（含自引用）</summary>
    Static,
    /// <summary>潜在静态引用 - 1110 零件调用，目标在所有机械中匹配</summary>
    PotentialStatic,
    /// <summary>动态调用 - callFuncN / loopFun / addClickCB 等字符串形式</summary>
    Dynamic,
}

/// <summary>
/// 调用图边
/// </summary>
public sealed record CallGraphEdge(
    CallGraphNode From,
    CallGraphNode To,
    CallEdgeType EdgeType,
    int Line
);

/// <summary>
/// 入口点类型
/// </summary>
public enum EntryPointType
{
    /// <summary>事件脚本入口 (event_xxx 目录下的脚本)</summary>
    EventScript,
    /// <summary>动态回调入口 (addClickCB / setAnimEvent 等)</summary>
    DynamicCallback,
    /// <summary>每帧循环入口 (loopFun / fixedLoopAct)</summary>
    PerFrameLoop,
}

/// <summary>
/// 调用图入口点
/// </summary>
public sealed record CallGraphEntryPoint(
    CallGraphNode Node,
    EntryPointType Type,
    string? TriggerDescription
);

/// <summary>
/// 调用图统计信息
/// </summary>
public sealed record CallGraphStatistics(
    int TotalNodes,
    int TotalEdges,
    int EntryPointCount,
    int DeadCodeCount,
    int StaticEdgeCount,
    int PotentialStaticEdgeCount,
    int DynamicEdgeCount,
    int DeviceCount
);

/// <summary>
/// 未解析的 act 调用 — 目标零件存在但目标方法不在该零件内，可能形成自调用递归
/// </summary>
public sealed record UnresolvedActCall(
    CallGraphNode From,
    string TargetDevicePath,
    string MethodName,
    int Line
);

/// <summary>
/// 调用图中的静态环路
/// </summary>
public sealed record CallGraphCycle(
    IReadOnlyList<CallGraphNode> Path,
    CallGraphEdge ClosingEdge
);

/// <summary>
/// 调用图报告
/// </summary>
public sealed class CallGraphReport
{
    public required IReadOnlyList<CallGraphNode> Nodes { get; init; }
    public required IReadOnlyList<CallGraphEdge> Edges { get; init; }
    public required IReadOnlyList<CallGraphEntryPoint> EntryPoints { get; init; }
    public required IReadOnlyList<CallGraphNode> DeadCodeCandidates { get; init; }
    public required CallGraphStatistics Statistics { get; init; }
    public required IReadOnlyList<UnresolvedActCall> UnresolvedActCalls { get; init; }
    public required IReadOnlyList<CallGraphCycle> Cycles { get; init; }
}

// =============================================================================
// 构建器
// =============================================================================

/// <summary>
/// 调用图构建器。
/// 四阶段：枚举节点 → 构建边 → 入口点 → 死代码。
/// </summary>
public sealed class CallGraphBuilder
{
    private readonly ProjectModel _model;
    private readonly ScriptAnalysisService _analysis;

    private readonly List<CallGraphNode> _nodes = [];
    private readonly Dictionary<(string DevicePath, string MethodName), CallGraphNode> _nodeLookup = [];
    private readonly Dictionary<string, List<CallGraphNode>> _methodNameIndex = new(StringComparer.Ordinal);
    private readonly List<CallGraphEdge> _edges = [];
    private readonly List<CallGraphEntryPoint> _entryPoints = [];
    private readonly List<CallGraphNode> _deadCodeCandidates = [];
    private readonly List<UnresolvedActCall> _unresolvedActCalls = [];
    private readonly List<CallGraphCycle> _cycles = [];

    public CallGraphBuilder(ProjectModel model, ScriptAnalysisService analysis)
    {
        _model = model;
        _analysis = analysis;
    }

    public CallGraphReport Build()
    {
        CollectAllNodes();
        ResolveEdgesAndEntryPoints();
        FindDeadCode();
        FindRecursion();

        return new CallGraphReport
        {
            Nodes = _nodes,
            Edges = _edges,
            EntryPoints = _entryPoints,
            DeadCodeCandidates = _deadCodeCandidates,
            Statistics = new CallGraphStatistics(
                TotalNodes: _nodes.Count,
                TotalEdges: _edges.Count,
                EntryPointCount: _entryPoints.Count,
                DeadCodeCount: _deadCodeCandidates.Count,
                StaticEdgeCount: _edges.Count(static e => e.EdgeType == CallEdgeType.Static),
                PotentialStaticEdgeCount: _edges.Count(static e => e.EdgeType == CallEdgeType.PotentialStatic),
                DynamicEdgeCount: _edges.Count(static e => e.EdgeType == CallEdgeType.Dynamic),
                DeviceCount: _nodes.Select(static n => n.DevicePath).Distinct(StringComparer.Ordinal).Count()
            ),
            UnresolvedActCalls = _unresolvedActCalls,
            Cycles = _cycles
        };
    }

    // =========================================================================
    // Phase 1 - 枚举节点
    // =========================================================================

    private void CollectAllNodes()
    {
        foreach (DeviceEntity device in _model.List<DeviceEntity>())
        {
            string devicePath = device.device目录_完整相对路径;

            foreach (ScriptGroupEntityBase group in device.ScriptGroups)
            {
                foreach (ScriptEntryInfo entry in group.ScriptEntries)
                {
                    string? scriptPath = ResolveScriptPath(entry, group);
                    if (scriptPath == null)
                        continue;

                    CallGraphNode node = new(
                        entry.Name,
                        devicePath,
                        device.DeviceId,
                        device.DeviceType,
                        device.LogicName,
                        scriptPath,
                        group.EventName,
                        entry.IsPrivate
                    );

                    _nodes.Add(node);
                    _nodeLookup[(devicePath, entry.Name)] = node;

                    if (!_methodNameIndex.ContainsKey(entry.Name))
                        _methodNameIndex[entry.Name] = [];
                    _methodNameIndex[entry.Name].Add(node);
                }
            }
        }
    }

    private string? ResolveScriptPath(ScriptEntryInfo entry, ScriptGroupEntityBase group)
    {
        if (entry.IsCodeRepo)
        {
            if (!GuidText.Validate(entry.Guid))
                return null;
            if (_model.FindByGuid<CodeRepoScriptEntity>(entry.Guid) is CodeRepoScriptEntity codeRepo)
                return codeRepo.实体_完整相对路径标识;
            return null;
        }

        InlineScriptEntityBase? inline = group.Scripts
            .FirstOrDefault(s => string.Equals(s.ScriptName, entry.Name, StringComparison.Ordinal));
        return inline?.实体_完整相对路径标识;
    }

    // =========================================================================
    // Phase 2+3 - 边构建 + 入口点检测（合并为单次 AST 遍历）
    // =========================================================================

    private void ResolveEdgesAndEntryPoints()
    {
        // 作用域分组
        Dictionary<(string CarrierKind, string CarrierGuid), List<DeviceEntity>> devicesByCarrier = [];
        foreach (DeviceEntity device in _model.List<DeviceEntity>())
        {
            (string CarrierKind, string CarrierGuid) key = (device.CarrierKind, device.CarrierGuid);
            if (!devicesByCarrier.ContainsKey(key))
                devicesByCarrier[key] = [];
            devicesByCarrier[key].Add(device);
        }

        // 静态入口：事件脚本
        foreach (CallGraphNode node in _nodes)
        {
            if (node.EventName != null)
                _entryPoints.Add(new CallGraphEntryPoint(node, EntryPointType.EventScript, node.EventName));
        }

        // 遍历所有零件的所有脚本（含事件脚本与代码仓库脚本），单次 AST 遍历同时完成边构建和动态入口点检测
        foreach (DeviceEntity callerDevice in _model.List<DeviceEntity>())
        {
            List<DeviceEntity> carrierDevices = GetScopeDevices(callerDevice, devicesByCarrier);
            string callerDevicePath = callerDevice.device目录_完整相对路径;

            foreach (ScriptGroupEntityBase group in callerDevice.ScriptGroups)
            {
                foreach (ScriptEntryInfo entry in group.ScriptEntries)
                {
                    if (!_nodeLookup.ContainsKey((callerDevicePath, entry.Name)))
                        continue;
                    CallGraphNode fromNode = _nodeLookup[(callerDevicePath, entry.Name)];

                    string? scriptPath = ResolveScriptPath(entry, group);
                    if (scriptPath == null)
                        continue;

                    BescriptParseData? parseData = _analysis.ParseScript(scriptPath);
                    if (parseData?.Ast == null)
                        continue;

                    ProcessAstNodes(parseData.Ast.RootNodes, callerDevice, carrierDevices, devicesByCarrier, fromNode);
                }
            }
        }
    }

    private void ProcessAstNodes(
        IReadOnlyList<BescriptAstNode> roots,
        DeviceEntity callerDevice,
        List<DeviceEntity> carrierDevices,
        Dictionary<(string CarrierKind, string CarrierGuid), List<DeviceEntity>> devicesByCarrier,
        CallGraphNode fromNode)
    {
        foreach (BescriptAstNode astNode in roots.FlattenNodes())
        {
            if (astNode.Kind != BescriptStatementKind.Call)
                continue;

            BescriptCallSignature call = ((IHasCall)astNode).Call;
            string methodName = ExtractMethodNameFromSaveName(call.SaveName);

            // 规则1+2: Act 调用
            if (call is ActCallSignature actCall)
            {
                ResolveActCall(callerDevice, actCall, carrierDevices, fromNode, astNode.Line);
            }
            // 规则3: 动态调用（忽略 Fun/None/Toolf）
            else if (call.Kind is not (BescriptCallKind.Fun or BescriptCallKind.None or BescriptCallKind.Toolf))
            {
                string? methodText = ExtractDynamicMethodText(call, methodName);
                if (methodText == null)
                    continue;

                ResolveDynamicCall(methodText, fromNode, astNode.Line);

                // 动态入口点检测
                EntryPointType? epType = ClassifyEntryPoint(methodName);
                if (epType != null)
                    AddDynamicEntryPoints(methodText, methodName, epType.Value);
            }
        }
    }

    private static List<DeviceEntity> GetScopeDevices(
        DeviceEntity device,
        Dictionary<(string CarrierKind, string CarrierGuid), List<DeviceEntity>> devicesByCarrier)
    {
        (string CarrierKind, string CarrierGuid) carrierKey = (device.CarrierKind, device.CarrierGuid);
        if (!devicesByCarrier.ContainsKey(carrierKey))
            return [];

        List<DeviceEntity> carrierDevices = devicesByCarrier[carrierKey];

        // 1110 作用域切换
        if (device.DeviceType == DeviceTypeIds.BirthBuilding
            && GuidText.Validate(device.MechFileGuid)
            && devicesByCarrier.ContainsKey(("Mech", device.MechFileGuid)))
        {
            return devicesByCarrier[("Mech", device.MechFileGuid)];
        }

        return carrierDevices;
    }

    // =========================================================================
    // 规则1+2: Act 调用解析
    // =========================================================================

    private void ResolveActCall(
        DeviceEntity callerDevice,
        ActCallSignature call,
        List<DeviceEntity> carrierDevices,
        CallGraphNode fromNode,
        int line)
    {
        string ownerName = call.OwnerName;

        // "this" 关键字
        if (!call.OwnerNameIsAlias && string.Equals(ownerName, "this", StringComparison.Ordinal))
        {
            string targetPath = ResolveSelfTarget(callerDevice);
            (string targetPath, string FunctionName) key = (targetPath, call.FunctionName);
            if (_nodeLookup.ContainsKey(key))
            {
                _edges.Add(new CallGraphEdge(fromNode, _nodeLookup[key], CallEdgeType.Static, line));
            }
            return;
        }

        // 解析目标零件
        DeviceEntity? targetDevice = ResolveTargetDevice(ownerName, call.OwnerNameIsAlias, carrierDevices);
        if (targetDevice == null)
            return;

        // 规则1: 静态边
        AddEdgeIfExists(fromNode, targetDevice.device目录_完整相对路径, call.FunctionName, CallEdgeType.Static, line);

        // 规则2: 1110 潜在静态引用（非自身目标）
        if (callerDevice.DeviceType == DeviceTypeIds.BirthBuilding
            && !string.Equals(targetDevice.device目录_完整相对路径, callerDevice.device目录_完整相对路径, StringComparison.Ordinal))
        {
            ResolvePotentialStatic1110(targetDevice, call.FunctionName, fromNode, line);
        }
    }

    private string ResolveSelfTarget(DeviceEntity callerDevice)
    {
        if (callerDevice.DeviceType != DeviceTypeIds.BirthBuilding
            || !GuidText.Validate(callerDevice.MechFileGuid)
            || _model.FindByGuid<MechEntityBase>(callerDevice.MechFileGuid) is not MechEntityBase mech)
        {
            return callerDevice.device目录_完整相对路径;
        }

        return mech.Devices.OfType<DeviceEntity>()
            .First(static d => d.DeviceType == DeviceTypeIds.CoreDevice)
            .device目录_完整相对路径;
    }

    private static DeviceEntity? ResolveTargetDevice(
        string? ownerName,
        bool ownerNameIsAlias,
        List<DeviceEntity> carrierDevices)
    {
        string? lookupName = ownerName;
        if (ownerNameIsAlias && ownerName is not null && ownerName.StartsWith("/", StringComparison.Ordinal))
            lookupName = ownerName[1..];

        // 数字 ID 查找
        if (!ownerNameIsAlias && int.TryParse(lookupName, out int targetId))
        {
            foreach (DeviceEntity d in carrierDevices)
            {
                if (d.DeviceId == targetId)
                    return d;
            }
            return null;
        }

        // 别名查找（不唯一时不解析）
        DeviceEntity? match = null;
        foreach (DeviceEntity d in carrierDevices)
        {
            if (string.Equals(d.LogicName, lookupName, StringComparison.Ordinal))
            {
                if (match != null)
                    return null;
                match = d;
            }
        }
        return match;
    }

    /// <summary>
    /// 规则2: 1110 的 Act 调用非自身目标时，在所有机械中匹配
    /// (同名 OR 同DeviceId) AND 同DeviceType
    /// </summary>
    private void ResolvePotentialStatic1110(
        DeviceEntity resolvedTarget,
        string functionName,
        CallGraphNode fromNode,
        int line)
    {
        int targetType = resolvedTarget.DeviceType;
        string? targetLogicName = resolvedTarget.LogicName;
        int targetDeviceId = resolvedTarget.DeviceId;

        foreach (CallGraphNode node in _nodes)
        {
            if (node.DeviceType != targetType
                || !string.Equals(node.MethodName, functionName, StringComparison.Ordinal)
                || ReferenceEquals(node, fromNode))
            {
                continue;
            }

            bool matches;
            if (targetLogicName is not null)
            {
                matches = string.Equals(node.DeviceLogicName, targetLogicName, StringComparison.Ordinal);
            }
            else
            {
                matches = node.DeviceId == targetDeviceId;
            }

            if (matches)
                _edges.Add(new CallGraphEdge(fromNode, node, CallEdgeType.PotentialStatic, line));
        }
    }

    // =========================================================================
    // 规则3: 动态调用解析
    // =========================================================================

    private void ResolveDynamicCall(string methodText, CallGraphNode fromNode, int line)
    {
        // 带"."的去前缀（取最后一个"."之后的部分）
        int lastDot = methodText.LastIndexOf('.');
        string bareName = methodText;
        if (lastDot >= 0)
            bareName = methodText[(lastDot + 1)..];

        if (!_methodNameIndex.ContainsKey(bareName))
            return;

        foreach (CallGraphNode target in _methodNameIndex[bareName])
            _edges.Add(new CallGraphEdge(fromNode, target, CallEdgeType.Dynamic, line));
    }

    /// <summary>
    /// 从动态调用中提取方法名字符串参数。
    /// 条件与 LintEngine.CallTargets.cs:271-321 一致。
    /// </summary>
    private static string? ExtractDynamicMethodText(BescriptCallSignature call, string methodName)
    {
        // setAnimEvent 的回调在第3个参数
        if (string.Equals(methodName, "setAnimEvent", StringComparison.Ordinal))
        {
            if (call.Arguments.Count < 3)
                return null;

            BescriptCallArgument cbArg = call.Arguments[2];
            if (cbArg.IsVariable)
                return null;
            if (BescriptVarTypeHelper.TryParse(cbArg.TypeName) != BescriptVarType.String)
                return null;
            return cbArg.DecodedText;
        }

        if (!IsDynamicCallMethod(methodName))
            return null;

        if (call.Arguments.Count == 0)
            return null;

        BescriptCallArgument firstArg = call.Arguments[0];
        if (firstArg.IsVariable)
            return null;
        if (BescriptVarTypeHelper.TryParse(firstArg.TypeName) != BescriptVarType.String)
            return null;
        return firstArg.DecodedText;
    }

#pragma warning disable TK006 // 存档名到方法名的提取逻辑，独立职责
    private static string ExtractMethodNameFromSaveName(string saveName)
    {
        int lastDot = saveName.LastIndexOf('.');
        if (lastDot >= 0)
            return saveName[(lastDot + 1)..];
        return saveName;
    }
#pragma warning restore TK006

    private static bool IsDynamicCallMethod(string methodName) =>
        methodName.StartsWith("call", StringComparison.Ordinal)
        || methodName.StartsWith("netCallFun", StringComparison.Ordinal)
        || methodName.StartsWith("unReliableNetCallFun", StringComparison.Ordinal)
        || methodName.StartsWith("callReturn", StringComparison.Ordinal)
        || methodName.StartsWith("laterCall", StringComparison.Ordinal)
        || methodName is "loopFun" or "DisableFun" or "fixedLoopAct"
            or "addClickCB" or "addPointDownCB" or "addPointUpCB"
            or "addPointEnterCB" or "addPointExitCB";

    private static EntryPointType? ClassifyEntryPoint(string methodName)
    {
        if (methodName is "loopFun" or "fixedLoopAct")
            return EntryPointType.PerFrameLoop;

        if (methodName is "addClickCB" or "addPointDownCB" or "addPointUpCB"
            or "addPointEnterCB" or "addPointExitCB" or "setAnimEvent"
            or "SetFrameAnimCallBack" or "SetCallBack")
            return EntryPointType.DynamicCallback;

        return null;
    }

    private void AddDynamicEntryPoints(string methodText, string triggerMethod, EntryPointType epType)
    {
        int lastDot = methodText.LastIndexOf('.');
        string bareName = methodText;
        if (lastDot >= 0)
            bareName = methodText[(lastDot + 1)..];

        if (!_methodNameIndex.ContainsKey(bareName))
            return;

        foreach (CallGraphNode target in _methodNameIndex[bareName])
        {
            if (_entryPoints.All(ep => !ReferenceEquals(ep.Node, target)))
                _entryPoints.Add(new CallGraphEntryPoint(target, epType, triggerMethod));
        }
    }

    // =========================================================================
    // Phase 4 - 死代码分析
    // =========================================================================

    private void FindDeadCode()
    {
        // 构建邻接表
        Dictionary<CallGraphNode, List<CallGraphNode>> adjacency = new(ReferenceEqualityComparer.Instance);
        foreach (CallGraphEdge edge in _edges)
        {
            if (!adjacency.ContainsKey(edge.From))
                adjacency[edge.From] = [];
            adjacency[edge.From].Add(edge.To);
        }

        // BFS 从所有入口点出发
        HashSet<CallGraphNode> reachable = new(ReferenceEqualityComparer.Instance);
        Queue<CallGraphNode> queue = new();

        foreach (CallGraphEntryPoint ep in _entryPoints)
        {
            if (reachable.Add(ep.Node))
                queue.Enqueue(ep.Node);
        }

        while (queue.Count > 0)
        {
            CallGraphNode current = queue.Dequeue();
            if (adjacency.ContainsKey(current))
            {
                foreach (CallGraphNode target in adjacency[current])
                {
                    if (reachable.Add(target))
                        queue.Enqueue(target);
                }
            }
        }

        // 不可达 = 死代码
        foreach (CallGraphNode node in _nodes)
        {
            if (!reachable.Contains(node))
                _deadCodeCandidates.Add(node);
        }
    }

    // =========================================================================
    // 辅助
    // =========================================================================

    private void AddEdgeIfExists(CallGraphNode from, string targetDevicePath, string functionName, CallEdgeType edgeType, int line)
    {
        (string targetDevicePath, string functionName) key = (targetDevicePath, functionName);
        if (_nodeLookup.ContainsKey(key))
        {
            _edges.Add(new CallGraphEdge(from, _nodeLookup[key], edgeType, line));
        }
        else if (edgeType == CallEdgeType.Static)
        {
            _unresolvedActCalls.Add(new UnresolvedActCall(from, targetDevicePath, functionName, line));
        }
    }

    // =========================================================================
    // Phase 5 - 递归检测
    // =========================================================================

    private void FindRecursion()
    {
        Dictionary<CallGraphNode, List<CallGraphNode>> adjacency = new(ReferenceEqualityComparer.Instance);
        foreach (CallGraphEdge edge in _edges)
        {
            if (edge.EdgeType is not (CallEdgeType.Static or CallEdgeType.PotentialStatic))
                continue;

            if (!adjacency.ContainsKey(edge.From))
                adjacency[edge.From] = [];
            adjacency[edge.From].Add(edge.To);
        }

        HashSet<CallGraphNode> visiting = new(ReferenceEqualityComparer.Instance);
        HashSet<CallGraphNode> visited = new(ReferenceEqualityComparer.Instance);
        List<(CallGraphNode Node, CallGraphEdge? IncomingEdge)> stack = [];

        foreach (CallGraphNode node in _nodes)
            Dfs(node);

        return;

        void Dfs(CallGraphNode node)
        {
            if (visited.Contains(node))
                return;

            if (!visiting.Add(node))
                return;

            stack.Add((node, null));

            if (adjacency.TryGetValue(node, out List<CallGraphNode>? nextList))
            {
                foreach (CallGraphNode to in nextList)
                {
                    if (visiting.Contains(to))
                    {
                        CallGraphEdge closingEdge = _edges.First(e =>
                            ReferenceEquals(e.From, node) && ReferenceEquals(e.To, to));
                        _cycles.Add(new CallGraphCycle(BuildCyclePath(to, closingEdge), closingEdge));
                    }
                    else
                    {
                        stack.Add((to, null));
                        Dfs(to);
                        stack.RemoveAt(stack.Count - 1);
                    }
                }
            }

            visiting.Remove(node);
            visited.Add(node);
            stack.RemoveAt(stack.Count - 1);
        }

        IReadOnlyList<CallGraphNode> BuildCyclePath(CallGraphNode target, CallGraphEdge backEdge)
        {
            List<CallGraphNode> path = [backEdge.From, target];
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                path.Add(stack[i].Node);
                if (ReferenceEquals(stack[i].Node, target))
                    break;
            }

            path.Reverse();
            return path;
        }
    }
}
