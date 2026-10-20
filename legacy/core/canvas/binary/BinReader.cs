using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

/// <summary>
/// 二进制读取器 - 仅用于 Canvas 二进制解析
/// </summary>
/// <remarks>
/// 此类专门用于 .canvas 文件的二进制格式读取。
/// 不支持其他用途，也不应被扩展为通用二进制读取器。
/// </remarks>
public static class BinReader
{

    // Generic unmanaged type read
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<T>(ReadOnlySpan<byte> span, int offset) where T : unmanaged
    {
        return Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(span.Slice(offset)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SizeOf<T>() where T : unmanaged => Unsafe.SizeOf<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]

    // String reading - 7-bit encoded UTF-8
    public static (string Value, int NextOffset) ReadString(ReadOnlySpan<byte> span, int offset)
    {
        int stringLength = 0;
        bool stringLengthParsed = false;
        int step = 0;
        int bytesRead = 0;

        // 7-bit length decoding
        while (!stringLengthParsed)
        {
            byte part = Read<byte>(span, offset + bytesRead);
            stringLengthParsed = (part >> 7) == 0;
            int partCutter = part & 127;
            int toAdd = partCutter << (step * 7);
            stringLength += toAdd;
            step++;
            bytesRead++;
        }

        if (stringLength == 0) return (string.Empty, offset + bytesRead);

        ReadOnlySpan<byte> bytes = ReadSpan(span, offset + bytesRead, stringLength);
        bytesRead += stringLength;
        return (TextEncoding.Utf8.GetString(bytes), offset + bytesRead);
    }

    // Memory/Span reading
    public static ReadOnlySpan<byte> ReadSpan(ReadOnlySpan<byte> span, int offset, int len)
    {
        if (len == 0) return ReadOnlySpan<byte>.Empty;

        return span.Slice(offset, len);
    }
}
