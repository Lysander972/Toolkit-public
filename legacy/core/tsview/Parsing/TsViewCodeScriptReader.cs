using Toolkit.Core.Bescript;

namespace Toolkit.Core.TsView;

/// <summary>
/// .code 脚本解析结果。
/// 【设计】直接保留 BescriptScriptInfo，不做冗余的 commandLines 提取。
/// </summary>
internal sealed record TsViewCodeScriptParseResult(
    BescriptScriptInfo ScriptInfo
);

/// <summary>
/// .code 脚本文件读取器，解析 BEScript 格式。
/// </summary>
internal static class TsViewCodeScriptReader
{
    /// <summary>
    /// 解析脚本文本。
    /// </summary>
    internal static TsViewCodeScriptParseResult Parse(string parsePath, string text)
    {
        BescriptScriptInfo scriptInfo = BescriptTextParser.Parse(parsePath, text).Script;
        return new TsViewCodeScriptParseResult(scriptInfo);
    }

    /// <summary>
    /// 从 BescriptTextParser 的结果创建解析结果。
    /// </summary>
    internal static TsViewCodeScriptParseResult ParseFromResult(BescriptScriptInfo scriptInfo, string text)
    {
        return new TsViewCodeScriptParseResult(scriptInfo);
    }
}
