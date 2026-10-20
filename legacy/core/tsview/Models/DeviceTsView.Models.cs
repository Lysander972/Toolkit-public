namespace Toolkit.Core.TsView;

/// <summary>
/// 零件 TsView 读取结果，包含生成的 TypeScript 视图和引用的零件路径。
/// </summary>
public sealed record DeviceTsViewReadData(
    string DeviceRelativePath,
    string TsView,
    IReadOnlyList<string> ReferencedDeviceRelativePaths,
    IReadOnlyList<DeviceTsViewScriptIndex> ScriptIndexes,
    TsViewEditAllowance EditAllowance);

public sealed record DeviceTsViewPatchData(
    string DeviceRelativePath,
    bool Applied,
    string PatchedTsView,
    IReadOnlyList<string> AffectedRelativePaths
);

/// <summary>
/// TS 视图可修改的行号区段（包含起止行，1-based）。
/// </summary>
public sealed record TsViewEditRange(int StartLine, int EndLine);

/// <summary>
/// TS 视图行级编辑允许信息：<see cref="ForbiddenRanges"/> 为生成视图时锁定的「固定骨架」连续行区间。
/// </summary>
public sealed record TsViewEditAllowance(
    IReadOnlyList<TsViewEditRange> ForbiddenRanges,
    int TotalLines
);

/// <summary>
/// BES 行号到 TS 视图行号的映射条目。
/// </summary>
/// <remarks>
/// <para>BesLine: BES 源脚本中的行号（1-based）</para>
/// <para>TsLine: TS 视图中对应的行号（1-based）</para>
/// </remarks>
public sealed record TsViewLineMapping(int BesLine, int TsLine);

/// <summary>
/// 零件 TS 视图中的脚本行号索引。
/// </summary>
/// <remarks>
/// <para>用于 read_entities 返回脚本实体时提供行号范围。</para>
/// <para>LineMap: BES 行号到 TS 视图行号的映射表。</para>
/// </remarks>
public sealed record DeviceTsViewScriptIndex(
    string? SourceRelativePath,
    string? Guid,
    string ScriptName,
    int StartLine,
    int EndLine,
    IReadOnlyList<TsViewLineMapping>? LineMap = null
);

/// <summary>
/// TS 视图方法块信息，用于外部按方法整体替换。
/// </summary>
public sealed record TsViewMethodBlock(
    string Name,
    string RawName,
    string? SourceRelativePath,
    string? Guid,
    string? CodeRepoGuid,
    int StartLine,
    int EndLine
);

public sealed record DeviceTsViewBuildResult(
    string TsView,
    TsViewEditAllowance EditAllowance,
    IReadOnlyList<DeviceTsViewScriptIndex> ScriptIndexes,
    IReadOnlyList<string> ReferencedDeviceRelativePaths
)
{
    /// <summary>内部使用：Act 目标映射，用于异步校验。</summary>
    internal ActTargetMap? ActTargetMap { get; init; }
};

public sealed record TsViewPatch(
    int StartLine,
    int EndLine,
    string[] NewLines
);
