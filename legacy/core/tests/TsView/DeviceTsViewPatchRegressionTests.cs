using Xunit;
using System.Text;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// 针对已知问题的补丁尝试，用于重现并锁定错误输出。
/// </summary>
[Collection("WorkspaceState")]
public sealed class DeviceTsViewPatchRegressionTests
{
    [Fact]
    public void Patch_Map54_InsertJSDoc_WithoutBaseTsView_ShouldNotProduceNewErrors()
    {
        // 完全模拟 MCP patch_mech_view：不传 baseTsView，让它从文件重新生成
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        // 先 Read 获取当前视图内容，用于构造 patch
        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        string[] baseLines = TextLines.SplitLines(readData.TsView);

        // 找到 @events 和 class 行
        int eventsLineNum = -1;
        int classLineNum = -1;
        for (int i = 0; i < baseLines.Length; i++)
        {
            if (baseLines[i].Contains("@events(\"clientStart\"", StringComparison.Ordinal))
                eventsLineNum = i + 1;
            if (baseLines[i].Contains("export class Device_弹幕_54", StringComparison.Ordinal))
            {
                classLineNum = i + 1;
                break;
            }
        }
        Assert.True(eventsLineNum > 0 && classLineNum > 0, $"未找到目标行: events={eventsLineNum}, class={classLineNum}");

        // 在 @events 和 class 之间插入 JSDoc
        string[] patchLines = new[]
        {
            baseLines[eventsLineNum - 1],
            "    /** 54号零件注释：负责处理弹幕消息和游戏主逻辑 */",
            baseLines[classLineNum - 1]
        };
        TsViewPatch patch = new TsViewPatch(eventsLineNum, classLineNum, patchLines);

        // 关键：不传 baseTsView，和 MCP 一样让它重新生成
        (DeviceTsViewPatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("map.map_/54", new[] { patch }, baseTsView: null, persistChanges: false);

        if (errors.Count > 0)
        {
            string allErrors = string.Join("\n---\n", errors.Select(static e => $"[{e.Path}:{e.Line}] {e.Message}"));
            Assert.Fail($"patch_mech_view 产生 {errors.Count} 条错误:\n{allErrors}");
        }
        Assert.NotNull(data);
    }

    [Fact]
    public void Patch_Map255_InsertJSDoc_WithExistingLegacyInlineCanvas_ShouldNotProduceNewErrors()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/255");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        string[] baseLines = TextLines.SplitLines(readData.TsView);
        int eventsLineNum = -1;
        int classLineNum = -1;
        for (int i = 0; i < baseLines.Length; i++)
        {
            if (baseLines[i].Contains("@events(", StringComparison.Ordinal))
                eventsLineNum = i + 1;
            if (baseLines[i].Contains("export class Device_参数调节模块_255", StringComparison.Ordinal))
            {
                classLineNum = i + 1;
                break;
            }
        }

        Assert.True(eventsLineNum > 0 && classLineNum > 0, $"未找到目标行: events={eventsLineNum}, class={classLineNum}");

        string[] patchLines = new[]
        {
            baseLines[eventsLineNum - 1],
            "    /** 255号零件注释 */",
            baseLines[classLineNum - 1]
        };
        TsViewPatch patch = new TsViewPatch(eventsLineNum, classLineNum, patchLines);

        (DeviceTsViewPatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("map.map_/255", new[] { patch }, baseTsView: null, persistChanges: false);

        if (errors.Count > 0)
        {
            string allErrors = string.Join("\n---\n", errors.Select(static e => $"[{e.Path}:{e.Line}] {e.Message}"));
            Assert.Fail($"patch_mech_view 产生 {errors.Count} 条错误:\n{allErrors}");
        }

        Assert.NotNull(data);
    }

    [Fact]
    public void Patch_Map255_DeleteGlobalDeclarationWithRemainingThisUsage_ShouldReturnSingleDeletedGlobalDiagnostic()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/255");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        Assert.Contains("private 参数调节列表: BeList = BeList.GlobalVarInitialize();", readData.TsView, StringComparison.Ordinal);
        Assert.Contains("this.参数调节列表", readData.TsView, StringComparison.Ordinal);

        string[] baseLines = TextLines.SplitLines(readData.TsView);
        string[] patchedLines = baseLines
            .Where(static line =>
                !(line.Contains("@bsVar", StringComparison.Ordinal) && line.Contains("参数调节列表", StringComparison.Ordinal)) &&
                !string.Equals(line, "    private 参数调节列表: BeList = BeList.GlobalVarInitialize();", StringComparison.Ordinal))
            .ToArray();
        TsViewPatch patch = new(1, baseLines.Length, patchedLines);

        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
            services.DeviceTsView.Patch("map.map_/255", new[] { patch }, baseTsView: readData.TsView, persistChanges: false);

        Assert.NotEmpty(errors);
        string diagnostics = string.Join("\n", errors.Select(static e => $"[{e.Severity}|{e.Path}:{e.Line}|{e.Code}:{e.Subject}] {e.Message}"));
        Assert.True(
            errors.Count(static e => string.Equals(e.Code, TsViewDiagnosticCodes.DeletedGlobalVariableStillUsed, StringComparison.Ordinal)) == 1,
            diagnostics);
        string allErrors = string.Join("\n", errors.Select(static e => e.Message));
        Assert.Contains("将被删除的全局变量仍在 TS 视图中被使用: 参数调节列表", allErrors, StringComparison.Ordinal);
        Assert.Contains("删除全局变量声明会让这些 this.<变量名> 引用失去全局变量语义", allErrors, StringComparison.Ordinal);
        Assert.Contains("refactor_vars_to_local", allErrors, StringComparison.Ordinal);
        Assert.True(
            !allErrors.Contains("this. 仅允许用于全局变量: 参数调节列表", StringComparison.Ordinal),
            diagnostics);
        Assert.True(
            !allErrors.Contains("未知全局变量: 参数调节列表", StringComparison.Ordinal),
            diagnostics);
    }

    [Fact]
    public void Patch_Map54_AddLegacyFieldIntoInlineCanvas_ShouldBeRejected()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        string[] baseLines = TextLines.SplitLines(readData.TsView);
        int targetLineNum = -1;
        for (int i = 0; i < baseLines.Length; i++)
        {
            if (baseLines[i].Contains("<BeUIRect name=\"background\" pos=\"0,0,0\"", StringComparison.Ordinal))
            {
                targetLineNum = i + 1;
                break;
            }
        }

        Assert.True(targetLineNum > 0, "未找到目标 Canvas 行");

        string patchedLine = baseLines[targetLineNum - 1].Replace(" pos=\"0,0,0\"", " legacypos=\"0,0,0\"", StringComparison.Ordinal);
        TsViewPatch patch = new TsViewPatch(targetLineNum, targetLineNum, new[] { patchedLine });

        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("map.map_/54", new[] { patch }, baseTsView: readData.TsView, persistChanges: false);

        Assert.NotNull(errors);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, static e => e.Message.Contains("legacypos", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_Map54_OnDanmu_ShouldResolveConcreteEventEnum()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");

        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        Assert.Contains("$type.equal(BeEnum.fromBeConst<EventCommentGiftLike>(EventCommentGiftLike.Comment))", readData.TsView, StringComparison.Ordinal);
    }

    [Fact]
    public void Patch_MechBullet2300_GlobalColorEqual_ShouldFailAsUnknownGlobalMethod()
    {
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("机械/子弹/2300.mech_/0");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        Assert.Contains("color2: BeColor", readData.TsView, StringComparison.Ordinal);

        string[] baseLines = TextLines.SplitLines(readData.TsView);
        int targetLineNum = -1;
        for (int i = 0; i < baseLines.Length; i++)
        {
            if (baseLines[i].Contains("setPaintColor(color1, BeBool.fromBeConst(\"0\"));", StringComparison.Ordinal))
            {
                targetLineNum = i + 1;
                break;
            }
        }
        Assert.True(targetLineNum > 0, "未找到 Public_SetColor 中的 setPaintColor 调用");

        string targetLine = baseLines[targetLineNum - 1];
#pragma warning disable TK010 // 这里需要复制目标 TS 行缩进；TrimStart 只用于计算前导空白长度。
        string indent = targetLine[..(targetLine.Length - targetLine.TrimStart().Length)];
#pragma warning restore TK010
        TsViewPatch patch = new(targetLineNum, targetLineNum, new[]
        {
            targetLine,
            indent + "G.color.equal(color2, color2);"
        });

        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) =
            services.DeviceTsView.Patch("机械/子弹/2300.mech_/0", new[] { patch }, baseTsView: readData.TsView, persistChanges: false);

        Assert.NotEmpty(errors);
        string allErrors = string.Join("\n", errors.Select(static e => e.Message));
        Assert.Contains("未知全局方法: color.equal", allErrors, StringComparison.Ordinal);
        Assert.DoesNotContain("无法推断变量类型", allErrors, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_CachedVsRegenerated_ViewContent_ShouldBeIdentical()
    {
        // 对比：两次独立读取的视图应该相同
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        // 第一次读取（单例）
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services1 = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        (DeviceTsViewReadData? data1, IReadOnlyList<ToolError> errors1, IReadOnlyList<ToolError> _) = services1.DeviceTsView.Read("map.map_/54");
        Assert.Empty(errors1);
        Assert.NotNull(data1);

        // 第二次读取（重新初始化单例，避免跨实例缓存污染）
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services2 = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        (DeviceTsViewReadData? data2, IReadOnlyList<ToolError> errors2, IReadOnlyList<ToolError> _) = services2.DeviceTsView.Read("map.map_/54");
        Assert.Empty(errors2);
        Assert.NotNull(data2);

        // 对比内容
        string[] lines1 = TextLines.SplitLines(data1.TsView);
        string[] lines2 = TextLines.SplitLines(data2.TsView);

        string? diff = TestComparison.DiffLines(lines1, lines2);
        if (diff != null)
        {
            Assert.Fail($"视图内容不一致:\n{diff}");
        }
    }

    [Fact]
    public void Compare_WithBaseTsView_VsWithout_PatchErrors()
    {
        // 对比：传 baseTsView vs 不传 baseTsView 的 Patch 结果差异
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        // 先从原始工程读取视图（用于构造 patch）
        ToolkitServices.ShutdownSingleton();
        ToolkitServices originalServices = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = originalServices.DeviceTsView.Read("map.map_/54");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);
        ToolkitServices.ShutdownSingleton();

        string[] baseLines = TextLines.SplitLines(readData.TsView);

        // 找到 @events 和 class 行
        int eventsLineNum = -1;
        int classLineNum = -1;
        for (int i = 0; i < baseLines.Length; i++)
        {
            if (baseLines[i].Contains("@events(\"clientStart\"", StringComparison.Ordinal))
                eventsLineNum = i + 1;
            if (baseLines[i].Contains("export class Device_弹幕_54", StringComparison.Ordinal))
            {
                classLineNum = i + 1;
                break;
            }
        }
        Assert.True(eventsLineNum > 0 && classLineNum > 0);

        string[] patchLines = new[]
        {
            baseLines[eventsLineNum - 1],
            "    /** 54号零件注释 */",
            baseLines[classLineNum - 1]
        };
        TsViewPatch patch = new TsViewPatch(eventsLineNum, classLineNum, patchLines);

        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errorsA, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("map.map_/54", new[] { patch }, baseTsView: readData.TsView, persistChanges: false);
        (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> errorsB, IReadOnlyList<ToolError> _) = services.DeviceTsView.Patch("map.map_/54", new[] { patch }, baseTsView: null, persistChanges: false);

        StringBuilder output = new System.Text.StringBuilder();
        output.AppendLine($"Case A (传 baseTsView): {errorsA.Count} 条错误");
        foreach (ToolError? e in errorsA.Take(5))
        {
            output.AppendLine($"  [{e.Path}:{e.Line}] {e.Message}");
        }
        output.AppendLine($"Case B (不传 baseTsView): {errorsB.Count} 条错误");
        foreach (ToolError? e in errorsB.Take(5))
        {
            output.AppendLine($"  [{e.Path}:{e.Line}] {e.Message}");
        }

        Assert.True(errorsA.Count == errorsB.Count, $"错误数量不一致:\n{output}");
    }

    [Fact]
    public void Compare_DiskFile_VsGenerated_ViewContent()
    {
        // 对比磁盘上已存在的视图文件 vs 运行时生成的视图
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        string viewFilePath = SystemIOWrapper.Combine(projectRootFullPath, "temp", "toolkit", "views", "device", "map.map_", "54.ts");
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        // 如果磁盘文件不存在，跳过
        if (!SystemIOWrapper.FileExists(viewFilePath))
        {
            return;
        }

        // 读取磁盘文件
        string diskContent = SystemIOWrapper.ReadAllText(viewFilePath);
        string[] diskLines = TextLines.SplitLines(diskContent);

        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);
        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");
        Assert.Empty(errors);
        Assert.NotNull(data);

        string[] generatedLines = TextLines.SplitLines(data.TsView);

        // 对比（忽略 reference 路径差异）
        string[] filteredDiskLines = diskLines.Where(static l => !l.StartsWith("/// <reference", StringComparison.Ordinal)).ToArray();
        string[] filteredGenLines = generatedLines.Where(static l => !l.StartsWith("/// <reference", StringComparison.Ordinal)).ToArray();

        string? diff = TestComparison.DiffLines(filteredDiskLines, filteredGenLines);
        if (diff != null)
        {
            Assert.Fail($"磁盘文件与运行时生成不一致:\n{diff}");
        }
    }

    /// <summary>
    /// 测试直接 Patch 流程（无需 McpViewWorkspace）。
    /// 新基础设施：使用 ProjectModel + ProjectLogicContext + ToolkitServices。
    /// </summary>
    [Fact]
    public void DirectPatch_Flow()
    {
        // 完全按照新基础设施流程
        string projectRootFullPath = SystemIOWrapper.Combine(ToolkitRepoPaths.ProjectsRoot, "水果切切切");
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        ToolkitServices.ShutdownSingleton();
        ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectRootFullPath, browser);

        // 1. 读取视图
        (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read("map.map_/54");
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        // 2. 构造补丁（在头部注释后插入 JSDoc）
        string[] currentLines = TextLines.SplitLines(readData.TsView);
        int eventsLineNum = -1;
        int classLineNum = -1;
        for (int i = 0; i < currentLines.Length; i++)
        {
            if (currentLines[i].Contains("@events(\"clientStart\"", StringComparison.Ordinal))
                eventsLineNum = i + 1;
            if (currentLines[i].Contains("export class Device_弹幕_54", StringComparison.Ordinal))
            {
                classLineNum = i + 1;
                break;
            }
        }

        // 如果没有找到特定的装饰器行，跳过此测试
        if (eventsLineNum <= 0 || classLineNum <= 0)
        {
            return;
        }

        Assert.True(eventsLineNum > 0 && classLineNum > 0);

        List<string> patchedLines = currentLines.ToList();
        patchedLines.Insert(eventsLineNum, "    /** 测试注释 */");

        // 3. 构建整文件替换补丁
        TsViewPatch[] patches = new[] { new TsViewPatch(1, currentLines.Length, patchedLines.ToArray()) };

        // 4. 调用 Patch
        (DeviceTsViewPatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings) = services.DeviceTsView.Patch("map.map_/54", patches, baseTsView: readData.TsView, persistChanges: false);

        // 5. 输出详细错误信息
        if (errors.Count > 0)
        {
            StringBuilder errorDetails = new System.Text.StringBuilder();
            errorDetails.AppendLine($"共 {errors.Count} 条错误：");
            foreach (ToolError e in errors)
            {
                errorDetails.AppendLine($"  [{e.Path}:{e.Line}] {e.Message}");
            }
            Assert.Fail($"Patch 产生错误:\n{errorDetails}");
        }

        Assert.NotNull(data);
    }

    /// <summary>
    /// 诊断：遍历所有测试工程的所有零件，解析 TS 视图，
    /// 检查 ScriptUpdates 数量是否与 desiredScriptLists 条目数一致。
    /// 不一致 = 全量重建时必然丢文件。
    /// </summary>
    [Fact]
    public void Diagnose_ScriptUpdates_Vs_DesiredScriptLists_Consistency()
    {
        List<string> inconsistencies = [];
        using BrowserRenderService browser = new(1, Array.Empty<int>());

        foreach (ProjectDirectory project in TestProjectEnumerator.EnumerateProjects())
        {
            ToolkitServices.ShutdownSingleton();
            ToolkitServices services = TestWorkspace.InitializeSingletonWithRecordingPersistence(project.Path, browser);

            string[] devicePaths = services.Model.ListDevices()
                .Select(static d => d.实体_完整相对路径标识)
                .ToArray();

            foreach (string? devicePath in devicePaths)
            {
                (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> _) = services.DeviceTsView.Read(devicePath);
                if (readErrors.Count > 0 || readData == null) continue;

                List<string> lines = TextLines.SplitLines(readData.TsView).ToList();
                List<ToolError> warnings = [];
                List<ToolError> errors = [];

                DeviceTsView.WritebackParseResult patchResult = DeviceTsView.ParseDeviceTsViewForWriteback(
                    services.Model,
                    lines,
                    devicePath,
                    warnings,
                    collectedErrors: errors);

                int desiredInlineCount = 0;
                foreach ((string _, List<DeviceTsView.DesiredScriptListEntry>? entries) in patchResult.DesiredScriptLists)
                    desiredInlineCount += entries.Count(static e => !GuidText.Validate(e.Guid));

                // 统计 updates 中的非 CodeRepo 条目数
                int updateInlineCount = patchResult.Updates.Count(static u => !GuidText.Validate(u.Guid));

                if (desiredInlineCount != updateInlineCount)
                {
                    string detail = $"[{project.Name}] {devicePath}: desiredScriptLists={desiredInlineCount}, ScriptUpdates={updateInlineCount}, errors={errors.Count}";
                    foreach (ToolError? e in errors.Take(3))
                        detail += $"\n  ERR: [{e.Path}:{e.Line}] {e.Message}";
                    inconsistencies.Add(detail);
                }
            }
        }

        if (inconsistencies.Count > 0)
        {
            Assert.Fail($"发现 {inconsistencies.Count} 个零件的 ScriptUpdates 与 desiredScriptLists 不一致（全量重建时会丢文件）:\n{string.Join("\n---\n", inconsistencies)}");
        }
    }
}
