using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas 四向转换统一测试
///
/// 架构验证：
/// - 以 CanvasNode 为中心
/// - 四个方向均可无损互转
/// - 独立文件和内联版本的 Node 生成完全相同
/// </summary>
public sealed class CanvasConverterTests
{
    /// <summary>
    /// 构建测试用 CanvasNode
    /// </summary>
    private static CanvasNode BuildTestNode()
    {
        UILabelNode label = new UILabelNode
        {
            name = new StringVar { v = "testLabel" },
            parentAnchor = 4,
            anchor = 4,
            size = new Vector3Var { X = 200, Y = 50, Z = 1 }
        };
        label.text = "Test";
        label.FontSize = 24f;

        // 必须标记存在的字段，否则字段不会被序列化
        label.MarkFieldPresent("name");
        label.MarkFieldPresent("parentAnchor");
        label.MarkFieldPresent("anchor");
        label.MarkFieldPresent("size");
        label.MarkFieldPresent("text");
        label.MarkFieldPresent("FontSize");

        UICanvasNode root = new UICanvasNode
        {
            name = new StringVar { v = "test_canvas" }
        };
        root.Children.Add(label);

        return root;
    }

    [Fact]
    public void FourWay_BinaryRoundtrip_ShouldBeLossless()
    {
        // Node → 独立文件二进制
        CanvasNode originalNode = BuildTestNode();
        byte[] binary = CanvasConverter.ToBinary(originalNode, "test_canvas");

        // 独立文件二进制 → Node
        CanvasNodeResult result = CanvasConverter.FromBinary(binary);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);
        Assert.Equal("test_canvas", result.CanvasName);
        Assert.Single(root.Children);

        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(root.Children[0]);
        Assert.Equal("testLabel", parsedLabel.name.v);
        Assert.Equal("Test", Assert.IsType<StringVar>(parsedLabel.text).v);
        Assert.Equal(24f, Assert.IsType<FloatVar>(parsedLabel.FontSize).v);
    }

    [Fact]
    public void FourWay_InlineBase64Roundtrip_ShouldBeLossless()
    {
        // Node → 内联 Base64
        CanvasNode originalNode = BuildTestNode();
        string inlineBase64 = CanvasConverter.ToInlineBase64(originalNode, "test_canvas");

        // 验证格式
        Assert.StartsWith("canvas:test_canvas=", inlineBase64);

        // 内联 Base64 → Node
        CanvasNodeResult result = CanvasConverter.FromInline(inlineBase64);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);
        Assert.Equal("test_canvas", result.CanvasName);
        Assert.Single(root.Children);

        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(root.Children[0]);
        Assert.Equal("testLabel", parsedLabel.name.v);
        Assert.Equal("Test", Assert.IsType<StringVar>(parsedLabel.text).v);
    }

    [Fact]
    public void FourWay_NodeHjsonInlineRoundtrip_ShouldBeLossless()
    {
        CanvasNode originalNode = BuildTestNode();
        string inlineNode = CanvasConverter.ToInlineNodeHjson(originalNode, "TestCanvas");

        Assert.StartsWith("==hjson:TestCanvas:", inlineNode);

        CanvasNodeResult result = CanvasConverter.FromInline(inlineNode);
        Assert.True(result.Ok);
        CanvasNode root = Assert.IsType<UICanvasNode>(result.Root);
        Assert.Single(root.Children);

        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(root.Children[0]);
        Assert.Equal("testLabel", parsedLabel.name.v);
        Assert.Equal("Test", Assert.IsType<StringVar>(parsedLabel.text).v);
    }

    [Fact]
    public void FourWay_HtmlRoundtrip_ShouldBeLossless()
    {
        // Node → HTML 独立版
        CanvasNode originalNode = BuildTestNode();
        string html = CanvasConverter.ToHtml(originalNode, "test_canvas");

        // 验证 HTML 格式
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<BeCanvas", html);
        Assert.Contains("name=\"test_canvas\"", html);
        Assert.Contains("<BeUILabel", html);

        // HTML 独立版 → Node
        (CanvasNode root, string canvasName) = CanvasConverter.FromHtml(html);
        Assert.Equal("test_canvas", canvasName);
        CanvasNode parsedRoot = Assert.IsType<UICanvasNode>(root);
        Assert.Single(parsedRoot.Children);

        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(parsedRoot.Children[0]);
        Assert.Equal("testLabel", parsedLabel.name.v);
        Assert.Equal("Test", Assert.IsType<StringVar>(parsedLabel.text).v);
    }

    [Fact]
    public void FourWay_HtmlInlineRoundtrip_ShouldBeLossless()
    {
        // Node → HTML 内联版
        CanvasNode originalNode = BuildTestNode();
        string htmlInline = CanvasConverter.ToHtmlInline(originalNode, "test_canvas");

        // 验证内联 HTML 格式（无文档外壳）
        Assert.DoesNotContain("<!DOCTYPE html>", htmlInline);
        Assert.DoesNotContain("<html", htmlInline);
        Assert.Contains("<BeCanvas", htmlInline);
        Assert.Contains("name=\"test_canvas\"", htmlInline);
        Assert.Contains("<BeUILabel", htmlInline);

        // HTML 内联版 → Node
        (CanvasNode root, string canvasName) = CanvasConverter.FromHtmlInline(htmlInline);
        Assert.Equal("test_canvas", canvasName);
        CanvasNode parsedRoot = Assert.IsType<UICanvasNode>(root);
        Assert.Single(parsedRoot.Children);

        UILabelNode parsedLabel = Assert.IsType<UILabelNode>(parsedRoot.Children[0]);
        Assert.Equal("testLabel", parsedLabel.name.v);
        Assert.Equal("Test", Assert.IsType<StringVar>(parsedLabel.text).v);
    }

    [Fact]
    public void InlineNodeHjson_WithoutChildName_ShouldFail()
    {
        const string inlineNode =
            "==hjson:TestCanvas:[\n" +
            "  {\n" +
            "    type: UILabel\n" +
            "    text: Hello\n" +
            "    Pos: [0, 0, 0]\n" +
            "    size: [120, 40, 1]\n" +
            "  }\n" +
            "]==";

        CanvasNodeResult result = CanvasConverter.FromInline(inlineNode);
        Assert.False(result.Ok);
        Assert.Contains("缺少字符串 name 字段", result.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FourWay_BinaryAndInline_ProduceSameNode()
    {
        // 核心原则：同样名称同样内容的独立文件和内联版本，生成的 Node 完全相同

        CanvasNode originalNode = BuildTestNode();

        // Node → 独立文件二进制 → Node
        byte[] binary = CanvasConverter.ToBinary(originalNode, "test_canvas");
        CanvasNodeResult fromBinary = CanvasConverter.FromBinary(binary);

        // Node → 内联 Base64 → Node
        string inline = CanvasConverter.ToInlineBase64(originalNode, "test_canvas");
        CanvasNodeResult fromInline = CanvasConverter.FromInline(inline);

        // 两者 Node 应该相同
        Assert.True(fromBinary.Ok);
        Assert.True(fromInline.Ok);

        CanvasNode binaryRoot = Assert.IsType<UICanvasNode>(fromBinary.Root);
        CanvasNode inlineRoot = Assert.IsType<UICanvasNode>(fromInline.Root);
        Assert.Equal(binaryRoot.Children.Count, inlineRoot.Children.Count);
        Assert.Equal(fromBinary.CanvasName, fromInline.CanvasName);

        UILabelNode label1 = Assert.IsType<UILabelNode>(binaryRoot.Children[0]);
        UILabelNode label2 = Assert.IsType<UILabelNode>(inlineRoot.Children[0]);

        Assert.Equal(label1.name.v, label2.name.v);
        Assert.Equal(Assert.IsType<StringVar>(label1.text).v, Assert.IsType<StringVar>(label2.text).v);
        Assert.Equal(Assert.IsType<FloatVar>(label1.FontSize).v, Assert.IsType<FloatVar>(label2.FontSize).v);
    }

    [Fact]
    public void FourWay_CrossFormat_BinaryToHtmlToBinary()
    {
        // 独立文件二进制 → HTML → 独立文件二进制
        CanvasNode originalNode = BuildTestNode();
        byte[] binary1 = CanvasConverter.ToBinary(originalNode, "test_canvas");

        string html = CanvasConverter.BinaryToHtml(binary1);
        byte[] binary2 = CanvasConverter.HtmlToBinary(html);

        // 解析并比较 Node
        CanvasNodeResult result1 = CanvasConverter.FromBinary(binary1);
        CanvasNodeResult result2 = CanvasConverter.FromBinary(binary2);

        Assert.True(result1.Ok);
        Assert.True(result2.Ok);

        CanvasNode root1 = Assert.IsType<UICanvasNode>(result1.Root);
        CanvasNode root2 = Assert.IsType<UICanvasNode>(result2.Root);
        Assert.Equal(root1.Children.Count, root2.Children.Count);
    }

    [Fact]
    public void FourWay_CrossFormat_InlineBase64ToHtmlInlineToInlineBase64()
    {
        // 内联 Base64 → HTML 内联版 → 内联 Base64
        CanvasNode originalNode = BuildTestNode();
        string inline1 = CanvasConverter.ToInlineBase64(originalNode, "test_canvas");

        string htmlInline = CanvasConverter.InlineBase64ToHtmlInline(inline1);
        string inline2 = CanvasConverter.HtmlInlineToInlineBase64(htmlInline);

        // 解析并比较 Node
        CanvasNodeResult result1 = CanvasConverter.FromInline(inline1);
        CanvasNodeResult result2 = CanvasConverter.FromInline(inline2);

        Assert.True(result1.Ok);
        Assert.True(result2.Ok);

        Assert.Equal(result1.CanvasName, result2.CanvasName);
        CanvasNode root1 = Assert.IsType<UICanvasNode>(result1.Root);
        CanvasNode root2 = Assert.IsType<UICanvasNode>(result2.Root);
        Assert.Equal(root1.Children.Count, root2.Children.Count);
    }

    [Fact]
    public void FourWay_AllFormatsFromSameNode_ProduceConsistentNode()
    {
        // 从同一个 Node 出发，经过任意格式的往返，最终 Node 应该一致
        CanvasNode originalNode = BuildTestNode();

        // 路径1：Node → Binary → Node → HTML → Node
        byte[] binary = CanvasConverter.ToBinary(originalNode, "test_canvas");
        CanvasNodeResult fromBinary = CanvasConverter.FromBinary(binary);
        Assert.True(fromBinary.Ok);

        CanvasNode binaryRoot = Assert.IsType<UICanvasNode>(fromBinary.Root);
        string binaryCanvasName = fromBinary.CanvasName;
        string html = CanvasConverter.ToHtml(binaryRoot, binaryCanvasName);
        (CanvasNode fromHtml, string _) = CanvasConverter.FromHtml(html);

        // 路径2：Node → Inline Base64 → Node → HTML Inline → Node
        string inline = CanvasConverter.ToInlineBase64(originalNode, "test_canvas");
        CanvasNodeResult fromInline = CanvasConverter.FromInline(inline);
        Assert.True(fromInline.Ok);

        CanvasNode inlineRoot = Assert.IsType<UICanvasNode>(fromInline.Root);
        string inlineCanvasName = fromInline.CanvasName;
        string htmlInline = CanvasConverter.ToHtmlInline(inlineRoot, inlineCanvasName);
        (CanvasNode fromHtmlInline, string _) = CanvasConverter.FromHtmlInline(htmlInline);

        // 两条路径的终点 Node 应该相同
        Assert.Equal(fromHtml.Children.Count, fromHtmlInline.Children.Count);

        UILabelNode label1 = Assert.IsType<UILabelNode>(fromHtml.Children[0]);
        UILabelNode label2 = Assert.IsType<UILabelNode>(fromHtmlInline.Children[0]);

        Assert.Equal(label1.name.v, label2.name.v);
        Assert.Equal(Assert.IsType<StringVar>(label1.text).v, Assert.IsType<StringVar>(label2.text).v);
    }

    [Fact]
    public void ValidateNode_Should_Fail_When_LayoutPadding_Missing()
    {
        const string html =
            "<!DOCTYPE html>\n" +
            "<html lang=\"zh-CN\">\n" +
            "  <head><meta charset=\"UTF-8\" /></head>\n" +
            "  <body>\n" +
            "    <BeCanvas name=\"bad\" width=\"1080\" height=\"2160\">\n" +
            "      <BeUIRect name=\"content\" pos=\"0,0,0\" parentanchor=\"MIDDLE_CENTER\" anchor=\"MIDDLE_CENTER\" size=\"550,350,1\" color=\"rgba(255,255,255,255)\" borderwidth=\"2\" radius=\"5\" verticalspacing=\"15\" childexpandwidth=\"1\"></BeUIRect>\n" +
            "    </BeCanvas>\n" +
            "  </body>\n" +
            "</html>\n";

        // 新架构：HTML→Node 纯净转换，验证在 Node 层面完成
        (CanvasNode root, _) = CanvasConverter.FromHtml(html);
        Assert.NotNull(root);

        CanvasNode urRect = root.Children[0];
        List<string> errors = CanvasLayoutValidator.ValidateNode(urRect);

        Assert.True(errors.Count > 0, "应该检测到验证错误");
        Assert.Contains(errors, static e => e.Contains("padding", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateNode_Should_Fail_When_UIRect_BorderStyle_Missing()
    {
        const string html =
            "<!DOCTYPE html>\n" +
            "<html lang=\"zh-CN\">\n" +
            "  <head><meta charset=\"UTF-8\" /></head>\n" +
            "  <body>\n" +
            "    <BeCanvas name=\"bad_rect\" width=\"1080\" height=\"2160\">\n" +
            "      <BeUIRect name=\"content\" pos=\"0,0,0\" parentanchor=\"MIDDLE_CENTER\" anchor=\"MIDDLE_CENTER\" size=\"550,350,1\" color=\"rgba(255,255,255,255)\"></BeUIRect>\n" +
            "    </BeCanvas>\n" +
            "  </body>\n" +
            "</html>\n";

        // 新架构：HTML→Node 纯净转换，验证在 Node 层面完成
        (CanvasNode root, _) = CanvasConverter.FromHtml(html);
        Assert.NotNull(root);

        CanvasNode uiRect = root.Children[0];
        List<string> errors = CanvasLayoutValidator.ValidateNode(uiRect);

        Assert.True(errors.Count > 0, "应该检测到验证错误");
        Assert.Contains(errors, static e => e.Contains("borderwidth", StringComparison.Ordinal));
    }
}
