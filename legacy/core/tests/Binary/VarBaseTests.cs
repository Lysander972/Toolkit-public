using Xunit;
using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Binary;

/// <summary>
/// Tests for VarBase and derived variable types.
/// Test data based on real project variable patterns.
/// </summary>
public sealed class VarBaseTests
{
    #region BoolVar

    [Fact]
    public void BoolVar_Default_Value()
    {
        // Arrange & Act
        BoolVar bv = new BoolVar();

        // Assert
        Assert.Equal(VarType.Bool, bv.type);
        Assert.False(bv.v);
    }

    [Fact]
    public void BoolVar_Set_Value()
    {
        // Arrange
        BoolVar bv = new BoolVar();

        // Act
        bv.v = true;

        // Assert
        Assert.True(bv.v);
        Assert.Equal("True", bv.ToString());
    }

    [Fact]
    public void BoolVar_Save_And_Load_Binary()
    {
        BoolVar original = new BoolVar { v = true };
        BoolVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.True(loaded.v);
    }

    #endregion

    #region FloatVar

    [Fact]
    public void FloatVar_Default_Value()
    {
        // Arrange & Act
        FloatVar fv = new FloatVar();

        // Assert
        Assert.Equal(VarType.Float, fv.type);
        Assert.Equal(0f, fv.v);
    }

    [Fact]
    public void FloatVar_Set_Value()
    {
        // Arrange
        FloatVar fv = new FloatVar();

        // Act
        fv.v = 3.14f;

        // Assert
        Assert.Equal(3.14f, fv.v, 2);
    }

    [Fact]
    public void FloatVar_Save_And_Load_Binary()
    {
        FloatVar original = new FloatVar { v = 1.5f };
        FloatVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal(1.5f, loaded.v, 2);
    }

    #endregion

    #region LongVar

    [Fact]
    public void LongVar_Default_Value()
    {
        // Arrange & Act
        LongVar lv = new LongVar();

        // Assert
        Assert.Equal(VarType.Long, lv.type);
        Assert.Equal(0L, lv.v);
    }

    [Fact]
    public void LongVar_Set_Value()
    {
        // Arrange
        LongVar lv = new LongVar();

        // Act
        lv.v = 1234567890L;

        // Assert
        Assert.Equal(1234567890L, lv.v);
    }

    [Fact]
    public void LongVar_Save_And_Load_Binary()
    {
        LongVar original = new LongVar { v = -9876543210L };
        LongVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal(-9876543210L, loaded.v);
    }

    #endregion

    #region StringVar

    [Fact]
    public void StringVar_Default_Value()
    {
        // Arrange & Act
        StringVar sv = new StringVar();

        // Assert
        Assert.Equal(VarType.String, sv.type);
        Assert.Equal(string.Empty, sv.v);
    }

    [Fact]
    public void StringVar_Set_Value()
    {
        // Arrange
        StringVar sv = new StringVar();

        // Act
        sv.v = "test";

        // Assert
        Assert.Equal("test", sv.v);
    }

    [Fact]
    public void StringVar_Save_And_Load_Binary()
    {
        StringVar original = new StringVar { v = "Hello" };
        StringVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal("Hello", loaded.v);
    }

    [Fact]
    public void StringVar_Save_And_Load_Chinese_Characters()
    {
        // Real data from test project
        string varName = "参数调节列表";
        StringVar original = new StringVar { v = varName };
        StringVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal(varName, loaded.v);
    }

    #endregion

    #region Vector3Var

    [Fact]
    public void Vector3Var_Default_Value()
    {
        // Arrange & Act
        Vector3Var vv = new Vector3Var();

        // Assert
        Assert.Equal(VarType.Vector3, vv.type);
        Assert.Equal(0f, vv.X);
        Assert.Equal(0f, vv.Y);
        Assert.Equal(0f, vv.Z);
    }

    [Fact]
    public void Vector3Var_Set_Components()
    {
        // Arrange
        Vector3Var vv = new Vector3Var();

        // Act
        vv.X = 1.0f;
        vv.Y = 2.0f;
        vv.Z = 3.0f;

        // Assert
        Assert.Equal(1.0f, vv.X, 5);
        Assert.Equal(2.0f, vv.Y, 5);
        Assert.Equal(3.0f, vv.Z, 5);
    }

    [Fact]
    public void Vector3Var_ToString_Format()
    {
        // Arrange
        Vector3Var vv = new Vector3Var { X = 1.5f, Y = 2.5f, Z = 3.5f };

        // Act
        string result = vv.ToString();

        // Assert
        Assert.Equal("(1.5, 2.5, 3.5)", result);
    }

    [Fact]
    public void Vector3Var_Save_And_Load_Binary()
    {
        Vector3Var original = new Vector3Var { X = 10.5f, Y = -3.2f, Z = 0.0f };
        Vector3Var loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal(10.5f, loaded.X, 5);
        Assert.Equal(-3.2f, loaded.Y, 5);
        Assert.Equal(0.0f, loaded.Z, 5);
    }

    #endregion

    #region ColorVar

    [Fact]
    public void ColorVar_Default_Value()
    {
        // Arrange & Act
        ColorVar cv = new ColorVar();

        // Assert
        Assert.Equal(VarType.Color, cv.type);
        Assert.Equal(0f, cv.R);
        Assert.Equal(0f, cv.G);
        Assert.Equal(0f, cv.B);
        Assert.Equal(0f, cv.A);
    }

    [Fact]
    public void ColorVar_Set_RGBA()
    {
        // Arrange
        ColorVar cv = new ColorVar();

        // Act
        cv.R = 1.0f;
        cv.G = 0.5f;
        cv.B = 0.25f;
        cv.A = 0.75f;

        // Assert
        Assert.Equal(1.0f, cv.R, 5);
        Assert.Equal(0.5f, cv.G, 5);
        Assert.Equal(0.25f, cv.B, 5);
        Assert.Equal(0.75f, cv.A, 5);
    }

    [Fact]
    public void ColorVar_Palette_Name()
    {
        // Arrange
        ColorVar cv = new ColorVar();

        // Act
        cv.PaletteName = "primary_color";

        // Assert
        Assert.Equal("primary_color", cv.PaletteName);
    }

    [Fact]
    public void ColorVar_Save_And_Load_Binary()
    {
        ColorVar original = new ColorVar { R = 1.0f, G = 0.8f, B = 0.6f, A = 1.0f };
        ColorVar loaded = BinaryTestHelpers.SaveAndLoad(original);
        Assert.Equal(1.0f, loaded.R, 5);
        Assert.Equal(0.8f, loaded.G, 5);
        Assert.Equal(0.6f, loaded.B, 5);
        Assert.Equal(1.0f, loaded.A, 5);
    }

    #endregion

    #region Real Data Scenarios

    [Fact]
    public void Real_Data_Device_Global_Var_Patterns()
    {
        // Arrange - Real global variable patterns from test project
        // Source: test/data/projects/水果切切切/map.map_/255/vars.json
        BoolVar boolVar = new BoolVar { v = true };
        FloatVar floatVar = new FloatVar { v = 100.0f };
        StringVar stringVar = new StringVar { v = "使用帮助" };

        using BinWriter writer = new BinWriter(100);

        // Act
        boolVar.SaveToBinary(writer, 0);
        floatVar.SaveToBinary(writer, 0);
        stringVar.SaveToBinary(writer, 0);

        byte[] data = writer.ToArray();

        // Assert - Verify type bytes
        Assert.Equal((byte)VarType.Bool, data[0]);
        Assert.Equal((byte)VarType.Float, data[2]); // 1 byte type + 1 byte bool value
    }

    [Fact]
    public void Real_Data_Vector3_As_Device_Position()
    {
        // Arrange - Common pattern: device position in 3D space
        Vector3Var position = new Vector3Var
        {
            X = 10.0f,
            Y = 2.5f,
            Z = -5.0f
        };

        // Act
        string result = position.ToString();

        // Assert
        Assert.Equal("(10, 2.5, -5)", result);
    }

    [Fact]
    public void Real_Data_Color_As_Palette_Reference()
    {
        // Arrange - Pattern: color referencing palette
        ColorVar color = new ColorVar
        {
            PaletteName = "按钮背景颜色" // From real project palette
        };

        // Assert
        Assert.Equal("按钮背景颜色", color.PaletteName);
        Assert.Equal(VarType.Color, color.type);
    }

    #endregion
}
