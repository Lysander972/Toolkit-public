namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 名称合法规则（唯一权威）。
///
/// 规则：canvas 名称必须是合法文件名——内联名称、独立文件名、引用名三者一致，
/// 名称本身即文件系统兼容。不合法时提取/创建应报错，禁止替换净化。
/// </summary>
internal static class CanvasName
{
    private const string InvalidFileNameChars = "<>:\"/\\|?*";

    public static bool IsValid(string name)
    {
        return TryGetInvalidReason(name) is null;
    }

    /// <summary>
    /// 校验失败原因，合法返回 null。
    /// </summary>
    public static string? TryGetInvalidReason(string name)
    {
#pragma warning disable TK008 // 名称规则校验：空名不合法
        if (name.Length == 0)
        {
            return "canvas 名称为空";
        }
#pragma warning restore TK008

        char last = name[^1];
        if (last == '.' || last == ' ')
        {
            return "canvas 名称不能以点号或空格结尾";
        }

        foreach (char c in name)
        {
            if (c < ' ' || InvalidFileNameChars.Contains(c))
            {
                return $"canvas 名称包含非法文件字符: '{c}'";
            }
        }

        return null;
    }
}
