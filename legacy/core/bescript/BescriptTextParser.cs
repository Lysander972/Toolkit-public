using Toolkit.Core.Infra;

namespace Toolkit.Core.Bescript;

/// <summary>
/// BEScript 文本格式解析器。
/// </summary>
public static class BescriptTextParser
{
    public static (BescriptScriptInfo Script, List<ToolError> Errors, List<ToolError> Warnings) Parse(string scriptRelativePath, string text)
    {
        return BescriptSyntaxParser.ParseScript(scriptRelativePath, text);
    }
}
