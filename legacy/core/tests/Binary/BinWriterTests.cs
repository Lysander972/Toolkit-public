using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Binary;

public sealed class BinWriterTests : IDisposable
{
    private BinWriter? _writer;

    public void Dispose()
    {
        _writer?.Dispose();
    }

    [Fact]
    public void Constructor_Should_Initialize_Empty_Buffer()
    {
        _writer = new BinWriter(16);

        Assert.Equal(0, _writer.length);
        Assert.Equal(0, _writer.Span.Length);
        Assert.Empty(_writer.ToArray());
    }

    [Fact]
    public void Write_Should_Append_Unmanaged_Values_In_Order()
    {
        _writer = new BinWriter(16);

        _writer.Write(0x12345678);
        _writer.Write(3.5f);

        byte[] data = _writer.ToArray();
        Assert.Equal(8, _writer.length);
        Assert.Equal(0x12345678, BitConverter.ToInt32(data, 0));
        Assert.Equal(3.5f, BitConverter.ToSingle(data, 4), 3);
    }

    [Fact]
    public void Write_Should_Append_ByteSpan()
    {
        _writer = new BinWriter(8);
        ReadOnlySpan<byte> bytes = stackalloc byte[] { 0x01, 0x02, 0x03 };

        _writer.Write(bytes);

        byte[] data = _writer.ToArray();
        Assert.Equal(3, _writer.length);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, data);
    }

    [Fact]
    public void WriteAsString_Should_Write_7Bit_Length_And_Utf8_Data()
    {
        _writer = new BinWriter(16);

        _writer.WriteAsString("测试".AsSpan());

        byte[] data = _writer.ToArray();
        Assert.Equal(7, _writer.length);
        Assert.Equal(0x06, data[0]);
        Assert.Equal(TextEncoding.Utf8.GetBytes("测试"), data.AsSpan(1).ToArray());
    }

    [Fact]
    public void WriteAsString_Should_Use_MultiByte_7Bit_Length_When_Needed()
    {
        _writer = new BinWriter(8);
        string text = new string('a', 130);

        _writer.WriteAsString(text.AsSpan());

        byte[] data = _writer.ToArray();
        Assert.Equal(132, _writer.length);
        Assert.Equal(0x82, data[0]);
        Assert.Equal(0x01, data[1]);
        Assert.Equal((byte)'a', data[2]);
    }

    [Fact]
    public void Reset_Should_Clear_Written_Content()
    {
        _writer = new BinWriter(8);
        _writer.Write((byte)0xFF);

        _writer.Reset();

        Assert.Equal(0, _writer.length);
        Assert.Empty(_writer.ToArray());
    }
}
