namespace Toolkit.Core.Infra;

// =============================================================================
// 物理文件身份 — 链接归并的唯一基元
// =============================================================================
//
// 缓存层以相对路径为逻辑 key,但 NTFS 上多个路径可能指向同一物理文件:
//   hardlink → 共享同一 MFT 记录
//   symlink  → follow 到目标
//   junction → follow 到目标目录
// Windows 的共享冲突检查落在文件对象层而非路径层,若缓存层把两条路径当独立
// 文件各自追踪/探测,会形成无法自愈的死锁(详见 docs/Common_Cache_Spec.md)。
//
// 本基元用 GetFileInformationByHandle 的 (VolumeSerialNumber, FileIndex) 识别物理身份,
// 供文件树构建入口归并:同一物理文件只保留一个节点。
//
// 归并策略(见 FileSystemCache.Dedup*):
//   - reparse(symlink/junction):IsReparsePoint 廉价识别即排除,绝不开链接句柄
//     (避免 reparse 解析冲突);其工程内目标已由非 reparse 形式纳入,无信息损失。
//   - hardlink:ReparsePoint 检测不到,用 FileId 去重。
// =============================================================================

/// <summary>
/// 物理文件身份:卷序列号 + 文件索引。同一物理文件(hardlink/symlink follow 后)相同。
/// </summary>
internal readonly record struct FileId(uint VolumeSerialNumber, ulong FileIndex);

/// <summary>
/// 物理文件身份提供者 — 链接归并基元。
/// </summary>
internal interface IFileIdentity
{
    /// <summary>
    /// 获取路径的物理 FileId(follow 链接到最终目标,内部只读打开)。仅 Windows;
    /// 取值失败抛异常,遇共享冲突抛 IOException(HResult 32/33)由调用方转受阻。
    /// </summary>
    FileId GetFileId(string fullPath);

    /// <summary>
    /// 路径是否为 reparse point(symlink/junction)。仅属性查询,不开句柄。
    /// </summary>
    bool IsReparsePoint(string fullPath);
}

/// <summary>
/// 真实文件身份提供者 — 转发到 SystemIOWrapper(仅 Windows)。
/// </summary>
internal sealed class RealFileIdentity : IFileIdentity
{
    public static readonly RealFileIdentity Instance = new();

    private RealFileIdentity() { }

    public FileId GetFileId(string fullPath) => SystemIOWrapper.GetFileId(fullPath);

    public bool IsReparsePoint(string fullPath) => SystemIOWrapper.IsReparsePoint(fullPath);
}
