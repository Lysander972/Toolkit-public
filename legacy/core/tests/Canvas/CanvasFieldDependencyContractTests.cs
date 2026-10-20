using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas 字段依赖契约的 Lint 行为测试。
/// 契约唯一事实来源：docs/Canvas_Fields_Spec.md §7。
/// 本测试直接调用 CanvasLayoutValidator.ValidateNode，验证数据行为（违规即报错），不复刻任何原版加载实现。
/// </summary>
public sealed class CanvasFieldDependencyContractTests
{
    private static bool HasFieldCombinationError(List<string> errors)
        => errors.Exists(e => e.Contains("字段组合非法", StringComparison.Ordinal));

    private static bool HasErrorMentioning(List<string> errors, string keyword)
        => errors.Exists(e => e.Contains(keyword, StringComparison.Ordinal));

    /// <summary>构造一个字段齐全、守约的 UIRect 节点。</summary>
    private static UIRectNode HealthyRect()
    {
        UIRectNode node = new();
        node.MarkFieldPresent("type");
        node.MarkFieldPresent("guid");
        node.MarkFieldPresent("name");
        node.MarkFieldPresent("Pos");
        node.MarkFieldPresent("size");
        node.MarkFieldPresent("color");
        node.MarkFieldPresent("BorderWidth");
        node.MarkFieldPresent("Radius");
        return node;
    }

    [Fact]
    public void HealthyNode_NoFieldCombinationError()
    {
        List<string> errors = CanvasLayoutValidator.ValidateNode(HealthyRect());
        Assert.False(HasFieldCombinationError(errors));
    }

    [Fact]
    public void SlicedPicGuid_Orphan_ViolatesContract()
    {
        // slicedPicGuid 孤悬：九宫格四件套全部缺失（用户报告"原版不认"的根因）
        UIRectNode node = HealthyRect();
        node.MarkFieldPresent("slicedPicGuid");

        List<string> errors = CanvasLayoutValidator.ValidateNode(node);
        Assert.True(HasFieldCombinationError(errors));
        // 每个缺失字段都应被精确点名，而非笼统提示
        Assert.True(HasErrorMentioning(errors, "必须同时存在 slicedborderlb"));
        Assert.True(HasErrorMentioning(errors, "必须同时存在 slicedborderrt"));
        Assert.True(HasErrorMentioning(errors, "必须同时存在 slicedrectlb"));
        Assert.True(HasErrorMentioning(errors, "必须同时存在 slicedrectrt"));
    }

    [Fact]
    public void SlicedQuad_CompleteFive_FulfillsContract()
    {
        UIRectNode node = HealthyRect();
        node.MarkFieldPresent("slicedPicGuid");
        node.MarkFieldPresent("slicedBorderLB");
        node.MarkFieldPresent("slicedBorderRT");
        node.MarkFieldPresent("slicedRectLB");
        node.MarkFieldPresent("slicedRectRT");

        List<string> errors = CanvasLayoutValidator.ValidateNode(node);
        Assert.False(HasFieldCombinationError(errors));
    }

    [Fact]
    public void FillMethod_WithoutFillAmount_ViolatesContract()
    {
        UIRectNode node = HealthyRect();
        node.MarkFieldPresent("fillMethod");

        List<string> errors = CanvasLayoutValidator.ValidateNode(node);
        Assert.True(HasErrorMentioning(errors, "fillamount"));
    }

    [Fact]
    public void ShadowColor_WithoutShadowDistance_ViolatesContract()
    {
        UIRectNode node = HealthyRect();
        node.MarkFieldPresent("shadowColor");

        List<string> errors = CanvasLayoutValidator.ValidateNode(node);
        Assert.True(HasErrorMentioning(errors, "shadowdistance"));
    }

    [Fact]
    public void ScriptInline_ReadPath_DetectsSlicedOrphan()
    {
        // 完整模拟 read_lint（LintEngine.ValidateCanvasFormats 内联分支）：
        // 脚本源码 → BescriptScanner.ScanCanvasArgs → ParseInlineStatic → ValidateTree
        string script = @"begincodeV2
name:test
lines:
fun create.canvas(Canvas ==hjson@1maskRect@1
[
 {
  type@1UIRect
  name@1maskRect
  slicedPicGuid@139b7510d1f004c04ada30754573161c1
 }
]==)
endcode";

        BescriptScanner scanner = new();
        IReadOnlyList<BescriptCanvasArgRange> args = scanner.ScanCanvasArgs(script);
        Assert.Single(args);
        Assert.IsNotType<BescriptGuidCanvasArgRange>(args[0]);

        CanvasNodeResult result = CanvasPreview.ParseInlineStatic(args[0].RawArg);
        Assert.True(result.Ok);
        UICanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        List<string> errors = CanvasLayoutValidator.ValidateTree(root);
        Assert.Contains(errors, e => e.Contains("slicedpicguid", StringComparison.Ordinal));
    }
}
