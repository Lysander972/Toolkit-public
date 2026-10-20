using System.Text.RegularExpressions;

namespace Toolkit.Core.Sdk;

/// <summary>
/// BeScript 命名规则验证。
/// 属于 BeScript 语言规范定义。
/// </summary>
internal static class BeScriptNaming
{
    private const string IdentifierFirstChar = "[a-zA-Z_]";
    private const string IdentifierRestChars = "[a-zA-Z0-9_]*";
    private const string IdentifierPat = IdentifierFirstChar + IdentifierRestChars;

    /// <summary>标识符 token：BeScript 唯一标识符规则。字母或下划线开头，仅含字母、数字、下划线。</summary>
    public static readonly Regex Identifier =
        new("^" + IdentifierPat + "$", RegexOptions.Compiled);

    /// <summary>
    /// 全局函数 saveName 路径：标识符首段 + 零或多个([./]段)。
    /// 后续段允许数字开头（底层真实 saveName 如 ui.2dmode）。底层无字符级词法规则，此为 SDK 保守白名单。
    /// </summary>
    public static readonly Regex GlobalFuncPath =
        new("^" + IdentifierPat + "(?:[./][a-zA-Z0-9_]+)*$", RegexOptions.Compiled);
}
