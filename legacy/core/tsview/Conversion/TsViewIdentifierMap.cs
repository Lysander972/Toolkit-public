namespace Toolkit.Core.TsView;

/// <summary>
/// TS 标识符映射统一入口。
/// 所有 TS 标识符转义/反转义必须走这里。
/// </summary>
internal static class TsViewIdentifierMap
{
    internal static string ToTsIdentifier(string originalName)
        => TsViewNaming.ToBaseIdentifier(originalName);

    internal static string FromTsIdentifier(string identifier)
        => TsViewNaming.FromBaseIdentifier(identifier);

    internal static bool IsValidIdentifier(string name)
        => TsViewNaming.IsValidIdentifier(name);

    internal static string EnsureUniqueIdentifier(string baseName, ISet<string> usedNames)
        => TsViewNaming.EnsureUniqueIdentifier(baseName, usedNames);
}
