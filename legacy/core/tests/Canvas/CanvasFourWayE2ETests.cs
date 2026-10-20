using Xunit;
using System.Text;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Canvas;

/// <summary>
/// Canvas 四向转换端到端测试
///
/// 测试策略：
/// 1. 使用真实 canvas 文件作为输入
/// 2. 每个方向走完整圈，验证字节级无损
/// 3. 任意两个方向的 Node 必须全等
///
/// 四向转换验证：
/// - 独立文件二进制 ↔ CanvasNode ↔ HTML 独立版
/// - 内联 Base64 ↔ CanvasNode ↔ HTML 内联版
///
/// 全等原则：
/// - 同样内容经任意路径转换后的 Node 必须完全相同
/// - 转一圈回到原格式必须是字节级相等
/// </summary>
public sealed class CanvasFourWayE2ETests
{
    /// <summary>
    /// 测试用的 canvas 文件相对路径列表（相对于 ToolkitRepoPaths.ProjectsRoot）
    /// 自动发现所有 canvas 文件
    /// </summary>
    public static readonly IReadOnlyList<string> TestCanvasRelativePaths = DiscoverAllCanvasFiles();

    private static List<string> DiscoverAllCanvasFiles()
    {
        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenHashes = [];
        List<string> list = [];
        string projectsRoot = ToolkitRepoPaths.ProjectsRoot;
        if (SystemIOWrapper.DirectoryExists(projectsRoot))
        {
            foreach (string file in SystemIOWrapper.EnumerateFiles(projectsRoot, "*.canvas", SearchOption.AllDirectories))
            {
                string relativePath = SystemIOWrapper.GetRelativePath(projectsRoot, file);
                if (!seenPaths.Add(relativePath)) continue;
                byte[] bytes = SystemIOWrapper.ReadAllBytes(file);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes));
                if (seenHashes.Add(hash))
                    list.Add(relativePath);
            }
        }
        return list;
    }

    /// <summary>
    /// 验证两个 CanvasNode 树完全相等
    /// </summary>
    private static void AssertNodesFullyEqual(CanvasNode expected, CanvasNode actual, string path = "root")
    {
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.name.v, actual.name.v);
        Assert.Equal(expected.Children.Count, actual.Children.Count);

        // 比较 PresentFields（通过内部 API）
        IReadOnlySet<string> expectedFields = expected.GetPresentFields();
        IReadOnlySet<string> actualFields = actual.GetPresentFields();
        if (expectedFields.Count != actualFields.Count)
        {
            List<string> extra = actualFields.Except(expectedFields).ToList();
            List<string> missing = expectedFields.Except(actualFields).ToList();
            if (extra.Any()) Console.WriteLine($"[{path}] Extra fields in actual: {string.Join(", ", extra)}");
            if (missing.Any()) Console.WriteLine($"[{path}] Missing fields in actual: {string.Join(", ", missing)}");
        }
        Assert.Equal(expectedFields.Count, actualFields.Count);
        foreach (string field in expectedFields)
        {
            Assert.True(actualFields.Contains(field), $"PresentFields mismatch at {path}: missing {field}");
        }

        // 比较 PresentFieldOrder（通过内部 API）
        IReadOnlyList<string> expectedOrder = expected.GetPresentFieldOrder();
        IReadOnlyList<string> actualOrder = actual.GetPresentFieldOrder();
        Assert.Equal(expectedOrder.Count, actualOrder.Count);
        for (int i = 0; i < expectedOrder.Count; i++)
        {
            Assert.Equal(expectedOrder[i], actualOrder[i]);
        }

        // 比较 Transform
        if (expectedFields.Contains("parentAnchor")) Assert.Equal(Assert.IsType<LongVar>(expected.parentAnchor).v, Assert.IsType<LongVar>(actual.parentAnchor).v);
        if (expectedFields.Contains("anchor")) Assert.Equal(Assert.IsType<LongVar>(expected.anchor).v, Assert.IsType<LongVar>(actual.anchor).v);
        if (expectedFields.Contains("widthExtend")) Assert.Equal(Assert.IsType<LongVar>(expected.widthExtend).v, Assert.IsType<LongVar>(actual.widthExtend).v);
        if (expectedFields.Contains("heightExtend")) Assert.Equal(Assert.IsType<LongVar>(expected.heightExtend).v, Assert.IsType<LongVar>(actual.heightExtend).v);
        if (expectedFields.Contains("rot"))
        {
            Assert.Equal(Assert.IsType<FloatVar>(expected.rot).v, Assert.IsType<FloatVar>(actual.rot).v);
        }

        // 比较 Pos
        if (expected.Pos != null)
        {
            Assert.NotNull(actual.Pos);
            Assert.Equal(expected.Pos.X, actual.Pos.X, 3);
            Assert.Equal(expected.Pos.Y, actual.Pos.Y, 3);
            Assert.Equal(expected.Pos.Z, actual.Pos.Z, 3);
        }
        else
        {
            Assert.Null(actual.Pos);
        }

        // 比较 Size
        if (expected.size != null)
        {
            Assert.NotNull(actual.size);
            Assert.Equal(expected.size.X, actual.size.X, 3);
            Assert.Equal(expected.size.Y, actual.size.Y, 3);
            Assert.Equal(expected.size.Z, actual.size.Z, 3);
        }
        else
        {
            Assert.Null(actual.size);
        }

        // 递归比较子节点
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertNodesFullyEqual(expected.Children[i], actual.Children[i], $"{path}/Children[{i}]");
        }
    }

    #region 独立文件二进制 - 完整圈测试

    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void Binary_HtmlRoundtrip_ShouldBeLossless(byte[] originalBytes)
    {
        // 独立文件二进制 → Node → HTML → Node → 独立文件二进制（便捷方法内部经过 Node）
        string html = CanvasConverter.BinaryToHtml(originalBytes);
        byte[] roundtripBytes = CanvasConverter.HtmlToBinary(html);

        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void Binary_NodeRoundtrip_ShouldBeLossless(byte[] originalBytes)
    {
        // 独立文件二进制 → Node → 独立文件二进制
        CanvasNodeResult result = CanvasConverter.FromBinary(originalBytes);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        byte[] roundtripBytes = CanvasConverter.ToBinary(result.Root, result.CanvasName);
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void Binary_HtmlNodePath_ShouldProduceEqualNode(byte[] originalBytes)
    {
        // 路径1：独立文件二进制 → Node
        CanvasNodeResult result1 = CanvasConverter.FromBinary(originalBytes);
        Assert.True(result1.Ok);

        // 路径2：独立文件二进制 → Node → HTML → Node（便捷方法 BinaryToHtml 内部经过 Node）
        string html = CanvasConverter.BinaryToHtml(originalBytes);
        (CanvasNode node2, string name2) = CanvasConverter.FromHtml(html);

        // 两个 Node 必须全等
        Assert.Equal(result1.CanvasName, name2);
        Assert.NotNull(result1.Root);
        AssertNodesFullyEqual(result1.Root, node2);
    }

    #endregion

    #region 内联 Base64 - 完整圈测试

    [Theory]
    [MemberData(nameof(GetTestCanvasAsInlineData))]
    public void InlineBase64_HtmlInlineRoundtrip_ShouldBeLossless(string originalInline)
    {
        // 内联 Base64 → HTML 内联版 → 内联 Base64
        string htmlInline = CanvasConverter.InlineBase64ToHtmlInline(originalInline);
        string roundtripInline = CanvasConverter.HtmlInlineToInlineBase64(htmlInline);

        Assert.Equal(originalInline, roundtripInline);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasAsInlineData))]
    public void InlineBase64_NodeRoundtrip_ShouldBeLossless(string originalInline)
    {
        // 内联 Base64 → Node → 内联 Base64
        CanvasNodeResult result = CanvasConverter.FromInline(originalInline);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        string roundtripInline = CanvasConverter.ToInlineBase64(result.Root, result.CanvasName);
        Assert.Equal(originalInline, roundtripInline);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasAsInlineData))]
    public void InlineBase64_HtmlInlineNodePath_ShouldProduceEqualNode(string originalInline)
    {
        // 路径1：内联 Base64 → Node
        CanvasNodeResult result1 = CanvasConverter.FromInline(originalInline);
        Assert.True(result1.Ok);

        // 路径2：内联 Base64 → HTML 内联版 → Node
        string htmlInline = CanvasConverter.InlineBase64ToHtmlInline(originalInline);
        (CanvasNode node2, string name2) = CanvasConverter.FromHtmlInline(htmlInline);

        // 两个 Node 必须全等
        Assert.Equal(result1.CanvasName, name2);
        Assert.NotNull(result1.Root);
        AssertNodesFullyEqual(result1.Root, node2);
    }

    #endregion

    #region 跨格式 - 独立文件与内联一致性

    [Theory]
    [MemberData(nameof(GetTestCanvasDataWithInline))]
    public void BinaryAndInline_ShouldProduceEqualNodes(byte[] binaryData, string inlineData)
    {
        // 核心原则：同样内容，独立文件和内联版本生成的 Node 必须完全相同

        // 独立文件二进制 → Node
        CanvasNodeResult result1 = CanvasConverter.FromBinary(binaryData);
        Assert.True(result1.Ok);

        // 内联 Base64 → Node
        CanvasNodeResult result2 = CanvasConverter.FromInline(inlineData);
        Assert.True(result2.Ok);

        // 名称必须相同
        Assert.Equal(result1.CanvasName, result2.CanvasName);

        // Node 必须全等
        Assert.NotNull(result1.Root);
        Assert.NotNull(result2.Root);
        AssertNodesFullyEqual(result1.Root, result2.Root);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasDataWithInline))]
    public void CrossFormat_HtmlPaths_ShouldProduceEqualNodes(byte[] binaryData, string inlineData)
    {
        // 独立文件 → Node → HTML 独立版 → Node（便捷方法内部经过 Node）
        string htmlFull = CanvasConverter.BinaryToHtml(binaryData);
        (CanvasNode node1, string name1) = CanvasConverter.FromHtml(htmlFull);

        // 内联 → Node → HTML 内联版 → Node（便捷方法内部经过 Node）
        string htmlInline = CanvasConverter.InlineBase64ToHtmlInline(inlineData);
        (CanvasNode node2, string name2) = CanvasConverter.FromHtmlInline(htmlInline);

        // 名称必须相同
        Assert.Equal(name1, name2);

        // Node 必须全等
        AssertNodesFullyEqual(node1, node2);
    }

    #endregion

    #region 内联 Node Hjson - 完整圈测试

    [Theory]
    [MemberData(nameof(GetTestCanvasAsNodeHjsonData))]
    public void NodeHjson_TextRoundtrip_ShouldBeCharacterLevelIdentical(string originalHjson)
    {
        // Hjson 文本 → Node → Hjson 文本，字符级一致
        CanvasNodeResult result = CanvasInlineNodeHjsonCodec.Parse("TestCanvas", originalHjson);
        Assert.True(result.Ok);

        Assert.NotNull(result.Root);
        string roundtripHjson = CanvasInlineNodeHjsonCodec.Serialize(result.Root);
        Assert.Equal(originalHjson, roundtripHjson);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasAsNodeHjsonData))]
    public void NodeHjson_NodeRoundtrip_ShouldProduceEqualNode(string originalHjson)
    {
        // Hjson → Node → Hjson → Node，两次 Node 全等
        CanvasNodeResult result1 = CanvasInlineNodeHjsonCodec.Parse("TestCanvas", originalHjson);
        Assert.True(result1.Ok);

        Assert.NotNull(result1.Root);
        string roundtripHjson = CanvasInlineNodeHjsonCodec.Serialize(result1.Root);
        CanvasNodeResult result2 = CanvasInlineNodeHjsonCodec.Parse("TestCanvas", roundtripHjson);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);
        AssertNodesFullyEqual(result1.Root, result2.Root);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasDataWithNodeHjson))]
    public void BinaryAndNodeHjson_ShouldProduceEqualChildNodes(byte[] binaryData, string hjsonData)
    {
        // Hjson 只表达子节点列表，根节点为合成默认值。
        // 因此只比较子节点，不比较根节点。
        CanvasNodeResult result1 = CanvasConverter.FromBinary(binaryData);
        Assert.True(result1.Ok);

        CanvasNodeResult result2 = CanvasInlineNodeHjsonCodec.Parse("TestCanvas", hjsonData);
        Assert.True(result2.Ok);

        Assert.NotNull(result1.Root);
        Assert.NotNull(result2.Root);
        Assert.Equal(result1.Root.Children.Count, result2.Root.Children.Count);
        for (int i = 0; i < result1.Root.Children.Count; i++)
        {
            AssertNodesFullyEqual(result1.Root.Children[i], result2.Root.Children[i], $"Children[{i}]");
        }
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void FullCircle_BinaryToNodeHjsonToBinary_ChildNodes_ShouldBeLossless(byte[] originalBytes)
    {
        // 二进制 → Node → Hjson → Node → 放回原始根 → 二进制，验证子节点字节级一致
        CanvasNodeResult result1 = CanvasConverter.FromBinary(originalBytes);
        Assert.True(result1.Ok);

        Assert.NotNull(result1.Root);
        string hjson = CanvasInlineNodeHjsonCodec.Serialize(result1.Root);
        CanvasNodeResult result2 = CanvasInlineNodeHjsonCodec.Parse("TestCanvas", hjson);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        result1.Root.Children.Clear();
        foreach (CanvasNode child in result2.Root.Children)
        {
            result1.Root.Children.Add(child);
        }

        byte[] roundtripBytes = CanvasConverter.ToBinary(result1.Root, result1.CanvasName);
        if (!originalBytes.SequenceEqual(roundtripBytes))
        {
            StringBuilder msg = new System.Text.StringBuilder();
            for (int i = 0; i < Math.Min(originalBytes.Length, roundtripBytes.Length); i++)
            {
                if (originalBytes[i] != roundtripBytes[i])
                {
                    msg.AppendLine($"[Diff] pos={i}, expected=0x{originalBytes[i]:X2}, actual=0x{roundtripBytes[i]:X2}");
                    // 显示差异位置前后上下文（对齐到4字节边界）
                    int alignedPos = (i / 4) * 4;
                    int contextStart = Math.Max(0, alignedPos - 8);
                    msg.AppendLine($"[Diff] context expected: {string.Join(" ", originalBytes.Skip(contextStart).Take(24).Select(static b => $"{b:X2}"))}");
                    msg.AppendLine($"[Diff] context actual:   {string.Join(" ", roundtripBytes.Skip(contextStart).Take(24).Select(static b => $"{b:X2}"))}");
                    break;
                }
            }
            msg.AppendLine($"[Hjson]\n{hjson}");
            Assert.Fail(msg.ToString());
        }
    }

    #endregion

    #region 完整圈 - 任意路径最终回到原点

    [Theory]
    [MemberData(nameof(GetTestCanvasData))]
    public void FullCircle_BinaryToHtmlToInlineToHtmlToBinary_ShouldBeLossless(byte[] originalBytes)
    {
        // 完整圈：独立文件二进制 → HTML 独立版 → Node → HTML 内联版 → 内联 Base64 → Node → 独立文件二进制
        CanvasNodeResult result1 = CanvasConverter.FromBinary(originalBytes);
        Assert.True(result1.Ok);

        // Node → HTML 内联版 → 内联 Base64 → Node
        Assert.NotNull(result1.Root);
        string htmlInline = CanvasConverter.ToHtmlInline(result1.Root, result1.CanvasName);
        string inlineBase64 = CanvasConverter.HtmlInlineToInlineBase64(htmlInline);
        CanvasNodeResult result2 = CanvasConverter.FromInline(inlineBase64);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        // Node → 独立文件二进制
        byte[] roundtripBytes = CanvasConverter.ToBinary(result2.Root, result2.CanvasName);

        // 必须字节级相等
        Assert.Equal(originalBytes, roundtripBytes);
    }

    [Theory]
    [MemberData(nameof(GetTestCanvasAsInlineData))]
    public void FullCircle_InlineToHtmlToBinaryToHtmlToInline_ShouldBeLossless(string originalInline)
    {
        // 完整圈：内联 Base64 → HTML 内联版 → Node → HTML 独立版 → 独立文件二进制 → Node → 内联 Base64
        CanvasNodeResult result1 = CanvasConverter.FromInline(originalInline);
        Assert.True(result1.Ok);

        // Node → HTML 独立版 → 独立文件二进制 → Node
        Assert.NotNull(result1.Root);
        string htmlFull = CanvasConverter.ToHtml(result1.Root, result1.CanvasName);
        byte[] binary = CanvasConverter.HtmlToBinary(htmlFull);
        CanvasNodeResult result2 = CanvasConverter.FromBinary(binary);
        Assert.True(result2.Ok);
        Assert.NotNull(result2.Root);

        // Node → 内联 Base64
        string roundtripInline = CanvasConverter.ToInlineBase64(result2.Root, result2.CanvasName);

        // 必须字符串级相等
        Assert.Equal(originalInline, roundtripInline);
    }

    #endregion

    #region 测试数据生成

    private static string GetFullPath(string relativePath)
    {
        return SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, relativePath);
    }

    public static IEnumerable<object[]> GetTestCanvasData()
    {
        foreach (string canvasRelativePath in TestCanvasRelativePaths)
        {
            string fullPath = GetFullPath(canvasRelativePath);
            if (SystemIOWrapper.FileExists(fullPath))
            {
                byte[] bytes = SystemIOWrapper.ReadAllBytes(fullPath);
                yield return new object[] { bytes };
            }
        }
    }

    public static IEnumerable<object[]> GetTestCanvasAsInlineData()
    {
        foreach (string canvasRelativePath in TestCanvasRelativePaths)
        {
            string fullPath = GetFullPath(canvasRelativePath);
            if (SystemIOWrapper.FileExists(fullPath))
            {
                byte[] bytes = SystemIOWrapper.ReadAllBytes(fullPath);

                // 先解析获取名称
                CanvasPreview preview = new CanvasPreview();
                CanvasNodeResult result = preview.ParseBytes(bytes);
                if (result.Ok && result.Root is { } root)
                {
                    // 生成内联格式的二进制（无文件头）
                    byte[] inlineBytes = preview.ConvertToInlineBinary(root);
                    string inlineBase64 = CanvasInlineArg.BuildLegacyBase64RawText(result.CanvasName, inlineBytes);
                    yield return new object[] { inlineBase64 };
                }
            }
        }
    }

    public static IEnumerable<object[]> GetTestCanvasDataWithInline()
    {
        foreach (string canvasRelativePath in TestCanvasRelativePaths)
        {
            string fullPath = GetFullPath(canvasRelativePath);
            if (SystemIOWrapper.FileExists(fullPath))
            {
                byte[] bytes = SystemIOWrapper.ReadAllBytes(fullPath);

                // 解析获取名称
                CanvasPreview preview = new CanvasPreview();
                CanvasNodeResult result = preview.ParseBytes(bytes);
                if (result.Ok && result.Root is { } root)
                {
                    // 生成内联格式
                    byte[] inlineBytes = preview.ConvertToInlineBinary(root);
                    string inlineBase64 = CanvasInlineArg.BuildLegacyBase64RawText(result.CanvasName, inlineBytes);
                    yield return new object[] { bytes, inlineBase64 };
                }
            }
        }
    }

    public static IEnumerable<object[]> GetTestCanvasAsNodeHjsonData()
    {
        foreach (string canvasRelativePath in TestCanvasRelativePaths)
        {
            string fullPath = GetFullPath(canvasRelativePath);
            if (SystemIOWrapper.FileExists(fullPath))
            {
                byte[] bytes = SystemIOWrapper.ReadAllBytes(fullPath);
                CanvasNodeResult result = CanvasConverter.FromBinary(bytes);
                if (result.Ok && result.Root is { } root)
                {
                    string hjson = CanvasInlineNodeHjsonCodec.Serialize(root);
                    yield return new object[] { hjson };
                }
            }
        }
    }

    public static IEnumerable<object[]> GetTestCanvasDataWithNodeHjson()
    {
        foreach (string canvasRelativePath in TestCanvasRelativePaths)
        {
            string fullPath = GetFullPath(canvasRelativePath);
            if (SystemIOWrapper.FileExists(fullPath))
            {
                byte[] bytes = SystemIOWrapper.ReadAllBytes(fullPath);
                CanvasNodeResult result = CanvasConverter.FromBinary(bytes);
                if (result.Ok && result.Root is { } root)
                {
                    string hjson = CanvasInlineNodeHjsonCodec.Serialize(root);
                    yield return new object[] { bytes, hjson };
                }
            }
        }
    }

    #endregion
}
