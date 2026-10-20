using System.Globalization;
using System.Text;
using Hjson;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

/// <summary>
/// Hjson 文本写入器。
///
/// 游戏工程文本的格式规则：
/// - VectorArray：内联格式 [v1,v2,v3]
/// - 普通数组：多行格式
/// - 浮点数：使用 float 精度
/// </summary>
internal static class SimpleJsonWriter
{
    /// <summary>
    /// 将 JsonValue 序列化为游戏工程文本。
    /// </summary>
    public static string Serialize(JsonValue value)
    {
        Writer writer = new();
        WriteValue(writer, value, isRoot: true);
        return writer.ToString();
    }

    private static void WriteValue(Writer w, JsonValue value, bool isRoot)
    {
        switch (value.JsonType)
        {
            case JsonType.Object:
                WriteObject(w, (JsonObject)value, isRoot);
                break;
            case JsonType.Array:
                if (isRoot)
                {
                    // 根级数组：需要输出 [ 和 ]
                    w.StartRootArray();
                    WriteArray(w, (JsonArray)value);
                    w.EndRootArray();
                }
                else
                {
                    WriteArray(w, (JsonArray)value);
                }
                break;
            case JsonType.String:
                w.WriteValue(EscapeString((string)value));
                break;
            case JsonType.Boolean:
                if ((bool)value)
                    w.WriteValue("true");
                else
                    w.WriteValue("false");
                break;
            case JsonType.Number:
                w.WriteValue(FormatNumber(value));
                break;
            case JsonType.Unknown:
                throw new InvalidDataException("Hjson 值类型不支持序列化");
            default:
                throw new InvalidDataException("Hjson 值类型不支持序列化");
        }
    }

    private static void WriteObject(Writer w, JsonObject obj, bool isRoot)
    {
        if (!isRoot)
        {
            w.StartObject();
        }

        foreach (KeyValuePair<string, JsonValue> kv in obj)
        {
            if (kv.Value == null)
                continue;

            if (kv.Value is VectorArray vecArr)
            {
                // VectorArray 类型：内联格式
                w.WriteProperty(kv.Key, SerializeVectorArray(vecArr));
            }
            else if (kv.Value.JsonType == JsonType.Object)
            {
                // 嵌套对象：key:\n{...}
                w.WriteKey(kv.Key);
                WriteObject(w, (JsonObject)kv.Value, isRoot: false);
            }
            else if (kv.Value.JsonType == JsonType.Array)
            {
                // 普通数组：多行格式
                w.WriteKey(kv.Key);
                w.StartArray();
                WriteArray(w, (JsonArray)kv.Value);
                w.EndArray();
            }
            else
            {
                // 简单值：key:value
                w.WriteProperty(kv.Key, FormatValue(kv.Value));
            }
        }

        if (!isRoot)
        {
            w.EndObject();
        }
    }

    /// <summary>
    /// 将对象中的指定字段标记为向量数组。
    /// 仅当字段值为"指定长度的纯数值数组"时转换，否则抛错。
    /// </summary>
    internal static void MarkVectorArrayProperty(JsonObject obj, string propertyName, int expectedLength)
    {
        if (expectedLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLength));

        if (!obj.TryGetValue(propertyName, out JsonValue? value))
            return;
        if (value is VectorArray)
            return;
        if (value is not JsonArray arr)
            return;
        if (!IsExactNumericArray(arr, expectedLength))
            throw new InvalidDataException($"{propertyName} 必须为长度 {expectedLength} 的数值数组");

        obj[propertyName] = new VectorArray(arr);
    }

    private static bool IsExactNumericArray(JsonArray arr, int expectedLength)
    {
        if (arr.Count != expectedLength)
            return false;

        foreach (JsonValue? item in arr)
        {
            if (item.JsonType != JsonType.Number)
                return false;
        }

        return true;
    }

    private static void WriteArray(Writer w, JsonArray arr)
    {
        foreach (JsonValue? item in arr)
        {
            if (item is VectorArray vecArr)
            {
                w.WriteValue(SerializeVectorArray(vecArr));
                continue;
            }

            switch (item.JsonType)
            {
                case JsonType.String:
                    w.WriteValue(EscapeString((string)item));
                    break;
                case JsonType.Boolean:
                    if ((bool)item)
                        w.WriteValue("true");
                    else
                        w.WriteValue("false");
                    break;
                case JsonType.Number:
                    w.WriteValue(FormatNumber(item));
                    break;
                case JsonType.Object:
                    WriteObject(w, (JsonObject)item, isRoot: false);
                    break;
                case JsonType.Array:
                    // 嵌套普通数组：内联
                    w.WriteValue(SerializeInlineArray((JsonArray)item));
                    break;
                case JsonType.Unknown:
                    throw new InvalidDataException("Hjson 值类型不支持序列化");
                default:
                    throw new InvalidDataException("Hjson 值类型不支持序列化");
            }
        }
    }

    /// <summary>
    /// 序列化向量数组（内联格式，float 精度）。
    /// </summary>
    private static string SerializeVectorArray(JsonArray arr)
    {
        StringBuilder sb = new();
        sb.Append('[');
        for (int i = 0; i < arr.Count; i++)
        {
            if (i > 0) sb.Append(',');
            double d = arr[i].Qd();
            if (d == 0.0)
                if (double.IsNegative(d))
                    sb.Append("-0");
                else
                    sb.Append('0');
            else if (Math.Abs(d - Math.Truncate(d)) < double.Epsilon && Math.Abs(d) < long.MaxValue)
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else
                sb.Append(((float)d).ToString("R", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string SerializeInlineArray(JsonArray arr)
    {
        StringBuilder sb = new();
        sb.Append('[');
        bool first = true;
        foreach (JsonValue? item in arr)
        {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append(FormatValue(item));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string FormatValue(JsonValue value)
    {
        if (value is VectorArray vecArr)
            return SerializeVectorArray(vecArr);

        return value.JsonType switch
        {
            JsonType.String => EscapeString((string)value),
            JsonType.Boolean => (bool)value ? "true" : "false",
            JsonType.Number => FormatNumber(value),
            _ => value.ToString()
        };
    }

    private static string FormatNumber(JsonValue value)
    {
        double d = value.Qd();
        // 保留负零的符号位（-0.0 和 +0.0 二进制表示不同）
        if (d == 0.0)
        {
            if (double.IsNegative(d))
                return "-0";
            return "0";
        }
        if (Math.Abs(d - Math.Truncate(d)) < double.Epsilon && Math.Abs(d) < long.MaxValue)
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        return ((float)d).ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 按游戏工程文本格式转义字符串。
    /// </summary>
    private static string EscapeString(string v)
    {
        string lfText = TextLineEndings.ToLf(v);

        if (lfText == "null")
            return "\"null\"";

#pragma warning disable TK008 // Hjson 格式规则：空字符串必须引号包裹，否则 key: 后无值，解析器无法识别
        bool needsQuotes = lfText.Length == 0 ||
#pragma warning restore TK008
            lfText.IndexOfAny([' ', '[', ']', '/', ':', '\\']) >= 0 ||
            lfText.IndexOf('\n') >= 0 ||
            lfText is "true" or "false" ||
            double.TryParse(lfText, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

        if (!needsQuotes)
            return lfText;

        StringBuilder sb = new(lfText.Length + 2);
        sb.Append('"');
        foreach (char c in lfText)
        {
            switch (c)
            {
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// 内部写入器，首项不换行，后续项之间换行。
    /// </summary>
    private sealed class Writer
    {
        private readonly StringBuilder _sb = new();
        private int _tabs;
        private int _lines;

        public void WriteKey(string key)
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append(key).Append(':');
        }

        public void WriteProperty(string key, string value)
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append(key).Append(':').Append(value);
        }

        public void WriteValue(string value)
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append(value);
        }

        public void StartObject()
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append('{');
            _tabs++;
        }

        public void EndObject()
        {
            _tabs--;
            _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append('}');
        }

        public void StartArray()
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append('[');
            _tabs++;
        }

        public void EndArray()
        {
            _tabs--;
            _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append(']');
        }

        /// <summary>
        /// 根级数组开始。
        /// </summary>
        public void StartRootArray()
        {
            if (_lines++ > 0)
                _sb.Append('\n');
            _sb.Append(' ', _tabs);
            _sb.Append('[');
            _tabs++;
        }

        /// <summary>
        /// 根级数组结束。
        /// </summary>
        public void EndRootArray()
        {
            _tabs--;
            _sb.Append('\n');
            _sb.Append(']');
        }

        public override string ToString() => _sb.ToString();
    }
}
