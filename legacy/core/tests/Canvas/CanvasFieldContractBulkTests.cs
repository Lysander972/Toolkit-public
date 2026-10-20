using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// 全量真实 .canvas 字段依赖契约守约测试。
/// 遍历 test/data/projects 下所有 .canvas（经 SHA1 去重），断言它们都满足字段依赖契约。
/// 契约唯一事实来源：docs/Canvas_Fields_Spec.md §7。
/// 价值：反向证明契约正确（真实原版导出数据全部守约，无假阳性），并守住院线——
/// 后续任何让真实数据违规的改动都会被本测试拦截。
/// </summary>
public sealed class CanvasFieldContractBulkTests
{
    public static IEnumerable<object[]> AllCanvasBytes => CanvasFourWayE2ETests.GetTestCanvasData();

    [Theory]
    [MemberData(nameof(AllCanvasBytes))]
    public void RealCanvas_FulfillsFieldDependencyContract(byte[] canvasBytes)
    {
        CanvasNodeResult result = CanvasConverter.FromBinary(canvasBytes);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        List<string> errors = CanvasLayoutValidator.ValidateTree(result.Root);

        // 只聚焦字段依赖契约（字段组合非法），不被数值/富文本等无关问题干扰
        Assert.DoesNotContain(errors, e => e.Contains("字段组合非法", StringComparison.Ordinal));
    }
}
