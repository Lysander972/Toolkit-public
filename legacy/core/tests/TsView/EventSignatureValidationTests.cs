using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class EventSignatureValidationTests
{
    /// <summary>
    /// 复现 ValidateEventMethodSignature 的核心校验逻辑：
    /// 剥离所有名为 "this" 的参数后与 expected 比较。
    /// </summary>
    private static ToolError? Validate(
        IReadOnlyList<TsViewParamSpec> actual,
        IReadOnlyList<TsViewParamSpec> expected,
        string eventName)
    {
        IReadOnlyList<TsViewParamSpec> stripped = actual.Where(p => p.Name != "this").ToArray();

        if (stripped.Count != expected.Count)
            return new ToolError(null, null, null, $"事件方法签名不匹配: {eventName}。参数数量不一致: 期望 {expected.Count} 个，实际 {stripped.Count} 个。", ToolDiagnosticSeverity.Fatal);

        for (int i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(stripped[i].TypeName, expected[i].TypeName, StringComparison.Ordinal))
                return new ToolError(null, null, null, $"事件方法签名不匹配: {eventName}。参数 {i + 1} 类型不一致: 期望 {expected[i].TypeName}，实际 {stripped[i].TypeName}。", ToolDiagnosticSeverity.Fatal);
        }

        return null;
    }

    // ── 生成侧注入 this 的场景 ──────────────────────────────────────────

    [Fact]
    public void InjectedThis_ParamsMatch_ShouldPass()
    {
        TsViewParamSpec[] actual = [new("this", "Device_测试_0"), new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        TsViewParamSpec[] expected = [new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        Assert.Null(Validate(actual, expected, "clientStart"));
    }

    [Fact]
    public void InjectedThis_ParamsMismatch_ShouldReject()
    {
        TsViewParamSpec[] actual = [new("this", "Device_测试_0"), new("$mapIndex", "BeString")];
        TsViewParamSpec[] expected = [new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        ToolError? error = Validate(actual, expected, "clientStart");
        Assert.NotNull(error);
        Assert.Equal(ToolDiagnosticSeverity.Fatal, error.Severity);
    }

    [Fact]
    public void InjectedThis_NoExpectedParams_ShouldPass()
    {
        TsViewParamSpec[] actual = [new("this", "Device_测试_0")];
        Assert.Null(Validate(actual, [], "clientUpdate"));
    }

    // ── 无 this 的场景（AI 新建可能缺失） ──────────────────────────────

    [Fact]
    public void NoThis_ParamsMatch_ShouldPass()
    {
        TsViewParamSpec[] actual = [new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        TsViewParamSpec[] expected = [new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        Assert.Null(Validate(actual, expected, "clientStart"));
    }

    [Fact]
    public void NoThis_ParamsMismatch_ShouldReject()
    {
        TsViewParamSpec[] actual = [new("$mapIndex", "BeString"), new("$rule", "BeString")];
        TsViewParamSpec[] expected = [new("$mapIndex", "BeFloat"), new("$rule", "BeString")];
        ToolError? error = Validate(actual, expected, "clientStart");
        Assert.NotNull(error);
        Assert.Equal(ToolDiagnosticSeverity.Fatal, error.Severity);
    }

    [Fact]
    public void Empty_NoExpectedParams_ShouldPass()
    {
        Assert.Null(Validate([], [], "clientUpdate"));
    }

    // ── BeScript 参数 this 经转义后不会与注入 this 冲突 ─────────────────

    [Fact]
    public void BescriptThisParam_EscapedToX_this()
    {
        // BeScript 参数名 "this" 是 TS 保留字，ToTsIdentifier 必须转义为 "X_this"
        string escaped = TsViewTypeUtils.ToTsIdentifier("this");
        Assert.Equal("X_this", escaped);
        Assert.NotEqual("this", escaped);
    }

    [Fact]
    public void EscapedThisParam_NotStrippedByValidation()
    {
        // BeScript 参数 "this" 转义为 "X_this" 后，不应被 this 剥离逻辑移除
        TsViewParamSpec[] actual = [new("this", "Device_测试_0"), new("X_this", "BeString"), new("$rule", "BeString")];
        TsViewParamSpec[] expected = [new("X_this", "BeString"), new("$rule", "BeString")];
        Assert.Null(Validate(actual, expected, "clientStart"));
    }

    [Fact]
    public void EscapedThisOnly_InjectionPresent_NoConflict()
    {
        // 零参数事件，BeScript 参数 "this" 被转义为 X_this，与注入的 this 共存不冲突
        TsViewParamSpec[] actual = [new("this", "Device_测试_0"), new("X_this", "BeString")];
        TsViewParamSpec[] expected = [new("X_this", "BeString")];
        Assert.Null(Validate(actual, expected, "someEvent"));
    }
}
