using System.Text.Json;

using Bazel;

using Toolkit.Core.TsView;

using Xunit;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// 增量转译（片段缓存 + 骨架装配，生产路径）与全量 AST 参考路径（直接全文件官方 printer）
/// 的输出字节级一致性门禁。
/// 全量参考实现（baselineReference.ts）不进生产 bundle，经测试 superset bundle（tsconfig.test.json
/// 连编生产源码 + 参考）注入：静态构造把它装进 TsCompilerHost.OverrideTraversalScript 并 Reset，
/// 使引擎池加载 superset（生产行为不变，仅多出全量参考入口）。纯单元测试：手写 DeviceTsViewBuildContext。
/// </summary>
public sealed class TsViewIncrementalEquivalenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    static TsViewIncrementalEquivalenceTests()
    {
        // 注入测试 superset bundle 并强制引擎池以此重建：全量参考只在此在轨。
        TsCompilerHost.OverrideTraversalScript = LoadTestSupersetBundle();
        TsCompilerHost.Reset();
    }

    private static string LoadTestSupersetBundle()
    {
        string? path = Runfiles.Create().Rlocation(
            "_main/src/core/tsview/v8/tsview-traversal-test.DO_NOT_READ_OR_EDIT.generated.js");
        if (path is null || !File.Exists(path))
            throw new InvalidOperationException(
                "未找到测试 superset bundle（tsview-traversal-test.generated.js）。");
        return File.ReadAllText(path);
    }

    private static string BuildInputJson(DeviceTsViewBuildContext ctx) =>
        JsonSerializer.Serialize(ctx, JsonOptions);

    private static string BuildTsView(DeviceTsViewBuildContext ctx)
    {
        string json = TsCompilerHost.BuildDeviceView(BuildInputJson(ctx));
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out JsonElement err) && err.GetString() is not null)
            Assert.Fail($"增量路径返回错误: {err.GetString()}");
        return doc.RootElement.GetProperty("tsView").GetString()!;
    }

    private static DeviceTsViewBuildContext MakeContext(
        string className = "TestDevice_DeviceType",
        bool isBirthBuilding = false,
        IReadOnlyList<TsViewGlobalVarInfo>? globalVars = null,
        IReadOnlyList<ScriptMethodInfo>? scripts = null,
        IReadOnlyList<EventMethodGroupInfo>? events = null) => new()
        {
            ReferencePaths = [],
            CommentLines = ["// test device"],
            ExtraCommentLines = [],
            OuterNamespace = "TestDevice",
            DeviceClassName = className,
            BaseTypeName = "DeviceType_1110",
            IsBirthBuilding = isBirthBuilding,
            AllowedEvents = [],
            GlobalVars = globalVars ?? [],
            ScriptMethodInfos = scripts ?? [],
            EventMethodInfos = events ?? [],
        };

    private static ScriptMethodInfo MakeScript(string name = "test") => new(
        OriginalName: name,
        MethodName: name,
        SourcePath: null,
        Guid: null,
        IsCodeRepo: false,
        Header: new DeviceTsViewBescriptHeader(name, null, false, false, false, null, null, [], null),
        IsAsync: false,
        IsPrivate: false,
        Parameters: [],
        ReturnType: "void",
        Body: new DeviceTsViewScriptBody([], [])
    );

    private static void AssertEquivalent(DeviceTsViewBuildContext ctx)
    {
        string inputJson = BuildInputJson(ctx);
        string incrementalJson = TsCompilerHost.BuildDeviceView(inputJson);
        string baselineJson = TsCompilerHost.BuildDeviceViewBaseline(inputJson);

        using JsonDocument inc = JsonDocument.Parse(incrementalJson);
        using JsonDocument baseline = JsonDocument.Parse(baselineJson);

        if (baseline.RootElement.TryGetProperty("error", out JsonElement baseErr) && baseErr.GetString() is not null)
            Assert.Fail($"全量参考路径返回错误: {baseErr.GetString()}");
        if (inc.RootElement.TryGetProperty("error", out JsonElement incErr) && incErr.GetString() is not null)
            Assert.Fail($"增量路径返回错误: {incErr.GetString()}");

        string incrementalTsView = inc.RootElement.GetProperty("tsView").GetString()!;
        string baselineTsView = baseline.RootElement.GetProperty("tsView").GetString()!;

        // 字节级等价：逐字符相等，一个字符的差异都不允许。
        Assert.Equal(baselineTsView, incrementalTsView);
    }

    [Fact]
    public void EmptyDevice_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext());
    }

    [Fact]
    public void GlobalVars_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext(globalVars:
        [
            new TsViewGlobalVarInfo("count", "count", "BeInt", "0", null),
            new TsViewGlobalVarInfo("name", "name", "BeString", "", "a name"),
        ]));
    }

    [Fact]
    public void ScriptMethods_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext(scripts: [MakeScript("test1"), MakeScript("test2")]));
    }

    [Fact]
    public void EventMethods_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext(events:
        [
            new EventMethodGroupInfo("OnTick", "OnTick", [], [MakeScript("onTick")], "tick event"),
        ]));
    }

    [Fact]
    public void BirthBuilding_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext(
            isBirthBuilding: true,
            scripts: [MakeScript("birthTest")],
            globalVars: [new TsViewGlobalVarInfo("x", "x", "BeInt", "0", null)]
        ));
    }

    [Fact]
    public void Combined_IncrementalEqualsBaseline()
    {
        AssertEquivalent(MakeContext(
            globalVars: [new TsViewGlobalVarInfo("count", "count", "BeInt", "0", null)],
            scripts: [MakeScript("method1"), MakeScript("method2")],
            events:
            [
                new EventMethodGroupInfo("OnTick", "OnTick", [],
                    [MakeScript("onTick1"), MakeScript("onTick2")], "tick event"),
            ]
        ));
    }

    [Fact]
    public void CachedFragment_SecondBuildStillEquivalent()
    {
        DeviceTsViewBuildContext ctx = MakeContext(scripts: [MakeScript("cached")]);
        string inputJson = BuildInputJson(ctx);

        // 第一次构建（填充片段缓存），仍须与全量参考一致。
        AssertEquivalent(ctx);

        // 第二次构建（命中缓存），再钉一次。
        string incrementalJson = TsCompilerHost.BuildDeviceView(inputJson);
        string baselineJson = TsCompilerHost.BuildDeviceViewBaseline(inputJson);

        string inc = JsonDocument.Parse(incrementalJson).RootElement.GetProperty("tsView").GetString()!;
        string baseline = JsonDocument.Parse(baselineJson).RootElement.GetProperty("tsView").GetString()!;
        Assert.Equal(baseline, inc);
    }

    [Fact]
    public void ContentChange_SamePartAndNameDifferentContent_NotStale()
    {
        // 同零件 + 同名全局变量、不同类型（BeInt vs BeString）：内容指纹变化→miss 重算，
        // 第二次构建必须反映新内容，不许命中旧片段停在 BeInt。
        DeviceTsViewBuildContext ctxA = MakeContext(globalVars:
            [new TsViewGlobalVarInfo("count", "count", "BeInt", "0", null)]);
        DeviceTsViewBuildContext ctxB = MakeContext(globalVars:
            [new TsViewGlobalVarInfo("count", "count", "BeString", "0", null)]);

        // 先构建 A 填充缓存，再构建 B（同名不同内容）。
        BuildTsView(ctxA);
        string viewB = BuildTsView(ctxB);

        // 必须出现新类型 BeString；若缓存键缺内容指纹会停在被缓存的 BeInt。
        Assert.Contains("BeString", viewB);
        Assert.DoesNotContain("BeInt.GlobalVarInitialize", viewB);
    }
}
