namespace Toolkit.Core.Sdk;

/// <summary>
/// 文档引用（路径 + 片段）。
/// </summary>
public sealed record DocRef(
    string? Path,
    string? Fragment
);

public static class BuiltinMethodDocRefs
{
    public static (DocRef? HumanDoc, DocRef? AgentDoc) Get(MethodSignature sig)
    {
        string basePath = sig.Category switch
        {
            MethodCategory.GlobalFunc => "sdk/builtin/fun",
            MethodCategory.VarFunc => sig.OwnerType is null
                ? throw new ArgumentException("VarFunc 方法签名必须包含 OwnerType", nameof(sig))
                : $"sdk/builtin/varf/{sig.OwnerType}",
            MethodCategory.DeviceAction => sig.OwnerType is null
                ? throw new ArgumentException("DeviceAction 方法签名必须包含 OwnerType", nameof(sig))
                : $"sdk/builtin/act/{sig.OwnerType}",
            MethodCategory.EditorFunc => sig.OwnerType is null ? "sdk/builtin/dvf" : $"sdk/builtin/dvf/{sig.OwnerType}",
            _ => throw new ArgumentOutOfRangeException(nameof(sig.Category), sig.Category, "未知方法分类")
        };

        string anchor = sig.GetFullPath();
        DocRef docRef = new(basePath, anchor);
        return (docRef, docRef);
    }
}
