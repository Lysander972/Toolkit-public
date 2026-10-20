namespace Toolkit.Core.Persistence;

// =============================================================================
// Spine 实体（Spine 骨骼动画资产）
// =============================================================================
// 一个 Spine 资产 = 同目录、同 baseName 的文件组：
//   <name>.atlas                  图集描述符（附件名 → png 矩形区域，并声明纹理页名）
//   <name>.skel 或 <name>.json    骨骼+动画数据（二进制 .skel 优先，或文本 .json，二选一）
//   <name>.png (×N)               打包纹理（文件名由 .atlas 内 texNames 声明）
// 每个文件独立 GUID（原版 SpineDevice 持 atlasFileGuid/skelFileGuid/jsonFileGuid/pngFileGuids）。
//
// 工程组织（Toolkit 建模）：
//   每个资产独立成 <name>.spine_ 派生目录（沿用 .map_/.mech_ 后缀约定），目录内放上述文件组。
//   SpineEntity 是目录级别容器实体，atlas/skel/png 作为 ResourceEntity 子实体。
//
// 资产文件组：.atlas 与同名 .skel/.json 位于同级目录，纹理由 atlas 中的图片名称关联。
//
// 识别约定：靠派生目录后缀 .spine_ 识别（SpineEntityBuilder，仅注册于工程根与常规目录）。
//   Spine 无主文件，无法套用 .map.guid 锚点模式（无对应主文件会被 GuidGuard 判悬空删除），
//   故 SpineEntity 无锚点文件、无实体级 GUID——引用按文件级（各子 ResourceEntity 的 GUID）。
//
// Spine 官方文档：
//   导出格式            http://en.esotericsoftware.com/spine-export
//   二进制 .skel 格式   http://en.esotericsoftware.com/spine-binary-format
//   JSON 格式           http://en.esotericsoftware.com/spine-json-format
//   Unity 资产（三文件） http://en.esotericsoftware.com/spine-unity-assets
// =============================================================================

/// <summary>
/// Spine 资产实体（只读版本）。
/// 目录级别容器实体：标识符 = .spine_ 派生目录名，无实体级 GUID。
/// 直属子实体：资源文件（ResourceEntity）+ 子目录（DirectoryEntity）。
/// 仅可直属于工程根或常规目录。
/// </summary>
public sealed record SpineEntity : DirectoryEntityBase, IReadOnlyEntity<WritableSpineEntity>
{
    public SpineEntity(string directoryName)
    {
        _directoryName = directoryName;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.Spine;

    /// <summary>
    /// 提供 Spine 目录的构建器集合（仅资源 + 目录，不递归识别 Spine）。
    /// </summary>
    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForSpineDirectory();

    /// <summary>
    /// 加载自身：递增版本号。
    /// </summary>
    public override void LoadSelf(EntityLoadErrorCollector errors)
    {
        IncrementVersion();
    }

    /// <summary>
    /// 转换为可写副本。
    /// </summary>
    public WritableSpineEntity ToWritable()
    {
        WritableSpineEntity writable = new(DirectoryName)
        {
            Parent = Parent,
            Version = Version
        };
        writable.Children = CloneChildrenToWritable(Children, writable);
        return writable;
    }

    public override void Validate()
    {
        ValidateParentLink();
        if (!DirectoryName.EndsWith(ProjectEntityLayout.SpineDirExt, StringComparison.Ordinal))
            throw new InvalidOperationException($"Spine 目录名必须以 {ProjectEntityLayout.SpineDirExt} 结尾: {实体_完整相对路径标识}");
    }
}

/// <summary>
/// 可写 Spine 资产实体。用完即丢弃。
/// 不支持重命名（重命名需联动内部文件组的 baseName，超出当前范围）。
/// </summary>
public sealed record WritableSpineEntity : DirectoryEntityBase, IWritableEntity
{
    public WritableSpineEntity(string directoryName)
    {
        _directoryName = directoryName;
    }

    /// <summary>标识符（可写）</summary>
    public new string 实体_无相对路径标识符
    {
        get => _directoryName;
        internal set => _directoryName = value;
    }

    /// <summary>子实体列表（可写）</summary>
    public new List<ProjectEntity> Children
    {
        get => _children;
        set => _children = value;
    }

    public override ProjectEntityKind Kind => ProjectEntityKind.Spine;

    internal override IEnumerable<ISubEntityBuilder> GetEntityBuilders()
        => EntityBuilderSets.ForSpineDirectory();

    public void Rename(string newName) => throw new NotSupportedException("Spine 资产不支持重命名");

    public override void Validate()
    {
        ValidateParentLink();
        if (!DirectoryName.EndsWith(ProjectEntityLayout.SpineDirExt, StringComparison.Ordinal))
            throw new InvalidOperationException($"Spine 目录名必须以 {ProjectEntityLayout.SpineDirExt} 结尾: {实体_完整相对路径标识}");
    }
}
