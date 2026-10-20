namespace Toolkit.Core.Sdk;

internal static class UiMethodExpansion
{
    public const string UiBaseType = VarFuncInheritance.UiBaseType;

    public static IReadOnlyList<string> UiDerivedTypes => VarFuncInheritance.UiDerivedTypes;

    public static bool IsUiBaseMethod(MethodSignature sig)
    {
        return sig.Category == MethodCategory.VarFunc &&
               string.Equals(sig.OwnerType, UiBaseType, StringComparison.Ordinal);
    }

    public static bool IsUiDerivedType(string? ownerType)
    {
        return VarFuncInheritance.IsUiDerivedType(ownerType);
    }
}
