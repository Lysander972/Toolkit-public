using Toolkit.Core.Infra;

namespace Toolkit.Core.Bescript;

/// <summary>
/// BeScript 脚本来源
/// </summary>
public sealed record BescriptSource(string? ScriptRelativePath, string Text);

/// <summary>
/// BeScript 解析入口 - 纯文本处理，无文件 IO，无持久化层耦合。
/// </summary>
public static class BescriptParser
{
    /// <summary>
    /// 解析 BeScript 文本。
    /// </summary>
    /// <param name="scriptRelativePath">脚本相对路径（仅用于错误信息）</param>
    /// <param name="text">脚本文本内容</param>
    /// <returns>解析结果、错误列表、警告列表</returns>
    public static (BescriptParseData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Parse(
        string scriptRelativePath,
        string text)
    {
        (BescriptScriptInfo info, List<ToolError> parseErrors, List<ToolError> parseWarnings) = BescriptTextParser.Parse(scriptRelativePath, text);

        BescriptParseData data = new(
            Name: info.Header.Name,
            Comment: info.Header.Comment,
            Debug: info.Header.Debug,
            Skip: info.Header.Skip,
            Private: info.Header.Private,
            Guid: info.Header.Guid,
            Color: info.Header.Color,
            Parameters: info.Header.Parameters,
            ReturnParameter: info.Header.ReturnParameter,
            Ast: info.Ast
        );

        return (data, parseErrors, parseWarnings);
    }
}

/// <summary>
/// BeScript 解析结果
/// </summary>
public sealed record BescriptParseData(
    string Name,
    string? Comment,
    bool Debug,
    bool Skip,
    bool Private,
    string? Guid,
    BescriptColor? Color,
    IReadOnlyList<BescriptParameter> Parameters,
    BescriptParameter? ReturnParameter,
    BescriptAst Ast
);
