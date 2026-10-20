// This test file accesses internal members of Toolkit.Core via
// the InternalsVisibleTo attribute in the project configuration.
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// Tests for WorkspacePath utility class.
/// Test data based on real project structure from test/data/projects
/// </summary>
public sealed class WorkspacePathTests
{
    [Fact]
    public void RequireRelativePath_Should_Accept_Valid_Relative_Path()
    {
        string path = "map.map_/255/scripts/list.json";

        string result = WorkspacePath.RequireRelativePath(path);

        Assert.Equal(path, result);
    }

    [Fact]
    public void RequireRelativePath_Should_Allow_Dot_Root()
    {
        string result = WorkspacePath.RequireRelativePath(".");
        Assert.Equal(".", result);
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Backslashes()
    {
        string path = @"map.map_\255\scripts\list.json";
        Assert.Throws<ArgumentException>(() => WorkspacePath.RequireRelativePath(path));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Leading_Dot_Slash()
    {
        string path = "./map.map_/255";
        Assert.Throws<ArgumentException>(() => WorkspacePath.RequireRelativePath(path));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Absolute_Windows_Path()
    {
        string absolutePath = Path.Combine(Path.GetTempPath(), "Projects", "map.map_");
        Assert.Throws<ArgumentException>(() => WorkspacePath.RequireRelativePath(absolutePath));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Absolute_Unix_Path()
    {
        string absolutePath = "/usr/local/map.map_";
        Assert.Throws<ArgumentException>(() => WorkspacePath.RequireRelativePath(absolutePath));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_UNC_Path()
    {
        string uncPath = @"\\server\share\map.map_";
        Assert.Throws<ArgumentException>(() => WorkspacePath.RequireRelativePath(uncPath));
    }

    [Fact]
    public void RequireRelativePath_Should_Throw_For_Whitespace_Input()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("   "));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Leading_Or_Trailing_Whitespace()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("  map.map_/255"));
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("map.map_/255  "));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Trailing_Slash()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("map.map_/255/"));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Empty_Segment()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("map.map_//255"));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_Dot_Segment()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("map.map_/./255"));
    }

    [Fact]
    public void RequireRelativePath_Should_Reject_DotDot_Segment()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.RequireRelativePath("map.map_/../255"));
    }

    [Fact]
    public void IsHiddenSegment_Should_Return_True_For_Dot_Prefixed()
    {
        // Arrange & Act
        bool result1 = WorkspacePath.IsHiddenSegment(".git");
        bool result2 = WorkspacePath.IsHiddenSegment(".vscode");
        bool result3 = WorkspacePath.IsHiddenSegment(".cache");

        // Assert
        Assert.True(result1);
        Assert.True(result2);
        Assert.True(result3);
    }

    [Fact]
    public void IsHiddenSegment_Should_Return_False_For_Normal_Segment()
    {
        // Arrange & Act
        bool result = WorkspacePath.IsHiddenSegment("map.map_");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsHiddenSegment_Should_Return_False_For_Empty_String()
    {
        // Act
        bool result = WorkspacePath.IsHiddenSegment(string.Empty);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsHiddenSegment_Should_Return_False_For_Null()
    {
        // Act
        bool result = WorkspacePath.IsHiddenSegment(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsIgnoredSegment_Should_Return_True_For_Hidden_Segments()
    {
        // Arrange & Act
        bool result = WorkspacePath.IsIgnoredSegment(".git");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsIgnoredSegment_Should_Return_False_For_Temp()
    {
        // Arrange & Act
        bool result = WorkspacePath.IsIgnoredSegment("temp");

        // Assert - temp 现在作为 TempRootEntity 处理，不再被忽略
        Assert.False(result);
    }

    [Fact]
    public void IsIgnoredSegment_Should_Return_False_For_ModDirectories()
    {
        // Arrange & Act
        bool result1 = WorkspacePath.IsIgnoredSegment("MechEditorCode");
        bool result2 = WorkspacePath.IsIgnoredSegment("MechEditorToolCode");

        // Assert - 编辑器 Mod 目录现在作为 ModDirectoryEntity 处理，不再被忽略
        Assert.False(result1);
        Assert.False(result2);
    }

    [Fact]
    public void IsIgnoredSegment_Should_Be_Case_Sensitive()
    {
        // Arrange & Act
        bool result2 = WorkspacePath.IsIgnoredSegment("TEMP");
        bool result3 = WorkspacePath.IsIgnoredSegment("Temp");

        // Assert - Now case-sensitive
        Assert.False(result2);
        Assert.False(result3);
    }

    [Fact]
    public void IsIgnoredSegment_Should_Return_False_For_Normal_Segment()
    {
        // Arrange & Act
        bool result = WorkspacePath.IsIgnoredSegment("map.map_");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsHiddenSegment_Should_Detect_Hidden_Segments()
    {
        // Arrange - Real structure from test projects
        string path1 = ".git/config";
        string path2 = "map.map_/255/scripts";

        // Act
        bool result1 = WorkspacePath.ContainsHiddenSegment(path1);
        bool result2 = WorkspacePath.ContainsHiddenSegment(path2);

        // Assert - temp 现在作为实体处理，不再被检测为隐藏
        Assert.True(result1, ".git should be detected as hidden");
        Assert.False(result2, "Normal path should not contain hidden segments");
    }

    [Fact]
    public void ContainsHiddenSegment_Should_Not_Detect_Temp_As_Hidden()
    {
        // Arrange - temp 现在作为 TempRootEntity 处理
        string path = "map.map_/temp/cache";

        // Act
        bool result = WorkspacePath.ContainsHiddenSegment(path);

        // Assert - temp 不再被检测为隐藏
        Assert.False(result, "temp should not be detected as hidden since it's now an entity");
    }

    [Fact]
    public void ContainsHiddenSegment_Should_Throw_For_Empty_Path()
    {
        Assert.Throws<ArgumentException>(static () => WorkspacePath.ContainsHiddenSegment(string.Empty));
    }

    [Fact]
    public void IsHiddenUnderRoot_Should_Not_Detect_Temp_As_Hidden()
    {
        // Arrange - temp 现在作为 TempRootEntity 处理
        string root = "projects";
        string fullPath = "projects/temp/toolkit/views";

        // Act
        bool result = WorkspacePath.IsHiddenUnderRoot(root, fullPath);

        // Assert - temp 不再被检测为隐藏
        Assert.False(result);
    }

    [Fact]
    public void IsHiddenUnderRoot_Should_Return_False_When_Not_Under_Root()
    {
        // Arrange
        string root = "projects";
        string fullPath = "other/regular/folder"; // No hidden segments

        // Act
        bool result = WorkspacePath.IsHiddenUnderRoot(root, fullPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsHiddenUnderRoot_Should_Throw_For_Empty_FullPath()
    {
        string root = "projects";
        Assert.Throws<ArgumentException>(() => WorkspacePath.IsHiddenUnderRoot(root, string.Empty));
    }

    [Fact]
    public void RequireRelativePath_Should_Handle_Complex_Real_World_Paths()
    {
        string path1 = "map.map_/255/event_clientUpdate/list.json";
        string path2 = "temp/toolkit/views/map.map_/255.ts";

        Assert.Equal(path1, WorkspacePath.RequireRelativePath(path1));
        Assert.Equal(path2, WorkspacePath.RequireRelativePath(path2));
    }

    [Fact]
    public void IsSameOrUnderRoot_Should_Accept_Same_Path()
    {
        string root = "map.map_/255";
        Assert.True(WorkspacePath.IsSameOrUnderRoot(root, root));
    }

    [Fact]
    public void IsSameOrUnderRoot_Should_Accept_Child_Path()
    {
        string root = "map.map_/255";
        string child = "map.map_/255/scripts/list.json";
        Assert.True(WorkspacePath.IsSameOrUnderRoot(root, child));
    }

    [Fact]
    public void IsSameOrUnderRoot_Should_Reject_Unrelated_Path()
    {
        string root = "map.map_/255";
        string other = "map.map_/2550/scripts/list.json";
        Assert.False(WorkspacePath.IsSameOrUnderRoot(root, other));
    }

    [Fact]
    public void RequirePathUnderRoot_Should_Throw_When_Not_Under_Root()
    {
        Assert.Throws<ArgumentException>(static () =>
            WorkspacePath.RequirePathUnderRoot("map.map_/255", "map.map_/other/device.json"));
    }
}
