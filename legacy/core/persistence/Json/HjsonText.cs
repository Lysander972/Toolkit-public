using Hjson;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 核心设计警告 / CORE DESIGN WARNING
// =============================================================================
//
// 本文件包含 Hjson 格式处理工具。这些是 internal 类型，因为工作区应通过
// 实体模型操作文件内容，这些工具仅用于实体内部的 Hjson 处理。
//
// 禁止：
// 1. 将这些类型改为 public
// 2. 在其他模块直接使用这些工具处理文件内容
//
// =============================================================================

/// <summary>
/// Hjson 文本处理工具。
///
/// 警告：这是内部工具，工作区应通过实体模型操作文件。
///
/// 游戏工程文本的序列化格式：
/// - 键值对无空格：key:value
/// - 数组内联：[v1,v2,v3]
/// - 单空格缩进
/// - 根对象无花括号
/// </summary>
internal static class HjsonText
{
    private const string ListJsonNotArrayError = "顶层必须为数组";
    internal static JsonValue Parse(string text)
    {
        if (text.EndsWith('\n') || text.EndsWith('\r'))
            throw new InvalidDataException("Hjson 文本不得以换行结束");

        return HjsonValue.Parse(text);
    }

    /// <summary>
    /// 将 JSON 文本转换为游戏工程 Hjson 格式。
    /// </summary>
    public static string ToHjsonTextFromJson(string jsonText)
    {
        JsonValue value = Parse(jsonText);
        RemoveNullProperties(value);
        return SimpleJsonWriter.Serialize(value);
    }

    /// <summary>
    /// 将 JsonValue 序列化为游戏工程 Hjson 文本。
    /// </summary>
    internal static string ToHjsonText(JsonValue value)
    {
        return SimpleJsonWriter.Serialize(value);
    }

    private static void RemoveNullProperties(JsonValue value)
    {
        if (value is JsonObject obj)
        {
            List<string> toRemove = [];
            foreach (KeyValuePair<string, JsonValue> kv in obj)
            {
                if (kv.Value == null)
                {
                    toRemove.Add(kv.Key);
                    continue;
                }

                RemoveNullProperties(kv.Value);
            }

            foreach (string key in toRemove)
            {
                obj.Remove(key);
            }
        }
        else if (value is JsonArray arr)
        {
            foreach (JsonValue? item in arr)
            {
                RemoveNullProperties(item);
            }

        }
    }

    private static void AppendListJsonItems(JsonArray arr, List<ListJsonItem> items)
    {
        foreach (JsonValue? element in arr)
        {
            if (element.JsonType == JsonType.String)
            {
                string name = (string)element;
                // 字符串格式 = 内联脚本
                items.Add(new ListJsonItem(name, string.Empty, IsCodeRepo: false, IsPrivate: false));
                continue;
            }

            if (element is JsonObject obj)
            {
                HjsonSchema.EnsureNoUnknownKeys(obj, "list.json 条目", "name", "guid", "private");
                string name = HjsonSchema.RequireString(obj, "name", "list.json 条目");
                string guid = HjsonSchema.RequireString(obj, "guid", "list.json 条目");
                bool isPrivate = HjsonSchema.OptionalBool(obj, "private") == true;

                if (!GuidText.Validate(guid))
                {
                    throw new InvalidDataException("list.json 条目 GUID 无效");
                }

                // 对象格式 = 链接脚本条目，必须携带 GUID。
                items.Add(new ListJsonItem(name, guid, IsCodeRepo: true, IsPrivate: isPrivate));
                continue;
            }

            throw new InvalidDataException("list.json 条目类型无效");
        }
    }

    private static void ParseListItemsCore(string text, List<ListJsonItem> items)
    {
        JsonValue root = Parse(text);
        if (root is not JsonArray arr)
        {
            throw new InvalidDataException(ListJsonNotArrayError);
        }

        AppendListJsonItems(arr, items);
        foreach (ListJsonItem item in items)
        {
#pragma warning disable TK008 // 解析边界统一校验：list.json 条目名称为空是无效数据，拒绝加载
            if (item.Name.Length == 0)
            {
                throw new InvalidDataException("list.json 条目名称为空");
            }
#pragma warning restore TK008

            if (BescriptTextCodec.ReplaceInvalidChar(item.Name.AsSpan()).IndexOf('@') >= 0)
            {
                throw new InvalidDataException("list.json 名称不允许包含需转义字符");
            }
        }
    }

    internal static string BuildListJsonText(IReadOnlyList<ListJsonItem> items)
    {
        JsonArray arr = [];
        foreach (ListJsonItem item in items)
        {
            if (!item.IsCodeRepo)
            {
                // IsCodeRepo 是状态来源；内联项不得携带与该状态矛盾的 GUID 载荷。
#pragma warning disable TK011
                if (item.Guid != string.Empty)
#pragma warning restore TK011
                    throw new InvalidDataException("list.json 内联脚本不允许携带 GUID");

                arr.Add(item.Name);
                continue;
            }

            if (!GuidText.Validate(item.Guid))
                throw new InvalidDataException("list.json 链接脚本 GUID 无效");

            JsonObject obj = [];
            obj["guid"] = item.Guid;
            if (item.IsPrivate)
            {
                obj["private"] = true;
            }
            obj["name"] = item.Name;
            arr.Add(obj);
        }

        return ToHjsonText(arr);
    }

    internal static string? UpdateListGuidByName(string listText, string scriptName, string guid, bool addIfNotFound = false)
    {
        if (!GuidText.Validate(guid))
            throw new ArgumentException("GUID 格式无效", nameof(guid));

        JsonValue root = Parse(listText);
        if (root is not JsonArray arr)
            throw new InvalidDataException("list.json 顶层必须为数组");

        bool found = false;
        for (int i = 0; i < arr.Count; i++)
        {
            JsonValue el = arr[i];
            if (el.JsonType == JsonType.String)
            {
                string name = (string)el;
                if (!string.Equals(name, scriptName, StringComparison.Ordinal))
                    continue;
                found = true;
                arr[i] = new JsonObject { ["guid"] = guid, ["name"] = name };
                break;
            }

            if (el is JsonObject obj)
            {
                if (!obj.TryGetValue("name", out JsonValue? nameProp) || nameProp.JsonType != JsonType.String)
                    continue;
                string name = (string)nameProp;
                if (!string.Equals(name, scriptName, StringComparison.Ordinal))
                    continue;
                found = true;
                obj["guid"] = guid;
                obj["name"] = name;
                break;
            }
        }

        if (!found)
        {
            if (addIfNotFound)
                arr.Add(new JsonObject { ["guid"] = guid, ["name"] = scriptName });
            else
                return null;
        }

        return ToHjsonText(arr);
    }

    public static List<ListJsonItem> ParseListItems(string text)
    {
        List<ListJsonItem> items = [];
        ParseListItemsCore(text, items);
        return items;
    }

    public static List<VarsJsonItem> ParseVarsJsonItems(string text)
    {
        JsonValue root = Parse(text);
        if (root is not JsonArray arr)
            throw new InvalidDataException("顶层必须为数组");

        List<VarsJsonItem> items = [];

        foreach (JsonValue? el in arr)
        {
            if (el is not JsonObject obj)
                throw new InvalidDataException("vars.json 条目必须为对象");

            HjsonSchema.EnsureNoUnknownKeys(obj, "vars.json 条目", "name", "type", "comment");
            string name = HjsonSchema.RequireString(obj, "name", "vars.json 条目");
            if (BescriptTextCodec.ReplaceInvalidChar(name.AsSpan()).IndexOf('@') >= 0)
                throw new InvalidDataException("vars.json 变量名不允许包含需转义字符");

            string typeName = HjsonSchema.RequireString(obj, "type", "vars.json 条目");
            string? comment = HjsonSchema.OptionalString(obj, "comment", "vars.json 条目");

            items.Add(new VarsJsonItem(name, typeName, comment));
        }

        return items;
    }

    public static string BuildVarsJsonText(IReadOnlyList<VarsJsonItem> vars)
    {
        JsonArray arr = [];
        foreach (VarsJsonItem v in vars)
        {
            JsonObject obj = new()
            {
                ["name"] = v.Name,
                ["type"] = v.TypeName
            };
            if (!string.IsNullOrWhiteSpace(v.Comment))
            {
                obj["comment"] = v.Comment;
            }
            arr.Add(obj);
        }

        return ToHjsonText(arr);
    }
}
