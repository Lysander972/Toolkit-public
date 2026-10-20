using System.Globalization;
using Hjson;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// Hjson 补丁规范实现
// =============================================================================
// 设计原则见 docs/JsonPatching_Spec.md
//
// 核心规则：
// 1. 原始文本必须保存，用于还原未建模部分
// 2. 只解析/修改声明的字段，其他部分原样保留
// 3. 禁止任何自动补全/自动修正
// 4. 补丁范围可大可小，但都遵守同一规则
// 5. 局部绝对精准，超出范围不管
//
// 文件处理规则：
// - map.json    : 整体原样保持
// - mech.json   : 解析 list 作为零件索引，整体原样保持
// - device.json : 只解析 ext.logicName/ext.mechFileGUID
// - vars.json   : 整体重建
// - list.json   : 整体重建
// - .guid       : 仅替换 guid 值，保持原格式
// - setting.json: 整体原样保持，只解析 paletteExcel
// =============================================================================

// =============================================================================
// 基础类型
// =============================================================================

/// <summary>
/// 内联数组。序列化时输出为单行 [1,2,3] 格式。
/// </summary>
public class VectorArray : JsonArray
{
    public VectorArray() : base() { }
    public VectorArray(JsonArray source) { foreach (JsonValue? v in source) Add(v); }
    public VectorArray(double[] values) { foreach (double v in values) Add(v); }
}

/// <summary>
/// Hjson 记录基类。
/// 提供原始文本保存 + 关键字段解析的通用模式。
/// </summary>
public abstract record HjsonRecord
{
    /// <summary>
    /// 原始文本。用于还原未建模部分。
    /// </summary>
    protected string RawText { get; private set; } = string.Empty;

    /// <summary>
    /// 空构造。用于新建场景。
    /// </summary>
    protected HjsonRecord() { }

    /// <summary>
    /// 从文本构造。先保存原始文本，再调用解析。
    /// </summary>
    protected HjsonRecord(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        RawText = rawText;
        Parse();
    }

    /// <summary>
    /// 从 JsonObject 构造。保存原始表示，再调用解析。
    /// </summary>
    protected HjsonRecord(JsonObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        RawText = HjsonText.ToHjsonText(obj);
        ParseFromObject(obj);
    }

    /// <summary>
    /// 抽象解析方法。子类实现：从 RawText 提取关键字段。
    /// </summary>
    protected virtual void Parse()
    {
        if (RawText.AsSpan().Trim().IsEmpty) return;
        try
        {
            JsonValue root = HjsonText.Parse(RawText);
            if (root is JsonObject obj)
                ParseFromObject(obj);
        }
        catch
        {
            // 解析失败，保持默认值
        }
    }

    /// <summary>
    /// 从 JsonObject 解析。子类实现：提取关键字段。
    /// 只提取需要修改的字段，其他字段忽略。
    /// </summary>
    protected abstract void ParseFromObject(JsonObject? obj);

    /// <summary>
    /// 抽象序列化方法。子类实现：返回最终文本。
    /// </summary>
    public abstract string ToText();

    /// <summary>
    /// 深拷贝。
    /// </summary>
    public abstract HjsonRecord DeepCopy();
}

// =============================================================================
// 基础解析工具
// =============================================================================

public static class HjsonSchema
{
    // 可选字段
    public static string? OptionalString(JsonObject? obj, string key, string? context = null)
    {
        if (obj == null || !obj.ContainsKey(key))
            return null;
        JsonValue value = obj[key];
        if (value.JsonType != JsonType.String)
        {
            string errorContext = key;
            if (context is not null)
                errorContext = context;
            throw new InvalidDataException($"{errorContext} 必须为字符串");
        }
        return (string)value;
    }

    public static bool? OptionalBool(JsonObject? obj, string key)
    {
        if (obj == null || !obj.ContainsKey(key))
            return null;
        JsonValue value = obj[key];
        if (value.JsonType != JsonType.Boolean)
            throw new InvalidDataException($"字段 {key} 必须为布尔值");
        return (bool)value;
    }

    // 必填字段
    public static int RequireInt(JsonObject? obj, string key, string context)
    {
        if (obj == null || !obj.ContainsKey(key))
            throw new InvalidDataException($"{context} 缺少必填字段: {key}");
        JsonValue value = obj[key];
        if (value.JsonType != JsonType.Number)
            throw new InvalidDataException($"{context}.{key} 必须为数字");
        double d = value.Qd();
        if (d < int.MinValue || d > int.MaxValue || Math.Abs(d - Math.Truncate(d)) > double.Epsilon)
            throw new InvalidDataException($"{context}.{key} 必须为整数");
        return (int)d;
    }

    public static string RequireString(JsonObject? obj, string key, string context)
    {
        if (obj == null || !obj.ContainsKey(key))
            throw new InvalidDataException($"{context} 缺少必填字段: {key}");
        JsonValue value = obj[key];
        if (value.JsonType != JsonType.String)
            throw new InvalidDataException($"{context}.{key} 必须为字符串");
        return (string)value;
    }

    public static JsonArray RequireArrayProperty(JsonObject? obj, string key, string context)
    {
        if (obj == null || !obj.ContainsKey(key))
            throw new InvalidDataException($"{context} 缺少必填字段: {key}");
        if (obj[key] is not JsonArray arr)
            throw new InvalidDataException($"{context}.{key} 必须为数组");
        return arr;
    }

    // 验证
    public static void EnsureNoUnknownKeys(JsonObject? obj, string context, params string[] allowedKeys)
    {
        if (obj == null) return;
        HashSet<string> allowSet = new(allowedKeys, StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonValue> kv in obj)
        {
            if (!allowSet.Contains(kv.Key))
                throw new InvalidDataException($"{context} 存在未定义字段: {kv.Key}");
        }
    }

    // 宽松读取：按目标类型从 JsonValue 转换。
    // Number 优先直取，其他类型走 ToString → TryParse

    public static string CoerceString(JsonValue? value, string context)
    {
        if (value == null)
            throw new InvalidDataException($"{context} 值不能为空");
        if (value.JsonType == JsonType.String)
            return (string)value;
        return value.ToString();
    }

    public static long CoerceLong(JsonValue? value, string context)
    {
        if (value?.JsonType == JsonType.Number)
        {
            double d = value.Qd();
            if (Math.Truncate(d) != d)
                throw new InvalidDataException($"{context} 必须为整数");
            return (long)d;
        }

        if (value != null && long.TryParse(value.ToString(), out long parsed))
            return parsed;
        throw new InvalidDataException($"{context} 必须为整数");
    }

    public static float CoerceFloat(JsonValue? value, string context)
    {
        if (value?.JsonType == JsonType.Number)
            return (float)value.Qd();
        if (value != null && float.TryParse(value.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed))
            return parsed;
        throw new InvalidDataException($"{context} 必须为数值");
    }

    public static bool CoerceBool(JsonValue? value)
    {
        if (value?.JsonType == JsonType.Boolean)
            return (bool)value;
        return value?.ToString() == "true";
    }

    // 克隆
    public static JsonObject CloneObject(JsonObject source)
    {
        JsonObject clone = [];
        foreach (KeyValuePair<string, JsonValue> kv in source)
            clone[kv.Key] = CloneValue(kv.Value);
        return clone;
    }

    public static JsonArray CloneArray(JsonArray source)
    {
        if (source is VectorArray)
            return new VectorArray(source);
        JsonArray clone = [];
        foreach (JsonValue? value in source)
            clone.Add(CloneValue(value));
        return clone;
    }

    public static JsonValue? CloneValue(JsonValue? value)
    {
        if (value is null) return null;
        return value.JsonType switch
        {
            JsonType.Object => CloneObject((JsonObject)value),
            JsonType.Array => CloneArray((JsonArray)value),
            JsonType.String => (string)value,
            JsonType.Boolean => (bool)value,
            JsonType.Number => value.Qd(),
            _ => value.Qstr()
        };
    }
}

// =============================================================================
// 配置类型（均继承 HjsonRecord）
// =============================================================================

// -----------------------------------------------------------------------------
// MapJsonRecord: map.json（整体原样保持）
// -----------------------------------------------------------------------------

/// <summary>
/// map.json 记录。整体原样保持，不解析任何字段。
/// </summary>
public sealed record MapJsonRecord : HjsonRecord
{
    public MapJsonRecord() : base() { }
    public MapJsonRecord(string rawText) : base(rawText) { }

    protected override void ParseFromObject(JsonObject? obj) { }

    public override string ToText() => RawText;

    public override HjsonRecord DeepCopy() => new MapJsonRecord(RawText);
}

// -----------------------------------------------------------------------------
// MechJsonRecord: mech.json（解析 list 作为零件权威索引，整体原样保持）
// 原版已知行为：删零件时只删 mech.json 条目不删目录，
// 因此 mech.json 条目数可能少于磁盘零件目录数。
// -----------------------------------------------------------------------------

/// <summary>
/// mech.json 记录。解析 list 中的零件索引，其他内容原样保留。
/// </summary>
public sealed record MechJsonRecord : HjsonRecord
{
    private readonly List<MechJsonDeviceEntry> _deviceEntries = [];

    public IReadOnlyList<MechJsonDeviceEntry> DeviceEntries => _deviceEntries;

    public MechJsonRecord() : base() { }
    public MechJsonRecord(string rawText) : base(rawText) { }

    protected override void Parse()
    {
        JsonValue root = HjsonText.Parse(RawText);
        if (root is not JsonObject obj)
            throw new InvalidDataException("mech.json 根节点必须为对象");
        ParseFromObject(obj);
    }

    protected override void ParseFromObject(JsonObject? obj)
    {
        JsonArray list = HjsonSchema.RequireArrayProperty(obj, "list", "mech.json");
        ReadEntries(list);
    }

    public override string ToText() => RawText;

    private void ReadEntries(JsonArray list)
    {
        foreach (JsonValue? item in list)
        {
            if (item is JsonObject childObject)
            {
                JsonArray childList = HjsonSchema.RequireArrayProperty(childObject, "list", "mech.json.list[]");
                ReadEntries(childList);
                continue;
            }

            if (item.JsonType != JsonType.String)
            {
                throw new InvalidDataException("mech.json.list 只能包含'ID-类型'字符串或带 list 的对象");
            }

            string text = (string)item;
            int separatorIndex = text.IndexOf('-', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= text.Length - 1)
            {
                throw new InvalidDataException($"mech.json.list 条目格式错误: {text}");
            }

            string deviceIdText = text[..separatorIndex];
            string deviceTypeText = text[(separatorIndex + 1)..];
            if (!int.TryParse(deviceIdText, out int deviceId))
            {
                throw new InvalidDataException($"mech.json.list 条目中的零件ID无效: {text}");
            }
            if (!int.TryParse(deviceTypeText, out int deviceType))
            {
                throw new InvalidDataException($"mech.json.list 条目中的零件类型无效: {text}");
            }

            _deviceEntries.Add(new MechJsonDeviceEntry(deviceId, deviceType));
        }
    }

    public override HjsonRecord DeepCopy() => new MechJsonRecord(RawText);
}

public sealed record MechJsonDeviceEntry(int DeviceId, int DeviceType);

// -----------------------------------------------------------------------------
// VarsConfig: vars.json（整体重建）
// -----------------------------------------------------------------------------

/// <summary>
/// 变量配置（vars.json）。整体重建。
/// 原版格式：根是数组，字段为 name/type/comment
/// </summary>
public sealed record VarsConfig : HjsonRecord
{
    public List<VarInfo> Variables { get; set; } = [];

    public VarsConfig() : base() { }
    public VarsConfig(string rawText) : base(rawText) { }

    protected override void Parse()
    {
        // vars.json 根是数组，不是对象
        if (RawText.AsSpan().Trim().IsEmpty) return;

        try
        {
            JsonValue root = HjsonText.Parse(RawText);
            if (root is JsonArray arr)
            {
                foreach (JsonValue? item in arr)
                {
                    if (item is not JsonObject itemObj)
                        continue;
                    string? name = HjsonSchema.OptionalString(itemObj, "name");
                    // 兼容 type 和 typeName 两种字段名
                    string? typeName = HjsonSchema.OptionalString(itemObj, "type");
                    if (name == null || typeName == null)
                        continue;
                    string? comment = HjsonSchema.OptionalString(itemObj, "comment");
                    Variables.Add(new VarInfo(name, typeName, comment));
                }
            }
        }
        catch
        {
            // 解析失败，保持默认值
        }
    }

    protected override void ParseFromObject(JsonObject? obj) { }

    public override string ToText()
    {
        // 原版格式：根是数组，字段为 name/type
        JsonArray arr = [];
        foreach (VarInfo v in Variables)
        {
            JsonObject item = new()
            {
                ["name"] = v.Name,
                ["type"] = v.TypeName
            };
            if (v.Comment != null)
                item["comment"] = v.Comment;
            arr.Add(item);
        }
        return HjsonText.ToHjsonText(arr);
    }

    /// <summary>
    /// 是否需要写文件。原来不存在且列表为空时不创建文件。
    /// </summary>
    public bool ShouldWrite => Variables.Count > 0 || RawText.AsSpan().Trim().Length > 0;

    public override HjsonRecord DeepCopy()
    {
        VarsConfig copy = new(RawText);
        copy.Variables.Clear();
        foreach (VarInfo v in Variables)
            copy.Variables.Add(v);
        return copy;
    }
}

/// <summary>
/// 变量信息。
/// </summary>
public sealed record VarInfo(string Name, string TypeName, string? Comment);

// -----------------------------------------------------------------------------
// DeviceJsonRecord: device.json（只解析 ext.logicName/ext.mechFileGUID/stickToBone）
// -----------------------------------------------------------------------------

/// <summary>
/// device.json 记录。只解析 type、ext.logicName、ext.mechFileGUID、stickToBone，整体原样保持。
/// </summary>
public sealed record DeviceJsonRecord : HjsonRecord
{
    /// <summary>零件类型</summary>
    public int DeviceType { get; private set; }

    /// <summary>逻辑名称（ext.logicName）</summary>
    public JsonValue? LogicNameValue { get; private set; }

    /// <summary>机械文件 GUID（ext.mechFileGUID）</summary>
    public string? MechFileGuid { get; private set; }

    /// <summary>
    /// 骨骼绑定字段（顶层 stickToBone）。引擎原版仅在非空时序列化。
    /// 用于判断骨骼绑定状态和相关零件能力。
    /// </summary>
    public string? StickToBone { get; private set; }

    public DeviceJsonRecord() : base() { }
    public DeviceJsonRecord(string rawText) : base(rawText) { }

    protected override void ParseFromObject(JsonObject? obj)
    {
        if (obj == null) return;

        // 解析 type
        DeviceType = HjsonSchema.RequireInt(obj, "type", "device.json");

        // 解析 stickToBone（顶层，可选；引擎仅在非空时写入）
        StickToBone = HjsonSchema.OptionalString(obj, "stickToBone");

        // 解析 ext（可选：无 ext 时 logicName 视为空、mechFileGUID 视为 null）
        if (obj.ContainsKey("ext") && obj["ext"] is JsonObject extObj)
        {
            if (extObj.ContainsKey("logicName"))
            {
                JsonValue logicNameValue = extObj["logicName"];
                if (logicNameValue.JsonType == JsonType.String || logicNameValue.JsonType == JsonType.Number)
                    LogicNameValue = HjsonSchema.CloneValue(logicNameValue);
            }
            MechFileGuid = HjsonSchema.OptionalString(extObj, "mechFileGUID");
        }
    }

    public override string ToText() => RawText;

    public override HjsonRecord DeepCopy() => new DeviceJsonRecord(RawText);
}

// -----------------------------------------------------------------------------
// ListJsonRecord: list.json（脚本列表，整体重建）
// -----------------------------------------------------------------------------

/// <summary>
/// list.json 记录。脚本列表，整体重建。
/// 支持两种格式：
/// - 字符串：内联脚本名称
/// - 对象：{ guid: "xxx", name: "脚本名" } (CodeRepo 引用)
/// </summary>
public sealed record ListJsonRecord : HjsonRecord
{
    public List<ListJsonEntry> Entries { get; } = [];

    public ListJsonRecord() : base() { }
    public ListJsonRecord(string rawText) : base(rawText) { }

    protected override void Parse()
    {
        foreach (ListJsonItem item in HjsonText.ParseListItems(RawText))
        {
            if (!item.IsCodeRepo)
            {
                Entries.Add(ListJsonEntry.Inline(item.Name));
                continue;
            }

            Entries.Add(ListJsonEntry.CodeRepo(item.Name, item.Guid));
        }
    }

    protected override void ParseFromObject(JsonObject? obj) { }

    public override string ToText()
    {
        JsonArray arr = [];
        foreach (ListJsonEntry entry in Entries)
        {
            if (entry.IsCodeRepo)
            {
                if (!GuidText.Validate(entry.Guid))
                    throw new InvalidDataException("list.json 代码仓库条目 GUID 无效");

                arr.Add(new JsonObject
                {
                    ["guid"] = entry.Guid,
                    ["name"] = entry.Name
                });
            }
            else
                arr.Add(entry.Name);
        }
        return HjsonText.ToHjsonText(arr);
    }

    public override HjsonRecord DeepCopy()
    {
        ListJsonRecord copy = new(RawText);
        copy.Entries.Clear();
        foreach (ListJsonEntry entry in Entries)
            copy.Entries.Add(entry with { });
        return copy;
    }
}

/// <summary>
/// list.json 条目。
/// </summary>
/// <param name="Name">脚本名称</param>
/// <param name="Guid">CodeRepo GUID（内联脚本为 null）</param>
/// <param name="IsCodeRepo">是否为 CodeRepo 引用</param>
public sealed record ListJsonEntry(string Name, string Guid, bool IsCodeRepo)
{
    /// <summary>
    /// 创建内联脚本条目。
    /// </summary>
    public static ListJsonEntry Inline(string name) => new(name, string.Empty, false);

    /// <summary>
    /// 创建 CodeRepo 引用条目。
    /// </summary>
    public static ListJsonEntry CodeRepo(string name, string guid) => new(name, guid, true);
}

// -----------------------------------------------------------------------------
// GuidJsonRecord: .guid 文件（仅替换 guid 值，保持原格式）
// -----------------------------------------------------------------------------

/// <summary>
/// GUID 文件记录。仅替换 guid 值，保持原格式。
/// 原版格式：
/// - 纯 GUID：32位十六进制字符串（整文件只有 GUID）
/// - 键值对：guid:xxx 后面可能还有其他字段如 quality:80
/// </summary>
public sealed record GuidJsonRecord : HjsonRecord
{
    /// <summary>GUID 值</summary>
    public string Guid { get; private set; } = string.Empty;

    /// <summary>是否为键值对格式（有 guid: 前缀）</summary>
    public bool HasKeyValueFormat { get; private set; }

    /// <summary>九宫格 slice 值（可选）。非 null 时写盘必带 slice 行。</summary>
    public string? Slice { get; private set; }
    private string _originalGuid = string.Empty;
    private string? _originalSlice;
    private GuidFile.Content? _content;

    public GuidJsonRecord() : base() { }
    public GuidJsonRecord(string rawText) : base(rawText) { }

    protected override void Parse()
    {
        _content = GuidFile.Parse(RawText);
        Guid = _content.Guid;
        HasKeyValueFormat = _content.Object is not null;
        Slice = _content.Slice;
        _originalGuid = Guid;
        _originalSlice = Slice;
    }

    protected override void ParseFromObject(JsonObject? obj) { }

    /// <summary>
    /// 设置新的 GUID 值。
    /// </summary>
    public void SetGuid(string newGuid)
    {
        if (!GuidText.Validate(newGuid))
            throw new ArgumentException("GUID 格式无效", nameof(newGuid));
        Guid = newGuid;
    }

    /// <summary>
    /// 设置九宫格 slice。格式为原版事实：8 个整数十进制像素内缩 "bL,bR,bB,bT,rL,rR,rB,rT"，
    /// 写盘为带引号字符串行 slice:"..."。
    /// </summary>
    public void SetSlice(string slice)
    {
        Slice = slice;
    }

    public override string ToText()
    {
        if (Guid == _originalGuid && Slice == _originalSlice)
            return RawText;

        if (!HasKeyValueFormat && Slice == null)
            return Guid;

        JsonObject obj = _content is GuidFile.Content { Object: JsonObject objectValue }
            ? objectValue
            : [];
        obj["guid"] = Guid;
        if (Slice != null)
            obj["slice"] = Slice;
        return HjsonText.ToHjsonText(obj);
    }

    public override HjsonRecord DeepCopy() => new GuidJsonRecord(RawText) { Guid = Guid, HasKeyValueFormat = HasKeyValueFormat, Slice = Slice };

}

// -----------------------------------------------------------------------------
// SettingJsonRecord: setting.json（整体原样保持，只解析引用字段）
// -----------------------------------------------------------------------------

/// <summary>
/// setting.json 记录。整体原样保持，只解析文件引用字段。
/// </summary>
public sealed record SettingJsonRecord : HjsonRecord
{
    public string? PaletteExcel { get; private set; }

    /// <summary>语言表/翻译表 GUID（原版 setting.json 键 "languageExcel"）</summary>
    public string? LanguageExcel { get; private set; }

    /// <summary>封面图 GUID（原版 setting.json 键 "coverPic"）</summary>
    public string? CoverPic { get; private set; }

    public SettingJsonRecord() : base() { }
    public SettingJsonRecord(string rawText) : base(rawText) { }

    protected override void ParseFromObject(JsonObject? obj)
    {
        PaletteExcel = HjsonSchema.OptionalString(obj, "paletteExcel");
        LanguageExcel = HjsonSchema.OptionalString(obj, "languageExcel");
        CoverPic = HjsonSchema.OptionalString(obj, "coverPic");
    }

    public override string ToText() => RawText;

    public override HjsonRecord DeepCopy() => new SettingJsonRecord(RawText);
}
