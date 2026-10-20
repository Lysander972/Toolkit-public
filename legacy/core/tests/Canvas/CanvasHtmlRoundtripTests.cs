using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas Node 到 HTML 的双向转换测试
///
/// 测试架构：
/// - CanvasNode → HTML（CanvasHtmlViewCodec.ToHtml）
/// - HTML → CanvasNode（CanvasHtmlViewCodec.FromHtml）
///
/// 原则：全程无损，往返后数据结构一致
///
/// 注意：
/// - HTML 标签名使用 Be 前缀的 DSL 标签（BeUIRect, BeUILabel, BeUIButton 等），与 TS 视图运行时类型对齐
/// - HTML 属性名必须是全小写
/// - HTML 视图使用枚举名称（如 UPPER_LEFT）而非数值
/// - 颜色格式为 rgba(r,g,b,a) 或 palette(name)
/// </summary>
public sealed class CanvasHtmlRoundtripTests
{
    /// <summary>
    /// 验证两个 CanvasNode 树的子节点结构是否等价
    /// 注意：Canvas 名称在虚拟根节点上，不在子节点上
    /// </summary>
    private static void AssertChildrenEqual(CanvasNode expected, CanvasNode actual)
    {
        Assert.Equal(expected.Children.Count, actual.Children.Count);

        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertNodesEqual(expected.Children[i], actual.Children[i]);
        }
    }

    /// <summary>
    /// 验证两个 CanvasNode 是否等价
    /// </summary>
    private static void AssertNodesEqual(CanvasNode expected, CanvasNode actual)
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.name.v, actual.name.v);
        Assert.Equal(expected.Children.Count, actual.Children.Count);

        if (expected.parentAnchor is LongVar expectedParentAnchor)
            Assert.Equal(expectedParentAnchor.v, Assert.IsType<LongVar>(actual.parentAnchor).v);
        if (expected.anchor is LongVar expectedAnchor)
            Assert.Equal(expectedAnchor.v, Assert.IsType<LongVar>(actual.anchor).v);
        if (expected.widthExtend is LongVar expectedWidthExtend)
            Assert.Equal(expectedWidthExtend.v, Assert.IsType<LongVar>(actual.widthExtend).v);
        if (expected.heightExtend is LongVar expectedHeightExtend)
            Assert.Equal(expectedHeightExtend.v, Assert.IsType<LongVar>(actual.heightExtend).v);

        if (expected.Pos != null)
        {
            Vector3Var actualPos = Assert.IsType<Vector3Var>(actual.Pos);
            Assert.Equal(expected.Pos.X, actualPos.X, 3);
            Assert.Equal(expected.Pos.Y, actualPos.Y, 3);
            Assert.Equal(expected.Pos.Z, actualPos.Z, 3);
        }

        if (expected.size != null)
        {
            Vector3Var actualSize = Assert.IsType<Vector3Var>(actual.size);
            Assert.Equal(expected.size.X, actualSize.X, 3);
            Assert.Equal(expected.size.Y, actualSize.Y, 3);
            Assert.Equal(expected.size.Z, actualSize.Z, 3);
        }

        // 递归比较子节点
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertNodesEqual(expected.Children[i], actual.Children[i]);
        }
    }

    [Fact]
    public void HtmlToNode_ShouldPreserveNameFieldOrder_EvenWhenEmpty()
    {
        const string html = """
<BeCanvas name="test" width="1080" height="2160">
  <BeUIRect guid="1" pos="0,0,0" name="" size="100,50,1" color="rgba(255,255,255,255)" borderwidth="0" radius="0"></BeUIRect>
</BeCanvas>
""";

        (CanvasNode root, _) = CanvasHtmlViewCodec.HtmlToNode(html);
        CanvasNode rect = Assert.Single(root.Children);

        string[] order = rect.GetPresentFieldOrder().ToArray();
        Assert.Equal(
            new[] { "type", "guid", "Pos", "name", "size", "color", "BorderWidth", "Radius" },
            order);

        string outputHtml = CanvasHtmlViewCodec.NodeToHtmlInline(root, "test");
        Assert.Contains(
            "<BeUIRect guid=\"1\" pos=\"0,0,0\" name=\"\" size=\"100,50,1\" color=\"rgba(255,255,255,255)\" borderwidth=\"0\" radius=\"0\"></BeUIRect>",
            outputHtml);
    }

    [Fact]
    public void HtmlToNode_SelfClosingRoot_ShouldParse_AndNormalizeOnOutput()
    {
        const string html = """
<BeCanvas name="empty" width="1080" height="2160" />
""";

        (CanvasNode root, string canvasName) = CanvasHtmlViewCodec.HtmlToNode(html);

        Assert.Equal("empty", canvasName);
        Assert.Empty(root.Children);

        string outputHtml = CanvasHtmlViewCodec.NodeToHtmlInline(root, canvasName);
        Assert.Contains(
            "<BeCanvas name=\"empty\" width=\"1080\" height=\"2160\"></BeCanvas>",
            outputHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/>", outputHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlToNode_SelfClosingChild_ShouldParse_AndNormalizeOnOutput()
    {
        const string html = """
<BeCanvas name="self_closing" width="1080" height="2160">
  <BeUIRect name="root" parentanchor="MIDDLE_CENTER" anchor="MIDDLE_CENTER" pos="0,0,0" size="100,50,1">
    <BeUILabel name="label" parentanchor="MIDDLE_CENTER" anchor="MIDDLE_CENTER" pos="0,10,0" size="100,30,1" text="OK" fontsize="18" />
  </BeUIRect>
</BeCanvas>
""";

        (CanvasNode root, string canvasName) = CanvasHtmlViewCodec.HtmlToNode(html);

        Assert.Equal("self_closing", canvasName);
        CanvasNode rect = Assert.Single(root.Children);
        CanvasNode label = Assert.Single(rect.Children);
        Assert.Equal("UILabel", label.Type);

        string outputHtml = CanvasHtmlViewCodec.NodeToHtmlInline(root, canvasName);
        Assert.Contains(
            "<BeUILabel name=\"label\" parentanchor=\"MIDDLE_CENTER\" anchor=\"MIDDLE_CENTER\" pos=\"0,10,0\" size=\"100,30,1\" text=\"OK\" fontsize=\"18\"></BeUILabel>",
            outputHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("/>", outputHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Roundtrip_SimpleHtml_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""test_canvas"" width=""1080"" height=""1920"">
      <BeUIRect name=""root"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" pos=""0,0,0"" size=""1080,1920,1"">
        <BeUILabel name=""label1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" pos=""0,100,0"" size=""200,50,1"" text=""Hello World"" fontsize=""24""></BeUILabel>
      </BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("test_canvas", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        // 验证子节点结构一致
        AssertChildrenEqual(result.Root, result2.Root);
    }

    [Fact]
    public void Roundtrip_ComplexHtml_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""complex_canvas"" width=""1080"" height=""1920"">
      <BeUIRect name=""root"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" pos=""0,0,0"" size=""800,600,1"" color=""rgba(51,77,102,255)"" radius=""10"" borderwidth=""2"">
        <BeUILabel name=""titleLabel"" parentanchor=""UPPER_CENTER"" anchor=""UPPER_CENTER"" pos=""0,-50,0"" size=""400,60,1"" text=""Title"" fontsize=""32"" alignment=""MIDDLE_CENTER""></BeUILabel>
        <BeUIButton name=""okButton"" parentanchor=""LOWER_CENTER"" anchor=""LOWER_CENTER"" pos=""0,100,0"" size=""200,80,1"" text=""OK"" fontsize=""20"" clickcbname=""onOkClick""></BeUIButton>
      </BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("complex_canvas", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证解析结果
        Assert.Single(result.Root.Children);
        CanvasNode root = result.Root.Children[0];
        Assert.Equal("UIRect", root.Type);
        Assert.Equal("root", root.name.v);
        Assert.Equal(2, root.Children.Count);

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        AssertChildrenEqual(result.Root, result2.Root);
    }

    [Fact]
    public void Roundtrip_AllNodeTypes_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""all_types"" width=""1080"" height=""1920"">
      <BeUIRect name=""rect1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,100,1"" color=""rgba(255,0,0,255)"" radius=""5"" fillmethod=""HORIZONTAL"" fillamount=""0.5""></BeUIRect>
      <BeUIButton name=""button1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""150,50,1"" text=""Click"" clickcbname=""onClick""></BeUIButton>
      <BeUILabel name=""label1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""200,30,1"" text=""Label Text"" fontsize=""18""></BeUILabel>
      <BeUILabelTextPro name=""textPro1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""200,30,1"" text=""TextPro Text"" underlayenabled=""1"" glowenabled=""1""></BeUILabelTextPro>
      <BeUIInput name=""input1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""300,40,1"" text="""" multiline=""0"" contenttype=""STANDARD""></BeUIInput>
      <BeUIRawImage name=""rawImage1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,100,1""></BeUIRawImage>
      <BeUIRectScrollRect name=""scroll1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""400,300,1"" scrollhorizontaldisabled=""0"" scrollverticaldisabled=""1""></BeUIRectScrollRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("all_types", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证所有节点类型
        Assert.Equal(7, result.Root.Children.Count);
        Assert.Equal("UIRect", result.Root.Children[0].Type);
        Assert.Equal("UIButton", result.Root.Children[1].Type);
        Assert.Equal("UILabel", result.Root.Children[2].Type);
        Assert.Equal("TextPro", result.Root.Children[3].Type);
        Assert.Equal("UIInput", result.Root.Children[4].Type);
        Assert.Equal("UIRawImage", result.Root.Children[5].Type);
        Assert.Equal("ScrollRect", result.Root.Children[6].Type);

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        AssertChildrenEqual(result.Root, result2.Root);
    }

    [Fact]
    public void Roundtrip_EnumFields_ShouldParseCorrectly()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""enum_test"" width=""1080"" height=""1920"">
      <BeUIRect name=""rect"" parentanchor=""UPPER_LEFT"" anchor=""LOWER_RIGHT"" widthextend=""PERCENT"" heightextend=""ASPECT"" size=""1,0.5,0""></BeUIRect>
      <BeUILabel name=""label"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" alignment=""UPPER_CENTER"" size=""100,50,1""></BeUILabel>
      <BeUIInput name=""input"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" contenttype=""DECIMAL_NUMBER"" size=""100,30,1"" text=""123""></BeUIInput>
      <BeUIRect name=""fillRect"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" fillmethod=""RADIAL_90"" fillamount=""0.75"" size=""100,100,1""></BeUIRect>
      <BeUIRect name=""fillRect360"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" fillmethod=""RADIAL_360"" fillamount=""0.5"" size=""100,100,1""></BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string _) = CanvasHtmlViewCodec.FromHtmlWithName(html);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        UICanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        // 验证枚举值解析正确
        CanvasNode rect = root.Children[0];
        Assert.Equal(0, Assert.IsType<LongVar>(rect.parentAnchor).v); // UPPER_LEFT
        Assert.Equal(8, Assert.IsType<LongVar>(rect.anchor).v); // LOWER_RIGHT
        Assert.Equal(1, Assert.IsType<LongVar>(rect.widthExtend).v); // PERCENT
        Assert.Equal(3, Assert.IsType<LongVar>(rect.heightExtend).v); // ASPECT

        UILabelNode label = Assert.IsType<UILabelNode>(root.Children[1]);
        Assert.Equal(1, Assert.IsType<LongVar>(label.Alignment).v); // UPPER_CENTER

        UIInputNode input = Assert.IsType<UIInputNode>(root.Children[2]);
        Assert.Equal(2, Assert.IsType<LongVar>(input.contentType).v); // DECIMAL_NUMBER

        UIRectNode fillRect = Assert.IsType<UIRectNode>(root.Children[3]);
        Assert.Equal(2, Assert.IsType<LongVar>(fillRect.fillMethod).v); // RADIAL_90
        Assert.Equal(0.75f, Assert.IsType<FloatVar>(fillRect.fillAmount).v);

        UIRectNode fillRect360 = Assert.IsType<UIRectNode>(root.Children[4]);
        Assert.Equal(4, Assert.IsType<LongVar>(fillRect360.fillMethod).v); // RADIAL_360
        Assert.Equal(0.5f, Assert.IsType<FloatVar>(fillRect360.fillAmount).v);

        string outputHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        Assert.Contains("fillmethod=\"RADIAL_90\"", outputHtml, StringComparison.Ordinal);
        Assert.Contains("fillmethod=\"RADIAL_360\"", outputHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_UIInput_MissingText_ShouldFail()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""input_missing_text"" width=""1080"" height=""1920"">
      <BeUIInput name=""input1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,30,1""></BeUIInput>
    </BeCanvas>
  </body>
</html>";

        // 新架构：HTML→Node 纯净转换，验证在 Node 层面完成
        (CanvasNode root, string _) = CanvasHtmlViewCodec.HtmlToNode(html);
        Assert.NotNull(root);

        CanvasNode uiInput = root.Children[0];
        List<string> errors = CanvasLayoutValidator.ValidateNode(uiInput);

        Assert.True(errors.Count > 0, "应该检测到验证错误");
        Assert.Contains(errors, static e => e.Contains("text", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_UIInput_LineSpacing_ShouldFail()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""input_invalid_field"" width=""1080"" height=""1920"">
      <BeUIInput name=""input1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,30,1"" text=""abc"" linespacing=""1""></BeUIInput>
    </BeCanvas>
  </body>
</html>";

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtmlWithName(html));
        Assert.Contains("HTML视图存在未知属性: BeUIInput.linespacing", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CanvasTypesNodes.ts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Roundtrip_ColorFormats_ShouldParseCorrectly()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""color_test"" width=""1080"" height=""1920"">
      <BeUIRect name=""rgbaRect"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,100,1"" color=""rgba(255,128,64,200)""></BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string _) = CanvasHtmlViewCodec.FromHtmlWithName(html);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证 rgba 颜色
        UIRectNode rgbaRect = Assert.IsType<UIRectNode>(result.Root.Children[0]);
        Assert.NotNull(rgbaRect.color);
        Assert.Equal(1f, rgbaRect.color.R); // 255/255
        Assert.Equal(128f / 255f, rgbaRect.color.G, 3);
        Assert.Equal(64f / 255f, rgbaRect.color.B, 3);
        Assert.Equal(200f / 255f, rgbaRect.color.A, 3);
    }

    [Fact]
    public void Roundtrip_ChineseQuotes_Should_Use_UnicodeEscapes_InHtml()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""“画布”"" width=""1080"" height=""1920"">
      <BeUILabel name=""label1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,50,1"" text=""“中文内容”""></BeUILabel>
    </BeCanvas>
  </body>
</html>";

        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("“画布”", name1);

        string outputHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        Assert.Contains("\\u201c画布\\u201d", outputHtml, StringComparison.Ordinal);
        Assert.Contains("\\u201c中文内容\\u201d", outputHtml, StringComparison.Ordinal);

        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(outputHtml);
        Assert.Equal("“画布”", name2);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes2);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);
        UILabelNode label = Assert.IsType<UILabelNode>(Assert.Single(root.Children));
        Assert.Equal("“中文内容”", Assert.IsType<StringVar>(label.text).v);
    }

    [Fact]
    public void Roundtrip_EmptyCanvas_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""empty_canvas"" width=""1080"" height=""1920"">
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("empty_canvas", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);
        Assert.Empty(result.Root.Children);

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);
        Assert.Empty(result2.Root.Children);
    }

    [Fact]
    public void Roundtrip_DeepNestedHtml_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""nested"" width=""1080"" height=""1920"">
      <BeUIRect name=""level0"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""800,600,1"">
        <BeUIRect name=""level1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""600,400,1"">
          <BeUIRect name=""level2"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""400,200,1"">
            <BeUILabel name=""deepLabel"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""200,50,1"" text=""Deep"" fontsize=""16""></BeUILabel>
          </BeUIRect>
        </BeUIRect>
      </BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("nested", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 验证嵌套深度
        CanvasNode current = result.Root.Children[0];
        int depth = 0;
        while (current.Children.Count > 0)
        {
            depth++;
            current = current.Children[0];
        }
        Assert.Equal(3, depth); // level0 -> level1 -> level2 -> deepLabel

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        AssertChildrenEqual(result.Root, result2.Root);
    }

    [Fact]
    public void Roundtrip_TextProEffects_ShouldParseCorrectly()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""textpro_effects"" width=""1080"" height=""1920"">
      <BeUILabelTextPro name=""effectsText"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""400,100,1"" text=""Glowing Text"" fontsize=""28"" facedilate=""0.1"" underlayenabled=""1"" glowenabled=""1"" glowoffset=""0.5"" vertexgradientenabled=""1"" bevelenabled=""1""></BeUILabelTextPro>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string _) = CanvasHtmlViewCodec.FromHtmlWithName(html);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        UITextProNode textPro = Assert.IsType<UITextProNode>(root.Children[0]);
        Assert.Equal(0.1f, Assert.IsType<FloatVar>(textPro.FaceDilate).v);
        Assert.True(Assert.IsType<BoolVar>(textPro.bUNDERLAY).v);
        Assert.True(Assert.IsType<BoolVar>(textPro.bGLOW).v);
        Assert.Equal(0.5f, Assert.IsType<FloatVar>(textPro.GlowOffset).v);
        Assert.True(Assert.IsType<BoolVar>(textPro.enableVertexGradient).v);
        Assert.True(Assert.IsType<BoolVar>(textPro.bBEVEL).v);
    }

    [Fact]
    public void Roundtrip_LayoutProperties_ShouldParseCorrectly()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""layout_test"" width=""1080"" height=""1920"">
      <BeUIRect name=""layoutRect"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""800,600,1"" cellsize=""100,50,0"" layoutspacing=""10,10,0"" verticalspacing=""5"" horizontalspacing=""10"" childexpandwidth=""1"" childexpandheight=""0"" childalignment=""MIDDLE_CENTER"" layoutpadding=""20,20,0"">
        <BeUIRect name=""child1"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,50,1""></BeUIRect>
        <BeUIRect name=""child2"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,50,1""></BeUIRect>
      </BeUIRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string _) = CanvasHtmlViewCodec.FromHtmlWithName(html);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);
        UIRectNode rect = Assert.IsType<UIRectNode>(root.Children[0]);
        Vector3Var cellSize = Assert.IsType<Vector3Var>(rect.cellSize);
        Assert.Equal(100f, cellSize.X);
        Assert.Equal(50f, cellSize.Y);
        Vector3Var spacing = Assert.IsType<Vector3Var>(rect.spacing);
        Assert.Equal(10f, spacing.X);
        Assert.Equal(10f, spacing.Y);
        Assert.Equal(5f, Assert.IsType<FloatVar>(rect.verticle_spacing).v);
        Assert.Equal(10f, Assert.IsType<FloatVar>(rect.horizontal_spacing).v);
        Assert.True(Assert.IsType<BoolVar>(rect.childExpendWidth).v);
        Assert.False(Assert.IsType<BoolVar>(rect.childExpendHeight).v);
        Assert.Equal(4, Assert.IsType<LongVar>(rect.childAlignment).v); // MIDDLE_CENTER
    }

    [Fact]
    public void Roundtrip_ScrollRect_ShouldParseCorrectly()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""scroll_test"" width=""1080"" height=""1920"">
      <BeUIRectScrollRect name=""scrollView"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""500,400,1"" scrollhorizontaldisabled=""0"" scrollverticaldisabled=""1"">
        <BeUIRect name=""content"" parentanchor=""UPPER_CENTER"" anchor=""UPPER_CENTER"" size=""1000,2000,1"">
          <BeUILabel name=""item1"" parentanchor=""UPPER_CENTER"" anchor=""UPPER_CENTER"" pos=""0,0,0"" size=""500,100,1"" text=""Item 1""></BeUILabel>
          <BeUILabel name=""item2"" parentanchor=""UPPER_CENTER"" anchor=""UPPER_CENTER"" pos=""0,110,0"" size=""500,100,1"" text=""Item 2""></BeUILabel>
        </BeUIRect>
      </BeUIRectScrollRect>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string _) = CanvasHtmlViewCodec.FromHtmlWithName(html);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        UIScrollRectNode scroll = Assert.IsType<UIScrollRectNode>(root.Children[0]);
        BoolVar scrollHorizontal = Assert.IsType<BoolVar>(scroll.scrollHorizontal);
        BoolVar scrollVertical = Assert.IsType<BoolVar>(scroll.scrollVertical);
        Assert.False(scrollHorizontal.v);
        Assert.True(scrollVertical.v);
        Assert.Single(scroll.Children);

        CanvasNode content = scroll.Children[0];
        Assert.Equal("UIRect", content.Type);
        Assert.Equal("content", content.name.v);
        Assert.Equal(2, content.Children.Count);
    }

    [Fact]
    public void FromHtml_InvalidTag_ShouldFail()
    {
        // 无效标签
        string html = @"<!DOCTYPE html>
<html>
  <body>
    <BeCanvas name=""test"" width=""1080"" height=""1920"">
      <InvalidTag name=""invalid""></InvalidTag>
    </BeCanvas>
  </body>
</html>";

        Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtml(html));
    }

    [Fact]
    public void FromHtml_LowercaseEngineTag_ShouldFail()
    {
        const string html = """
<BeCanvas name="case_sensitive" width="1080" height="2160">
  <uirect name="panel" parentanchor="MIDDLE_CENTER" anchor="MIDDLE_CENTER" pos="0,0,0" size="100,100,1"></uirect>
</BeCanvas>
""";

        Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtml(html));
    }

    [Fact]
    public void FromHtml_NestedCanvas_ShouldFail()
    {
        // 嵌套 Canvas
        string html = @"<!DOCTYPE html>
<html>
  <body>
    <BeCanvas name=""outer"" width=""1080"" height=""1920"">
      <BeCanvas name=""inner"" width=""1080"" height=""1920"">
      </BeCanvas>
    </BeCanvas>
  </body>
</html>";

        Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtml(html));
    }

    [Fact]
    public void FromHtml_MissingCanvas_ShouldFail()
    {
        // 缺少 Canvas
        string html = @"<!DOCTYPE html>
<html>
  <body>
    <BeUIRect name=""rect"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""100,100,1""></BeUIRect>
  </body>
</html>";

        Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtml(html));
    }

    [Fact]
    public void FromHtml_MissingRootWidth_ShouldReferenceNodeSchema()
    {
        const string html = """
<BeCanvas name="missing_width" height="2160"></BeCanvas>
""";

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CanvasHtmlViewCodec.FromHtmlWithName(html));
        Assert.Contains("HTML视图缺少必要属性: BeCanvas.width", ex.Message, StringComparison.Ordinal);
        Assert.Contains("CanvasTypesNodes.ts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Roundtrip_UnicodeText_ShouldBe_Lossless()
    {
        string html = @"<!DOCTYPE html>
<html lang=""zh-CN"">
  <body>
    <BeCanvas name=""unicode_test"" width=""1080"" height=""1920"">
      <BeUILabel name=""chinese"" parentanchor=""MIDDLE_CENTER"" anchor=""MIDDLE_CENTER"" size=""400,50,1"" text=""中文文本测试"" fontsize=""24""></BeUILabel>
    </BeCanvas>
  </body>
</html>";

        // HTML → 二进制 → CanvasNode
        (byte[] bytes1, string name1) = CanvasHtmlViewCodec.FromHtmlWithName(html);
        Assert.Equal("unicode_test", name1);

        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(bytes1);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);

        UILabelNode chineseLabel = Assert.IsType<UILabelNode>(root.Children[0]);
        Assert.Equal("中文文本测试", Assert.IsType<StringVar>(chineseLabel.text).v);

        // CanvasNode → HTML → 二进制 → CanvasNode
        string roundtripHtml = CanvasHtmlViewCodec.ToHtml(bytes1);
        (byte[] bytes2, string name2) = CanvasHtmlViewCodec.FromHtmlWithName(roundtripHtml);
        Assert.Equal(name1, name2);

        CanvasNodeResult result2 = preview.ParseBytes(bytes2);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        AssertChildrenEqual(result.Root, result2.Root);
    }
}
