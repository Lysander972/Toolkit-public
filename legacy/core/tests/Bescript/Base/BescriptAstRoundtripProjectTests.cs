using System.Text;
using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Bescript.Base;

/// <summary>
/// 使用真实工程脚本验证 BeScript 文本 ↔ BescriptAst 的无损往返。
/// 基于 test/data/projects 下所有可解析的 .code 文件。
/// 要求：剥离行号信息后，字节级相同。
/// </summary>
public sealed class BescriptAstRoundtripProjectTests
{
    [Fact]
    public void BescriptAst_Roundtrip_ShouldMatch_ProjectScripts()
    {
        foreach (ProjectDirectory project in TestProjectEnumerator.EnumerateProjects())
        {
            IEnumerable<string> codePaths = TestProjectEnumerator.EnumerateAllScripts(project.Path);

            ParallelTestRunner.RunParallel(
                codePaths,
                ParallelTestRunner.SafeTest<string>(static codePath =>
                {
                    string text = SystemIOWrapper.ReadAllText(codePath);
                    (BescriptScriptInfo info, List<ToolError> errors, List<ToolError> _) = BescriptTextParser.Parse(codePath, text);
                    if (errors.Count > 0)
                    {
                        throw new Exception($"解析失败: {codePath}: {errors[0].Message}");
                    }

                    BescriptAst ast = info.Ast;
                    string serialized = BescriptAstSerializer.SerializeFull(ast);

                    string normalizedOriginal = StripLineNumbersFromScript(text);
                    string normalizedSerialized = StripLineNumbersFromScript(serialized);

                    if (normalizedOriginal != normalizedSerialized)
                    {
                        throw new Exception($"字节级不一致: {codePath}\n--- 原文(剥离行号后) ---\n{normalizedOriginal}\n--- 序列化结果 ---\n{normalizedSerialized}");
                    }
                }),
                contextName: $"BescriptAstRoundtrip:{project.Name}");
        }
    }

    /// <summary>
    /// 剥离脚本命令区的行号前缀。
    /// 仅对 lines: 和 endcode 之间的行进行处理。
    /// </summary>
    private static string StripLineNumbersFromScript(string text)
    {
        StringBuilder sb = new StringBuilder();
        bool inCommandSection = false;

        using StringReader reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!inCommandSection)
            {
                if (line.StartsWith("lines:", StringComparison.Ordinal))
                {
                    inCommandSection = true;
                }
                sb.Append(line).Append('\n');
                continue;
            }

            if (line.StartsWith("endcode", StringComparison.Ordinal))
            {
                sb.Append(line).Append('\n');
                inCommandSection = false;
                continue;
            }

            // 剥离行号前缀
            sb.Append(StripLineNumber(line)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将 "12:  code" 或 "   12:  code" 等形式中的行号部分去掉。
    /// </summary>
    private static string StripLineNumber(string line)
    {
        ReadOnlySpan<char> span = line.AsSpan();
        int index = 0;
        while (index < span.Length && span[index] == ' ')
        {
            index++;
        }

        ReadOnlySpan<char> rest = span[index..];
        int colonIndex = rest.IndexOf(':');
        if (colonIndex <= 0)
        {
            return line;
        }

        ReadOnlySpan<char> numberPart = rest[..colonIndex];
        for (int i = 0; i < numberPart.Length; i++)
        {
            if (!char.IsDigit(numberPart[i]))
            {
                return line;
            }
        }

        ReadOnlySpan<char> after = rest[(colonIndex + 1)..];
        return after.ToString();
    }
}
