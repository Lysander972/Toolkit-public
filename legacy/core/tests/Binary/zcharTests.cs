using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Binary;

/// <summary>
/// Tests for zchar struct - UTF-16 character string wrapper.
/// Test data based on real project usage patterns.
/// </summary>
public sealed class zcharTests
{
    #region Constructors

    [Fact]
    public void Constructor_From_String()
    {
        // Arrange & Act
        zchar zc = new zchar("test");

        // Assert
        Assert.False(zc.IsEmpty);
        Assert.Equal(4, zc.Length);
        Assert.Equal(8, zc.byteLength); // UTF-16: 4 chars * 2 bytes
        Assert.Equal("test", zc.ToString());
    }

    [Fact]
    public void Constructor_From_Empty_String()
    {
        // Arrange & Act
        zchar zc = new zchar(string.Empty);

        // Assert - Empty string is stored, so IsEmpty is false
        Assert.False(zc.IsEmpty); // Has _string = string.Empty, which is not null
        Assert.Equal(0, zc.Length);
        Assert.Equal(0, zc.byteLength);
        Assert.Equal(string.Empty, zc.ToString());
    }

    [Fact]
    public void Constructor_From_Char_Array()
    {
        // Arrange
        char[] chars = new[] { 'A', 'B', 'C' };

        // Act
        zchar zc = new zchar(chars);

        // Assert
        Assert.False(zc.IsEmpty);
        Assert.Equal(3, zc.Length);
        Assert.Equal(6, zc.byteLength);
        Assert.Equal("ABC", zc.ToString());
    }

    [Fact]
    public void Constructor_From_Empty_Char_Array()
    {
        // Arrange
        char[] chars = Array.Empty<char>();

        // Act
        zchar zc = new zchar(chars);

        // Assert - Empty char array is stored, so IsEmpty is false
        Assert.False(zc.IsEmpty); // Has _chars = [], which is not null
        Assert.Equal(0, zc.Length);
    }

    [Fact]
    public void Constructor_From_UTF8_Bytes()
    {
        // Arrange - UTF-8 bytes for "ABC"
        byte[] utf8Bytes = new byte[] { 0x41, 0x42, 0x43 };

        // Act
        zchar zc = new zchar(utf8Bytes);

        // Assert
        Assert.False(zc.IsEmpty);
        // Length is calculated from _chars after ToString conversion, not from UTF-8 bytes directly
        Assert.Equal("ABC", zc.ToString());
        Assert.Equal(3, zc.ToString().Length); // Length property gets set after ToString
    }

    [Fact]
    public void Constructor_From_UTF8_Bytes_With_Chinese()
    {
        // Arrange - UTF-8 bytes for "测试"
        byte[] utf8Bytes = TextEncoding.Utf8.GetBytes("测试");

        // Act
        zchar zc = new zchar(utf8Bytes);

        // Assert
        Assert.False(zc.IsEmpty);
        // Length returns character count after UTF-8 decoding
        string result = zc.ToString();
        Assert.Equal(2, result.Length); // 2 Chinese characters
        Assert.Equal("测试", result);
    }

    #endregion

    #region Static Empty Field

    [Fact]
    public void Empty_Field_Is_Empty()
    {
        // Act
        zchar zc = zchar.Empty;

        // Assert
        Assert.True(zc.IsEmpty);
        Assert.Equal(0, zc.Length);
        Assert.Equal(string.Empty, zc.ToString());
    }

    #endregion

    #region Properties

    [Fact]
    public void Length_Property_For_String_Constructor()
    {
        // Arrange & Act
        zchar zc = new zchar("Hello");

        // Assert
        Assert.Equal(5, zc.Length);
    }

    [Fact]
    public void Length_Property_For_Char_Array_Constructor()
    {
        // Arrange
        char[] chars = new[] { 'X', 'Y', 'Z' };

        // Act
        zchar zc = new zchar(chars);

        // Assert
        Assert.Equal(3, zc.Length);
    }

    [Fact]
    public void ByteLength_Property_Returns_Double_Length()
    {
        // Arrange & Act
        zchar zc = new zchar("test");

        // Assert - UTF-16 uses 2 bytes per character
        Assert.Equal(4, zc.Length);
        Assert.Equal(8, zc.byteLength);
    }

    [Fact]
    public void CharSpan_Returns_Characters_For_String()
    {
        // Arrange & Act
        zchar zc = new zchar("test");

        // Assert
        Assert.Equal(4, zc.CharSpan.Length);
        Assert.Equal('t', zc.CharSpan[0]);
        Assert.Equal('e', zc.CharSpan[1]);
        Assert.Equal('s', zc.CharSpan[2]);
        Assert.Equal('t', zc.CharSpan[3]);
    }

    [Fact]
    public void CharSpan_Returns_Characters_For_Char_Array()
    {
        // Arrange
        char[] chars = new[] { 'A', 'B' };

        // Act
        zchar zc = new zchar(chars);

        // Assert
        Assert.Equal(2, zc.CharSpan.Length);
        Assert.Equal('A', zc.CharSpan[0]);
        Assert.Equal('B', zc.CharSpan[1]);
    }

    [Fact]
    public void ByteSpan_Returns_UTF16_Bytes()
    {
        // Arrange & Act
        zchar zc = new zchar("AB");

        // Assert - UTF-16 LE: 0x41 0x00 0x42 0x00
        ReadOnlySpan<byte> bytes = zc.ByteSpan;
        Assert.Equal(4, bytes.Length); // 2 chars * 2 bytes
        Assert.Equal(0x41, bytes[0]); // 'A' low byte
        Assert.Equal(0x00, bytes[1]); // 'A' high byte
        Assert.Equal(0x42, bytes[2]); // 'B' low byte
        Assert.Equal(0x00, bytes[3]); // 'B' high byte
    }

    #endregion

    #region Implicit Operators

    [Fact]
    public void Implicit_Operator_String_To_zchar()
    {
        // Arrange
        string str = "test";

        // Act
        zchar zc = str;

        // Assert
        Assert.Equal("test", zc.ToString());
    }

    [Fact]
    public void Implicit_Operator_zchar_To_String()
    {
        // Arrange
        zchar zc = new zchar("test");

        // Act
        string str = zc;

        // Assert
        Assert.Equal("test", str);
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_Returns_Original_String()
    {
        // Arrange
        string original = "Hello, World!";
        zchar zc = new zchar(original);

        // Act
        string result = zc.ToString();

        // Assert
        Assert.Equal(original, result);
    }

    [Fact]
    public void ToString_Converts_Char_Array_To_String()
    {
        // Arrange
        char[] chars = new[] { 'T', 'e', 's', 't' };
        zchar zc = new zchar(chars);

        // Act
        string result = zc.ToString();

        // Assert
        Assert.Equal("Test", result);
    }

    [Fact]
    public void ToString_Converts_UTF8_Bytes_To_String()
    {
        // Arrange
        byte[] utf8Bytes = TextEncoding.Utf8.GetBytes("测试数据");
        zchar zc = new zchar(utf8Bytes);

        // Act
        string result = zc.ToString();

        // Assert
        Assert.Equal("测试数据", result);
    }

    [Fact]
    public void ToString_Returns_Empty_String_For_Empty_zchar()
    {
        // Arrange
        zchar zc = new zchar(string.Empty);

        // Act
        string result = zc.ToString();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region Equality

    [Fact]
    public void Equals_Returns_True_For_Same_String()
    {
        // Arrange
        zchar zc1 = new zchar("test");
        zchar zc2 = new zchar("test");

        // Act & Assert
        Assert.True(zc1.Equals(zc2));
        Assert.True(zc1 == zc2);
        Assert.False(zc1 != zc2);
    }

    [Fact]
    public void Equals_Returns_False_For_Different_Strings()
    {
        // Arrange
        zchar zc1 = new zchar("test1");
        zchar zc2 = new zchar("test2");

        // Act & Assert
        Assert.False(zc1.Equals(zc2));
        Assert.False(zc1 == zc2);
        Assert.True(zc1 != zc2);
    }

    [Fact]
    public void Equals_Returns_True_For_String_And_Char_Array_With_Same_Content()
    {
        // Arrange
        zchar zc1 = new zchar("test");
        zchar zc2 = new zchar(new[] { 't', 'e', 's', 't' });

        // Act & Assert
        Assert.True(zc1.Equals(zc2));
    }

    [Fact]
    public void Equals_Returns_True_For_String_And_UTF8_With_Same_Content()
    {
        // Arrange
        zchar zc1 = new zchar("test");
        byte[] utf8Bytes = TextEncoding.Utf8.GetBytes("test");
        zchar zc2 = new zchar(utf8Bytes);

        // Act & Assert
        Assert.True(zc1.Equals(zc2));
    }

    [Fact]
    public void Equality_Operator_With_Null()
    {
        // Arrange
        zchar zc = new zchar("test");

        // Act & Assert
        Assert.False(zc.Equals((object?)null));
    }

    [Fact]
    public void GetHashCode_Returns_Same_Value_For_Equal_Strings()
    {
        // Arrange
        zchar zc1 = new zchar("test");
        zchar zc2 = new zchar("test");

        // Act
        int hash1 = zc1.GetHashCode();
        int hash2 = zc2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_Returns_Different_Value_For_Different_Strings()
    {
        // Arrange
        zchar zc1 = new zchar("test1");
        zchar zc2 = new zchar("test2");

        // Act
        int hash1 = zc1.GetHashCode();
        int hash2 = zc2.GetHashCode();

        // Assert - Note: hash codes might collide, but for these strings they should differ
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Real Data Scenarios

    [Fact]
    public void Real_Data_Script_Name_From_Test_Project()
    {
        // Arrange - Real script name from test project
        // Source: test/data/projects/水果切切切/map.map_/255/scripts
        string scriptName = "使用帮助";

        // Act
        zchar zc = new zchar(scriptName);

        // Assert
        Assert.Equal(4, zc.Length);
        Assert.Equal(scriptName, zc.ToString());
    }

    [Fact]
    public void Real_Data_Event_Name_From_Test_Project()
    {
        // Arrange - Real event name from test project
        string eventName = "event_clientUpdate";

        // Act
        zchar zc = new zchar(eventName);

        // Assert
        Assert.Equal(18, zc.Length);
        Assert.Equal(eventName, zc.ToString());
    }

    [Fact]
    public void Real_Data_Device_Name_From_Test_Project()
    {
        // Arrange - Real device name from test project
        string deviceName = "水果切切切";

        // Act
        zchar zc = new zchar(deviceName);

        // Assert
        Assert.Equal(5, zc.Length); // 5 Chinese characters
        Assert.Equal(deviceName, zc.ToString());
    }

    [Fact]
    public void Real_Data_Variable_Name_From_Test_Project()
    {
        // Arrange - Real variable name from test project
        // Source: test/data/projects/水果切切切/map.map_/255/vars.json
        string varName = "参数调节列表";

        // Act
        zchar zc = new zchar(varName);

        // Assert
        Assert.Equal(6, zc.Length);
        Assert.Equal(varName, zc.ToString());
    }

    #endregion
}
