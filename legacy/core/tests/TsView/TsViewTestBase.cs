using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public abstract class TsViewTestBase
{
    protected static void AssertBescriptAstEqual(BescriptAst expected, BescriptAst actual)
    {
        Assert.Equal(expected.RootNodes.Count, actual.RootNodes.Count);
        for (int i = 0; i < expected.RootNodes.Count; i++)
        {
            AssertNode(expected.RootNodes[i], actual.RootNodes[i]);
        }

        Assert.Equal(expected.DeclaredVariables.Count, actual.DeclaredVariables.Count);
        foreach (KeyValuePair<string, BescriptVarType> kv in expected.DeclaredVariables)
        {
            Assert.True(actual.DeclaredVariables.TryGetValue(kv.Key, out BescriptVarType val), $"Missing variable {kv.Key}");
            Assert.Equal(kv.Value, val);
        }
    }

    private static void AssertNode(BescriptAstNode expected, BescriptAstNode actual)
    {
        Assert.Equal(expected.Depth, actual.Depth);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.IsDisabled, actual.IsDisabled);
        if (expected is IHasCall expectedHasCall && actual is IHasCall actualHasCall)
        {
            AssertCall(expectedHasCall.Call, actualHasCall.Call);
        }
        else
        {
            Assert.Equal(expected is IHasCall, actual is IHasCall);
        }
        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertNode(expected.Children[i], actual.Children[i]);
        }
    }

    private static void AssertCall(BescriptCallSignature? expected, BescriptCallSignature? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected is null, actual is null);
            return;
        }

        Assert.Equal(expected.Kind, actual.Kind);
        string? expectedOwnerType = expected switch { VarfCallSignature v => v.OwnerType, ActCallSignature a => a.OwnerType, DvfCallSignature d => d.OwnerType, _ => null };
        string? actualOwnerType = actual switch { VarfCallSignature v => v.OwnerType, ActCallSignature a => a.OwnerType, DvfCallSignature d => d.OwnerType, _ => null };
        Assert.Equal(expectedOwnerType, actualOwnerType);
        Assert.Equal(expected.FunctionName, actual.FunctionName);
        Assert.Equal(expected.ReturnVar?.Name, actual.ReturnVar?.Name);
        Assert.Equal(expected.Arguments.Count, actual.Arguments.Count);
    }

    protected static IReadOnlyList<(int LineNumber, string LineText)> ToLines(params string[] textLines)
    {
        return textLines.Select(static (t, i) => (i + 1, t)).ToList();
    }

    protected static string? TryConvertMethodBody(IReadOnlyList<(int LineNumber, string LineText)> lines)
    {
        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(lines, ctx, expectedRetVar: null, []);
        return result.IsSuccess ? null : result.Error;
    }

    protected static string? TryParseOnly(IReadOnlyList<(int LineNumber, string LineText)> lines)
    {
        string text = string.Join("\n", lines.Select(static l => l.LineText));
        string json = TsCompilerHost.FormatMethodBody(text);
        MethodBodyFormatResult result = System.Text.Json.JsonSerializer.Deserialize<MethodBodyFormatResult>(json, BodyWritebackJson.Options)
            ?? throw new InvalidOperationException("TS 官方方法体格式化返回空结果。");
        return result.Error;
    }

    private sealed record MethodBodyFormatResult(string Text, string? Error, int? ErrorLine);
}
