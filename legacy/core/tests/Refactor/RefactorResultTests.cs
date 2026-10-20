// This test file accesses internal members of Toolkit.Core via
// the InternalsVisibleTo attribute in the project configuration.
using Toolkit.Core.Tests.Common;
using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Refactor;

/// <summary>
/// Tests for RefactorResult and RefactorHelper.
/// Test data based on real project structure from test/data/projects
/// </summary>
public sealed class RefactorResultTests : TempDirectoryTestFixture
{
    public RefactorResultTests() : base("Toolkit.Core.Tests.RefactorResult")
    {
    }

    #region RefactorResult<T> Tests

    public class TestData
    {
        public string Name { get; set; } = "test";
    }

    [Fact]
    public void RefactorResult_Success_Should_Create_Success_Result()
    {
        // Arrange
        TestData data = new TestData { Name = "success" };

        // Act
        RefactorResult<TestData> result = RefactorResult<TestData>.Success(data);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("success", result.Data.Name);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RefactorResult_Success_With_Warnings_Should_Include_Warnings()
    {
        // Arrange
        TestData data = new TestData { Name = "test" };
        ToolError[] warnings = new[]
        {
            new ToolError(null, null, null, "Warning 1", ToolDiagnosticSeverity.Warning),
            new ToolError(null, null, null, "Warning 2", ToolDiagnosticSeverity.Warning)
        };

        // Act
        RefactorResult<TestData> result = RefactorResult<TestData>.Success(data, warnings);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void RefactorResult_Fail_Should_Create_Failed_Result()
    {
        // Arrange
        string errorMessage = "Operation failed";

        // Act
        RefactorResult<TestData> result = RefactorResult<TestData>.Fail(errorMessage);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.Single(result.Errors);
        Assert.Equal(errorMessage, result.Errors[0].Message);
    }

    #endregion

    #region RefactorHelper Tests

    [Fact]
    public void ValidateScriptName_Should_Throw_When_Name_Contains_Code_Extension()
    {
        // Arrange
        string name = "script.code";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RefactorHelper.ValidateScriptName(name));
    }

    [Fact]
    public void ValidateScriptName_Should_Pass_For_Valid_Name()
    {
        // Arrange - valid names without spaces or extensions
        string[] names = new[] { "myscript", "Script.CODE", "script.Code", "使用帮助" };

        foreach (string? name in names)
        {
            // Act & Assert - should not throw
            Exception exception = Record.Exception(() => RefactorHelper.ValidateScriptName(name));
            Assert.Null(exception);
        }
    }

    [Fact]
    public void ValidateScriptName_Should_Throw_When_Name_Contains_Space()
    {
        // Arrange
        string name = "my script";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => RefactorHelper.ValidateScriptName(name));
    }

    [Fact]
    public void ValidateScriptName_Should_Throw_When_Name_Has_Leading_Trailing_Whitespace()
    {
        // Arrange
        string[] names = new[] { " script", "script ", "  script  " };

        foreach (string? name in names)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => RefactorHelper.ValidateScriptName(name));
        }
    }

    [Fact]
    public void ValidateScriptName_Should_Throw_When_Name_Is_Empty()
    {
        // Arrange
        string?[] names = new[] { string.Empty, "   ", (string?)null };

        foreach (string? name in names)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => RefactorHelper.ValidateScriptName(name));
        }
    }

    [Fact]
    public void ExtractDevicePathFromSourcePath_Should_Return_DevicePath()
    {
        string? devicePath = ScriptPathHelper.ExtractDevicePathFromSourcePath("main.map_/1/scripts/test.code");

        Assert.NotNull(devicePath);
        Assert.Equal("main.map_/1", devicePath);
    }

    [Fact]
    public void ExtractEventNameFromSourcePath_Should_Return_EventName()
    {
        string? eventName = ScriptPathHelper.ExtractEventNameFromSourcePath("main.map_/1/event_onStart/test.code");

        Assert.NotNull(eventName);
        Assert.Equal("onStart", eventName);
    }

    [Fact]
    public void GetScriptStemFromSourcePath_Should_Remove_Code_Ext()
    {
        string stem = ScriptPathHelper.GetScriptStemFromSourcePath("repository/helper.code");

        Assert.Equal("helper", stem);
    }

    [Fact]
    public void IsDeviceScriptDir_Should_Return_True_For_Scripts_Dir()
    {
        // Arrange - Now case-sensitive, only lowercase "scripts" is recognized
        string path = "scripts";

        // Act
        bool isDeviceDir = RefactorHelper.IsDeviceScriptDir(path);

        // Assert
        Assert.True(isDeviceDir, $"Should be device script dir: {path}");
    }

    [Fact]
    public void IsDeviceScriptDir_Should_Be_Case_Sensitive_For_Scripts()
    {
        // Arrange - uppercase variants should NOT be recognized
        string[] paths = new[] { "Scripts", "SCRIPTS" };

        foreach (string? path in paths)
        {
            // Act
            bool isDeviceDir = RefactorHelper.IsDeviceScriptDir(path);

            // Assert - Now case-sensitive
            Assert.False(isDeviceDir, $"Should NOT be device script dir: {path}");
        }
    }

    [Fact]
    public void IsDeviceScriptDir_Should_Return_True_For_Event_Dirs()
    {
        // Arrange - Now case-sensitive, only lowercase "event_" prefix is recognized
        string[] eventDirs = new[] { "event_start", "event_click", "event_update" };

        foreach (string? dir in eventDirs)
        {
            // Act
            bool isDeviceDir = RefactorHelper.IsDeviceScriptDir(dir);

            // Assert
            Assert.True(isDeviceDir, $"Should be device event dir: {dir}");
        }
    }

    [Fact]
    public void IsDeviceScriptDir_Should_Be_Case_Sensitive_For_Event_Dirs()
    {
        // Arrange - uppercase variants should NOT be recognized
        string[] eventDirs = new[] { "EVENT_CLICK", "Event_Update" };

        foreach (string? dir in eventDirs)
        {
            // Act
            bool isDeviceDir = RefactorHelper.IsDeviceScriptDir(dir);

            // Assert - Now case-sensitive
            Assert.False(isDeviceDir, $"Should NOT be device event dir: {dir}");
        }
    }

    [Fact]
    public void IsDeviceScriptDir_Should_Return_False_For_Normal_Dirs()
    {
        // Arrange
        string[] normalDirs = new[] { "assets", "code", "event", "scripts_backup" };

        foreach (string? dir in normalDirs)
        {
            // Act
            bool isDeviceDir = RefactorHelper.IsDeviceScriptDir(dir);

            // Assert
            Assert.False(isDeviceDir, $"Should not be device script dir: {dir}");
        }
    }

    #endregion
}
