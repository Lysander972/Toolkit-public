using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 资源导入结果
/// </summary>
public sealed record ResourceImportData(
    /// <summary>资源文件路径（相对工程根）</summary>
    string RelativePath,
    /// <summary>是否成功写入</summary>
    bool Applied,
    /// <summary>受影响的文件路径列表</summary>
    IReadOnlyList<string> AffectedPaths,
    /// <summary>新建分支分配的 GUID（覆盖分支为 null），供调用方回传给 Agent 减少读操作</summary>
    string? CreatedGuid = null
);

/// <summary>
/// 资源导入 - 将外部生成的二进制资产写入工程
///
/// 使用规范：
/// ============
/// 目录必须已存在，不自动建目录；同名资源已存在时必须显式 overwrite
/// 新建资源自动分配 GUID 并生成 .guid 伴生文件；slice 非 null 时把九宫格参数写入 .guid（格式见 GuidJsonRecord.SetSlice）
/// </summary>
public sealed class ResourceImport
{
    private static readonly Log ImportLog = Log.For(LogModule.Core);
    private readonly ProjectModel _model;

    public ResourceImport(ProjectModel model)
    {
        _model = model;
    }

    public (bool Success, ResourceImportData Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Import(
        string directory,
        string fileName,
        byte[] data,
        bool overwrite,
        string? slice = null)
    {
        string directoryRelativePath = WorkspacePath.RequireRelativePath(directory);
        string resourceRelativePath = RelativePathUtils.Combine(directoryRelativePath, fileName);

        if (_model.Find<ResourceEntity>(resourceRelativePath) is ResourceEntity existingResource)
        {
            if (!overwrite)
            {
                return (false,
                    new ResourceImportData(resourceRelativePath, Applied: false, AffectedPaths: Array.Empty<string>()),
                    new[] { new ToolError(null, null, null, $"同名资源已存在（overwrite=false 拒绝覆盖）: {resourceRelativePath}") },
                    Array.Empty<ToolError>());
            }

            try
            {
                // 覆盖保留既有实体 GUID；调用方必须从该实体读取，禁止以新 GUID 替换引用。
                IReadOnlyList<string> paths = EntityWriteScope.ApplyContentChange<ResourceEntity, WritableResourceEntity>(
                    _model,
                    existingResource,
                    resource => resource.ToWritable(),
                    writable =>
                    {
                        writable.Data = data;
                        if (slice != null)
                        {
                            // 加载成功的资源实体必然持有 .guid 记录（LoadSelf 解析失败即工程级加载错误）
                            GuidJsonRecord guidJson = writable.GuidJson ?? throw new InvalidOperationException($"资源实体缺少 .guid 记录: {resourceRelativePath}");
                            guidJson.SetSlice(slice);
                        }
                    });
                return (true,
                    new ResourceImportData(resourceRelativePath, Applied: paths.Count > 0, AffectedPaths: paths),
                    Array.Empty<ToolError>(),
                    Array.Empty<ToolError>());
            }
            catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
            {
                ImportLog.Exception(ex, "ResourceImport.Import.Overwrite");
                return (false,
                    new ResourceImportData(resourceRelativePath, Applied: false, AffectedPaths: Array.Empty<string>()),
                    new[] { new ToolError(null, null, null, $"保存变更失败: {ex.Message}") },
                    Array.Empty<ToolError>());
            }
        }

        if (_model.Find<DirectoryEntity>(directoryRelativePath) is not DirectoryEntity parentEntity)
        {
            return (false,
                new ResourceImportData(resourceRelativePath, Applied: false, AffectedPaths: Array.Empty<string>()),
                new[] { new ToolError(null, null, null, $"目录不存在: {directoryRelativePath}") },
                Array.Empty<ToolError>());
        }

        try
        {
            // 资源 GUID 生命周期唯一归 ResourceImport：其他工作流只能消费此处新建或既有资源的 GUID，
            // 禁止自行生成 GUID、维护缓存 GUID 或新增平行导入路径，否则 Canvas 引用会与资源实体分裂。
            string guid = GuidText.NewGuid();
            IReadOnlyList<string> paths = EntityWriteScope.ApplyCreate<DirectoryEntity, WritableDirectoryEntity, WritableResourceEntity>(
                _model,
                parentEntity,
                parent => parent.ToWritable(),
                writableParent =>
                {
                    WritableResourceEntity writableResource = new()
                    {
                        Parent = writableParent,
                        Guid = guid,
                        Data = data,
                        GuidJson = new GuidJsonRecord()
                    };
                    writableResource.GuidJson.SetGuid(guid);
                    if (slice != null)
                    {
                        writableResource.GuidJson.SetSlice(slice);
                    }
                    writableResource.SetIdentifierInternal(fileName);
                    return writableResource;
                },
                (writableParent, writableResource) => writableParent.Children.Add(writableResource));

            return (true,
                new ResourceImportData(resourceRelativePath, Applied: paths.Count > 0, AffectedPaths: paths, CreatedGuid: guid),
                Array.Empty<ToolError>(),
                Array.Empty<ToolError>());
        }
        catch (Exception ex) when (ex is not WorkspaceFileBlockedException and not WorkspaceStateChangedException)
        {
            ImportLog.Exception(ex, "ResourceImport.Import.Create");
            return (false,
                new ResourceImportData(resourceRelativePath, Applied: false, AffectedPaths: Array.Empty<string>()),
                new[] { new ToolError(null, null, null, $"保存变更失败: {ex.Message}") },
                Array.Empty<ToolError>());
        }
    }
}
