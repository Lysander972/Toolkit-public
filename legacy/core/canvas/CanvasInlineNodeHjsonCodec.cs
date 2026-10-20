using System.Globalization;
using System.Reflection;
using Hjson;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

internal static class CanvasInlineNodeHjsonCodec
{
    internal static CanvasNodeResult Parse(string canvasName, string hjsonText)
    {
        try
        {
            JsonValue rootValue = HjsonText.Parse(hjsonText);
            if (rootValue is not JsonArray array)
            {
                return Fail("新版内联 Canvas 顶层必须为数组");
            }

            UICanvasNode root = new()
            {
                name = new StringVar { v = canvasName },
                size = new Vector3Var { X = 1, Y = 1, Z = 0 },
                Pos = new Vector3Var { X = 0.5f, Y = 0.5f, Z = 0 },
                parentAnchor = new LongVar { v = 4 },
                anchor = new LongVar { v = 4 },
                widthExtend = new LongVar { v = 1 },
                heightExtend = new LongVar { v = 1 }
            };
            foreach (JsonValue? item in array)
            {
                if (item is not JsonObject obj)
                {
                    return Fail("新版内联 Canvas 顶层数组项必须为对象");
                }

                root.Children.Add(ParseNodeObject(obj));
            }

            return new CanvasNodeResult
            {
                Ok = true,
                CanvasName = canvasName,
                Root = root
            };
        }
        catch (Exception ex)
        {
            return Fail($"新版内联 Canvas Hjson 解析失败: {ex.Message}");
        }
    }

    internal static string Serialize(CanvasNode root)
    {
        JsonArray array = [];
        CanvasPreview preview = new();
        foreach (CanvasNode child in root.Children)
        {
            DictVar dict = preview.ConvertNodeToDict(child);
            array.Add(SerializeDict(dict));
        }

        return HjsonText.ToHjsonText(array);
    }

    private static CanvasNodeResult Fail(string message)
    {
        return new CanvasNodeResult
        {
            Ok = false,
            Diagnostics = { new ToolError(null, null, null, message, ToolDiagnosticSeverity.Error) }
        };
    }

    private static CanvasNode ParseNodeObject(JsonObject obj)
    {
        if (!obj.TryGetValue("type", out JsonValue? typeValue) || typeValue.JsonType != JsonType.String)
        {
            throw new InvalidOperationException("新版内联 Canvas 节点缺少字符串 type 字段");
        }

        if (!obj.TryGetValue("name", out JsonValue? nameValue) || nameValue.JsonType != JsonType.String)
        {
            throw new InvalidOperationException("新版内联 Canvas 节点缺少字符串 name 字段");
        }

        string typeName = (string)typeValue;
        if (!CanvasNodeSchema.IsSupportedType(typeName))
        {
            throw new InvalidOperationException($"新版内联 Canvas 不支持的节点类型: {typeName}");
        }

        CanvasNode node = CanvasNodeSchema.CreateNode(typeName);

        // 忠实按照 Hjson 字段顺序处理，完全通用
        foreach ((string key, JsonValue? value) in obj)
        {
            node.MarkFieldPresent(key);

            // type/sons/name 需要特殊处理
            if (key == "type")
                continue;
            if (key == "name")
            {
                node.name = new StringVar { v = HjsonSchema.CoerceString(value, "name") };
                continue;
            }
            if (key == "sons")
            {
                if (value is not JsonArray sonsArray)
                    throw new InvalidOperationException("Canvas 的 sons 必须为数组");

                foreach (JsonValue? childValue in sonsArray)
                {
                    if (childValue is not JsonObject childObj)
                        throw new InvalidOperationException("Canvas 的 sons 子项必须为对象");
                    node.Children.Add(ParseNodeObject(childObj));
                }
                continue;
            }

            // 所有其他字段（包括 name 和 guid）完全通用处理
            ParseFieldValue(node, key, value);
        }

        return node;
    }

    private static void ParseFieldValue(CanvasNode node, string fieldName, JsonValue? value)
    {
        PropertyInfo prop = node.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"节点类型 '{node.Type}' 缺少属性 '{fieldName}'");

        Type targetType = prop.PropertyType;
        if (Nullable.GetUnderlyingType(targetType) is { } underlyingType)
            targetType = underlyingType;

        VarBase result;
        if (targetType == typeof(StringVar))
        {
            result = new StringVar { v = HjsonSchema.CoerceString(value, fieldName) };
        }
        else if (targetType == typeof(LongVar))
        {
            result = new LongVar { v = HjsonSchema.CoerceLong(value, fieldName) };
        }
        else if (targetType == typeof(FloatVar))
        {
            result = new FloatVar { v = HjsonSchema.CoerceFloat(value, fieldName) };
        }
        else if (targetType == typeof(BoolVar))
        {
            result = new BoolVar { v = HjsonSchema.CoerceBool(value) };
        }
        else if (targetType == typeof(Vector3Var))
        {
            if (value is not JsonArray vectorArray || vectorArray.Count != 3)
            {
                throw new InvalidOperationException($"字段 {fieldName} 必须为 3 元数值数组");
            }

            result = new Vector3Var
            {
                X = HjsonSchema.CoerceFloat(vectorArray[0], $"{fieldName}[0]"),
                Y = HjsonSchema.CoerceFloat(vectorArray[1], $"{fieldName}[1]"),
                Z = HjsonSchema.CoerceFloat(vectorArray[2], $"{fieldName}[2]")
            };
        }
        else if (targetType == typeof(ColorVar))
        {
            if (value?.JsonType == JsonType.String)
            {
                result = new ColorVar { PaletteName = (string)value };
            }
            else if (value is not JsonArray colorArray || colorArray.Count != 4)
            {
                throw new InvalidOperationException($"字段 {fieldName} 必须为 4 元数值数组或调色板字符串");
            }
            else
            {
                result = new ColorVar
                {
                    R = HjsonSchema.CoerceFloat(colorArray[0], $"{fieldName}[0]"),
                    G = HjsonSchema.CoerceFloat(colorArray[1], $"{fieldName}[1]"),
                    B = HjsonSchema.CoerceFloat(colorArray[2], $"{fieldName}[2]"),
                    A = HjsonSchema.CoerceFloat(colorArray[3], $"{fieldName}[3]")
                };
            }
        }
        else if (targetType == typeof(ListVar))
        {
            if (value is not JsonArray listArray)
            {
                throw new InvalidOperationException($"字段 {fieldName} 必须为数组");
            }

            ListVar list = new();
            foreach (JsonValue? item in listArray)
            {
                list.v.Add(ParseGenericValue(item, fieldName));
            }

            result = list;
        }
        else if (targetType == typeof(DictVar))
        {
            if (value is not JsonObject dictObj)
            {
                throw new InvalidOperationException($"字段 {fieldName} 必须为对象");
            }

            DictVar dict = new();
            foreach ((string key, JsonValue? dictValue) in dictObj)
            {
                dict.Add(new zchar(key), ParseGenericValue(dictValue, $"{fieldName}.{key}"));
            }

            result = dict;
        }
        else
        {
            throw new InvalidOperationException($"字段 {fieldName} 的类型 {targetType.Name} 不支持新版内联 Canvas Hjson");
        }

        prop.SetValue(node, result);
    }

    private static VarBase ParseGenericValue(JsonValue value, string fieldName)
    {
        switch (value.JsonType)
        {
            case JsonType.String:
                return new StringVar { v = (string)value };
            case JsonType.Boolean:
                return new BoolVar { v = (bool)value };
            case JsonType.Number:
                double number = value.Qd();
                if (Math.Truncate(number) == number)
                {
                    return new LongVar { v = (long)number };
                }

                return new FloatVar { v = (float)number };
            case JsonType.Array:
                ListVar list = new();
                foreach (JsonValue? item in (JsonArray)value)
                {
                    list.v.Add(ParseGenericValue(item, fieldName));
                }

                return list;
            case JsonType.Object:
                DictVar dict = new();
                foreach ((string key, JsonValue? dictValue) in (JsonObject)value)
                {
                    dict.Add(new zchar(key), ParseGenericValue(dictValue, $"{fieldName}.{key}"));
                }

                return dict;
            case JsonType.Unknown:
                throw new InvalidOperationException($"字段 {fieldName} 的值类型不支持");
            default:
                throw new InvalidOperationException($"字段 {fieldName} 的值类型不支持");
        }
    }

    private static JsonObject SerializeDict(DictVar dict)
    {
        JsonObject obj = [];
        // 使用 GetOrderedEntries 确保按照插入顺序序列化，而不是 Dictionary 的不确定顺序
        foreach ((zchar key, VarBase value) in dict.GetOrderedEntries())
        {
            obj[key.ToString()] = SerializeVar(value);
        }

        return obj;
    }

    private static JsonValue SerializeVar(VarBase value)
    {
        return value switch
        {
            StringVar sv => sv.v,
            BoolVar bv => bv.v,
            LongVar lv => lv.v,
            FloatVar fv => double.Parse(fv.v.ToString("G9", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            Vector3Var vec => new VectorArray(new[] { (double)vec.X, vec.Y, vec.Z }),
            ColorVar { PaletteName: { Length: > 0 } paletteName } => paletteName,
            ColorVar color => new VectorArray(new[] { (double)color.R, color.G, color.B, color.A }),
            DictVar dict => SerializeDict(dict),
            ListVar list => new JsonArray(list.v.Select(SerializeVar).ToArray()),
            _ => throw new InvalidOperationException($"不支持的 Var 类型: {value.GetType().Name}")
        };
    }

}
