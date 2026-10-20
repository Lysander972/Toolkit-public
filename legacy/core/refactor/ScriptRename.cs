using Toolkit.Core.Analysis;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Refactor;

public sealed record ScriptRenameData(string Kind, string OldName, string NewName, string ScriptRelativePath, string NewScriptRelativePath,
    int UpdatedReferenceCount, IReadOnlyList<string> UpdatedReferenceFiles, IReadOnlyList<string> UpdatedListFiles,
    IReadOnlyList<string> PotentialReferenceFiles, bool Applied);

public sealed class ScriptRenameTool
{
    private readonly BescriptScanner _scanner = new();
    private readonly ProjectModel _model;

    public ScriptRenameTool(ProjectModel model)
    {
        _model = model;
    }

    public RefactorResult<ScriptRenameData> Rename(string scriptPath, string newName)
    {
        try { RefactorHelper.ValidateScriptName(newName); }
        catch (ArgumentException ex) { return RefactorResult<ScriptRenameData>.Fail(ex.Message); }
        if (newName.IndexOfAny(new[] { '/', '\\' }) >= 0) return RefactorResult<ScriptRenameData>.Fail("newName 不能包含路径分隔符");
        if (BescriptTextCodec.ReplaceInvalidChar(newName.AsSpan()).IndexOf('@') >= 0)
            return RefactorResult<ScriptRenameData>.Fail("脚本名不允许包含需转义字符");

        string relativePath = WorkspacePath.RequireRelativePath(scriptPath);

        string oldName;
        string guid = string.Empty;

        if (_model.Find(relativePath) is not ProjectEntity baseScriptEntity)
            return RefactorResult<ScriptRenameData>.Fail($"未找到脚本实体: {relativePath}");

        if (baseScriptEntity is InlineScriptEntity inlineScript)
        {
            oldName = inlineScript.ScriptName;
        }
        else if (baseScriptEntity is CodeRepoScriptEntity codeRepoScript)
        {
            oldName = codeRepoScript.ScriptName;
            guid = codeRepoScript.Guid;
        }
        else
        {
            return RefactorResult<ScriptRenameData>.Fail($"未找到脚本实体: {relativePath}");
        }
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return RefactorResult<ScriptRenameData>.Fail("新名称与旧名称相同");

        string scriptDir = RelativePathUtils.GetParentDirectory(relativePath);

        string newFullPath = RelativePathUtils.Combine(scriptDir, newName + ProjectEntityLayout.CodeExt);

        // 检查目标脚本是否已存在
        if (_model.EntityExists(newFullPath))
        {
            return RefactorResult<ScriptRenameData>.Fail($"目标脚本已存在: {WorkspacePath.RequireRelativePath(newFullPath)}");
        }

        bool isDevice = RefactorHelper.IsDeviceScriptDir(scriptDir);

        // 身份收集+条目验证+受影响文件收集（须在写回前调用：CodeRepo 的 CallerName 来自当前 list.json 状态）
        (IReadOnlyList<ScriptIdentity> identities, List<string> listFiles, string? collectError) =
            CollectIdentities(isDevice, scriptDir, guid, oldName);
        if (collectError != null)
            return RefactorResult<ScriptRenameData>.Fail(collectError);

        int cnt;
        IReadOnlyList<string> updated;
        IReadOnlyList<string> potentialFiles;

        try
        {
            WritableProjectRootEntity writableRoot = _model.Root.ToWritable();
            (cnt, updated, potentialFiles) = UpdateReferences(writableRoot, newName, identities);

            string oldFileName = RelativePathUtils.GetLastSegment(relativePath);
            string newFileName = RelativePathUtils.GetLastSegment(newFullPath);

            if (isDevice)
            {
                ProjectEntity current = writableRoot;
                foreach (string segment in scriptDir.Split('/'))
                    current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
                WritableScriptGroupEntity writableParent = (WritableScriptGroupEntity)current;

                FileRename.UpdateScriptEntries(writableParent, oldName, newName);
                WritableInlineScriptEntity writableScript = writableParent.Scripts
                    .Cast<WritableInlineScriptEntity>()
                    .Single(script => script.实体_无相对路径标识符 == oldFileName);
                writableScript.SourceText = UpdateNameHeader(writableScript.SourceText, newName);
                writableScript.SetScriptFileNameInternal(newFileName);
            }
            else
            {
                ProjectEntity current = writableRoot;
                if (scriptDir != WorkspacePath.RootDirectory)
                {
                    foreach (string segment in scriptDir.Split('/'))
                        current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
                }
                IHasChildren<ProjectEntity> parentWithChildren = (IHasChildren<ProjectEntity>)current;

                WritableCodeRepoScriptEntity repoScript = parentWithChildren.Children
                    .OfType<WritableCodeRepoScriptEntity>()
                    .Single(script => script.实体_无相对路径标识符 == oldFileName);
                repoScript.SourceText = UpdateNameHeader(repoScript.SourceText, newName);
                repoScript.SetIdentifierInternal(newFileName);

                FileRename.UpdateScriptEntriesRecursive(writableRoot, guid, oldName, newName);
            }

            _model.SaveChanges(writableRoot);
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<ScriptRenameData>.Fail($"保存变更失败: {ex.Message}");
        }

        IReadOnlyList<ToolError> potentialWarnings = potentialFiles.Count > 0
            ? potentialFiles.Select(f => new ToolError(f, null, null,
                $"包含对脚本 '{oldName}' 的 act 调用，但无法静态确认目标零件，需自行分析是否需要改为 '{newName}'")).ToList<ToolError>()
            : Array.Empty<ToolError>();

        return RefactorResult<ScriptRenameData>.Success(new ScriptRenameData(isDevice ? "device" : "repository", oldName, newName,
            relativePath, WorkspacePath.RequireRelativePath(newFullPath), cnt, updated, listFiles, potentialFiles, true), potentialWarnings);
    }

    private static string UpdateNameHeader(string text, string name)
    {
        // AST 结构化修改：解析 → 更新 Header.Name → 序列化。
        // 真实工程脚本均为规范格式（BescriptAstRoundtripProjectTests 字节级锚），序列化无损。
        BescriptScriptInfo info = BescriptTextParser.Parse(string.Empty, text).Script;
        BescriptAst ast = info.Ast;
        BescriptAst newAst = ast with { Header = ast.Header with { Name = name } };
        return BescriptAstSerializer.SerializeFull(newAst);
    }

    /// <summary>
    /// 脚本在某零件中的使用身份。
    /// 决定了该脚本名的"调用主体"：哪个零件拥有这个脚本、外部 act 调用时用的名字。
    /// </summary>
    private sealed record ScriptIdentity(string DevicePath, string CallerName);

    /// <summary>
    /// 调用方脚本的运行上下文：脚本实体 + 所有已知的链接零件路径。
    /// </summary>
    private sealed record CallerScriptContext(ProjectEntity Script, IReadOnlyList<string> KnownDevicePaths);

    /// <summary>
    /// 收集被改名脚本的所有身份、验证条目存在性、收集受影响的 list.json 路径。
    /// 须在写回前调用：CodeRepo 的 CallerName 来自当前 list.json 状态。
    /// </summary>
    private (IReadOnlyList<ScriptIdentity> Identities, List<string> ListFiles, string? Error)
        CollectIdentities(bool isDevice, string scriptDir, string guid, string oldName)
    {
        if (isDevice)
        {
            if (_model.Find<ScriptGroupEntity>(scriptDir) is not ScriptGroupEntity sg)
                return ([], [], $"未找到脚本组: {scriptDir}");

            bool found = false;
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (entry.Name == oldName) { found = true; break; }
            }
            if (!found)
                return ([], [], $"list.json 中未找到脚本条目: {oldName}");

            string devicePath = RelativePathUtils.GetParentDirectory(scriptDir);
            return ([new ScriptIdentity(devicePath, oldName)], [sg.listJson_完整相对路径], null);
        }

        List<ScriptIdentity> identities = [];
        List<string> listFiles = [];
        foreach (ScriptGroupEntity sg in _model.List<ScriptGroupEntity>())
        {
            string groupPath = sg.实体_完整相对路径标识;
            string devicePath = RelativePathUtils.GetParentDirectory(groupPath);
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (string.Equals(entry.Guid, guid, StringComparison.Ordinal))
                {
                    identities.Add(new ScriptIdentity(devicePath, entry.Name));
                    listFiles.Add(sg.listJson_完整相对路径);
                    break;
                }
            }
        }
        return (identities, listFiles, null);
    }

    /// <summary>
    /// 枚举全库所有可能的调用方脚本及其已知零件上下文。
    /// </summary>
    private IEnumerable<CallerScriptContext> GetAllCallerContexts()
    {
        foreach (InlineScriptEntity inl in _model.List<InlineScriptEntity>())
        {
            string groupPath = RelativePathUtils.GetParentDirectory(inl.实体_完整相对路径标识);
            string devicePath = RelativePathUtils.GetParentDirectory(groupPath);
            yield return new CallerScriptContext(inl, [devicePath]);
        }

        // 建立 guid → [devicePaths]，用于 CodeRepo 调用方的零件上下文收集
        Dictionary<string, List<string>> guidToDevicePaths = new(StringComparer.Ordinal);
        foreach (ScriptGroupEntity sg in _model.List<ScriptGroupEntity>())
        {
            string groupPath = sg.实体_完整相对路径标识;
            string devicePath = RelativePathUtils.GetParentDirectory(groupPath);
            foreach (ScriptEntryInfo entry in sg.ScriptEntries)
            {
                if (!entry.IsCodeRepo || !GuidText.Validate(entry.Guid)) continue;
                if (!guidToDevicePaths.TryGetValue(entry.Guid, out List<string>? paths))
                {
                    paths = [];
                    guidToDevicePaths[entry.Guid] = paths;
                }
                paths.Add(devicePath);
            }
        }

        foreach (CodeRepoScriptEntity repo in _model.ListCodeRepoScripts())
        {
            string guid = repo.Guid;
            IReadOnlyList<string> devicePaths = GuidText.Validate(guid)
                && guidToDevicePaths.TryGetValue(guid, out List<string>? linked)
                ? linked
                : Array.Empty<string>();
            yield return new CallerScriptContext(repo, devicePaths);
        }
    }

    /// <summary>
    /// 判断一个 act 调用是否可静态确认指向某个已知身份的零件。
    /// </summary>
    private bool TryConfirmActCall(BescriptActCallRange r, IReadOnlyList<string> callerDevicePaths, IReadOnlyList<ScriptIdentity> matchingIdentities)
    {
        if (callerDevicePaths.Count == 0) return false;

        string? confirmedDevicePath = null;
        foreach (string callerDevicePath in callerDevicePaths)
        {
            if (_model.Find<DeviceEntity>(callerDevicePath) is not DeviceEntity callerDevice)
                return false;

            string? resolved = RefactorHelper.ResolveActTargetDevicePath(r.OwnerName, r.OwnerNameIsAlias, callerDevice, _model);
            if (resolved == null) return false;

            bool matchesAny = false;
            foreach (ScriptIdentity id in matchingIdentities)
            {
                if (string.Equals(resolved, id.DevicePath, StringComparison.Ordinal))
                {
                    matchesAny = true;
                    break;
                }
            }
            if (!matchesAny) return false;

            if (confirmedDevicePath == null)
                confirmedDevicePath = resolved;
            else if (!string.Equals(confirmedDevicePath, resolved, StringComparison.Ordinal))
                return false;
        }
        return confirmedDevicePath != null;
    }

    /// <summary>
    /// 引用更新：
    ///   B = 能静态确认目标零件的 act 调用 → 直接修改脚本文本
    ///   A-B = 无法确认的 → 脚本路径列入 potentialFiles 作为警告
    /// </summary>
    private (int, IReadOnlyList<string>, IReadOnlyList<string>) UpdateReferences(
        WritableProjectRootEntity writableRoot,
        string newName,
        IReadOnlyList<ScriptIdentity> identities)
    {
        Dictionary<string, List<ScriptIdentity>> nameToIdentities = new(StringComparer.Ordinal);
        foreach (ScriptIdentity identity in identities)
        {
            if (!nameToIdentities.TryGetValue(identity.CallerName, out List<ScriptIdentity>? list))
            {
                list = [];
                nameToIdentities[identity.CallerName] = list;
            }
            list.Add(identity);
        }

        if (nameToIdentities.Count == 0)
            return (0, Array.Empty<string>(), Array.Empty<string>());

        List<string> appliedFiles = [];
        List<string> potentialFiles = [];
        int cnt = 0;

        foreach (CallerScriptContext caller in GetAllCallerContexts())
        {
            string text = caller.Script switch
            {
                InlineScriptEntity inl => inl.SourceText,
                CodeRepoScriptEntity repo => repo.SourceText,
                _ => throw new InvalidOperationException($"Unexpected script type: {caller.Script.GetType()}")
            };

            IReadOnlyList<BescriptActCallRange> ranges = _scanner.ScanActCalls(text);

            string[] lines = TextLines.SplitLines(text);
            Dictionary<int, List<(int, int, string)>> reps = [];
            bool hasPotential = false;

            foreach (BescriptActCallRange r in ranges)
            {
                if (!nameToIdentities.TryGetValue(r.ScriptName, out List<ScriptIdentity>? matchingIdentities))
                    continue;

                if (TryConfirmActCall(r, caller.KnownDevicePaths, matchingIdentities))
                {
                    int li = r.Range.Line - 1;
                    if (li >= 0 && li < lines.Length)
                    {
                        if (!reps.TryGetValue(li, out List<(int, int, string)>? repList))
                        {
                            repList = [];
                            reps[li] = repList;
                        }
                        repList.Add((r.Range.Column, r.Range.Length, newName));
                        cnt++;
                    }
                }
                else
                {
                    hasPotential = true;
                }
            }

            if (reps.Count > 0)
            {
                foreach ((int li, List<(int, int, string)> repList) in reps)
                    lines[li] = TextReplacementApplier.ApplyReplacements(lines[li], repList);

                ProjectEntity current = writableRoot;
                foreach (string segment in caller.Script.实体_完整相对路径标识.Split('/'))
                    current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);

                string newText = TextLines.BuildLines(lines);
                if (caller.Script is InlineScriptEntity)
                    ((WritableInlineScriptEntity)current).SourceText = newText;
                else
                    ((WritableCodeRepoScriptEntity)current).SourceText = newText;
                appliedFiles.Add(caller.Script.实体_完整相对路径标识);
            }

            if (hasPotential)
                potentialFiles.Add(caller.Script.实体_完整相对路径标识);
        }

        return (cnt, appliedFiles, potentialFiles);
    }
}
