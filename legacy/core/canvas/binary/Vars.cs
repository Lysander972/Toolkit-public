using System.Runtime.CompilerServices;

namespace Toolkit.Core.Canvas;

// 此文件包含 Canvas 二进制解析使用的所有变量类型
// 这些类型仅用于 .canvas 文件的 VarBase 树结构解析

#region VarType枚举

/// <summary>
/// 变量类型枚举 - Canvas 二进制解析使用
/// </summary>
/// <remarks>
/// 这些类型对应 .canvas 文件二进制格式中的 VarType 字节。
/// </remarks>
public enum VarType : byte
{
    Void = 0,
    Float = 1,
    UI = 3,
    Vector3 = 4,
    Bool = 5,
    String = 6,
    UISpine = 8,
    UIRect = 9,
    UIButton = 10,
    UILabel = 11,
    Color = 12,
    UIInput = 14,
    List = 16,
    Dict = 17,
    UIRawImage = 20,
    Long = 24,
    Struct = 26,
    Canvas = 27
}

#endregion

#region 基类

/// <summary>
/// 变量基类 - Canvas 二进制解析使用
/// </summary>
/// <remarks>
/// 此类及其派生类用于 .canvas 文件的二进制格式解析（VarBase 树结构）。
/// 这是原版游戏的序列化格式，Toolkit 仅保留必要的解析逻辑以支持 Canvas 预览和 HTML 视图。
/// </remarks>
public abstract class VarBase
{
    public virtual VarType type => VarType.Void;

    public void LoadFromBinary(ReadOnlySpan<byte> span) => LoadFromBinary(span, 0);
    public virtual int LoadFromBinary(ReadOnlySpan<byte> span, int offset) => offset;
    public virtual void SaveToBinary(BinWriter writer, int level) { }

    /// <summary>
    /// 根据类型创建对应的变量对象
    /// </summary>
    public static VarBase Create(VarType type)
    {
        return type switch
        {
            VarType.Void => throw new Exception($"VarType.Void 不支持实例化"),
            VarType.Float => new FloatVar(),
            VarType.Long => new LongVar(),
            VarType.String => new StringVar(),
            VarType.Bool => new BoolVar(),
            VarType.Vector3 => new Vector3Var(),
            VarType.Color => new ColorVar(),
            // UI 类型 - Canvas 解析必需
            VarType.UI => new UIVar(),
            VarType.UISpine => new UISpineVar(),
            VarType.UIRect => new UIRectVar(),
            VarType.UIButton => new UIButtonVar(),
            VarType.UILabel => new UILabelVar(),
            VarType.UIInput => new UIInputVar(),
            VarType.UIRawImage => new UIRawImageVar(),
            VarType.Canvas => new CanvasVar(),
            // 集合类型
            VarType.List => new ListVar(),
            VarType.Dict => new DictVar(),
            VarType.Struct => new StructVar(),
            _ => throw new Exception($"不支持的变量类型 {type}")
        };
    }
}

#endregion

#region 基础类型

/// <summary>
/// 布尔类型变量 - Canvas 解析使用
/// </summary>
public class BoolVar : VarBase
{
    private bool _v;

    public bool v
    {
        get => _v;
        set => _v = value;
    }

    public override VarType type => VarType.Bool;

    // 隐式转换操作符
    public static implicit operator bool(BoolVar var) => var._v;
    public static implicit operator BoolVar(bool value) => new() { v = value };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write(_v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _v = BinReader.Read<bool>(span, offset);
        return offset + BinReader.SizeOf<bool>();
    }

    public override string ToString() => _v.ToString();
}

/// <summary>
/// 浮点类型变量 - Canvas 解析使用
/// </summary>
public class FloatVar : VarBase
{
    private float _v;

    public float v
    {
        get => _v;
        set => _v = value;
    }

    public override VarType type => VarType.Float;

    // 隐式转换操作符
    public static implicit operator float(FloatVar var) => var._v;
    public static implicit operator FloatVar(float value) => new() { v = value };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write(_v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _v = BinReader.Read<float>(span, offset);
        return offset + BinReader.SizeOf<float>();
    }

    public override string ToString() => _v.ToString();
}

/// <summary>
/// 长整型变量 - Canvas 解析使用
/// </summary>
public class LongVar : VarBase
{
    private long _v;

    public long v
    {
        get => _v;
        set => _v = value;
    }

    public override VarType type => VarType.Long;

    // 隐式转换操作符
    public static implicit operator long(LongVar var) => var._v;
    public static implicit operator LongVar(long value) => new() { v = value };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write(_v);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _v = BinReader.Read<long>(span, offset);
        return offset + BinReader.SizeOf<long>();
    }

    public override string ToString() => _v.ToString();
}

/// <summary>
/// 字符串类型变量 - Canvas 解析使用
/// </summary>
public class StringVar : VarBase
{
    private string _v = string.Empty;

    public string v
    {
        get => _v;
        set => _v = value;
    }

    public override VarType type => VarType.String;

    // 隐式转换操作符
    public static implicit operator string(StringVar var) => var._v;
    public static implicit operator StringVar(string value) => new() { v = value };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.WriteAsString(_v.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        (string value, int nextOffset) = BinReader.ReadString(span, offset);
        _v = value;
        return nextOffset;
    }

    public override string ToString() => _v;
}

/// <summary>
/// 三维向量类型变量 - Canvas 解析使用
/// </summary>
public class Vector3Var : VarBase
{
    private float _x, _y, _z;

    public float X
    {
        get => _x;
        set => _x = value;
    }

    public float Y
    {
        get => _y;
        set => _y = value;
    }

    public float Z
    {
        get => _z;
        set => _z = value;
    }

    public override VarType type => VarType.Vector3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write(_x);
        writer.Write(_y);
        writer.Write(_z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _x = BinReader.Read<float>(span, offset);
        offset += BinReader.SizeOf<float>();
        _y = BinReader.Read<float>(span, offset);
        offset += BinReader.SizeOf<float>();
        _z = BinReader.Read<float>(span, offset);
        return offset + BinReader.SizeOf<float>();
    }

    public override string ToString() => $"({_x}, {_y}, {_z})";
}

/// <summary>
/// 颜色类型变量 - Canvas 解析使用
/// </summary>
public class ColorVar : VarBase
{
    private float _r, _g, _b, _a;
    private string? _paletteName;

    public float R
    {
        get => _r;
        set => _r = value;
    }

    public float G
    {
        get => _g;
        set => _g = value;
    }

    public float B
    {
        get => _b;
        set => _b = value;
    }

    public float A
    {
        get => _a;
        set => _a = value;
    }

    public string? PaletteName
    {
        get => _paletteName;
        set => _paletteName = value;
    }

    public override VarType type => VarType.Color;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        if (!string.IsNullOrWhiteSpace(_paletteName))
        {
            writer.Write(-1f);
            writer.WriteAsString(_paletteName.AsSpan());
            return;
        }

        writer.Write(_r);
        writer.Write(_g);
        writer.Write(_b);
        writer.Write(_a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _r = BinReader.Read<float>(span, offset);
        offset += BinReader.SizeOf<float>();
        if (_r < 0) // Palette support
        {
            (string value, int nextOffset) = BinReader.ReadString(span, offset);
            _paletteName = value;
            _r = _g = _b = _a = 1f;
            return nextOffset;
        }
        else
        {
            _g = BinReader.Read<float>(span, offset);
            offset += BinReader.SizeOf<float>();
            _b = BinReader.Read<float>(span, offset);
            offset += BinReader.SizeOf<float>();
            _a = BinReader.Read<float>(span, offset);
            return offset + BinReader.SizeOf<float>();
        }
    }

    public override string ToString() => $"({_r:F3}, {_g:F3}, {_b:F3}, {_a:F3})";
}

#endregion

#region UI类型

/// <summary>
/// UI变量基类 - Canvas 解析使用
/// </summary>
public class UIVar : VarBase
{
    public override VarType type => VarType.UI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        return offset;
    }

    public override string ToString() => $"UI";
}

/// <summary>
/// UISpine 变量 - Canvas 解析使用
/// </summary>
public class UISpineVar : UIVar
{
    public override VarType type => VarType.UISpine;
}

/// <summary>
/// UIRect 变量 - Canvas 解析使用
/// </summary>
public class UIRectVar : UIVar
{
    public override VarType type => VarType.UIRect;
}

/// <summary>
/// UIButton 变量 - Canvas 解析使用
/// </summary>
public class UIButtonVar : UIVar
{
    public override VarType type => VarType.UIButton;
}

/// <summary>
/// UILabel 变量 - Canvas 解析使用
/// </summary>
public class UILabelVar : UIVar
{
    public override VarType type => VarType.UILabel;
}

/// <summary>
/// UIInput 变量 - Canvas 解析使用
/// </summary>
public class UIInputVar : UIVar
{
    public override VarType type => VarType.UIInput;
}

/// <summary>
/// UIRawImage 变量 - Canvas 解析使用
/// </summary>
public class UIRawImageVar : UIVar
{
    public override VarType type => VarType.UIRawImage;
}

/// <summary>
/// Canvas 变量 - Canvas 解析使用
/// </summary>
public class CanvasVar : UIVar
{
    public override VarType type => VarType.Canvas;
}

#endregion

#region 集合类型

/// <summary>
/// 列表类型变量 - Canvas 解析使用
/// </summary>
public class ListVar : VarBase
{
    private List<VarBase> _v = [];

    public List<VarBase> v => _v;

    public override VarType type => VarType.List;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write((ushort)_v.Count);
        foreach (VarBase obj in _v)
        {
            obj.SaveToBinary(writer, level + 1);
        }
    }

    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _v.Clear();
        ushort count = BinReader.Read<ushort>(span, offset);
        offset += BinReader.SizeOf<ushort>();
        while (_v.Count < count)
        {
            VarType type1 = (VarType)BinReader.Read<byte>(span, offset);
            offset += BinReader.SizeOf<byte>();
            VarBase obj = Create(type1);
            offset = obj.LoadFromBinary(span, offset);
            _v.Add(obj);
        }
        return offset;
    }

    public override string ToString() => $"List[{_v.Count}]";
}

/// <summary>
/// 字典类型变量 - Canvas 解析使用
/// </summary>
public class DictVar : VarBase
{
    private readonly Dictionary<zchar, VarBase> _v = [];
    private readonly List<zchar> _keys = [];

    public override VarType type => VarType.Dict;

    public int Count => _v.Count;

    public IEnumerable<zchar> Keys => _keys;

    public VarBase this[zchar key]
    {
        get => _v[key];
        set => Add(key, value);
    }

    // TryGetValue 标准模式，数据来源单一
    public VarBase? FindValue(zchar key) =>
        _v.ContainsKey(key) ? _v[key] : null;

    internal void Add(zchar key, VarBase value)
    {
        if (!_v.ContainsKey(key))
            _keys.Add(key);
        _v[key] = value;
    }

    public IEnumerable<(zchar key, VarBase value)> GetOrderedEntries()
    {
        foreach (zchar key in _keys)
            yield return (key, _v[key]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write((ushort)Count);
        foreach (zchar key in _keys)
        {
            VarBase value = _v[key];
            writer.WriteAsString(key.CharSpan);
            value.SaveToBinary(writer, level + 1);
        }
    }

    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        _v.Clear();
        _keys.Clear();
        ushort count = BinReader.Read<ushort>(span, offset);
        offset += BinReader.SizeOf<ushort>();
        for (int i = 0; i < count; i++)
        {
            (string key, int keyEndOffset) = BinReader.ReadString(span, offset);
            offset = keyEndOffset;
            VarType varType = (VarType)BinReader.Read<byte>(span, offset);
            offset += BinReader.SizeOf<byte>();
            VarBase obj = Create(varType);
            offset = obj.LoadFromBinary(span, offset);
            zchar zKey = new zchar(key);
            _v[zKey] = obj;
            _keys.Add(zKey);
        }
        return offset;
    }

    public override string ToString() => $"Dict[{Count}]";
}

/// <summary>
/// 结构体类型变量 - Canvas 解析使用
/// </summary>
public class StructVar : VarBase
{
    private VarBase[] _v = Array.Empty<VarBase>();

    public VarBase[] v => _v;

    public override VarType type => VarType.Struct;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SaveToBinary(BinWriter writer, int level)
    {
        writer.Write((byte)type);
        writer.Write((ushort)_v.Length);
        foreach (VarBase v1 in _v)
        {
            writer.WriteAsString(v1.ToString().AsSpan());
            v1.SaveToBinary(writer, level + 1);
        }
    }

    public override int LoadFromBinary(ReadOnlySpan<byte> span, int offset)
    {
        ushort count = BinReader.Read<ushort>(span, offset);
        offset += BinReader.SizeOf<ushort>();
        _v = new VarBase[count];
        for (int i = 0; i < count; i++)
        {
            // Key is discarded in simplified version
            (_, offset) = BinReader.ReadString(span, offset);
            VarType type1 = (VarType)BinReader.Read<byte>(span, offset);
            offset += BinReader.SizeOf<byte>();
            VarBase obj = Create(type1);
            offset = obj.LoadFromBinary(span, offset);
            _v[i] = obj;
        }
        return offset;
    }

    public override string ToString() => $"Struct[{_v.Length}]";
}

#endregion
