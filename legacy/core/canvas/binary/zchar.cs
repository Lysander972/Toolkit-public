using System.Runtime.InteropServices;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Canvas;

// 此文件包含 Canvas 二进制解析使用的辅助类型

/// <summary>
/// 零分配字符串包装器 - Canvas 解析使用
/// </summary>
/// <remarks>
/// 仅用于 .canvas 文件的 VarBase 树结构解析（DictVar 的键类型）。
/// </remarks>
#pragma warning disable CS8981 // 小写类型名称
public struct zchar : IEquatable<zchar>
{
#pragma warning restore CS8981
    public static readonly zchar Empty = new();

    private string? _string;
    private byte[]? _utf8Bytes;
    private char[]? _chars;

    public bool IsEmpty => _string == null && _utf8Bytes == null && _chars == null;
    public int Length
    {
        get
        {
            if (_string != null) return _string.Length;
            if (_chars != null) return _chars.Length;
            if (_utf8Bytes != null) return TextEncoding.Utf8.GetCharCount(_utf8Bytes);
            return 0;
        }
    }
    public int byteLength => Length * 2; // UTF-16 byte count

    public ReadOnlySpan<char> CharSpan
    {
        get
        {
            if (_string != null)
            {
                return _string.AsSpan();
            }

            if (_chars != null)
            {
                return _chars.AsSpan();
            }

            return ReadOnlySpan<char>.Empty;
        }
    }
    public ReadOnlySpan<byte> ByteSpan => MemoryMarshal.AsBytes(CharSpan);

    public zchar(string value)
    {
        _string = value;
        _utf8Bytes = null;
        _chars = null;
    }

    public zchar(char[] value)
    {
        _string = null;
        _utf8Bytes = null;
        _chars = value;
    }

    public zchar(byte[] utf8Bytes)
    {
        _string = null;
        _utf8Bytes = utf8Bytes;
        _chars = null;
    }

    public static implicit operator zchar(string value) => new(value);
    public static implicit operator string(zchar value) => value.ToString();

    public override string ToString()
    {
        if (_string != null) return _string;
        if (_chars != null) return new string(_chars);
        if (_utf8Bytes != null) return TextEncoding.Utf8.GetString(_utf8Bytes);
        return string.Empty;
    }

    public bool Equals(zchar other)
    {
        return ToString() == other.ToString();
    }

    public override bool Equals(object? obj)
    {
        return obj is zchar other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public static bool operator ==(zchar left, zchar right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(zchar left, zchar right)
    {
        return !left.Equals(right);
    }
}
