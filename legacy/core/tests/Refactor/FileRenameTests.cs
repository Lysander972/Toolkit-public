using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Toolkit.Core.Tests.Common;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;
using Toolkit.Core.Refactor;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Refactor;

/// <summary>
/// Tests for FileRename - file/directory rename and move operations.
///
/// 阻断场景：
/// - 参数校验（空路径、扩展名变更、源不存在、不支持重命名的实体类型）
/// - .code 文件改名（仅允许移动）
/// - 目标目录不存在（不支持自动补全目录）
/// - 目标位置已有同名文件（精确 / 仅大小写不同，不支持文件覆盖）
/// - 目标位置已有同名目录（精确 / 仅大小写不同，不支持目录合并或覆盖）
///
/// 放行场景：
/// - canvas 文件重命名、移动、移动+改名
/// - .code 文件移动（不改名）
/// - 目录重命名、目录移动
/// - 纯大小写变更重命名（源自身不算冲突）
/// </summary>
[Collection("WorkspaceState")]
public sealed class FileRenameTests : TempDirectoryTestFixture
{
    // 最小合法 canvas 二进制（4 字节，足以让 CanvasEntity 加载成功）
    private static readonly byte[] MinimalCanvasBytes = [0x10, 0x01, 0x00, 0x00];

    private int _counter;
    private FileRename? _rename;
    private ProjectModel? _model;

    public FileRenameTests() : base("Toolkit.Core.Tests.FileRename") { }

    // 每个测试用独立子目录，避免 model 缓存污染
    private string CreateTestProject()
    {
        int id = System.Threading.Interlocked.Increment(ref _counter);
        string projectRoot = SystemIOWrapper.Combine(TempDir, $"proj_{id}");
        SystemIOWrapper.CreateDirectory(projectRoot);

        // setting.json
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json"),
            "guid:0123456789abcdef0123456789abcdef\nname:test\nmainMap:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        SystemIOWrapper.WriteAllText(
            SystemIOWrapper.Combine(projectRoot, "setting.json.guid"),
            "0123456789abcdef0123456789abcdef");

        // 地图（mainMap 必须存在）
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(projectRoot, "map.map"), [0x01, 0x02]);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(projectRoot, "map.map.guid"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        string mapDir = SystemIOWrapper.Combine(projectRoot, "map.map_");
        SystemIOWrapper.CreateDirectory(mapDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "mech.json"), "{\"list\":[]}");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(mapDir, "map.json"), "{\"list\":[]}");

        // ui/ 目录：foo.canvas、bar.canvas
        string uiDir = SystemIOWrapper.Combine(projectRoot, "ui");
        SystemIOWrapper.CreateDirectory(uiDir);
        WriteCanvas(uiDir, "foo", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        WriteCanvas(uiDir, "bar", "cccccccccccccccccccccccccccccccc");

        // ui2/ 目录：baz.canvas
        string ui2Dir = SystemIOWrapper.Combine(projectRoot, "ui2");
        SystemIOWrapper.CreateDirectory(ui2Dir);
        WriteCanvas(ui2Dir, "baz", "dddddddddddddddddddddddddddddddd");

        // scripts/ 目录：test.code（代码仓库脚本，用于 .code 移动测试）
        string scriptsDir = SystemIOWrapper.Combine(projectRoot, "scripts");
        SystemIOWrapper.CreateDirectory(scriptsDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "test.code"), "begincodeV2\nname:test\nlines:\n//test\nendcode\n");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(scriptsDir, "test.code.guid"), "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

        // scripts2/ 目录（空，用于 .code 移动目标）
        string scripts2Dir = SystemIOWrapper.Combine(projectRoot, "scripts2");
        SystemIOWrapper.CreateDirectory(scripts2Dir);

        return projectRoot;
    }

#pragma warning disable TK006 // 测试辅助：写入 canvas 文件对
    private static void WriteCanvas(string dir, string name, string guid)
    {
        SystemIOWrapper.WriteAllBytes(SystemIOWrapper.Combine(dir, $"{name}.canvas"), MinimalCanvasBytes);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(dir, $"{name}.canvas.guid"), guid);
    }
#pragma warning restore TK006

    [MemberNotNull(nameof(_model), nameof(_rename))]
    private void Init(string projectRoot)
    {
        _model = TestWorkspace.LoadProject(projectRoot);
        _rename = new FileRename();
    }

    // =========================================================================
    // 阻断：参数校验
    // =========================================================================

    [Fact]
    public void Fail_When_Path_Is_Empty()
    {
        Init(CreateTestProject());
        ArgumentException ex = Assert.Throws<ArgumentException>(() => _rename.Rename("", "ui/bar.canvas", _model));
        Assert.Contains("path", ex.Message);
    }

    [Fact]
    public void Fail_When_NewPath_Is_Empty()
    {
        Init(CreateTestProject());
        ArgumentException ex = Assert.Throws<ArgumentException>(() => _rename.Rename("ui/foo.canvas", "", _model));
        Assert.Contains("path", ex.Message);
    }

    [Fact]
    public void Fail_When_Source_Equals_Target()
    {
        Init(CreateTestProject());
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/foo.canvas", _model);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Fail_When_Extension_Changed()
    {
        Init(CreateTestProject());
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/foo.png", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("扩展名", result.Errors[0].Message);
    }

    [Fact]
    public void Fail_When_Source_Not_Found()
    {
        Init(CreateTestProject());
        RefactorResult<FileRenameData> result = _rename.Rename("ui/nonexistent.canvas", "ui/other.canvas", _model);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Fail_When_Entity_Not_Renameable()
    {
        Init(CreateTestProject());
        // map.map_ 是 MapEntity，不支持重命名
        RefactorResult<FileRenameData> result = _rename.Rename("map.map_", "map2.map_", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持", result.Errors[0].Message);
    }

    [Fact]
    public void Fail_When_Code_File_Renamed()
    {
        Init(CreateTestProject());
        // .code 改名（不同名，同目录）→ 阻断
        RefactorResult<FileRenameData> result = _rename.Rename("scripts/test.code", "scripts/renamed.code", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("refactor_rename_script", result.Errors[0].Message);
    }

    // =========================================================================
    // 阻断：目标目录不存在
    // =========================================================================

    [Fact]
    public void Fail_When_Target_Dir_Not_Exist()
    {
        Init(CreateTestProject());
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "nonexistent/foo.canvas", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持自动补全目录", result.Errors[0].Message);
    }

    // =========================================================================
    // 阻断：目标位置已有同名文件
    // =========================================================================

    [Fact]
    public void Fail_When_Target_File_Exists_ExactMatch()
    {
        Init(CreateTestProject());
        // ui/bar.canvas 已存在，尝试把 foo 移动过去
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/bar.canvas", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持通过移动进行文件覆盖", result.Errors[0].Message);
    }

    [Fact]
    public void Fail_When_Target_File_Exists_CaseInsensitive()
    {
        Init(CreateTestProject());
        // ui/BAR.canvas 与 ui/bar.canvas 只能在大小写敏感文件系统上共存，当前不允许通过重命名制造该状态
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/BAR.canvas", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持通过移动进行文件覆盖", result.Errors[0].Message);
    }

    // =========================================================================
    // 阻断：目标位置已有同名目录
    // =========================================================================

    [Fact]
    public void Fail_When_Target_Dir_Exists_ExactMatch()
    {
        Init(CreateTestProject());
        // ui2 已存在，尝试把 ui 改名为 ui2
        RefactorResult<FileRenameData> result = _rename.Rename("ui", "ui2", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持通过移动进行目录合并或覆盖目录", result.Errors[0].Message);
    }

    [Fact]
    public void Fail_When_Target_Dir_Exists_CaseInsensitive()
    {
        Init(CreateTestProject());
        // UI2 与 ui2 只能在大小写敏感文件系统上共存，当前不允许通过重命名制造该状态
        RefactorResult<FileRenameData> result = _rename.Rename("ui", "UI2", _model);
        Assert.False(result.IsSuccess);
        Assert.Contains("不支持通过移动进行目录合并或覆盖目录", result.Errors[0].Message);
    }

    // =========================================================================
    // 放行：canvas 文件操作
    // =========================================================================

    [Fact]
    public void Success_Canvas_Rename_SameDir()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/renamed.canvas", _model);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("ui/foo.canvas", result.Data.OldRelativePath);
        Assert.Equal("ui/renamed.canvas", result.Data.NewRelativePath);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/renamed.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/renamed.canvas.guid")));
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "ui/renamed.canvas.guid")));
    }

    [Fact]
    public void Success_Canvas_Move_ToOtherDir()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui2/foo.canvas", _model);
        Assert.True(result.IsSuccess);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/foo.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/foo.canvas.guid")));
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "ui2/foo.canvas.guid")));
    }

    [Fact]
    public void Success_Canvas_Move_And_Rename()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui2/moved.canvas", _model);
        Assert.True(result.IsSuccess);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/moved.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/moved.canvas.guid")));
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "ui2/moved.canvas.guid")));
    }

    [Fact]
    public void McpRenameFile_Should_Move_Canvas_And_Guid()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices services = new(_model, new ProjectLogicContext(_model), browser);
        using McpToolRegistry registry = new(services, projectRoot, null, registerHandlers: false);

        using JsonDocument requestDoc = JsonDocument.Parse("""
            {
              "old_relative_path": "ui/foo.canvas",
              "new_relative_path": "ui2/mcpMoved.canvas"
            }
            """);
        MethodInfo? executeMethod = typeof(McpToolRegistry)
            .GetMethod("ExecuteRenameFile", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(executeMethod);

        ITuple? resultTuple = Assert.IsAssignableFrom<ITuple>(executeMethod.Invoke(registry, [requestDoc.RootElement]));
        Assert.NotNull(resultTuple);
        IReadOnlyList<ToolError> errors = Assert.IsAssignableFrom<IReadOnlyList<ToolError>>(resultTuple[1]);
        IReadOnlyList<string> affectedPathList = Assert.IsAssignableFrom<IReadOnlyList<string>>(resultTuple[3]);
        Assert.Empty(errors);
        Assert.Contains("ui2/mcpMoved.canvas", affectedPathList);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/mcpMoved.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui2/mcpMoved.canvas.guid")));
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "ui2/mcpMoved.canvas.guid")));
    }

    // =========================================================================
    // 放行：.code 文件移动（不改名）
    // =========================================================================

    [Fact]
    public void Success_Code_File_Move_Without_Rename()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        // 同名移动到 scripts2/，不改名 → 允许
        RefactorResult<FileRenameData> result = _rename.Rename("scripts/test.code", "scripts2/test.code", _model);
        Assert.True(result.IsSuccess);
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "scripts/test.code")));
        Assert.False(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "scripts/test.code.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "scripts2/test.code")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "scripts2/test.code.guid")));
        Assert.Equal("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "scripts2/test.code.guid")));
    }

    // =========================================================================
    // 放行：目录操作
    // =========================================================================

    [Fact]
    public void Success_Directory_Rename()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        RefactorResult<FileRenameData> result = _rename.Rename("ui", "ui_renamed", _model);
        Assert.True(result.IsSuccess);
        Assert.False(SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(projectRoot, "ui")));
        Assert.True(SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(projectRoot, "ui_renamed")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui_renamed/foo.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui_renamed/foo.canvas.guid")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui_renamed/bar.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui_renamed/bar.canvas.guid")));
    }

    [Fact]
    public void Success_Directory_Move_Into_Other_Dir()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        // 把 ui2 移动到 ui 内部（ui/ui2）
        RefactorResult<FileRenameData> result = _rename.Rename("ui2", "ui/ui2", _model);
        Assert.True(result.IsSuccess);
        Assert.False(SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(projectRoot, "ui2")));
        Assert.True(SystemIOWrapper.DirectoryExists(SystemIOWrapper.Combine(projectRoot, "ui/ui2")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/ui2/baz.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "ui/ui2/baz.canvas.guid")));
        Assert.Equal("dddddddddddddddddddddddddddddddd", SystemIOWrapper.ReadAllText(SystemIOWrapper.Combine(projectRoot, "ui/ui2/baz.canvas.guid")));
    }

    // =========================================================================
    // 放行：纯大小写变更（源自身不算冲突）
    // =========================================================================

    [Fact]
    public void Success_CaseOnly_Rename_File()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        // foo.canvas → FOO.canvas，源自身不算冲突
        RefactorResult<FileRenameData> result = _rename.Rename("ui/foo.canvas", "ui/FOO.canvas", _model);
        Assert.True(result.IsSuccess);
        string[] canvasFiles = SystemIOWrapper.GetFiles(SystemIOWrapper.Combine(projectRoot, "ui"), "*.canvas", SearchOption.TopDirectoryOnly);
        Assert.Contains(SystemIOWrapper.Combine(projectRoot, "ui/FOO.canvas"), canvasFiles);
        Assert.DoesNotContain(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas"), canvasFiles);
        string[] guidFiles = SystemIOWrapper.GetFiles(SystemIOWrapper.Combine(projectRoot, "ui"), "*.guid", SearchOption.TopDirectoryOnly);
        Assert.Contains(SystemIOWrapper.Combine(projectRoot, "ui/FOO.canvas.guid"), guidFiles);
        Assert.DoesNotContain(SystemIOWrapper.Combine(projectRoot, "ui/foo.canvas.guid"), guidFiles);
    }

    [Fact]
    public void Success_CaseOnly_Rename_Directory()
    {
        string projectRoot = CreateTestProject();
        Init(projectRoot);
        // ui → UI，源自身不算冲突
        RefactorResult<FileRenameData> result = _rename.Rename("ui", "UI", _model);
        Assert.True(result.IsSuccess);
        string[] rootDirs = SystemIOWrapper.GetDirectories(projectRoot, "*", SearchOption.TopDirectoryOnly);
        Assert.Contains(SystemIOWrapper.Combine(projectRoot, "UI"), rootDirs);
        Assert.DoesNotContain(SystemIOWrapper.Combine(projectRoot, "ui"), rootDirs);
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "UI/foo.canvas")));
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(projectRoot, "UI/foo.canvas.guid")));
    }

}
