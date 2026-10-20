using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

using Toolkit.Core.Canvas;

namespace Toolkit.Core.Tests.TsView;

/// <summary>
/// TsView 往返测试：读取源工程 -> 生成 TS 视图 -> 验证视图格式正确
/// 所有零件必须全部通过，任何错误立即终止测试
/// </summary>
[Collection("WorkspaceState")]
public sealed class DeviceTsViewRoundtripProjectTests
{
    public static IEnumerable<object[]> EnumerateProjects()
    {
        foreach (ProjectDirectory project in TestProjectEnumerator.EnumerateProjects())
        {
            yield return [project.Name, project.Path];
        }
    }

    [Theory]
    [MemberData(nameof(EnumerateProjects))]
    public void TsView_Roundtrip_ShouldMatch_Original_ProjectFiles(
        string projectName,
        string projectPath)
    {
        using BrowserRenderService browser = new(1, Array.Empty<int>());
        TestProgressReporter reporter = new TestProgressReporter($"TsViewRoundtrip:{projectName}");
        reporter.Report("start");
        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            ToolkitServices.ShutdownSingleton();
            ToolkitServices readServices = TestWorkspace.InitializeSingletonWithRecordingPersistence(projectPath, browser);

            string[] devicePaths = readServices.Model.ListDevices()
                .Select(static device => device.实体_完整相对路径标识)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();

            ConcurrentDictionary<string, string> tsViewCache = new ConcurrentDictionary<string, string>();
            ConcurrentDictionary<string, string[]> linesCache = new ConcurrentDictionary<string, string[]>();
            ConcurrentDictionary<string, IReadOnlyList<TsViewEditRange>> forbiddenRangesCache = new ConcurrentDictionary<string, IReadOnlyList<TsViewEditRange>>();

            // ========== 阶段1：从原始工程串行读取所有零件到内存 ==========
            for (int i = 0; i < devicePaths.Length; i++)
            {
                string devicePath = devicePaths[i];
                reporter.TryReport(i + 1, devicePaths.Length, "read");

                (DeviceTsViewReadData? readData, IReadOnlyList<ToolError> readErrs, IReadOnlyList<ToolError> _) = readServices.DeviceTsView.Read(devicePath);
                if (readErrs.Count > 0)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: Read failed:\n{readErrs[0]}");
                }

                Assert.NotNull(readData);
                string tsView = readData.TsView;
                string[] lines = TextLines.SplitLines(tsView);
                if (lines.Length == 0)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: TS view is empty");
                }

                tsViewCache[devicePath] = tsView;
                linesCache[devicePath] = lines;
                forbiddenRangesCache[devicePath] = readData.EditAllowance.ForbiddenRanges;
            }

            double readTime = sw.Elapsed.TotalSeconds;
            reporter.Report($"read done: {tsViewCache.Count}/{devicePaths.Length} in {readTime:F1}s");

            Assert.Equal(devicePaths.Length, tsViewCache.Count);

            // ========== 阶段2：对所有零件执行纯内存往返测试 ==========
            for (int i = 0; i < devicePaths.Length; i++)
            {
                string devicePath = devicePaths[i];
                reporter.TryReport(i + 1, devicePaths.Length, "patch");

                string tsView = tsViewCache[devicePath];
                string[] lines = linesCache[devicePath];
                IReadOnlyList<TsViewEditRange> forbiddenRanges = forbiddenRangesCache[devicePath];

                // 步骤1: 随机抽取片段进行补丁测试
                Random random = new Random(devicePath.GetHashCode());
                List<(int startLine, int endLine, string[] patchLines, bool expectBlocked)> patchTests = GenerateRandomPatchTests(lines, forbiddenRanges, random, maxTests: 5);

                foreach ((int startLine, int endLine, string[] patchLines, bool expectBlocked) in patchTests)
                {
                    TsViewPatch[] patches = new[] { new TsViewPatch(startLine, endLine, patchLines) };
                    (DeviceTsViewPatchData? _, IReadOnlyList<ToolError> patchErrs, IReadOnlyList<ToolError> _) = readServices.DeviceTsView.Patch(devicePath, patches, baseTsView: tsView, persistChanges: false);

                    // 固定骨架校验错误以此前缀开头
                    bool hasFixedStructureError = patchErrs.Any(static e => e.Message.StartsWith("TS视图固定骨架被改动", StringComparison.Ordinal));

                    if (expectBlocked)
                    {
                        if (!hasFixedStructureError)
                        {
                            Assert.Fail($"[{projectName}] {devicePath}: Patch [{startLine}-{endLine}] should be blocked by fixed structure check but passed");
                        }
                    }
                    else
                    {
                        if (hasFixedStructureError)
                        {
                            Assert.Fail($"[{projectName}] {devicePath}: Patch [{startLine}-{endLine}] unexpectedly blocked by fixed structure check:\n{string.Join("\n", patchErrs.Select(static e => e.Message))}");
                        }
                    }
                }

                // 步骤2: 完整 no-op patch
                TsViewPatch[] fullPatches = new[] { new TsViewPatch(1, lines.Length, lines) };
                (DeviceTsViewPatchData? fullPatchData, IReadOnlyList<ToolError> fullPatchErrs, IReadOnlyList<ToolError> _) = readServices.DeviceTsView.Patch(devicePath, fullPatches, baseTsView: tsView, persistChanges: false);
                if (fullPatchErrs.Count > 0)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: Full no-op patch failed:\n{string.Join("\n", fullPatchErrs.Select(static e => e.Message))}");
                }

                // 步骤3: 严格按行比较
                string? patchedTsView = fullPatchData?.PatchedTsView;
                if (patchedTsView == null)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: PatchedTsView is null");
                }

                // vars.json 必须无损（无任何写回意图）
                if (fullPatchData?.AffectedRelativePaths is { Count: > 0 })
                {
                    string[] varsTouched = fullPatchData.AffectedRelativePaths
                        .Where(static p => p.EndsWith("/vars.json", StringComparison.Ordinal))
                        .ToArray();
                    if (varsTouched.Length > 0)
                    {
                        Assert.Fail($"[{projectName}] {devicePath}: vars.json would change after no-op patch: {string.Join(", ", varsTouched)}");
                    }
                }

                string[] patchedLines = TextLines.SplitLines(patchedTsView);
                string? diffInfo = TestComparison.DiffLines(lines, patchedLines);
                if (diffInfo != null)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: TS view changed after patch_mech_view (no-op patch).\n{diffInfo}");
                }

                // 步骤4: 二次往返稳定性测试
                TsViewPatch[] patches2 = new[] { new TsViewPatch(1, patchedLines.Length, patchedLines) };
                (DeviceTsViewPatchData? patchData2, IReadOnlyList<ToolError> patchErrs2, IReadOnlyList<ToolError> _) = readServices.DeviceTsView.Patch(devicePath, patches2, baseTsView: patchedTsView, persistChanges: false);

                if (patchErrs2.Count > 0)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: Second roundtrip Patch failed:\n{string.Join("\n", patchErrs2.Select(static e => e.Message))}");
                }

                if (patchData2?.PatchedTsView == null)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: Second roundtrip PatchedTsView is null");
                }

                if (patchData2.AffectedRelativePaths is { Count: > 0 })
                {
                    string[] varsTouched = patchData2.AffectedRelativePaths
                        .Where(static p => p.EndsWith("/vars.json", StringComparison.Ordinal))
                        .ToArray();
                    if (varsTouched.Length > 0)
                    {
                        Assert.Fail($"[{projectName}] {devicePath}: vars.json would change after second no-op patch: {string.Join(", ", varsTouched)}");
                    }
                }

                string[] lines2 = TextLines.SplitLines(patchData2.PatchedTsView);
                string? diffInfo2 = TestComparison.DiffLines(patchedLines, lines2);
                if (diffInfo2 != null)
                {
                    Assert.Fail($"[{projectName}] {devicePath}: TS view unstable after second roundtrip.\n{diffInfo2}");
                }
            }
        }
        finally
        {
            ToolkitServices.ShutdownSingleton();
        }

        sw.Stop();
        reporter.Report($"done: total {sw.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// 生成随机补丁测试用例：
    /// 1. 随机抽取文件片段
    /// 2. 对片段内容进行换行变换（添加/删除换行）
    /// 3. 判断是否应该被固定骨架校验阻断
    ///
    /// 固定骨架行：三区段以外行的**内容**须与基线一致；删除非空骨架行会阻断。
    /// </summary>
    private static List<(int startLine, int endLine, string[] patchLines, bool expectBlocked)> GenerateRandomPatchTests(
        string[] lines,
        IReadOnlyList<TsViewEditRange> forbiddenRanges,
        Random random,
        int maxTests)
    {
        List<(int, int, string[], bool)> tests = [];
        if (lines.Length < 5) return tests;

        for (int i = 0; i < maxTests; i++)
        {
            // 随机选取片段范围（1-based）
            int start = random.Next(1, lines.Length);
            int length = random.Next(1, Math.Min(10, lines.Length - start + 2));
            int end = Math.Min(start + length - 1, lines.Length);

            // 提取片段内容（0-based）
            string[] segment = lines.Skip(start - 1).Take(end - start + 1).ToArray();
            if (segment.Length == 0) continue;

            // 随机对换行进行变换
            (string[] modified, int deletedLineNum) = ApplyRandomNewlineTransform(
                segment,
                start,
                random);

            // 固定骨架校验：仅当固定骨架行被删除时才阻断
            // 插入空行不改变现有行映射下的内容对比，不应阻断
            // 删除空行仅当该行落在固定骨架区间内才阻断
            bool expectBlocked = false;
            if (deletedLineNum >= 0)
            {
                foreach (TsViewEditRange forbidden in forbiddenRanges)
                {
                    if (deletedLineNum >= forbidden.StartLine && deletedLineNum <= forbidden.EndLine)
                    {
                        expectBlocked = true;
                        break;
                    }
                }
            }

            tests.Add((start, end, modified, expectBlocked));
        }

        return tests;
    }

    /// <summary>
    /// 对文本片段进行随机换行变换（内容不变，只改换行）：
    /// - 有 50% 概率在某行后添加空行
    /// - 有 30% 概率删除某个空行
    /// - 有 20% 概率保持不变
    ///
    /// 返回 (变换后的行数组, 被删除行的绝对行号)。未删除时行号为 -1。
    /// </summary>
    private static (string[] Lines, int DeletedLineNum) ApplyRandomNewlineTransform(
        string[] segment,
        int segmentStartLine,
        Random random)
    {
        int deletedLineNum = -1;
        int action = random.Next(100);

        if (action < 50 && segment.Length > 0)
        {
            // 在某行后插入空行 - 不会改变任何现有行内容
            int insertPos = random.Next(0, segment.Length);
            List<string> result = [];

            for (int i = 0; i < segment.Length; i++)
            {
                result.Add(segment[i]);
                if (i == insertPos)
                {
                    result.Add(string.Empty);
                }
            }

            return (result.ToArray(), deletedLineNum);
        }
        else if (action < 80 && segment.Length > 1)
        {
            // 尝试删除一个空行
            int emptyLineIndex = -1;
            for (int i = 0; i < segment.Length; i++)
            {
                if (string.Equals(segment[i], string.Empty, StringComparison.Ordinal))
                {
                    emptyLineIndex = i;
                    break;
                }
            }

            if (emptyLineIndex >= 0)
            {
                deletedLineNum = segmentStartLine + emptyLineIndex;
                List<string> result = [];
                for (int i = 0; i < segment.Length; i++)
                {
                    if (i != emptyLineIndex)
                    {
                        result.Add(segment[i]);
                    }
                }
                return (result.ToArray(), deletedLineNum);
            }
        }

        // 保持不变
        return (segment, deletedLineNum);
    }

}
