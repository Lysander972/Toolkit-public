using Toolkit.Core.Bescript;

namespace Toolkit.Core.TsView;

public sealed partial class DeviceTsView
{
    private static string ToTsGlobalVarBeTypeName(BescriptVarType type)
    {
        // vars.json 的类型应来自 BescriptVarType；异常/未知值直接抛出
        if (!BescriptVarTypeHelper.IsSupportedGlobalVarType(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的全局变量类型");
        }

        string? name = Enum.GetName(typeof(BescriptVarType), type);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的全局变量类型");
        }

        return "Be" + name;
    }

}
