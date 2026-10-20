using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Binary;

/// <summary>
/// 二进制序列化测试的共享辅助方法
/// </summary>
internal static class BinaryTestHelpers
{
    /// <summary>
    /// 保存变量到二进制并加载到新实例，跳过类型字节
    /// </summary>
    public static T SaveAndLoad<T>(T original, int bufferSize = 50) where T : VarBase, new()
    {
        using BinWriter writer = new BinWriter(bufferSize);
        original.SaveToBinary(writer, 0);
        byte[] data = writer.ToArray();

        T loaded = new T();
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        loaded.LoadFromBinary(span, 1); // Skip type byte
        return loaded;
    }

    /// <summary>
    /// 保存变量到二进制数据
    /// </summary>
    public static byte[] SaveToBinary(VarBase var, int bufferSize = 50)
    {
        using BinWriter writer = new BinWriter(bufferSize);
        var.SaveToBinary(writer, 0);
        return writer.ToArray();
    }

    /// <summary>
    /// 从二进制数据加载变量，跳过类型字节
    /// </summary>
    public static void LoadFromBinary<T>(T target, byte[] data) where T : VarBase
    {
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        target.LoadFromBinary(span, 1); // Skip type byte
    }
}
