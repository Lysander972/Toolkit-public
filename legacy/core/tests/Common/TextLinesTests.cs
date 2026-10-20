// This test file accesses internal members of Toolkit.Core via
// the InternalsVisibleTo attribute in the project configuration.
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// Tests for TextLines utility class.
/// Test data based on real content from test/data/projects
/// </summary>
public sealed class TextLinesTests
{
    [Fact]
    public void ToLf_Should_Convert_Crlf_And_Remove_Remaining_Cr()
    {
        string text = "line1\r\nline2\rline3\nline4";

        string result = TextLineEndings.ToLf(text);

        Assert.Equal("line1\nline2line3\nline4", result);
    }

    [Fact]
    public void SplitLines_Should_Split_On_Newlines()
    {
        // Arrange
        string text = "line1\nline2\nline3";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void SplitLines_Should_Handle_Windows_Line_Endings()
    {
        // Arrange
        string text = "line1\r\nline2\r\nline3";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void SplitLines_Should_Preserve_Trailing_Empty_Line()
    {
        // Arrange - File with trailing newline
        string text = "line1\nline2\n";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert - Split('\n') 不 trim 尾空行，保留原始分段
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("", lines[2]);
    }

    [Fact]
    public void SplitLines_Should_Handle_Single_Line()
    {
        // Arrange
        string text = "single line";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert
        Assert.Single(lines);
        Assert.Equal("single line", lines[0]);
    }

    [Fact]
    public void SplitLines_Should_Return_Single_Empty_Line_For_Empty_String()
    {
        // Arrange
        string text = string.Empty;

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert - Split('\n') 对空字符串返回 [""]
        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }

    [Fact]
    public void SplitLines_Should_Handle_Real_ListJson_Content()
    {
        // Arrange - Real content from test project list.json（写入时已统一为 LF）
        // Source: test/data/projects/水果切切切/map.map_/255/scripts/list.json
        string text = @"[
 按钮
 使用帮助
 初始化快捷调试面板
]";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert
        Assert.Equal(5, lines.Length);
        Assert.Equal("[", lines[0]);
        Assert.Contains("按钮", lines[1]);
    }

    [Fact]
    public void SplitLines_Should_Handle_Mixed_Line_Endings()
    {
        // Arrange - Mixed line endings（写入后统一为 LF）
        string text = "line1\nline2\nline3";

        // Act
        string[] lines = TextLines.SplitLines(text);

        // Assert
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void InsertLines_Should_Insert_At_Beginning()
    {
        // Arrange
        string[] lines = new[] { "line2", "line3" };
        string[] toInsert = new[] { "line1" };

        // Act
        string[] result = TextLines.InsertLines(lines, 0, toInsert);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line2", result[1]);
        Assert.Equal("line3", result[2]);
    }

    [Fact]
    public void InsertLines_Should_Insert_At_End()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2" };
        string[] toInsert = new[] { "line3" };

        // Act
        string[] result = TextLines.InsertLines(lines, 2, toInsert);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line2", result[1]);
        Assert.Equal("line3", result[2]);
    }

    [Fact]
    public void InsertLines_Should_Insert_In_Middle()
    {
        // Arrange
        string[] lines = new[] { "line1", "line3" };
        string[] toInsert = new[] { "line2a", "line2b" };

        // Act
        string[] result = TextLines.InsertLines(lines, 1, toInsert);

        // Assert
        Assert.Equal(4, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line2a", result[1]);
        Assert.Equal("line2b", result[2]);
        Assert.Equal("line3", result[3]);
    }

    [Fact]
    public void InsertLines_Should_Throw_When_Index_Is_Negative()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2" };
        string[] toInsert = new[] { "new" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLines.InsertLines(lines, -1, toInsert));
    }

    [Fact]
    public void InsertLines_Should_Throw_When_Index_Exceeds_Length()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2" };
        string[] toInsert = new[] { "new" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLines.InsertLines(lines, 999, toInsert));
    }

    [Fact]
    public void InsertLines_Should_Handle_Empty_Insert_List()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2" };
        string[] toInsert = Array.Empty<string>();

        // Act
        string[] result = TextLines.InsertLines(lines, 1, toInsert);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(lines, result);
    }

    [Fact]
    public void RemoveLines_Should_Remove_Single_Line()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act
        string[] result = TextLines.RemoveLines(lines, 1, 1);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line3", result[1]);
    }

    [Fact]
    public void RemoveLines_Should_Remove_Multiple_Lines()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3", "line4", "line5" };

        // Act
        string[] result = TextLines.RemoveLines(lines, 1, 3);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line5", result[1]);
    }

    [Fact]
    public void RemoveLines_Should_Remove_From_Beginning()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act
        string[] result = TextLines.RemoveLines(lines, 0, 0);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("line2", result[0]);
        Assert.Equal("line3", result[1]);
    }

    [Fact]
    public void RemoveLines_Should_Remove_To_End()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act
        string[] result = TextLines.RemoveLines(lines, 1, 2);

        // Assert
        Assert.Single(result);
        Assert.Equal("line1", result[0]);
    }

    [Fact]
    public void RemoveLines_Should_Throw_When_Range_Invalid()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLines.RemoveLines(lines, 2, 0));
    }

    [Fact]
    public void RemoveLines_Should_Throw_When_StartIndex_Is_Negative()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLines.RemoveLines(lines, -1, 1));
    }

    [Fact]
    public void RemoveLines_Should_Throw_When_EndIndex_Exceeds_Length()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TextLines.RemoveLines(lines, 1, 999));
    }

    [Fact]
    public void RemoveLines_Should_Handle_All_Lines()
    {
        // Arrange
        string[] lines = new[] { "line1", "line2", "line3" };

        // Act
        string[] result = TextLines.RemoveLines(lines, 0, 2);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void InsertAndRemoveLines_Should_Work_Together()
    {
        // Arrange
        string[] lines = new[] { "a", "b", "c", "d" };

        // Act - Remove "b", "c" and insert "x", "y"
        string[] afterRemove = TextLines.RemoveLines(lines, 1, 2);
        string[] afterInsert = TextLines.InsertLines(afterRemove, 1, new[] { "x", "y" });

        // Assert
        Assert.Equal(4, afterInsert.Length);
        Assert.Equal("a", afterInsert[0]);
        Assert.Equal("x", afterInsert[1]);
        Assert.Equal("y", afterInsert[2]);
        Assert.Equal("d", afterInsert[3]);
    }
}
