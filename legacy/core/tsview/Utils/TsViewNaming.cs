using System.Globalization;
using System.Text;

using Toolkit.Core.Analysis;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// TsView 标识符命名工具类。
///
/// 【职责边界】
/// - 标识符合法性验证（唯一入口：IsValidIdentifier）
/// - 原始名称 ↔ TS 标识符的双向转换（仅供 TsViewIdentifierMap 调用）
/// - 方法标识符构建与去重
///
/// 【硬性要求】
/// - 标识符转义/反转义必须只通过 TsViewIdentifierMap，禁止任何旁路调用
///
/// 【不处理】
/// - 类型名转换 → 使用 TsViewTypeUtils.ToTsBeTypeName / TryParseBescriptVarType
/// - 字符串字面量生成 → 使用 TsViewTypeUtils.ToTsStringLiteral
/// </summary>
internal static class TsViewNaming
{
    internal const string EscapedNameAbuseHint = "原始 BeScript 标识符禁止滥用转义后内容。";

    /// <summary>
    /// 【唯一入口】验证字符串是否为合法的 TS/JS 标识符。
    /// 禁止在其他地方重复实现此逻辑。
    /// </summary>
    internal static bool IsValidIdentifier(string name)
    {
        return TsCompilerHost.IsValidIdentifier(name);
    }

    internal static string GetMethodNameFromScriptStem(string stem)
    {
        return TsViewIdentifierMap.ToTsIdentifier(stem);
    }

    internal static string BuildCodeRepoSymbolFromSourcePath(string sourcePath)
    {
        string namespacePath = CodeRepoPath.NormalizeForNamespace(sourcePath);

        string[] parts = namespacePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("代码仓库路径为空，无法生成符号名");
        }

        IEnumerable<string> ids = parts.Select(TsViewIdentifierMap.ToTsIdentifier);
        return "CodeRepo." + string.Join('.', ids);
    }

    /// <summary>
    /// 将任意原始名称转换为合法的 TS 标识符或限定名。
    /// 只允许 TsViewIdentifierMap 调用。
    ///
    /// 转义规则：
    /// 1. 按 "." 作为分隔拆分。
    /// 2. 每一段应用基本转义：
    ///    - 合法标识符且非保留字：原样返回
    ///    - 其他情况：X_ 前缀 + 逐字符处理（合法字符保留，非法字符转 _xHEX_）
    /// 3. 最终拼回，原本多少个 "." 分节，现在就还是多少个 "."。
    /// </summary>
    internal static string ToBaseIdentifier(string originalName)
    {
#pragma warning disable TK011 // 公共 API 边界防御：空字符串无意义，提前抛异常避免下游返回 "X_" 掩盖错误
        if (originalName == string.Empty)
        {
            throw new InvalidOperationException("标识符为空，无法映射");
        }
#pragma warning restore TK011

        string[] segments = originalName.Split('.');
        return string.Join('.', segments.Select(ToSingleBaseIdentifier));
    }

    private static string ToSingleBaseIdentifier(string name)
    {
        bool isTsKeyword = BescriptDtsGenerator.IsReservedWord(name);
        if (TsCompilerHost.IsValidIdentifier(name) && !isTsKeyword)
        {
            return name;
        }

        // 统一使用 X_ 前缀格式，不分裂
        StringBuilder sb = new(name.Length + 8);
        sb.Append("X_");
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (IsIdentifierPart(rune))
            {
                sb.Append(rune.ToString());
                continue;
            }

            sb.Append("_x").Append(rune.Value.ToString("X")).Append('_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建方法标识符，统一调用 TsViewIdentifierMap。
    /// </summary>
    internal static string BuildMethodIdentifier(string rawName)
    {
        return TsViewIdentifierMap.ToTsIdentifier(rawName);
    }

    /// <summary>
    /// 检测"把转义后的 TS 标识符文本当成原始名"这一类污染输入。
    /// 例如：rawName = "X_输入判断_x27_选阵营"（应当写原始名 "输入判断'选阵营"）。
    /// </summary>
    internal static string? TryDetectEscapedIdentifierLiteral(string rawName)
    {
        string decoded;
        try
        {
            decoded = FromBaseIdentifier(rawName);
        }
        catch
        {
            return null;
        }

        if (string.Equals(decoded, rawName, StringComparison.Ordinal))
        {
            return null;
        }

        string reEncoded;
        try
        {
            reEncoded = ToBaseIdentifier(decoded);
        }
        catch
        {
            return null;
        }

        if (!string.Equals(reEncoded, rawName, StringComparison.Ordinal))
        {
            return null;
        }

        return decoded;
    }

    /// <summary>
    /// 【唯一入口】生成唯一标识符（去重）。
    /// 禁止在其他地方重复实现此逻辑。
    /// </summary>
    /// <param name="baseName">基础标识符名称（可能是带点的路径）</param>
    /// <param name="usedNames">已使用的名称集合，方法会自动将结果添加到集合中</param>
    /// <returns>唯一的标识符</returns>
    internal static string EnsureUniqueIdentifier(string baseName, ISet<string> usedNames)
    {
        string name = baseName;
        int suffix = 2;
        while (usedNames.Contains(name))
        {
            name = baseName + "_" + suffix;
            suffix++;
        }

        usedNames.Add(name);
        return name;
    }

    /// <summary>
    /// 将 TS 标识符或路径还原为原始名称。
    /// 只允许 TsViewIdentifierMap 调用。
    ///
    /// 解码规则：
    /// 1. 按 "." 作为分隔拆分。
    /// 2. 每一段应用基本解码：
    ///    - 无 X_ 前缀：原样返回（未转义的合法标识符）
    ///    - 有 X_ 前缀：去掉前缀，_xHEX_ 还原为对应字符
    /// 3. 最终拼回。
    /// </summary>
    internal static string FromBaseIdentifier(string identifier)
    {
#pragma warning disable TK011 // 公共 API 边界防御：空字符串无意义，提前抛异常避免下游返回来源掩盖错误
        if (identifier == string.Empty)
        {
            throw new InvalidOperationException("标识符为空，无法反向映射");
        }
#pragma warning restore TK011

        string[] segments = identifier.Split('.');
        return string.Join('.', segments.Select(FromSingleBaseIdentifier));
    }

    private static string FromSingleBaseIdentifier(string identifier)
    {
#pragma warning disable TK011 // 空段（如 "foo..bar" 分割产生）需原样保留以维持往返一致性
        if (identifier == string.Empty)
        {
            return string.Empty;
        }
#pragma warning restore TK011

        // 无 X_ 前缀：未转义，原样返回
        if (!identifier.StartsWith("X_", StringComparison.Ordinal))
        {
            return identifier;
        }

        // 有 X_ 前缀：解码
        string encoded = identifier[2..];
        StringBuilder sb = new(encoded.Length);

        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '_' && i + 3 < encoded.Length && encoded[i + 1] == 'x')
            {
                int hexStart = i + 2;
                int hexEnd = hexStart;
                while (hexEnd < encoded.Length && encoded[hexEnd] is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')
                {
                    hexEnd++;
                }

                if (hexEnd > hexStart && hexEnd < encoded.Length && encoded[hexEnd] == '_')
                {
                    string hex = encoded.Substring(hexStart, hexEnd - hexStart);
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int codepoint) &&
                        Rune.TryCreate(codepoint, out Rune rune))
                    {
                        sb.Append(rune.ToString());
                        i = hexEnd;
                        continue;
                    }
                }

                // _x 开头但解码失败：格式错误
                throw new InvalidOperationException($"标识符转义格式错误: {identifier}（位置 {i + 2}）");
            }

            sb.Append(encoded[i]);
        }

        return sb.ToString();
    }


    private static bool IsIdentifierPart(Rune rune)
    {
        if (rune.Value <= 0x7F)
        {
            char c = (char)rune.Value;
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }

        // 只放行字母/数字（保留中文等常见字符）
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is System.Globalization.UnicodeCategory.UppercaseLetter
            or System.Globalization.UnicodeCategory.LowercaseLetter
            or System.Globalization.UnicodeCategory.TitlecaseLetter
            or System.Globalization.UnicodeCategory.ModifierLetter
            or System.Globalization.UnicodeCategory.OtherLetter
            or System.Globalization.UnicodeCategory.LetterNumber
            or System.Globalization.UnicodeCategory.DecimalDigitNumber;
    }

}
