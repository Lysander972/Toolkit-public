using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// TS 视图 patch 中“误用未压缩原始 BeScript 方法名 → 压缩名”自动纠偏的测试。
/// </summary>
/// <remarks>
/// 真实压缩样本（NumericSuffix 规则）：
/// <list type="bullet">
///   <item>Global: bool.and(2参)..bool.and4(6参) → 压缩名 bool.and</item>
///   <item>VarFunc: List.add(1参)..List.add7(8参) → 压缩名 add</item>
///   <item>DeviceAction: MechShooter.generate0(1参)..generate8(9参) → 压缩名 generate</item>
/// </list>
/// 负面用例验证精准门控：只有输入名精确命中真实 bes 原始名才纠偏，零猜测。
/// </remarks>
public sealed class TsViewMethodNameCorrectionTests
{
    // ═══════════════════════════════════════════════════════════════════
    // Global 函数纠偏（bool.and 家族）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveGlobal_WhenRawUncompressedName_ShouldCorrectToCompressed()
    {
        // bool.and3(5参) 是真实 bes 原始名，TS 视图合法的是压缩名 bool.and
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveGlobalWithCorrection(
            "bool.and3", null, 5);

        Assert.True(resolved.Result.Found);
        Assert.True(resolved.Corrected);
        Assert.Equal("bool.and3", resolved.Result.BesMethodName);
        Assert.Equal("bool.and", resolved.CorrectedTsName);
    }

    [Fact]
    public void ResolveGlobal_WhenCompressedName_ShouldResolveWithoutCorrection()
    {
        // bool.and 自身是合法压缩名，第一次查即命中，不触发纠偏
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveGlobalWithCorrection(
            "bool.and", null, 2);

        Assert.True(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveGlobal_WhenSuffixExceedsFamilyLimit_ShouldFail()
    {
        // bool.and 家族上限 bool.and4，bool.and99 不存在 → 精准门控拒绝
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveGlobalWithCorrection(
            "bool.and99", null, 100);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveGlobal_WhenRawNameButWrongArgCount_ShouldFail()
    {
        // bool.and 家族合法 argCount 为 2..6；argCount=100 超出范围，压缩名命中但双射未命中
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveGlobalWithCorrection(
            "bool.and3", null, 100);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveGlobal_WhenForgedName_ShouldFail()
    {
        // 完全不存在的伪造名 → 精准门控拒绝
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveGlobalWithCorrection(
            "bool.nonexistent", null, 2);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // VarFunc 纠偏（List.add 家族 + 未压缩方法加后缀）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveVarFunc_WhenRawUncompressedName_ShouldCorrectToCompressed()
    {
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveVarFuncWithCorrection(
            "List", "add3", null, 4);

        Assert.True(resolved.Result.Found);
        Assert.True(resolved.Corrected);
        Assert.Equal("add3", resolved.Result.BesMethodName);
        Assert.Equal("add", resolved.CorrectedTsName);
    }

    [Fact]
    public void ResolveVarFunc_WhenSuffixExceedsFamilyLimit_ShouldFail()
    {
        // List.add 家族上限 add7，add8 不存在
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveVarFuncWithCorrection(
            "List", "add8", null, 9);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveVarFunc_WhenUncompressedMethodGivenNumericSuffix_ShouldFail()
    {
        // List.count 无压缩族；强行加数字后缀 count1 不是真实 bes 方法 → 精准门控拒绝
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveVarFuncWithCorrection(
            "List", "count1", null, 0);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveVarFunc_WhenRawNameButWrongArgCount_ShouldFail()
    {
        // List.add 家族合法 argCount 为 1..8；argCount=100 超出范围，压缩名命中但双射未命中
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveVarFuncWithCorrection(
            "List", "add3", null, 100);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // DeviceAction 纠偏（MechShooter.generate 家族）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveDeviceAction_WhenRawUncompressedName_ShouldCorrectToCompressed()
    {
        // generate0..generate8 压缩为 generate；generate3 = 4 参
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveDeviceActionWithCorrection(
            "MechShooter", "generate3", null, 4);

        Assert.True(resolved.Result.Found);
        Assert.True(resolved.Corrected);
        Assert.Equal("generate3", resolved.Result.BesMethodName);
        Assert.Equal("generate", resolved.CorrectedTsName);
    }

    [Fact]
    public void ResolveDeviceAction_WhenSuffixExceedsFamilyLimit_ShouldFail()
    {
        // generate 家族上限 generate8，generate9 不存在
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveDeviceActionWithCorrection(
            "MechShooter", "generate9", null, 10);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    [Fact]
    public void ResolveDeviceAction_WhenRawNameButWrongArgCount_ShouldFail()
    {
        // generate 家族合法 argCount 为 1..9；argCount=100 超出范围，压缩名命中但双射未命中
        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveDeviceActionWithCorrection(
            "MechShooter", "generate3", null, 100);

        Assert.False(resolved.Result.Found);
        Assert.False(resolved.Corrected);
    }

    // ═══════════════════════════════════════════════════════════════════
    // UI 子类压缩纠偏（UIButton.PointDownCB 家族）
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveVarFunc_WhenUiSubclassCompressedMethod_ShouldCorrect()
    {
        // UIButton 自身挂有 PointDownCB 压缩族；动态取真实成员避免硬编
        IReadOnlyList<BescriptDtsGenerator.MemberCompressionEntry> candidates =
            BescriptDtsGenerator.GetVarFuncOriginalCandidates("UIButton", "PointDownCB");
        Assert.NotEmpty(candidates);

        BescriptDtsGenerator.MemberCompressionEntry rawEntry = candidates.First(
            c => !string.Equals(c.OriginalName, "PointDownCB", StringComparison.Ordinal));

        TsViewCallMapper.CorrectionResolve resolved = TsViewCallMapper.ResolveVarFuncWithCorrection(
            "UIButton", rawEntry.OriginalName, rawEntry.GenericBeTypeName, rawEntry.ParameterCount);

        Assert.True(resolved.Result.Found);
        Assert.True(resolved.Corrected);
        Assert.Equal("PointDownCB", resolved.CorrectedTsName);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 端到端：构造期分类放行 + 映射期发单点 Warning
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void EndToEnd_VarfCallWithRawUncompressedName_ShouldParseAndEmitWarning()
    {
        ParsedBody parsed = ParseBodyWithWarnings(
            "var __lst: BeList;",
            "__lst.add1(__a, __b);");

        Assert.True(parsed.Result.IsSuccess, parsed.Result.Error);
        // 构造期分类放行 + 映射期发单点纠偏 Warning
        Assert.Contains(parsed.Warnings, w =>
            w.Severity == ToolDiagnosticSeverity.Warning &&
            w.Message.Contains("add1", StringComparison.Ordinal) &&
            w.Message.Contains("压缩", StringComparison.Ordinal));
    }

    // ═══════════════════════════════════════════════════════════════════
    // 多调用通用场景
    // patch_mech_view 单点 patch 与 patch_mech_view_multiedit 都经 BuildPatch →
    // 同一 body 解析层，故 body 级测试覆盖所有入口。List.add 家族：add(1)..add7(8)。
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiCall_AllCorrectable_ShouldEmitWarningPerCorrection()
    {
        ParsedBody parsed = ParseBodyWithWarnings(
            "var __lst: BeList;",
            "__lst.add1(__a, __b);",
            "__lst.add2(__c, __d, __e);");

        Assert.True(parsed.Result.IsSuccess, parsed.Result.Error);
        Assert.Equal(2, CountCorrectionWarnings(parsed.Warnings));
    }

    [Fact]
    public void MultiCall_CorrectableAndCorrect_ShouldEmitWarningOnlyForCorrections()
    {
        ParsedBody parsed = ParseBodyWithWarnings(
            "var __lst: BeList;",
            "__lst.add1(__a, __b);",
            "__lst.add(__c);",
            "__lst.add2(__d, __e, __f);");

        Assert.True(parsed.Result.IsSuccess, parsed.Result.Error);
        Assert.Equal(2, CountCorrectionWarnings(parsed.Warnings));
    }

    [Fact]
    public void MultiCall_CorrectableAndUncorrectable_ShouldFail()
    {
        Assert.False(ParseBodyWithWarnings(
            "var __lst: BeList;",
            "__lst.add1(__a, __b);",
            "__lst.count1(__c);").Result.IsSuccess);
    }

    [Fact]
    public void MultiCall_UncorrectableAndCorrect_ShouldFail()
    {
        Assert.False(ParseBodyWithWarnings(
            "var __lst: BeList;",
            "__lst.count1(__a);",
            "__lst.add(__b);").Result.IsSuccess);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 辅助方法
    // ═══════════════════════════════════════════════════════════════════

    private sealed record ParsedBody(
        TsViewBodyParser.TsBodyParseResult Result,
        List<ToolError> Warnings);

    private static ParsedBody ParseBodyWithWarnings(params string[] lines)
    {
        ScriptConversionContext context = new(
            BuiltinMethodRegistry.Instance,
            ActTargetMap.Empty,
            new Dictionary<string, BescriptVarType>());
        List<ToolError> warnings = new();
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(
            ToLines(lines), context, expectedRetVar: null, warnings);
        return new ParsedBody(result, warnings);
    }

    private static int CountCorrectionWarnings(List<ToolError> warnings)
        => warnings.Count(w => w.Severity == ToolDiagnosticSeverity.Warning && w.Message.Contains("未压缩", StringComparison.Ordinal));

    private static IReadOnlyList<(int LineNumber, string LineText)> ToLines(params string[] lines)
        => lines.Select((text, idx) => (idx + 1, text)).ToList();
}
