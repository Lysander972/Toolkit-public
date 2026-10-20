using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

// ┌────────────────────────────────────────────────────────────────────────────┐
// │ 翻译规范 (Translation Convention)                                          │
// │ • Mech → 机械 (禁止译为"机甲")                                              │
// │ • Device → 零件 (禁止译为"设备"，除非指物理设备如手机)                       │
// └────────────────────────────────────────────────────────────────────────────┘

public sealed record GlobalVarInfo(string Name, string Type, bool IsGlobal);
public sealed record GlobalVarListData(IReadOnlyList<GlobalVarInfo> Variables);
public sealed record GlobalVarOp(string Op, string Name, string? NewName, string? Type);
public sealed record GlobalVarOpResult(string Op, string Name, bool Success, string? Message, int? ReferenceCount);
public sealed record GlobalVarBatchData(IReadOnlyList<GlobalVarOpResult> Results, IReadOnlyList<string> AffectedScripts, bool Applied, string? BehaviorNote = null);
public sealed record GlobalVarRenameItem(string OldName, string NewName);
public sealed record VarToGlobalData(string GlobalVarName, bool VarsJsonUpdated, IReadOnlyList<string> AffectedPaths);
public sealed record VarsToLocalData(IReadOnlyList<string> RemovedVars, bool VarsJsonUpdated, IReadOnlyList<string> AffectedPaths);

public sealed class GlobalVarOperation
{
    private readonly ProjectModel _model;
    private readonly BescriptScanner _scanner = new();

    public GlobalVarOperation(ProjectModel model)
    {
        _model = model;
    }

    public RefactorResult<GlobalVarListData> List(string devicePath)
    {
        string deviceRelativePath = WorkspacePath.RequireRelativePath(devicePath);
        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity device)
            return RefactorResult<GlobalVarListData>.Fail($"未找到零件实体: {deviceRelativePath}");

        // 通过 ProjectModel 获取 vars 配置（device.Vars 在 Load 时已解析）
        GlobalVarInfo[] vars = device.Vars.Variables
            .Where(static v => v.Name.Length > 0)
            .Select(static v => new GlobalVarInfo(v.Name, v.TypeName, true))
            .ToArray();

        return RefactorResult<GlobalVarListData>.Success(new GlobalVarListData(vars));
    }

    public RefactorResult<GlobalVarBatchData> Batch(string devicePath, IReadOnlyList<GlobalVarOp> operations)
    {
        if (operations.Count == 0)
            return RefactorResult<GlobalVarBatchData>.Fail("operations 不能为空");

        string deviceRelativePath = WorkspacePath.RequireRelativePath(devicePath);
        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity device)
            return RefactorResult<GlobalVarBatchData>.Fail($"未找到零件实体: {deviceRelativePath}");

        // 从 device.Vars 初始化 items
        List<VarsJsonItem> items = device.Vars.Variables
            .Select(v => new VarsJsonItem(v.Name, v.TypeName, v.Comment))
            .ToList();

        Dictionary<string, int> nameToIndex = items.Select((item, i) => (item, i)).Where(x => x.item.Name.Length > 0)
            .GroupBy(x => x.item.Name).ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);

        List<GlobalVarOpResult> results = [];
        HashSet<string> affected = new(StringComparer.Ordinal);
        List<ToolError> warnings = [];
        WritableProjectRootEntity writableRoot = _model.Root.ToWritable();

        foreach (GlobalVarOp op in operations)
        {
            GlobalVarOpResult r = op.Op switch
            {
                "create" => ExecCreate(op, items, nameToIndex),
                "rename" => ExecRename(op, items, nameToIndex, deviceRelativePath, affected, warnings, writableRoot),
                "delete" => ExecDelete(op, items, nameToIndex),
                _ => new GlobalVarOpResult(op.Op, op.Name, false, $"未知 op: {op.Op}", null)
            };
            results.Add(r);
        }

        // 统一持久化所有变更（vars.json + 脚本引用更新）
        try
        {
            ProjectEntity current = writableRoot;
            foreach (string segment in deviceRelativePath.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            WritableDeviceEntity writableDevice = (WritableDeviceEntity)current;

            writableDevice.Vars = new VarsConfig
            {
                Variables = items.Select(i => new VarInfo(i.Name, i.TypeName, i.Comment)).ToList()
            };
            _model.SaveChanges(writableRoot);
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<GlobalVarBatchData>.Fail($"保存变更失败: {ex.Message}");
        }

        return RefactorResult<GlobalVarBatchData>.Success(new GlobalVarBatchData(results, affected.ToArray(), true), warnings);
    }

    public RefactorResult<GlobalVarBatchData> RenameOnlyDeviceScripts(string devicePath, IReadOnlyList<GlobalVarRenameItem> renames)
    {
        if (renames.Count == 0)
            return RefactorResult<GlobalVarBatchData>.Fail("renames 不能为空");

        string deviceRelativePath = WorkspacePath.RequireRelativePath(devicePath);
        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity device)
            return RefactorResult<GlobalVarBatchData>.Fail($"未找到零件实体: {deviceRelativePath}");

        List<VarsJsonItem> items = device.Vars.Variables
            .Select(v => new VarsJsonItem(v.Name, v.TypeName, v.Comment))
            .ToList();

        Dictionary<string, int> nameToIndex = items.Select((item, i) => (item, i))
            .Where(x => x.item.Name.Length > 0)
            .GroupBy(x => x.item.Name)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);

        HashSet<string> seenOldNames = new(StringComparer.Ordinal);
        HashSet<string> seenNewNames = new(StringComparer.Ordinal);
        foreach (GlobalVarRenameItem rename in renames)
        {
            if (rename.OldName == rename.NewName) return RefactorResult<GlobalVarBatchData>.Fail($"新名称与旧名称相同: {rename.OldName}");
            if (BescriptTextCodec.ReplaceInvalidChar(rename.NewName.AsSpan()).IndexOf('@') >= 0)
                return RefactorResult<GlobalVarBatchData>.Fail($"变量名不允许包含需转义字符: {rename.NewName}");
            if (!seenOldNames.Add(rename.OldName))
                return RefactorResult<GlobalVarBatchData>.Fail($"oldName 重复: {rename.OldName}");
            if (!seenNewNames.Add(rename.NewName))
                return RefactorResult<GlobalVarBatchData>.Fail($"newName 重复: {rename.NewName}");
            if (!nameToIndex.ContainsKey(rename.OldName))
                return RefactorResult<GlobalVarBatchData>.Fail($"变量不存在: {rename.OldName}");
            if (nameToIndex.ContainsKey(rename.NewName))
                return RefactorResult<GlobalVarBatchData>.Fail($"newName 已存在: {rename.NewName}");
        }

        Dictionary<string, string> renameMap = renames.ToDictionary(x => x.OldName, x => x.NewName, StringComparer.Ordinal);
        foreach (GlobalVarRenameItem rename in renames)
        {
            string current = rename.NewName;
            while (renameMap.TryGetValue(current, out string? next))
            {
                if (string.Equals(next, rename.OldName, StringComparison.Ordinal))
                    return RefactorResult<GlobalVarBatchData>.Fail($"批量重命名存在环: {rename.OldName}");
                current = next;
            }
        }

        List<GlobalVarOpResult> results = [];
        HashSet<string> affected = new(StringComparer.Ordinal);
        List<ToolError> warnings = [];
        WritableProjectRootEntity writableRoot = _model.Root.ToWritable();
        foreach (GlobalVarRenameItem rename in renames)
        {
            int index = nameToIndex[rename.OldName];
            items[index] = new VarsJsonItem(rename.NewName, items[index].TypeName, items[index].Comment);
            nameToIndex.Remove(rename.OldName);
            nameToIndex[rename.NewName] = index;
        }

        foreach (InlineScriptEntity script in _model.EnumerateEntitiesUnderPath<InlineScriptEntity>(deviceRelativePath + "/"))
        {
            string text = script.SourceText;
            string patchedText = text;
            int total = 0;
            foreach (GlobalVarRenameItem rename in renames)
            {
                (string newText, int cnt) = RenameGlobalVarInScript(patchedText, rename.OldName, rename.NewName);
                if (cnt <= 0) continue;
                patchedText = newText;
                total += cnt;
            }

            if (total <= 0) continue;

            affected.Add(script.实体_完整相对路径标识);
            ProjectEntity current = writableRoot;
            foreach (string segment in script.实体_完整相对路径标识.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            WritableInlineScriptEntity writableScript = (WritableInlineScriptEntity)current;
            writableScript.SourceText = patchedText;
        }

        HashSet<string> visitedGuids = new(StringComparer.Ordinal);
        foreach (ScriptGroupEntity sg in _model.EnumerateEntitiesUnderPath<ScriptGroupEntity>(deviceRelativePath + "/"))
        {
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid)) continue;
                string entryGuid = entry.Guid;
                if (!visitedGuids.Add(entryGuid)) continue;
                if (_model.FindByGuid<CodeRepoScriptEntity>(entryGuid) is not CodeRepoScriptEntity codeRepo) continue;

                int refs = 0;
                foreach (GlobalVarRenameItem rename in renames)
                    refs += _scanner.ScanGlobalVarRefs(codeRepo.SourceText, new[] { rename.OldName }).Count;
                if (refs <= 0) continue;

                warnings.Add(new ToolError(codeRepo.实体_完整相对路径标识, null, null,
                    $"coderepo '{entry.Name}' 仍有 {refs} 处旧全局变量引用未级联修改。建议改名后立即执行 read_lint 检查断裂引用。"));
            }
        }

        foreach (GlobalVarRenameItem rename in renames)
            results.Add(new GlobalVarOpResult("rename", rename.OldName, true, null, null));

        try
        {
            ProjectEntity current = writableRoot;
            foreach (string segment in deviceRelativePath.Split('/'))
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            WritableDeviceEntity writableDevice = (WritableDeviceEntity)current;

            writableDevice.Vars = new VarsConfig
            {
                Variables = items.Select(i => new VarInfo(i.Name, i.TypeName, i.Comment)).ToList()
            };
            _model.SaveChanges(writableRoot);
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<GlobalVarBatchData>.Fail($"保存变更失败: {ex.Message}");
        }

        return RefactorResult<GlobalVarBatchData>.Success(
            new GlobalVarBatchData(results, affected.ToArray(), true,
                "本工具不会级联修改 coderepo 脚本。建议改名后立即执行 read_lint 检查断裂引用。"),
            warnings);
    }

#pragma warning disable TK006 // 全局变量操作方法，与 ExecRename/ExecDelete 等对称
    private GlobalVarOpResult ExecCreate(GlobalVarOp op, List<VarsJsonItem> items, Dictionary<string, int> idx)
    {
        if (string.IsNullOrWhiteSpace(op.Type)) return new GlobalVarOpResult(op.Op, op.Name, false, "create 需要 type", null);
        if (BescriptTextCodec.ReplaceInvalidChar(op.Name.AsSpan()).IndexOf('@') >= 0)
            return new GlobalVarOpResult(op.Op, op.Name, false, "变量名不允许包含需转义字符", null);
        if (idx.ContainsKey(op.Name)) return new GlobalVarOpResult(op.Op, op.Name, false, "变量已存在", null);
        items.Add(new VarsJsonItem(op.Name, op.Type));
        idx[op.Name] = items.Count - 1;
        return new GlobalVarOpResult(op.Op, op.Name, true, null, null);
    }
#pragma warning restore TK006

    private GlobalVarOpResult ExecRename(GlobalVarOp op, List<VarsJsonItem> items, Dictionary<string, int> idx,
        string deviceDir, HashSet<string> affected, List<ToolError> warnings, WritableProjectRootEntity writableRoot)
    {
        if (string.IsNullOrWhiteSpace(op.NewName)) return new GlobalVarOpResult(op.Op, op.Name, false, "rename 需要 newName", null);
        if (BescriptTextCodec.ReplaceInvalidChar(op.NewName.AsSpan()).IndexOf('@') >= 0)
            return new GlobalVarOpResult(op.Op, op.Name, false, "变量名不允许包含需转义字符", null);
        if (!idx.TryGetValue(op.Name, out int i)) return new GlobalVarOpResult(op.Op, op.Name, false, "变量不存在", null);
        if (idx.ContainsKey(op.NewName)) return new GlobalVarOpResult(op.Op, op.Name, false, "newName 已存在", null);

        items[i] = new VarsJsonItem(op.NewName, items[i].TypeName, items[i].Comment);
        // coderepo 被多零件链接时，此处修改将导致其他零件的全局变量引用断裂——已知行为，不负责处理，断裂情况通过 warnings 返回。
        int cnt = RenameInScripts(writableRoot, deviceDir, op.Name, op.NewName, affected, warnings);
        idx.Remove(op.Name); idx[op.NewName] = i;
        return new GlobalVarOpResult(op.Op, op.Name, true, null, cnt);
    }

#pragma warning disable TK006 // 全局变量操作方法，与 ExecCreate/ExecRename 对称
    private GlobalVarOpResult ExecDelete(GlobalVarOp op, List<VarsJsonItem> items, Dictionary<string, int> idx)
    {
        if (!idx.TryGetValue(op.Name, out int i)) return new GlobalVarOpResult(op.Op, op.Name, false, "变量不存在", null);
        items.RemoveAt(i);
        idx.Clear();
        for (int j = 0; j < items.Count; j++)
            if (items[j].Name.Length > 0 && !idx.ContainsKey(items[j].Name)) idx[items[j].Name] = j;
        return new GlobalVarOpResult(op.Op, op.Name, true, null, null);
    }
#pragma warning restore TK006

    private int RenameInScripts(WritableProjectRootEntity writableRoot, string devicePath, string old, string @new, HashSet<string> affected, List<ToolError> warnings)
    {
        int total = 0;

        // 内联脚本
        foreach (InlineScriptEntity script in _model.EnumerateEntitiesUnderPath<InlineScriptEntity>(devicePath + "/"))
        {
            string text = script.SourceText;
            (string newText, int cnt) = RenameGlobalVarInScript(text, old, @new);
            if (cnt > 0)
            {
                total += cnt;
                affected.Add(script.实体_完整相对路径标识);
                ProjectEntity current = writableRoot;
                foreach (string segment in script.实体_完整相对路径标识.Split('/'))
                    current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
                WritableInlineScriptEntity writableScript = (WritableInlineScriptEntity)current;
                writableScript.SourceText = newText;
            }
        }

        // coderepo 脚本（通过脚本组 guid 链接到此零件的）
        HashSet<string> visitedGuids = new(StringComparer.Ordinal);
        foreach (ScriptGroupEntity sg in _model.EnumerateEntitiesUnderPath<ScriptGroupEntity>(devicePath + "/"))
        {
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid)) continue;
                string entryGuid = entry.Guid;
                if (!visitedGuids.Add(entryGuid)) continue;
                if (_model.FindByGuid<CodeRepoScriptEntity>(entryGuid) is not CodeRepoScriptEntity codeRepo) continue;

                string text = codeRepo.SourceText;
                (string newText, int cnt) = RenameGlobalVarInScript(text, old, @new);
                if (cnt > 0)
                {
                    total += cnt;
                    affected.Add(codeRepo.实体_完整相对路径标识);
                    warnings.Add(new ToolError(codeRepo.实体_完整相对路径标识, null, null,
                        $"coderepo '{entry.Name}' 已同步修改。若该脚本被其他零件链接，其他零件处的全局变量引用将发生断裂，需手动处理。"));
                    ProjectEntity current = writableRoot;
                    foreach (string segment in codeRepo.实体_完整相对路径标识.Split('/'))
                        current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
                    WritableCodeRepoScriptEntity writableCodeRepo = (WritableCodeRepoScriptEntity)current;
                    writableCodeRepo.SourceText = newText;
                }
            }
        }

        return total;
    }

    private static (string NewText, int Count) RenameGlobalVarInScript(string text, string old, string @new)
    {
        BescriptScriptInfo info = BescriptTextParser.Parse(string.Empty, text).Script;
        BescriptAst ast = info.Ast;

        int total = 0;
        List<BescriptAstNode> newRootNodes = new(ast.RootNodes.Count);
        foreach (BescriptAstNode node in ast.RootNodes)
        {
            (BescriptAstNode newNode, int count) = RenameGlobalVarInNode(node, old, @new);
            newRootNodes.Add(newNode);
            total += count;
        }

        if (total == 0) return (text, 0);
        BescriptAst newAst = ast with { RootNodes = newRootNodes };
        return (BescriptAstSerializer.SerializeFull(newAst), total);
    }

    private static (BescriptAstNode Node, int Count) RenameGlobalVarInNode(BescriptAstNode node, string old, string @new)
    {
        int count = 0;

        IReadOnlyList<BescriptAstNode> newChildren = node.Children;
        if (node.Children.Count > 0)
        {
            List<BescriptAstNode> list = new(node.Children.Count);
            foreach (BescriptAstNode child in node.Children)
            {
                (BescriptAstNode newChild, int childCount) = RenameGlobalVarInNode(child, old, @new);
                list.Add(newChild);
                count += childCount;
            }
            newChildren = list;
        }

        if (node is IHasCall hasCall)
        {
            (BescriptCallSignature call, int callCount) = RenameGlobalVarInCall(hasCall.Call, old, @new);
            count += callCount;
            if (call == hasCall.Call && newChildren == node.Children)
                return (node, count);
            return (node.WithCallAndChildren(call, newChildren), count);
        }

        if (newChildren == node.Children)
            return (node, count);
        return (node.WithChildren(newChildren), count);
    }

    private static (BescriptCallSignature Call, int Count) RenameGlobalVarInCall(BescriptCallSignature call, string old, string @new)
    {
        int count = 0;
        bool changed = false;

        ReturnVarDecl? newReturnVar = call.ReturnVar;
        if (call.ReturnVar is { } ret && string.Equals(ret.Name, old, StringComparison.Ordinal))
        { newReturnVar = new ReturnVarDecl(ret.TypeName, @new); changed = true; count++; }

        string newFunctionName = call.FunctionName;
        if ((call.Kind == BescriptCallKind.Varf || call.Kind == BescriptCallKind.Dvf)
            && string.Equals(call.FunctionName, old, StringComparison.Ordinal))
        { newFunctionName = @new; changed = true; count++; }

        List<BescriptCallArgument>? newArgs = null;
        if (call.Arguments.Count > 0)
        {
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                BescriptCallArgument arg = call.Arguments[i];
                if (arg.IsVariable && string.Equals(arg.DecodedText, old, StringComparison.Ordinal))
                {
                    if (newArgs == null) newArgs = call.Arguments.ToList();
                    newArgs[i] = arg with { DecodedText = @new };
                    changed = true; count++;
                }
            }
        }

        if (!changed) return (call, 0);

        BescriptCallSignature result;
        switch (call)
        {
            case VarfCallSignature v:
                {
                    (string ownerName, bool matched) = TryRename(v.OwnerName, old, @new, v.OwnerNameIsAlias);
                    result = v with { ReturnVar = newReturnVar, FunctionName = newFunctionName, OwnerName = ownerName };
                    if (matched) count++;
                    break;
                }
            case ActCallSignature a:
                {
                    (string ownerName, bool matched) = TryRename(a.OwnerName, old, @new, a.OwnerNameIsAlias);
                    result = a with { ReturnVar = newReturnVar, OwnerName = ownerName };
                    if (matched) count++;
                    break;
                }
            case DvfCallSignature d:
                {
                    (string ownerName, bool matched) = TryRename(d.OwnerName, old, @new, d.OwnerNameIsAlias);
                    result = d with { ReturnVar = newReturnVar, FunctionName = newFunctionName, OwnerName = ownerName };
                    if (matched) count++;
                    break;
                }
            default:
                result = call with { ReturnVar = newReturnVar, FunctionName = newFunctionName };
                break;
        }

        if (newArgs is not null)
            result = result with { Arguments = newArgs };

        return (result, count);
    }

    private static (string Name, bool Matched) TryRename(string current, string oldName, string newName, bool isAlias)
    {
        if (!isAlias && string.Equals(current, oldName, StringComparison.Ordinal))
            return (newName, true);
        return (current, false);
    }

    /// <summary>
    /// 将局部变量提升为全局变量：仅在 vars.json 中创建全局变量，不修改任何 .code 文件。
    /// 类型从目标脚本方法体局部变量首次出现位置自动提取（需要显式类型注解 var xxx: Type）。
    /// </summary>
    public (VarToGlobalData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) CreateGlobalVarOnly(
        string scriptGroupPath,
        string scriptName,
        string varName,
        bool allowOtherUsages)
    {
        List<ToolError> errors = [];
        List<ToolError> warnings = [];
        List<string> affectedPaths = [];

        string sgPath = WorkspacePath.RequireRelativePath(scriptGroupPath);

        // 脚本组路径的上一级即为零件路径
        int lastSlash = sgPath.LastIndexOf('/');
        if (lastSlash <= 0)
        {
            errors.Add(new ToolError(sgPath, null, null, $"无法从脚本组路径 '{sgPath}' 推导零件路径"));
            return (null, errors, warnings);
        }
        string devRelPath = sgPath[..lastSlash];

        if (_model.Find<DeviceEntity>(devRelPath) is not DeviceEntity device)
        {
            errors.Add(new ToolError(devRelPath, null, null, $"未找到零件实体: {devRelPath}"));
            return (null, errors, warnings);
        }

        // 1. 检查全局变量是否已存在
        VarInfo? existing = device.Vars.Variables.FirstOrDefault(v => v.Name == varName);
        if (existing != null)
        {
            errors.Add(new ToolError(devRelPath, null, null, $"全局变量 '{varName}' 已存在于 vars.json 中，类型为 '{existing.TypeName}'"));
            return (null, errors, warnings);
        }

        // 2. 定位目标脚本并提取类型
        string targetScriptPath = $"{sgPath}/{scriptName}.code";
        if (_model.Find<InlineScriptEntity>(targetScriptPath) is not InlineScriptEntity targetScript)
        {
            errors.Add(new ToolError(targetScriptPath, null, null, $"未找到目标脚本: {targetScriptPath}"));
            return (null, errors, warnings);
        }

        string? varType = ExtractVarTypeFromScript(targetScript.SourceText, varName);
        if (varType is null)
        {
            errors.Add(new ToolError(targetScriptPath, null, null,
                $"在目标脚本中未找到变量 '{varName}' 的声明（BeScript 格式：类型名 变量名=...）"));
            return (null, errors, warnings);
        }

        // 3. 遍历零件下所有脚本，冲突检查
        IEnumerable<InlineScriptEntity> inlineScripts = _model.EnumerateEntitiesUnderPath<InlineScriptEntity>(devRelPath + "/");
        IEnumerable<ScriptGroupEntity> scriptGroups = _model.EnumerateEntitiesUnderPath<ScriptGroupEntity>(devRelPath + "/");

        foreach (InlineScriptEntity script in inlineScripts)
        {
            string? scriptVarType = ExtractVarTypeFromScript(script.SourceText, varName);
            if (scriptVarType is null)
                continue;

            bool isTarget = script.实体_完整相对路径标识 == targetScriptPath;
            if (scriptVarType != varType)
            {
                errors.Add(new ToolError(script.实体_完整相对路径标识, null, null,
                    $"脚本中声明了同名局部变量 '{varName}'，但类型不一致（目标类型：'{varType}'，此脚本类型：'{scriptVarType}'）。请先统一类型。"));
            }
            else if (!isTarget && !allowOtherUsages)
            {
                errors.Add(new ToolError(script.实体_完整相对路径标识, null, null,
                    $"脚本中也声明了局部变量 '{varName}'（类型一致），设置 allowOtherUsages=true 以允许此全局变量被多处使用。"));
            }
        }

        foreach (ScriptGroupEntity sg in scriptGroups)
        {
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid)) continue;
                string entryGuid = entry.Guid;
                if (_model.FindByGuid<CodeRepoScriptEntity>(entryGuid) is not CodeRepoScriptEntity codeRepoScript)
                    continue;
                string? scriptVarType = ExtractVarTypeFromScript(codeRepoScript.SourceText, varName);
                if (scriptVarType is null)
                    continue;

                string displayPath = $"CodeRepo/{entry.Name}.code";
                if (scriptVarType != varType)
                {
                    errors.Add(new ToolError(displayPath, null, null,
                        $"CodeRepo 脚本中声明了同名局部变量 '{varName}'，但类型不一致（目标类型：'{varType}'，此脚本类型：'{scriptVarType}'）。请先统一类型。"));
                }
                else if (!allowOtherUsages)
                {
                    errors.Add(new ToolError(displayPath, null, null,
                        $"CodeRepo 脚本中也声明了局部变量 '{varName}'（类型一致），设置 allowOtherUsages=true 以允许此全局变量被多处使用。"));
                }
            }
        }

        if (errors.Count > 0)
            return (null, errors, warnings);

        // 4. 只写 vars.json
        affectedPaths.Add($"{devRelPath}/vars.json");
        {
            List<VarInfo> vars = device.Vars.Variables.ToList();
            vars.Add(new VarInfo(varName, varType, null));

            EntityWriteScope.ApplyContentChange<DeviceEntity, WritableDeviceEntity>(
                _model,
                device,
                d => d.ToWritable(),
                writableDevice => writableDevice.Vars = new VarsConfig { Variables = vars });
        }

        return (new VarToGlobalData(varName, true, affectedPaths), errors, warnings);
    }

    /// <summary>
    /// 删除或降级全局变量：仅删除 vars.json 条目，不修改任何 .code 文件。
    /// requireNoRefs=true（默认）：有引用则拒绝，确保完全干净地删除。
    /// requireNoRefs=false：强制删除。视图重建后，有赋值的脚本自动降为局部变量无错误；
    /// 仅读取未赋值的脚本会出现"引用未声明局部变量"错误，由调用方逐处补充局部声明。
    /// </summary>
    public (VarsToLocalData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) VarsToLocal(
        string deviceRelativePath,
        IReadOnlyList<string> varNames,
        bool requireNoRefs)
    {
        List<ToolError> errors = [];
        List<ToolError> warnings = [];
        List<string> affectedPaths = [];

        if (varNames.Count == 0)
        {
            errors.Add(new ToolError(deviceRelativePath, null, null, "varNames 不能为空"));
            return (null, errors, warnings);
        }

        string devRelPath = WorkspacePath.RequireRelativePath(deviceRelativePath);
        if (_model.Find<DeviceEntity>(devRelPath) is not DeviceEntity device)
        {
            errors.Add(new ToolError(devRelPath, null, null, $"未找到零件实体: {devRelPath}"));
            return (null, errors, warnings);
        }

        // 校验所有变量存在
        HashSet<string> existingNames = new(
            device.Vars.Variables.Select(v => v.Name),
            StringComparer.Ordinal);

        foreach (string varName in varNames)
        {
            if (!existingNames.Contains(varName))
                errors.Add(new ToolError(devRelPath, null, null, $"全局变量 '{varName}' 不存在于 vars.json"));
        }

        if (errors.Count > 0)
            return (null, errors, warnings);

        // 引用检查
        if (requireNoRefs)
        {
            List<InlineScriptEntity> inlineScripts = _model.EnumerateEntitiesUnderPath<InlineScriptEntity>(devRelPath + "/").ToList();
            List<ScriptGroupEntity> scriptGroups = _model.EnumerateEntitiesUnderPath<ScriptGroupEntity>(devRelPath + "/").ToList();

            foreach (string varName in varNames)
            {
                foreach (InlineScriptEntity script in inlineScripts)
                {
                    int refs = CountRefsInScript(script.SourceText, varName);
                    if (refs > 0)
                        errors.Add(new ToolError(script.实体_完整相对路径标识, null, null,
                            $"全局变量 '{varName}' 在此脚本中有 {refs} 处引用（注意：/* */ 是禁用代码格式，包围的引用同样计入），设置 requireNoRefs=false 可跳过引用检查"));
                }

                foreach (ScriptGroupEntity sg in scriptGroups)
                {
                    foreach (ScriptEntryInfo entry in sg.ScriptEntries)
                    {
                        if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid)) continue;
                        string entryGuid = entry.Guid;
                        if (_model.FindByGuid<CodeRepoScriptEntity>(entryGuid) is not CodeRepoScriptEntity codeRepoScript)
                            continue;
                        int refs = CountRefsInScript(codeRepoScript.SourceText, varName);
                        if (refs > 0)
                            errors.Add(new ToolError($"CodeRepo/{entry.Name}.code", null, null,
                                $"全局变量 '{varName}' 在此 CodeRepo 脚本中有 {refs} 处引用（注意：/* */ 是禁用代码格式，包围的引用同样计入），设置 requireNoRefs=false 可跳过引用检查"));
                    }
                }
            }
        }

        if (errors.Count > 0)
            return (null, errors, warnings);

        // 只删 vars.json 条目
        affectedPaths.Add($"{devRelPath}/vars.json");
        {
            HashSet<string> removeSet = new(varNames, StringComparer.Ordinal);
            List<VarInfo> vars = device.Vars.Variables
                .Where(v => !removeSet.Contains(v.Name))
                .ToList();

            EntityWriteScope.ApplyContentChange<DeviceEntity, WritableDeviceEntity>(
                _model,
                device,
                d => d.ToWritable(),
                writableDevice => writableDevice.Vars = new VarsConfig { Variables = vars });
        }

        return (new VarsToLocalData(varNames.ToList(), true, affectedPaths), errors, warnings);
    }

    private int CountRefsInScript(string sourceText, string varName)
    {
        return _scanner.ScanGlobalVarRefs(sourceText, [varName]).Count;
    }

    /// <summary>
    /// 从 BeScript 源码中提取变量的类型名（通过 AST 的 DeclaredVariables）。
    /// </summary>
    private static string? ExtractVarTypeFromScript(string sourceText, string varName)
    {
        (BescriptParseData? data, _, _) = BescriptParser.Parse(string.Empty, sourceText);
        BescriptAst? ast = data?.Ast;
        if (ast == null) return null;

        if (!ast.DeclaredVariables.TryGetValue(varName, out BescriptVarType besType))
            return null;

        return besType.ToString();
    }
}
