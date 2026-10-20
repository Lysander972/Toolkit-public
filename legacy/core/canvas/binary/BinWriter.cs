using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

/// <summary>
/// 二进制写入器 - 仅用于 Canvas 二进制编码
/// </summary>
/// <remarks>
/// 此类专门用于 .canvas 文件的二进制格式写入（HTML 视图写回二进制）。
/// 不支持其他用途，也不应被扩展为通用二进制写入器。
/// </remarks>
public sealed class BinWriter : IDisposable
{
    private byte[] _bytes;
    private readonly bool _ownsBuffer;
    private bool _disposed;

    public BinWriter(int capacity)
    {
        _bytes = [];
        _ownsBuffer = false;
        if (capacity > 0)
        {
            _bytes = MyArrayPool<byte>.Rent(capacity);
            _ownsBuffer = true;
        }
        length = 0;
    }

    public int length { get; private set; }

    public Memory<byte> Memory => _bytes.AsMemory(0, length);
    public Span<byte> Span => _bytes.AsSpan(0, length);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void Reset() => length = 0;

    public void Write(ReadOnlySpan<byte> v)
    {
        if (v.Length == 0) return;
        EnsureCapacity(v.Length);
        v.CopyTo(_bytes.AsSpan(length));
        length += v.Length;
    }

    public void Write<T>(in T v) where T : unmanaged
    {
        EnsureCapacity(Unsafe.SizeOf<T>());
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(_bytes.AsSpan(length)), v);
        length += Unsafe.SizeOf<T>();
    }

    // String methods - 7-bit encoded UTF-8
    public void WriteAsString(ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
        {
            Write((byte)0);
            return;
        }

        int byteCount = TextEncoding.Utf8.GetByteCount(chars);
        int tmp = byteCount;

        // 7-bit length encoding
        while (tmp >= 0x80)
        {
            Write((byte)(tmp | 0x80));
            tmp >>= 7;
        }
        Write((byte)tmp);

        EnsureCapacity(byteCount);
        _ = TextEncoding.Utf8.GetBytes(chars, _bytes.AsSpan(length));
        length += byteCount;
    }


    public byte[] ToArray() => Memory.ToArray();

    private void EnsureCapacity(int additional)
    {
        if (_bytes.Length - length >= additional) return;
        int newCapacity = Math.Max(_bytes.Length * 2, length + additional);
        if (newCapacity == 0) newCapacity = Math.Max(16, additional);
        byte[] tmp = MyArrayPool<byte>.Rent(newCapacity);
        if (length > 0)
        {
            _bytes.AsSpan(0, length).CopyTo(tmp);
        }
        MyArrayPool<byte>.Return(_bytes, _ownsBuffer);
        _bytes = tmp;
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing && _ownsBuffer && _bytes.Length > 0)
        {
            MyArrayPool<byte>.Return(_bytes, true);
        }

        _bytes = Array.Empty<byte>();
        _disposed = true;
    }
}

internal static class MyArrayPool<T>
{
    public static T[] Rent(int length) => ArrayPool<T>.Shared.Rent(length);

    public static void Return(T[] array, bool clearArray = false)
    {
        if (array.Length == 0) return;
        ArrayPool<T>.Shared.Return(array, clearArray);
    }
}
