using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Canvas;

public class ByteCompareTests
{
    [Fact]
    public void Compare_Bytes_Detailed()
    {
        // 加载测试数据
        string testPath = "test/data/projects/颜色对对碰/UI_A2.canvas";
        string fullPath = Path.GetFullPath(testPath);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"File not found: {fullPath}");
            return;
        }

        byte[] originalBytes = File.ReadAllBytes(fullPath);
        Console.WriteLine($"Original size: {originalBytes.Length}");

        // 解析
        CanvasPreview preview = new CanvasPreview();
        CanvasNodeResult result = preview.ParseBytes(originalBytes);
        Assert.True(result.Ok);
        Assert.NotNull(result.Root);

        // 转换回二进制
        byte[] roundtripBytes = CanvasConverter.ToBinary(result.Root, result.CanvasName);
        Console.WriteLine($"Roundtrip size: {roundtripBytes.Length}");

        // 逐字节比较
        int minLen = Math.Min(originalBytes.Length, roundtripBytes.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (originalBytes[i] != roundtripBytes[i])
            {
                Console.WriteLine($"\nFirst difference at byte {i}:");
                Console.WriteLine($"  Original:  {originalBytes[i]:D3} (0x{originalBytes[i]:X2})");
                Console.WriteLine($"  Roundtrip: {roundtripBytes[i]:D3} (0x{roundtripBytes[i]:X2})");

                // 显示上下文
                int contextStart = Math.Max(0, i - 15);
                int contextEnd = Math.Min(minLen, i + 15);
                Console.WriteLine($"\nContext (bytes {contextStart}-{contextEnd}):");
                Console.WriteLine("  Orig:  " + string.Join(" ", originalBytes[contextStart..contextEnd].Select(static b => $"{b,3:D}")));
                Console.WriteLine("  Round: " + string.Join(" ", roundtripBytes[contextStart..contextEnd].Select(static b => $"{b,3:D}")));

                // 尝试解析上下文含义
                Console.WriteLine("\nParsing context:");
                AnalyzeBytes(originalBytes, roundtripBytes, contextStart, contextEnd, i);

                break;
            }
        }

        if (originalBytes.Length != roundtripBytes.Length)
        {
            Console.WriteLine($"\nLength mismatch: {originalBytes.Length} vs {roundtripBytes.Length}");
        }

        Assert.Equal(originalBytes, roundtripBytes);
    }

    private void AnalyzeBytes(byte[] original, byte[] roundtrip, int start, int end, int diffPos)
    {
        // VarType 枚举值
        Dictionary<byte, string> varTypes = new Dictionary<byte, string>
        {
            [0] = "Void",
            [1] = "Float",
            [3] = "UI",
            [4] = "Vector3",
            [5] = "Bool",
            [6] = "String",
            [9] = "UIRect",
            [10] = "UIButton",
            [11] = "UILabel",
            [12] = "Color",
            [14] = "UIInput",
            [16] = "List",
            [17] = "Dict",
            [20] = "UIRawImage",
            [24] = "Long",
            [26] = "Struct",
            [27] = "Canvas"
        };

        Console.WriteLine($"  Diff at {diffPos}:");
        Console.WriteLine($"    Original:  {original[diffPos]} = {varTypes.GetValueOrDefault(original[diffPos], "?")}");
        Console.WriteLine($"    Roundtrip: {roundtrip[diffPos]} = {varTypes.GetValueOrDefault(roundtrip[diffPos], "?")}");
    }
}
