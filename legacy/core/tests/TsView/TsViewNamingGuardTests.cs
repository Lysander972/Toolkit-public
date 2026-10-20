using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Tests.TsView;

public sealed class TsViewNamingGuardTests : TsViewTestBase
{
    [Fact]
    public void TryDetectEscapedIdentifierLiteral_ShouldDetect_EscapedText()
    {
        const string escaped = "X_输入判断_x27_选阵营";

        string? decoded = TsViewNaming.TryDetectEscapedIdentifierLiteral(escaped);

        Assert.NotNull(decoded);
        Assert.Equal("输入判断'选阵营", decoded);
    }

    [Fact]
    public void TryDetectEscapedIdentifierLiteral_ShouldDetect_KeywordEscapedText()
    {
        const string escapedKeyword = "X_class";

        string? decoded = TsViewNaming.TryDetectEscapedIdentifierLiteral(escapedKeyword);

        Assert.NotNull(decoded);
        Assert.Equal("class", decoded);
    }

    [Fact]
    public void TryDetectEscapedIdentifierLiteral_NormalName_ShouldNotDetect()
    {
        Assert.Null(TsViewNaming.TryDetectEscapedIdentifierLiteral("普通变量名"));
    }

    [Fact]
    public void ScanClass_BsVarRawNameUsesEscapedText_ShouldFail()
    {
        List<string> lines =
        [
            "@events(\"clientStart\")",
            "export class Device_Test_0 extends DeviceType_1101 {",
            "    // [Globals] BEGIN",
            "    @bsVar({ name: \"X_输入判断_x27_选阵营\" })",
            "    private X_输入判断_x27_选阵营: BeString = BeString.GlobalVarInitialize();",
            "    // [Globals] END",
            "    // [Script] BEGIN",
            "    /**",
            "    * name: 测试",
            "    * sourcePath: map.map_/0/scripts/测试.code",
            "    */",
            "    public 测试 = BeScript({ name: \"测试\" })(function () {",
            "        return;",
            "    });",
            "    // [Script] END",
            "}"
        ];

        TsViewAstScanner.ScanClassResult result = TsViewAstScanner.ScanClass(
            lines,
            className: "Device_Test_0",
            entryClassName: null,
            filePath: "test.ts");

        Assert.NotNull(result.Error);
        Assert.Contains("@bsVar.name 禁止使用转义后的标识符文本", result.Error.Message);
    }

    [Fact]
    public void ScanClass_BsVarComment_ShouldPreserve()
    {
        List<string> lines =
        [
            "@events(\"clientStart\")",
            "export class Device_Test_0 extends DeviceType_1101 {",
            "    // [Globals] BEGIN",
            "    @bsVar({ name: \"输入判断\", comment: \"坐标说明\" })",
            "    private 输入判断: BeString = BeString.GlobalVarInitialize();",
            "    // [Globals] END",
            "    // [Script] BEGIN",
            "    /**",
            "    * name: 测试",
            "    * sourcePath: map.map_/0/scripts/测试.code",
            "    */",
            "    public 测试 = BeScript({ name: \"测试\" })(function () {",
            "        return;",
            "    });",
            "    // [Script] END",
            "}"
        ];

        TsViewAstScanner.ScanClassResult result = TsViewAstScanner.ScanClass(
            lines,
            className: "Device_Test_0",
            entryClassName: null,
            filePath: "test.ts");

        Assert.Null(result.Error);
        Assert.Single(result.Globals);
        Assert.Equal("坐标说明", result.Globals[0].Comment);
    }

    [Fact]
    public void ScanClass_BeScriptNameUsesEscapedText_ShouldFail()
    {
        List<string> lines =
        [
            "@events(\"clientStart\")",
            "export class Device_Test_0 extends DeviceType_1101 {",
            "    // [Globals] BEGIN",
            "    // [Globals] END",
            "    // [Script] BEGIN",
            "    /**",
            "    * name: X_输入判断_x27_选阵营",
            "    * sourcePath: map.map_/0/scripts/输入判断'选阵营.code",
            "    */",
            "    public X_输入判断_x27_選阵营 = BeScript({ name: \"X_输入判断_x27_选阵营\" })(function () {",
            "        return;",
            "    });",
            "    // [Script] END",
            "}"
        ];

        TsViewAstScanner.ScanClassResult result = TsViewAstScanner.ScanClass(
            lines,
            className: "Device_Test_0",
            entryClassName: null,
            filePath: "test.ts");

        Assert.NotNull(result.Error);
        Assert.Contains("BeScript.name 禁止使用转义后的标识符文本", result.Error.Message);
    }

    [Fact]
    public void ScanClass_MethodParamUsesNonCanonicalEscapedName_ShouldFail()
    {
        List<string> lines =
        [
            "@events(\"clientStart\")",
            "export class Device_Test_0 extends DeviceType_1101 {",
            "    // [Globals] BEGIN",
            "    // [Globals] END",
            "    // [Script] BEGIN",
            "    /**",
            "    * name: 测试",
            "    * sourcePath: map.map_/0/scripts/测试.code",
            "    */",
            "    public 测试 = BeScript({ name: \"测试\" })(function (X_abc: BeString) {",
            "        return;",
            "    });",
            "    // [Script] END",
            "}"
        ];

        TsViewAstScanner.ScanClassResult result = TsViewAstScanner.ScanClass(
            lines,
            className: "Device_Test_0",
            entryClassName: null,
            filePath: "test.ts");

        Assert.NotNull(result.Error);
        Assert.Contains("方法参数名必须使用唯一映射的变量转义名", result.Error.Message);
    }

    [Fact]
    public void TryMapBody_LocalsVarUsesNonCanonicalEscapedName_ShouldFail()
    {
        IReadOnlyList<(int LineNumber, string LineText)> lines = ToLines(
            "var X_abc: BeString;",
            "return;");

        ScriptConversionContext ctx = new ScriptConversionContext(BuiltinMethodRegistry.Instance, actTargetMap: ActTargetMap.Empty, globalVariables: new Dictionary<string, BescriptVarType>());
        TsViewBodyParser.TsBodyParseResult result = TsViewBodyParser.ParseTsBodyToAst(
            lines,
            ctx,
            expectedRetVar: null,
            []);

        Assert.False(result.IsSuccess);
        Assert.Contains("唯一映射的变量转义名", result.Error);
    }

    [Fact]
    public void TryMapBody_ScrollViewMethodEnum_ShouldPass()
    {
        string? error = TryConvertMethodBody(ToLines(
            "var rankListHost: BeUIRect;",
            "var rankListSlider: BeUIRect;",
            "rankListHost.scrollView(rankListSlider, BeEnum.fromBeConst<ScrollViewMovement>(ScrollViewMovement.Elastic));"));

        Assert.Null(error);
    }

    [Fact]
    public void TryMapBody_ScriptMethodNameEscapedIdentifier_ShouldFail()
    {
        string? error = TryConvertMethodBody(ToLines(
            "var selfDevice: BeDevice;",
            "selfDevice.callFun(X_abc);"));

        Assert.NotNull(error);
        Assert.Contains("方法名位置", error, StringComparison.Ordinal);
    }
}
