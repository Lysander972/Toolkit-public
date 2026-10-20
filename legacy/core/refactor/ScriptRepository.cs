using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Search;

namespace Toolkit.Core.Refactor;

public sealed record ScriptToRepositoryData(
    string ScriptGuid,
    string RepositoryRelativePath,
    bool Applied
);

public sealed record ScriptLinkRepositoryData(
    string LinkedGuid,
    string RepositoryRelativePath,
    bool Applied
);

public sealed record ScriptDeleteRepositoryData(
    string DeletedGuid,
    string RepositoryRelativePath,
    bool Applied
);

/// <summary>
/// 脚本仓库管理器 - 处理内联脚本与代码仓库之间的转换和关联。
///
/// 【职责边界】本类是使用者的实现示例，展示了正确使用 ScriptGroupEntity 和 CodeRepoScriptEntity 的模式：
///
/// 1. ScriptGroupEntity 只管理 list.json 中的链接条目映射（ScriptEntryInfo）
///    - IsCodeRepo=false + Guid=null → 内联脚本
///    - IsCodeRepo=true  + Guid!=null → CodeRepo 引用
///
/// 2. CodeRepoScriptEntity 是独立实体，通过 ProjectModel 独立管理
///    - 物理位置：任意目录下的 *.code + *.code.guid（排除 .map_/.mech_ 目录及 MechEditorCode/MechEditorToolCode）
///    - 生命周期：独立于任何 ScriptGroupEntity
///
/// 3. 使用者的责任：
///    - 导出到仓库：创建 CodeRepoScriptEntity → 更新 ScriptGroupEntity 条目 → 删除内联脚本
///    - 链接到仓库：更新 ScriptGroupEntity 条目（引用已有 CodeRepoScriptEntity）
///    - 删除仓库：检查无引用后删除 CodeRepoScriptEntity
///
/// 【禁止】ScriptGroupEntity 不会级联操作 CodeRepoScriptEntity，两者是独立管理的关系。
/// </summary>
public sealed class ScriptRepository
{
    private readonly ProjectModel _model;
    private readonly CanvasPreview _canvasPreview;

    public ScriptRepository(ProjectModel model, CanvasPreview canvasPreview)
    {
        _model = model;
        _canvasPreview = canvasPreview;
    }

    public RefactorResult<ScriptToRepositoryData> ToRepository(
        string devicePath,
        string scriptName,
        string scriptGroup)
    {
        string deviceRelativePath = WorkspacePath.RequireRelativePath(devicePath);
        if (_model.Find<DeviceEntity>(deviceRelativePath) is not DeviceEntity)
        {
            return RefactorResult<ScriptToRepositoryData>.Fail($"未找到零件实体: {deviceRelativePath}。零件路径示例: main.map_/255 或 xxx/abc.mech_/1");
        }

        try { RefactorHelper.ValidateScriptName(scriptName); }
        catch (ArgumentException ex) { return RefactorResult<ScriptToRepositoryData>.Fail(ex.Message); }
        string scriptDir = RefactorHelper.ResolveDeviceScriptDir(deviceRelativePath, scriptGroup);

        // 通过 ProjectModel 获取脚本组实体（使用目录路径作为 实体_完整相对路径标识）
        if (_model.Find<ScriptGroupEntity>(scriptDir) is not ScriptGroupEntity scriptGroupEntity ||
            scriptGroupEntity.ListJsonText is not string listText)
        {
            return RefactorResult<ScriptToRepositoryData>.Fail(
                $"未找到脚本组: {scriptDir}。scriptGroup 必须是完整脚本组名（scripts 或 event_XXX），默认为 scripts。");
        }

        bool foundEntry = false;
        List<ListJsonItem> listItems = HjsonText.ParseListItems(listText);

        foreach (ListJsonItem item in listItems)
        {
            if (string.Equals(item.Name, scriptName, StringComparison.Ordinal))
            {
                foundEntry = true;
                if (item.IsCodeRepo && GuidText.Validate(item.Guid))
                {
                    return RefactorResult<ScriptToRepositoryData>.Fail(GuidText.AlreadyLinkedMessage(item.Guid));
                }
                break;
            }
        }

        if (!foundEntry)
        {
            return RefactorResult<ScriptToRepositoryData>.Fail($"list.json 中未找到脚本条目: {scriptName}");
        }

        // 通过 ProjectModel 获取内联脚本实体
        string sourceCodePath = RelativePathUtils.Combine(scriptDir, scriptName + ProjectEntityLayout.CodeExt);
        if (_model.Find<InlineScriptEntity>(sourceCodePath) is not InlineScriptEntity inlineScript)
        {
            return RefactorResult<ScriptToRepositoryData>.Fail($"未找到内联脚本: {sourceCodePath}");
        }

        string codeText = inlineScript.SourceText;

        // 内联脚本导出到代码仓库时生成新 GUID
        string scriptGuid = GuidText.NewGuid();

        string repoCodePath = ScriptPathHelper.BuildCodeRepositoryScriptPath(WorkspacePath.RootDirectory, scriptName);

        // 使用 ProjectModel 检查目标代码仓库脚本是否已存在
        if (_model.EntityExists(repoCodePath))
        {
            return RefactorResult<ScriptToRepositoryData>.Fail($"目标代码仓库脚本已存在: {WorkspacePath.RequireRelativePath(repoCodePath)}");
        }

        string patchedCodeText = EnsureScriptGuidHeader(codeText, scriptGuid);

        try
        {
            WritableProjectRootEntity writableRoot = _model.Root.ToWritable();

            WritableCodeRepoScriptEntity writableRepoEntity = new()
            {
                Parent = writableRoot,
                Guid = scriptGuid,
                SourceText = patchedCodeText,
                GuidJson = new GuidJsonRecord()
            };
            writableRepoEntity.GuidJson.SetGuid(scriptGuid);
            writableRepoEntity.SetIdentifierInternal(RelativePathUtils.GetLastSegment(repoCodePath));
            writableRoot.Children.Add(writableRepoEntity);

            ProjectEntity current = writableRoot;
            foreach (string segment in deviceRelativePath.Split('/'))
            {
                current = ((IHasChildren<ProjectEntity>)current).Children.Single(child => child.实体_无相对路径标识符 == segment);
            }

            WritableDeviceEntity writableDevice = (WritableDeviceEntity)current;
            WritableScriptGroupEntity writableScriptGroup = (WritableScriptGroupEntity)writableDevice.ScriptGroups
                .Single(group => group.实体_无相对路径标识符 == scriptGroup);
            writableScriptGroup.ScriptEntries = writableScriptGroup.ScriptEntries
                .Where(e => !string.Equals(e.Name, scriptName, StringComparison.Ordinal))
                .Concat(new[] { new ScriptEntryInfo(scriptName, scriptGuid, IsCodeRepo: true) })
                .ToList();

            string inlineFileName = RelativePathUtils.GetLastSegment(sourceCodePath);
            WritableInlineScriptEntity inlineToDelete = writableScriptGroup.Scripts
                .Cast<WritableInlineScriptEntity>()
                .Single(s => s.实体_无相对路径标识符 == inlineFileName);
            writableScriptGroup.Scripts.Remove(inlineToDelete);
            _model.SaveChanges(writableRoot);
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<ScriptToRepositoryData>.Fail($"写入失败: {ex.Message}");
        }

        string repositoryPath = WorkspacePath.RequireRelativePath(repoCodePath);
        return RefactorResult<ScriptToRepositoryData>.Success(new ScriptToRepositoryData(scriptGuid, repositoryPath, true));
    }

    public RefactorResult<ScriptLinkRepositoryData> LinkRepository(
        string devicePath,
        string scriptName,
        string scriptGroup,
        string targetGuid,
        bool force = false)
    {
        try { RefactorHelper.ValidateScriptName(scriptName); }
        catch (ArgumentException ex) { return RefactorResult<ScriptLinkRepositoryData>.Fail(ex.Message); }
        if (!GuidText.Validate(targetGuid)) return RefactorResult<ScriptLinkRepositoryData>.Fail(GuidText.FormatErrorMessage("targetGuid", targetGuid));

        string deviceRelPath = WorkspacePath.RequireRelativePath(devicePath);
        if (_model.Find<DeviceEntity>(deviceRelPath) is not DeviceEntity)
        {
            return RefactorResult<ScriptLinkRepositoryData>.Fail($"未找到零件实体: {deviceRelPath}。零件路径示例: main.map_/255 或 xxx/abc.mech_/1");
        }

        if (_model.FindByGuid<CodeRepoScriptEntity>(targetGuid) is not CodeRepoScriptEntity repoScript)
        {
            return RefactorResult<ScriptLinkRepositoryData>.Fail(GuidText.NotFoundMessage("代码仓库脚本", targetGuid));
        }
        string scriptDir = RefactorHelper.ResolveDeviceScriptDir(deviceRelPath, scriptGroup);

        // 通过 ProjectModel 获取脚本组实体（使用目录路径作为 实体_完整相对路径标识）
        if (_model.Find<ScriptGroupEntity>(scriptDir) is not ScriptGroupEntity targetScriptGroupEntity ||
            targetScriptGroupEntity.ListJsonText is not string listText)
        {
            return RefactorResult<ScriptLinkRepositoryData>.Fail(
                $"未找到脚本组: {scriptDir}。scriptGroup 必须是完整脚本组名（scripts 或 event_XXX），默认为 scripts。");
        }

        bool hasCodeRepoReference = false;
        List<ListJsonItem> listItems = HjsonText.ParseListItems(listText);

        foreach (ListJsonItem item in listItems)
        {
            if (string.Equals(item.Name, scriptName, StringComparison.Ordinal))
            {
                hasCodeRepoReference = item.IsCodeRepo;
                break;
            }
        }

        string inlinePath = RelativePathUtils.Combine(scriptDir, scriptName + ProjectEntityLayout.CodeExt);

        // 通过 ProjectModel 检查内联脚本是否存在
        bool hasInlineScript = _model.Find<InlineScriptEntity>(inlinePath) is InlineScriptEntity;

        if (!force)
        {
            if (hasCodeRepoReference)
            {
                return RefactorResult<ScriptLinkRepositoryData>.Fail("目标脚本已有代码仓库引用，未开启 force（开启 force 将强制覆盖，现有引用将丢失）");
            }

            if (hasInlineScript)
            {
                return RefactorResult<ScriptLinkRepositoryData>.Fail("目标脚本已有内联内容，未开启 force（开启 force 将强制覆盖，现有内容将丢失）");
            }
        }

        try
        {
            EntityWriteScope.ApplyDelete<ScriptGroupEntity, WritableScriptGroupEntity>(
                _model,
                targetScriptGroupEntity,
                group => group.ToWritable(),
                writableTargetGroup =>
                {
                    writableTargetGroup.ScriptEntries = writableTargetGroup.ScriptEntries
                        .Where(e => !string.Equals(e.Name, scriptName, StringComparison.Ordinal))
                        .Concat(new[] { new ScriptEntryInfo(scriptName, targetGuid, IsCodeRepo: true) })
                        .ToList();

                    if (!hasInlineScript)
                        return;

                    string inlineFileName = RelativePathUtils.GetLastSegment(inlinePath);
                    WritableInlineScriptEntity inlineToDelete = writableTargetGroup.Scripts
                        .Cast<WritableInlineScriptEntity>()
                        .Single(s => s.实体_无相对路径标识符 == inlineFileName);
                    writableTargetGroup.Scripts.Remove(inlineToDelete);
                });
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<ScriptLinkRepositoryData>.Fail($"写入失败: {ex.Message}");
        }

        return RefactorResult<ScriptLinkRepositoryData>.Success(new ScriptLinkRepositoryData(targetGuid, repoScript.实体_完整相对路径标识, true));
    }

    public RefactorResult<ScriptDeleteRepositoryData> DeleteRepository(
        string targetGuid)
    {
        if (!GuidText.Validate(targetGuid)) return RefactorResult<ScriptDeleteRepositoryData>.Fail(GuidText.FormatErrorMessage("targetGuid", targetGuid));

        if (_model.FindByGuid<CodeRepoScriptEntity>(targetGuid) is not CodeRepoScriptEntity repoScript)
        {
            return RefactorResult<ScriptDeleteRepositoryData>.Fail(GuidText.NotFoundMessage("代码仓库脚本", targetGuid));
        }

        string repoRelativePath = repoScript.实体_完整相对路径标识;
        List<GuidReferenceResult> refs = GuidReferenceSearchCore.SearchGuidReferences(_model, targetGuid, 2000);

        GuidReferenceResult[] externalRefs = refs
            .Where(r =>
            {
                string p = WorkspacePath.RequireRelativePathOrEmpty(r.Path);
                if (p.StartsWith("temp/toolkit/", StringComparison.Ordinal))
                {
                    return false;
                }

                if (string.Equals(p, repoRelativePath, StringComparison.Ordinal) ||
                    string.Equals(p, repoRelativePath + ProjectEntityLayout.GuidExt, StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            })
            .ToArray();

        if (externalRefs.Length > 0)
        {
            GuidReferenceResult first = externalRefs[0];
            string loc = $"{first.Path}:{first.Line}";
            return RefactorResult<ScriptDeleteRepositoryData>.Fail($"存在引用，无法删除（{externalRefs.Length} 条）。示例: {loc}");
        }

        try
        {
            string repoParentDir = RelativePathUtils.GetParentDirectory(repoRelativePath);
            bool isRootDir = repoParentDir == WorkspacePath.RootDirectory;

            if (isRootDir)
            {
                // 根目录使用 ProjectRootEntity
                EntityWriteScope.ApplyDelete<ProjectRootEntity, WritableProjectRootEntity>(
                    _model,
                    _model.Root,
                    parent => parent.ToWritable(),
                    writableParent =>
                    {
                        string repoFileName = RelativePathUtils.GetLastSegment(repoRelativePath);
                        WritableCodeRepoScriptEntity toDelete = writableParent.Children
                            .OfType<WritableCodeRepoScriptEntity>()
                            .Single(c => c.实体_无相对路径标识符 == repoFileName);
                        writableParent.Children.Remove(toDelete);
                    });
            }
            else
            {
                // 普通目录使用 DirectoryEntity
                if (repoScript.Parent is not DirectoryEntity dirParent)
                    throw new InvalidOperationException($"代码仓库脚本父级类型错误: {repoScript.Parent.GetType().Name}");

                EntityWriteScope.ApplyDelete<DirectoryEntity, WritableDirectoryEntity>(
                    _model,
                    dirParent,
                    parent => parent.ToWritable(),
                    writableParent =>
                    {
                        string repoFileName = RelativePathUtils.GetLastSegment(repoRelativePath);
                        WritableCodeRepoScriptEntity toDelete = writableParent.Children
                            .OfType<WritableCodeRepoScriptEntity>()
                            .Single(c => c.实体_无相对路径标识符 == repoFileName);
                        writableParent.Children.Remove(toDelete);
                    });
            }
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            return RefactorResult<ScriptDeleteRepositoryData>.Fail($"删除失败: {ex.Message}");
        }

        return RefactorResult<ScriptDeleteRepositoryData>.Success(new ScriptDeleteRepositoryData(targetGuid, repoRelativePath, true));
    }

    private static string EnsureScriptGuidHeader(string codeText, string guid)
    {
        string[] lines = TextLines.SplitLines(codeText);
        List<string> updated = new(lines.Length + 1);
        bool guidInserted = false; bool guidUpdated = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
#pragma warning disable TK010 // TrimStart 用于忽略 Bescript 源码缩进，检测 //guid: 前缀
            string trimmed = line.TrimStart();
            if (!guidUpdated && trimmed.StartsWith(BescriptSyntax.GuidPrefix, StringComparison.Ordinal))
            {
                int guidIndex = line.IndexOf(BescriptSyntax.GuidPrefix, StringComparison.Ordinal);
                int guidValueIndex = guidIndex + BescriptSyntax.GuidPrefix.Length;
                string ws = new(line[guidValueIndex..].TakeWhile(char.IsWhiteSpace).ToArray());
                updated.Add(line[..guidValueIndex] + ws + guid);
                guidUpdated = true; continue;
            }
            updated.Add(line);
            if (!guidInserted && !guidUpdated && trimmed.StartsWith(BescriptSyntax.BeginCodePrefix, StringComparison.Ordinal))
            { updated.Add(BescriptSyntax.GuidPrefix + guid); guidInserted = true; }
        }
        if (!guidInserted && !guidUpdated) updated.Insert(0, BescriptSyntax.GuidPrefix + guid);
        return TextLines.BuildLines(updated.ToArray());
    }

}
