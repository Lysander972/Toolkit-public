using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.Mcp;

/// <summary>
/// 验证 patch 写回路径拦截非 BMP 字符(emoji):LINT-TEXT-UNSUPPORTED-CHARACTER。
/// new_string 在字符串常量中注入 emoji(码点 &gt; 0xFFFF),写回 Bescript 文本被字符校验阻断。
/// DeviceTsView.Patch 是 patch_mech_view 的 TS 写回核心,handler 经 BuildPatch 调用同一校验链。
/// </summary>
[Collection("WorkspaceState")]
public sealed class PatchMechViewEmojiBlockTests
{
    [Fact]
    public void Patch_NewStringWithEmoji_ShouldBeBlockedByUnsupportedCharacterLint()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/255");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        // 在视图唯一的字符串常量(按钮 ★)中注入非 BMP 字符 😀
        string[] baseLines = TextLines.SplitLines(readData.TsView);
        string[] patchedLines = baseLines
            .Select(line => line.Contains("BeString.fromBeConst(\"★\")", StringComparison.Ordinal)
                ? line.Replace("BeString.fromBeConst(\"★\")", "BeString.fromBeConst(\"★😀\")", StringComparison.Ordinal)
                : line)
            .ToArray();
        Assert.NotEqual(baseLines, patchedLines);

        TsViewPatch patch = new(1, baseLines.Length, patchedLines);
        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
            services.DeviceTsView.Patch("map.map_/255", new[] { patch }, baseTsView: readData.TsView, persistChanges: false);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, static e => e.Message.Contains("不支持字符", StringComparison.Ordinal));
    }
}
