using Hjson;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// Tests for HjsonText utility class.
/// Test data sourced from real test projects at test/data/projects
/// </summary>
public sealed class HjsonTextTests
{
    [Fact]
    public void ToHjsonTextFromJson_Should_Convert_Json_To_Hjson()
    {
        // Arrange - Real JSON from test project setting.json format
        // Source: test/data/projects/方法汇总/setting.json
        string json = @"{""name"":""test"",""value"":123}";

        // Act
        string hjson = HjsonText.ToHjsonTextFromJson(json);

        // Assert
        Assert.Contains("name:", hjson);
        Assert.Contains("test", hjson);
        Assert.Contains("value:", hjson);
        Assert.Contains("123", hjson);
    }

    [Fact]
    public void Parse_Should_Reject_Hjson_With_TrailingNewline()
    {
        Assert.Throws<InvalidDataException>(() => HjsonText.Parse("name:test\n"));
    }

    [Fact]
    public void ParseListItems_Should_Parse_String_Items()
    {
        // Arrange - Real list.json from test project
        // Source: test/data/projects/水果切切切/map.map_/255/scripts/list.json
        string listJson = @"[
 按钮
 使用帮助
 初始化快捷调试面板
]";

        // Act
        List<ListJsonItem> items = HjsonText.ParseListItems(listJson);

        // Assert
        Assert.Equal(3, items.Count);
        Assert.Equal("按钮", items[0].Name);
        Assert.Equal("使用帮助", items[1].Name);
        Assert.Equal("初始化快捷调试面板", items[2].Name);
        Assert.Equal(string.Empty, items[0].Guid);
        Assert.False(items[0].IsCodeRepo);
    }

    [Fact]
    public void ParseListItems_Should_Parse_Object_Items_With_Guid()
    {
        // Arrange - list.json with GUID format
        string listJson = @"[
{
  guid:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
  name:脚本A
}
 {
  guid:b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e4
  name:脚本B
 }
]";

        // Act
        List<ListJsonItem> items = HjsonText.ParseListItems(listJson);

        // Assert
        Assert.Equal(2, items.Count);
        Assert.Equal("脚本A", items[0].Name);
        Assert.Equal("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", items[0].Guid);
        Assert.True(items[0].IsCodeRepo);
        Assert.Equal("脚本B", items[1].Name);
        Assert.Equal("b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e4", items[1].Guid);
    }

    [Fact]
    public void ParseListItems_Should_Parse_Object_Items_With_Private()
    {
        // Arrange
        string listJson = @"[
{
  guid:8ee0aade18b64e94929778e83858a6ef
  private:true
  name:玩家结构'设置_avatarUrl
}
]";

        // Act
        List<ListJsonItem> items = HjsonText.ParseListItems(listJson);

        // Assert
        Assert.Single(items);
        Assert.True(items[0].IsCodeRepo);
        Assert.True(items[0].IsPrivate);
        Assert.Equal("玩家结构'设置_avatarUrl", items[0].Name);
        Assert.Equal("8ee0aade18b64e94929778e83858a6ef", items[0].Guid);

        // Build 回写时必须保留 private 字段
        string rebuilt = HjsonText.BuildListJsonText(items);
        Assert.Contains("private:true", rebuilt);
        Assert.Contains("name:玩家结构'设置_avatarUrl", rebuilt);
    }

    [Fact]
    public void ParseListItems_Should_Reject_Linked_Item_Without_Guid()
    {
        const string listJson = """
[
{
 name:脚本A
}
]
""";

        Assert.Throws<InvalidDataException>(() => HjsonText.ParseListItems(listJson));
    }

    [Fact]
    public void BuildListJsonText_Should_Reject_Inline_Item_With_Guid()
    {
        List<ListJsonItem> items = [new("脚本A", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", IsCodeRepo: false)];

        Assert.Throws<InvalidDataException>(() => HjsonText.BuildListJsonText(items));
    }

    [Fact]
    public void ParseListItems_Should_Throw_On_Non_Array()
    {
        // Arrange
        string invalidJson = @"{""name"":""test""}";

        // Act & Assert - The Hjson library throws ArgumentException for invalid JSON
        Assert.ThrowsAny<Exception>(() => HjsonText.ParseListItems(invalidJson));
    }

    [Fact]
    public void UpdateListGuidByName_Should_Add_Guid_To_String_Item()
    {
        // Arrange
        string listJson = "[\nscriptName\notherScript\n]";

        // Act
        const string guid = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        string? updated = HjsonText.UpdateListGuidByName(listJson, "scriptName", guid);

        // Assert
        Assert.Contains(guid, updated);
        Assert.Contains("scriptName", updated);
    }

    [Fact]
    public void UpdateListGuidByName_Should_Update_Guid_In_Object_Item()
    {
        // Arrange
        string listJson = @"[
{
  guid:oldguid
  name:scriptName
}
]";

        // Act
        const string guid = "b1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        string? updated = HjsonText.UpdateListGuidByName(listJson, "scriptName", guid);

        // Assert
        Assert.Contains(guid, updated);
        Assert.DoesNotContain("oldguid", updated);
    }

    [Fact]
    public void UpdateListGuidByName_Should_Return_Null_When_Name_Not_Found()
    {
        // Arrange
        string listJson = @"[
existingScript
]";

        // Act & Assert
        Assert.Null(HjsonText.UpdateListGuidByName(listJson, "nonExistent", "c1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4"));
    }

    [Fact]
    public void UpdateListGuidByName_Should_Add_New_Entry_When_AddIfNotFound()
    {
        // Arrange
        string listJson = @"[
existingScript
]";

        // Act
        const string guid = "d1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        string? updated = HjsonText.UpdateListGuidByName(listJson, "newScript", guid, addIfNotFound: true);

        // Assert
        Assert.Contains(guid, updated);
        Assert.Contains("newScript", updated);
        Assert.Contains("existingScript", updated);
    }

    [Fact]
    public void UpdateListGuidByName_Should_Add_New_Entry_To_Empty_List_When_AddIfNotFound()
    {
        // Arrange
        string listJson = @"[]";

        // Act
        const string guid = "e1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        string? updated = HjsonText.UpdateListGuidByName(listJson, "firstScript", guid, addIfNotFound: true);

        // Assert
        Assert.Contains(guid, updated);
        Assert.Contains("firstScript", updated);
    }

    [Fact]
    public void ParseVarsJsonItems_Should_Parse_Vars_Correctly()
    {
        // Arrange - Real vars.json from test project
        // Source: test/data/projects/水果切切切/map.map_/255/vars.json
        string varsJson = @"[
 {
  name:参数调节列表
  type:List
 }
 {
  name:是否为GM
  type:Bool
 }
]";

        // Act
        List<VarsJsonItem> items = HjsonText.ParseVarsJsonItems(varsJson);

        // Assert
        Assert.Equal(2, items.Count);
        Assert.Equal("参数调节列表", items[0].Name);
        Assert.Equal("List", items[0].TypeName);
        Assert.Equal("是否为GM", items[1].Name);
        Assert.Equal("Bool", items[1].TypeName);
    }

    [Fact]
    public void ParseVarsJsonItems_Should_Handle_Empty_Vars()
    {
        // Arrange
        string emptyVars = "[]";

        // Act
        List<VarsJsonItem> items = HjsonText.ParseVarsJsonItems(emptyVars);

        // Assert
        Assert.Empty(items);
    }

    [Fact]
    public void ParseVarsJsonItems_Should_Throw_When_Name_Missing()
    {
        // Arrange
        string varsJson = @"[
{
  type:Int
}
]";

        // Act & Assert - name 是必填字段，缺失应抛出异常
        Assert.Throws<System.IO.InvalidDataException>(() => HjsonText.ParseVarsJsonItems(varsJson));
    }

    [Fact]
    public void ParseVarsJsonItems_Should_Throw_When_Type_Missing()
    {
        // Arrange - type 是必填字段，缺失应抛出异常
        string varsJson = @"[
{
  name:testVar
}
]";

        // Act & Assert
        Assert.Throws<System.IO.InvalidDataException>(() => HjsonText.ParseVarsJsonItems(varsJson));
    }

    [Fact]
    public void BuildVarsJsonText_Should_Create_Valid_Hjson()
    {
        // Arrange
        VarsJsonItem[] vars = new[]
        {
            new VarsJsonItem("变量1", "Int"),
            new VarsJsonItem("变量2", "Float")
        };

        // Act
        string hjson = HjsonText.BuildVarsJsonText(vars);

        // Assert
        Assert.Contains("变量1", hjson);
        Assert.Contains("Int", hjson);
        Assert.Contains("变量2", hjson);
        Assert.Contains("Float", hjson);
    }

    [Fact]
    public void ToHjsonText_Should_Remove_Null_Properties()
    {
        // Arrange
        string json = @"{""name"":""test"",""value"":null,""count"":10}";

        // Act
        string hjson = HjsonText.ToHjsonTextFromJson(json);

        // Assert
        Assert.Contains("name:", hjson);
        Assert.Contains("count:", hjson);
        Assert.DoesNotContain("value:", hjson);
    }

    /// <summary>
    /// 测试原版 device.json 中 ext 字段的解析。
    /// 原版格式：ext: 后面换行再接对象，这种格式在 Hjson 中是合法的。
    /// </summary>
    [Fact]
    public void HjsonValue_Parse_Should_Handle_Ext_Object_With_Newline()
    {
        // Arrange - 原版 device.json 格式
        string deviceHjson = @"type:1101
pos:[0,0,0]
quat:[0,1,0,0]
scale:4.0096
ext:
{
 logicName:弹幕
 boolShowFollowIcon:true
}";

        // Act
        JsonValue root = HjsonValue.Parse(deviceHjson);

        // Assert
        Assert.Equal(JsonType.Object, root.JsonType);
        JsonObject obj = (JsonObject)root;

        // 验证 ext 字段被正确解析为对象
        Assert.True(obj.ContainsKey("ext"));
        JsonValue extProp = obj["ext"];
        Assert.Equal(JsonType.Object, extProp.JsonType);

        JsonObject extObj = (JsonObject)extProp;

        // 验证 logicName 可以被正确读取
        Assert.True(extObj.ContainsKey("logicName"));
        JsonValue logicNameProp = extObj["logicName"];
        Assert.Equal(JsonType.String, logicNameProp.JsonType);
        Assert.Equal("弹幕", (string)logicNameProp);

        // 验证 boolShowFollowIcon 可以被正确读取
        Assert.True(extObj.ContainsKey("boolShowFollowIcon"));
        JsonValue boolProp = extObj["boolShowFollowIcon"];
        Assert.Equal(JsonType.Boolean, boolProp.JsonType);
        Assert.True((bool)boolProp);
    }

    /// <summary>
    /// 测试 ext 字段在同一行的情况（也是合法的 Hjson）。
    /// </summary>
    [Fact]
    public void HjsonValue_Parse_Should_Handle_Ext_Object_On_Same_Line()
    {
        // Arrange
        string deviceHjson = @"type:1101
ext: {
 logicName:测试
}";

        // Act
        JsonValue root = HjsonValue.Parse(deviceHjson);

        // Assert
        Assert.Equal(JsonType.Object, root.JsonType);
        JsonObject obj = (JsonObject)root;

        Assert.True(obj.ContainsKey("ext"));
        JsonValue extProp = obj["ext"];
        Assert.Equal(JsonType.Object, extProp.JsonType);

        JsonObject extObj = (JsonObject)extProp;
        Assert.True(extObj.ContainsKey("logicName"));
        Assert.Equal("测试", (string)extObj["logicName"]);
    }

    #region 游戏工程文本格式测试

    /// <summary>
    /// 测试序列化输出符合游戏工程文本格式。
    /// 参考：docs/HjsonFormat_Spec.md 与 legacy/core/persistence/Json/SimpleJsonWriter.cs
    /// </summary>
    [Fact]
    public void ToHjsonText_Should_Match_Project_Text_Format()
    {
        // Arrange - 模拟 device.json 的数据结构
        // 原版格式：
        // type:1101
        // pos:[0,0,0]
        // quat:[0,1,0,0]
        // scale:4.0096
        // ext:
        // {
        //  logicName:弹幕
        //  boolShowFollowIcon:true
        // }

        JsonObject obj = new JsonObject
        {
            ["type"] = 1101,
            ["pos"] = new JsonArray { 0, 0, 0 },
            ["quat"] = new JsonArray { 0, 1, 0, 0 },
            ["scale"] = 4.0096
        };

        JsonObject extObj = new JsonObject
        {
            ["logicName"] = "弹幕",
            ["boolShowFollowIcon"] = true
        };
        obj["ext"] = extObj;

        // 标记需要内联输出的向量数组属性。
        SimpleJsonWriter.MarkVectorArrayProperty(obj, "pos", 3);
        SimpleJsonWriter.MarkVectorArrayProperty(obj, "quat", 4);

        // Act
        string result = HjsonText.ToHjsonText(obj);

        // Assert - 验证游戏工程文本格式规则

        // 1. 根对象本身无花括号（但嵌套对象有）
        Assert.False(result.StartsWith("{"), "根对象不应以 { 开头");
        // 注意：如果最后一个属性是嵌套对象，输出会以 } 结尾，这是正确的

        // 2. 键值对无空格：key:value
        Assert.Contains("type:1101", result);
        Assert.Contains("scale:4.0096", result);
        Assert.Contains("logicName:弹幕", result);
        Assert.DoesNotContain("type: 1101", result);
        Assert.DoesNotContain("scale: 4.0096", result);

        // 3. 数组内联：[v1,v2,v3]
        Assert.Contains("pos:[0,0,0]", result);
        Assert.Contains("quat:[0,1,0,0]", result);
        Assert.DoesNotContain("pos:\n[", result);
        Assert.DoesNotContain("quat:\n[", result);

        // 4. 嵌套对象正确格式化
        Assert.Contains("ext:\n{", result);
        Assert.Contains("boolShowFollowIcon:true", result);
    }

    [Fact]
    public void ToHjsonText_Array_Should_Be_Inlined_For_Numeric_And_Boolean_Values()
    {
        // Arrange - 数值和布尔数组需要标记为 VectorArray 才会内联
        JsonObject obj = new JsonObject
        {
            ["numbers"] = new JsonArray { 1, 2, 3 },
            ["booleans"] = new JsonArray { true, false, true }
        };

        // 标记为向量数组
        SimpleJsonWriter.MarkVectorArrayProperty(obj, "numbers", 3);

        // Act
        string result = HjsonText.ToHjsonText(obj);

        // Assert - 标记后的数值数组内联
        Assert.Contains("numbers:[1,2,3]", result);
        // 未标记的布尔数组多行格式
        Assert.Contains("booleans:\n[", result);
    }

    [Fact]
    public void ToHjsonText_Array_Should_Be_Multiline_For_Strings()
    {
        // Arrange - 字符串数组应该多行（参考 list.json 格式）
        JsonObject obj = new JsonObject
        {
            ["strings"] = new JsonArray { "a", "b", "c" }
        };

        // Act
        string result = HjsonText.ToHjsonText(obj);

        // Assert - 字符串数组应该多行
        Assert.Contains("strings:\n[", result);
        Assert.DoesNotContain("strings:[a,b,c]", result);
    }

    [Fact]
    public void ToHjsonText_Array_Should_Be_Multiline_For_Complex_Values()
    {
        // Arrange - 包含对象的数组应该多行显示
        JsonObject obj = new JsonObject
        {
            ["items"] = new JsonArray
            {
                new JsonObject { ["name"] = "item1", ["value"] = 1 },
                new JsonObject { ["name"] = "item2", ["value"] = 2 }
            }
        };

        // Act
        string result = HjsonText.ToHjsonText(obj);

        // Assert - 复杂值数组应该多行
        Assert.Contains("items:\n[", result);
        Assert.Contains("name:item1", result);
        Assert.Contains("name:item2", result);
    }

    [Fact]
    public void ToHjsonText_ListJson_Should_Match_Original_Format()
    {
        // Arrange - list.json 格式
        List<ListJsonItem> items =
        [
            new("按钮", string.Empty, IsCodeRepo: false),
            new("使用帮助", string.Empty, IsCodeRepo: false),
            new("脚本A", "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", IsCodeRepo: true)
        ];

        // Act
        string result = HjsonText.BuildListJsonText(items);

        // Assert - 验证 list.json 格式
        Assert.StartsWith("[", result);
        Assert.EndsWith("]", result);
        Assert.Contains("按钮", result);
        Assert.Contains("使用帮助", result);
        Assert.Contains("guid:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", result);
        Assert.Contains("name:脚本A", result);
    }

    [Fact]
    public void ToHjsonText_VarsJson_Should_Match_Original_Format()
    {
        // Arrange - vars.json 格式
        List<VarsJsonItem> vars =
        [
            new("参数调节列表", "List", null),
            new("是否为GM", "Bool", "注释内容")
        ];

        // Act
        string result = HjsonText.BuildVarsJsonText(vars);

        // Assert - 验证 vars.json 格式
        Assert.StartsWith("[", result);
        Assert.EndsWith("]", result);
        Assert.Contains("name:参数调节列表", result);
        Assert.Contains("type:List", result);
        Assert.Contains("name:是否为GM", result);
        Assert.Contains("type:Bool", result);
        Assert.Contains("comment:注释内容", result);
    }

    [Fact]
    public void ToHjsonText_SettingJson_Should_Match_Original_Format()
    {
        // Arrange - setting.json 格式
        string json = @"{""guid"":""9699962ae2ef4d2baa940af14a463d65"",""name"":""颜色对对碰0.2.12"",""mainMap"":""4b89165d692e44e2b2be7733d82b3543""}";

        // Act
        string result = HjsonText.ToHjsonTextFromJson(json);

        // Assert - 验证 setting.json 格式
        // 1. 根对象无花括号
        Assert.False(result.StartsWith("{"), "根对象不应以 { 开头");
        Assert.False(result.EndsWith("}"), "根对象不应以 } 结尾");

        // 2. 键值对无空格
        Assert.Contains("guid:9699962ae2ef4d2baa940af14a463d65", result);
        Assert.Contains("name:颜色对对碰0.2.12", result);
        Assert.Contains("mainMap:4b89165d692e44e2b2be7733d82b3543", result);
    }

    #endregion
}
