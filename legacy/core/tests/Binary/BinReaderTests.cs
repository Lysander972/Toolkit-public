using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Binary;

public sealed class BinReaderTests
{
    [Fact]
    public void Read_Should_Read_Unmanaged_Value_And_Advance_Span()
    {
        byte[] data = new byte[] { 0x78, 0x56, 0x34, 0x12, 0xFF };
        ReadOnlySpan<byte> span = data;

        int value = BinReader.Read<int>(span, 0);

        Assert.Equal(0x12345678, value);
        Assert.Equal(0xFF, span[BinReader.SizeOf<int>()]);
    }

    [Fact]
    public void ReadString_Should_Decode_Ascii_And_Consume_All()
    {
        byte[] data = new byte[] { 0x05, 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        ReadOnlySpan<byte> span = data;

        (string text, int nextOffset) = BinReader.ReadString(span, 0);

        Assert.Equal("Hello", text);
        Assert.Equal(span.Length, nextOffset);
    }

    [Fact]
    public void ReadString_Should_Decode_Utf8_Chinese()
    {
        byte[] payload = TextEncoding.Utf8.GetBytes("测试");
        byte[] data = new byte[payload.Length + 1];
        data[0] = (byte)payload.Length;
        Array.Copy(payload, 0, data, 1, payload.Length);
        ReadOnlySpan<byte> span = data;

        (string text, int nextOffset) = BinReader.ReadString(span, 0);

        Assert.Equal("测试", text);
        Assert.Equal(span.Length, nextOffset);
    }

    [Fact]
    public void ReadString_Should_Decode_MultiByte_7Bit_Length()
    {
        byte[] payload = new byte[130];
        Array.Fill(payload, (byte)'a');

        byte[] data = new byte[payload.Length + 2];
        data[0] = 0x82;
        data[1] = 0x01;
        Array.Copy(payload, 0, data, 2, payload.Length);
        ReadOnlySpan<byte> span = data;

        (string text, int nextOffset) = BinReader.ReadString(span, 0);

        Assert.Equal(new string('a', 130), text);
        Assert.Equal(span.Length, nextOffset);
    }

    [Fact]
    public void ReadSpan_Should_Return_Requested_Length_And_Advance()
    {
        byte[] data = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        ReadOnlySpan<byte> span = data;

        ReadOnlySpan<byte> part = BinReader.ReadSpan(span, 0, 3);

        Assert.Equal(3, part.Length);
        Assert.Equal((byte)0x10, part[0]);
        Assert.Equal((byte)0x30, part[2]);
        Assert.Equal((byte)0x40, span[3]);
    }
}
