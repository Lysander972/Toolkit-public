using System.Reflection;
using Toolkit.Core.Infra;
using Xunit;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

// =============================================================================
// RefreshVersion 内容版本语义回归套件
//
// 契约：RefreshVersion 是内容版本——只在全树 diff 检测到真实实体变化时前进；
// 无影响刷新（心跳/自噪音）不得制造伪变更（下游视图重建/索引/Lint/树推送据此短路）。
// =============================================================================

[Collection("FileIO")]
public sealed class RefreshVersionContentSemanticsTests : TempDirectoryTestFixture
{
    public RefreshVersionContentSemanticsTests() : base("Toolkit.Core.Tests.RefreshVersionContentSemantics")
    {
    }

    private static void InvokeProcessPendingInvalidations(string source)
    {
        MethodInfo method = typeof(FileSystemCache).GetMethod("ProcessPendingInvalidations", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("未找到方法 ProcessPendingInvalidations");
        method.Invoke(FileSystemCache.Instance, new object?[] { source });
    }

    [Fact]
    public void NoImpactRefresh_KeepsVersion_ImpactRefresh_AdvancesVersion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WriteFile("setting.json", "{}");
        WriteFile("setting.json.guid", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        WriteFile("src/a.txt", "a");
        ProjectModel model = TestWorkspace.LoadProject(TempDir);
        long initialVersion = model.RefreshVersion;

        // 无任何文件变化的整体刷新：内容版本必须保持不变。
        InvokeProcessPendingInvalidations("test-无影响刷新");
        Assert.Equal(initialVersion, model.RefreshVersion);

        // 真实文件变化：内容版本必须前进。
        WriteFile("src/a.txt", "changed");
        InvokeProcessPendingInvalidations("test-有影响刷新");
        Assert.True(model.RefreshVersion > initialVersion,
            $"文件变化后 RefreshVersion 未前进（{model.RefreshVersion} <= {initialVersion}）");
    }

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = SystemIOWrapper.Combine(TempDir, relativePath);
        string directoryFullPath = SystemIOWrapper.GetParentDirectory(fullPath);
        if (!SystemIOWrapper.DirectoryExists(directoryFullPath))
        {
            SystemIOWrapper.CreateDirectory(directoryFullPath);
        }

        SystemIOWrapper.WriteAllText(fullPath, content);
    }
}
