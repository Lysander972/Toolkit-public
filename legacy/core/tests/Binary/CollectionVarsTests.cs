using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Binary;

/// <summary>
/// Tests for collection variable types (ListVar, DictVar, StructVar).
/// Test data based on real project collection patterns.
/// </summary>
public sealed class CollectionVarsTests
{
    #region ListVar

    [Fact]
    public void ListVar_Default_Value()
    {
        // Arrange & Act
        ListVar lv = new ListVar();

        // Assert
        Assert.Equal(VarType.List, lv.type);
        Assert.Empty(lv.v);
    }

    [Fact]
    public void ListVar_Add_Items()
    {
        // Arrange
        ListVar lv = new ListVar();

        // Act
        lv.v.Add(new FloatVar { v = 1.5f });
        lv.v.Add(new StringVar { v = "test" });

        // Assert
        Assert.Equal(2, lv.v.Count);
        Assert.Equal(1.5f, ((FloatVar)lv.v[0]).v, 2);
        Assert.Equal("test", ((StringVar)lv.v[1]).v);
    }

    [Fact]
    public void ListVar_ToString_Format()
    {
        // Arrange
        ListVar lv = new ListVar();
        lv.v.Add(new BoolVar { v = true });
        lv.v.Add(new FloatVar { v = 2.5f });

        // Act
        string result = lv.ToString();

        // Assert
        Assert.Equal("List[2]", result);
    }

    [Fact]
    public void ListVar_Save_And_Load_Binary()
    {
        // Arrange
        ListVar original = new ListVar();
        original.v.Add(new FloatVar { v = 1.0f });
        original.v.Add(new BoolVar { v = true });

        using BinWriter writer = new BinWriter(50);

        // Act
        original.SaveToBinary(writer, 0);
        byte[] data = writer.ToArray();

        // Assert
        ListVar loaded = new ListVar();
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        loaded.LoadFromBinary(span, 1); // Skip type

        Assert.Equal(2, loaded.v.Count);
        Assert.Equal(1.0f, ((FloatVar)loaded.v[0]).v, 5);
        Assert.True(((BoolVar)loaded.v[1]).v);
    }

    #endregion

    #region DictVar

    [Fact]
    public void DictVar_Default_Value()
    {
        // Arrange & Act
        DictVar dv = new DictVar();

        // Assert
        Assert.Equal(VarType.Dict, dv.type);
        Assert.Equal(0, dv.Count);
    }

    [Fact]
    public void DictVar_Add_Items()
    {
        // Arrange
        DictVar dv = new DictVar();

        // Act
        dv[new zchar("key1")] = new FloatVar { v = 1.5f };
        dv[new zchar("key2")] = new StringVar { v = "value" };

        // Assert
        Assert.Equal(2, dv.Count);
        Assert.Equal(1.5f, ((FloatVar)dv[new zchar("key1")]).v, 2);
        Assert.Equal("value", ((StringVar)dv[new zchar("key2")]).v);
    }

    [Fact]
    public void DictVar_ToString_Format()
    {
        // Arrange
        DictVar dv = new DictVar();
        dv[new zchar("a")] = new BoolVar { v = true };

        // Act
        string result = dv.ToString();

        // Assert
        Assert.Equal("Dict[1]", result);
    }

    [Fact]
    public void DictVar_Save_And_Load_Binary()
    {
        // Arrange
        DictVar original = new DictVar();
        original[new zchar("score")] = new FloatVar { v = 100.0f };
        original[new zchar("name")] = new StringVar { v = "test" };

        using BinWriter writer = new BinWriter(100);

        // Act
        original.SaveToBinary(writer, 0);
        byte[] data = writer.ToArray();

        // Assert
        DictVar loaded = new DictVar();
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        loaded.LoadFromBinary(span, 1); // Skip type

        Assert.Equal(2, loaded.Count);
        Assert.Equal(100.0f, ((FloatVar)loaded[new zchar("score")]).v, 5);
        Assert.Equal("test", ((StringVar)loaded[new zchar("name")]).v);
    }

    #endregion

    #region StructVar

    [Fact]
    public void StructVar_Default_Value()
    {
        // Arrange & Act
        StructVar sv = new StructVar();

        // Assert
        Assert.Equal(VarType.Struct, sv.type);
        Assert.Empty(sv.v);
    }

    [Fact]
    public void StructVar_Set_Fields()
    {
        // Arrange
        StructVar sv = new StructVar();

        // Act - Load from binary to set the internal array
        byte[] data = new byte[]
        {
            // Count: 2
            0x02, 0x00,
            // Key1: "field1" (7-bit encoded)
            0x06, 0x66, 0x69, 0x65, 0x6C, 0x64, 0x31,
            // Type: Float
            (byte)VarType.Float,
            // Value: 1.5f
            0x00, 0x00, 0xC0, 0x3F,
            // Key2: "field2"
            0x06, 0x66, 0x69, 0x65, 0x6C, 0x64, 0x32,
            // Type: String
            (byte)VarType.String,
            // Value: string.Empty
            0x00
        };
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        sv.LoadFromBinary(span);

        // Assert
        Assert.Equal(2, sv.v.Length);
    }

    [Fact]
    public void StructVar_ToString_Format()
    {
        // Arrange
        StructVar sv = new StructVar();
        byte[] data = new byte[]
        {
            0x01, 0x00, // Count: 1
            0x03, 0x61, 0x62, 0x63, // Key: "abc"
            (byte)VarType.Bool, // Type: Bool
            0x01 // Value: true
        };
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        sv.LoadFromBinary(span);

        // Act
        string result = sv.ToString();

        // Assert
        Assert.Equal("Struct[1]", result);
    }

    [Fact]
    public void StructVar_Save_And_Load_Binary()
    {
        // Arrange
        StructVar original = new StructVar();
        byte[] data = new byte[]
        {
            0x02, 0x00, // Count: 2
            0x03, 0x61, 0x62, 0x63, // Key1: "abc"
            (byte)VarType.Float, // Type: Float
            0xC3, 0xF5, 0x48, 0x40, // Value: 3.14f
            0x03, 0x64, 0x65, 0x66, // Key2: "def"
            (byte)VarType.Bool, // Type: Bool
            0x01 // Value: true
        };
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        original.LoadFromBinary(span);

        using BinWriter writer = new BinWriter(50);

        // Act
        original.SaveToBinary(writer, 0);
        byte[] writtenData = writer.ToArray();

        // Assert
        StructVar loaded = new StructVar();
        ReadOnlySpan<byte> loadedSpan = new ReadOnlySpan<byte>(writtenData);
        loaded.LoadFromBinary(loadedSpan, 1); // Skip type

        Assert.Equal(2, loaded.v.Length);
        Assert.Equal(3.14f, ((FloatVar)loaded.v[0]).v, 2);
        Assert.True(((BoolVar)loaded.v[1]).v);
    }

    #endregion

    #region UI Var Subclasses

    public static IEnumerable<object[]> UIVarTypeTestData()
    {
        yield return new object[] { typeof(UIRectVar), VarType.UIRect };
        yield return new object[] { typeof(UIButtonVar), VarType.UIButton };
        yield return new object[] { typeof(UILabelVar), VarType.UILabel };
        yield return new object[] { typeof(UIInputVar), VarType.UIInput };
        yield return new object[] { typeof(UIRawImageVar), VarType.UIRawImage };
        yield return new object[] { typeof(CanvasVar), VarType.Canvas };
    }

    [Theory]
    [MemberData(nameof(UIVarTypeTestData))]
    public void UIVar_Subclass_Should_Have_Correct_Type(Type varType, VarType expectedType)
    {
        // Arrange & Act
        object? instanceObject = Activator.CreateInstance(varType);
        if (instanceObject is null)
        {
            throw new InvalidOperationException("UI Var 类型无法创建实例");
        }
        VarBase instance = (VarBase)instanceObject;

        // Assert
        Assert.Equal(expectedType, instance.type);
    }

    #endregion

    #region Real Data Scenarios

    [Fact]
    public void Real_Data_ListVar_As_Global_Var_List()
    {
        // Arrange - Pattern from test project: list of global variables
        ListVar varList = new ListVar();
        varList.v.Add(new StringVar { v = "参数调节列表" }); // From real vars.json
        varList.v.Add(new FloatVar { v = 100.0f });

        // Act
        string result = varList.ToString();

        // Assert
        Assert.Equal("List[2]", result);
        Assert.Equal("参数调节列表", ((StringVar)varList.v[0]).v);
    }

    [Fact]
    public void Real_Data_DictVar_As_Property_Map()
    {
        // Arrange - Pattern: device properties as dictionary
        DictVar props = new DictVar();
        props[new zchar("x")] = new FloatVar { v = 10.5f };
        props[new zchar("y")] = new FloatVar { v = -3.2f };
        props[new zchar("name")] = new StringVar { v = "水果切切切" }; // From real project

        // Act
        string result = props.ToString();

        // Assert
        Assert.Equal("Dict[3]", result);
        Assert.Equal("水果切切切", ((StringVar)props[new zchar("name")]).v);
    }

    [Fact]
    public void Real_Data_StructVar_As_Composite_Data()
    {
        // Arrange - Pattern: composite data structure
        StructVar sv = new StructVar();
        byte[] data = new byte[]
        {
            0x03, 0x00, // Count: 3
            0x0C, // Key length: 12 bytes (4 Chinese chars * 3)
            0xE4, 0xB8, 0xAD, 0xE6, 0x96, 0x87, 0xE5, 0x90, 0x8D, 0xE7, 0xA7, 0xB0, // "中文名称"
            (byte)VarType.String,
            0x00, // Empty string
            0x03, 0x76, 0x61, 0x6C, // Key: "val"
            (byte)VarType.Float,
            0x00, 0x00, 0x80, 0x3F, // 1.0f
            0x05, 0x66, 0x6C, 0x61, 0x67, 0x73, // Key: "flags"
            (byte)VarType.Bool,
            0x01 // true
        };
        ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data);
        sv.LoadFromBinary(span);

        // Act
        string result = sv.ToString();

        // Assert
        Assert.Equal("Struct[3]", result);
    }

    #endregion
}
