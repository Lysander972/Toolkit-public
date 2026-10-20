using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

[Collection("WorkspaceState")]
public sealed class FileTreeBindingTests
{
    [Fact]
    public void SetCoverage_Allows_GetEntity()
    {
        FileTreeBinding binding = FileTreeBinding.Instance;
        binding.Clear();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main", "0123456789abcdef0123456789abcdef");

        // 构建覆盖映射
        EntityCoverage coverage = new EntityCoverage();
        coverage.Files[map.map文件_完整相对路径] = map;
        coverage.Files[map.GUID文件_完整相对路径] = map;
        coverage.Directories[map.派生目录_完整相对路径] = map;
        coverage.Files[map.map配置_完整相对路径] = map;
        coverage.Files[map.mech配置_完整相对路径] = map;

        binding.SetCoverage(coverage);

        Assert.Same(map, binding.GetEntity(map.map文件_完整相对路径));
        Assert.Same(map, binding.GetEntity(map.GUID文件_完整相对路径));
        Assert.Same(map, binding.GetEntity(map.派生目录_完整相对路径));
    }

    [Fact]
    public void GetEntity_ParentLookup_FindsParentEntity()
    {
        FileTreeBinding binding = FileTreeBinding.Instance;
        binding.Clear();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "main", "0123456789abcdef0123456789abcdef");

        // 只绑定目录，不绑定内部文件
        EntityCoverage coverage = new EntityCoverage();
        coverage.Directories[map.派生目录_完整相对路径] = map;

        binding.SetCoverage(coverage);

        // 内部文件应该向上找到父实体
        string childFilePath = map.派生目录_完整相对路径 + "/script.ts";
        Assert.Same(map, binding.GetEntity(childFilePath));
    }

    [Fact]
    public void AnalyzeChanges_Detects_Modified_And_Added_Files()
    {
        FileTreeBinding binding = FileTreeBinding.Instance;
        binding.Clear();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        DirectoryEntity dir = TestEntityFactory.CreateDirectory(root, "testdir");

        // 设置覆盖映射
        EntityCoverage coverage = new EntityCoverage();
        coverage.Directories[dir.实体_完整相对路径标识] = dir;
        binding.SetCoverage(coverage);

        FileTreeNode oldRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        oldRoot.AddFile(dir.实体_完整相对路径标识, new FileSignature(1, 10));
        FileTreeSnapshot oldSnapshot = new FileTreeSnapshot { Root = oldRoot };

        FileTreeNode newRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        newRoot.AddFile(dir.实体_完整相对路径标识, new FileSignature(2, 10));
        newRoot.AddFile("added.txt", new FileSignature(1, 1));
        FileTreeSnapshot newSnapshot = new FileTreeSnapshot { Root = newRoot };

        FileTreeChangeImpact impact = binding.AnalyzeChanges(oldSnapshot, newSnapshot);

        Assert.Contains(dir, impact.EntitiesToReload);
        Assert.Contains(dir.实体_完整相对路径标识, impact.ModifiedRelativePaths);
        Assert.Contains("added.txt", impact.NewRelativePaths);
    }

    [Fact]
    public void AnalyzeChanges_Should_Record_Guid_File_Modification()
    {
        FileTreeBinding binding = FileTreeBinding.Instance;
        binding.Clear();

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        CanvasEntity canvas = TestEntityFactory.CreateCanvas(root, "ui/main.canvas", "0123456789abcdef0123456789abcdef", [1]);
        EntityCoverage coverage = root.GetAllCoverage();
        binding.SetCoverage(coverage);

        FileTreeNode oldRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        FileTreeNode oldUi = oldRoot.AddChildDirectory("ui");
        oldUi.AddFile("main.canvas", new FileSignature(1, 10));
        oldUi.AddFile("main.canvas.guid", new FileSignature(1, 10));
        FileTreeSnapshot oldSnapshot = new FileTreeSnapshot { Root = oldRoot };

        FileTreeNode newRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        FileTreeNode newUi = newRoot.AddChildDirectory("ui");
        newUi.AddFile("main.canvas", new FileSignature(1, 10));
        newUi.AddFile("main.canvas.guid", new FileSignature(2, 10));
        FileTreeSnapshot newSnapshot = new FileTreeSnapshot { Root = newRoot };

        FileTreeChangeImpact impact = binding.AnalyzeChanges(oldSnapshot, newSnapshot);

        Assert.Contains(canvas, impact.EntitiesToReload);
        Assert.Contains("ui/main.canvas.guid", impact.ModifiedRelativePaths);
    }

    [Fact]
    public void AnalyzeChanges_AddedFileWithoutAncestorEntity_Should_ReturnNewPath_WithoutHanging()
    {
        FileTreeBinding binding = FileTreeBinding.Instance;
        binding.Clear();

        FileTreeNode oldRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        FileTreeSnapshot oldSnapshot = new FileTreeSnapshot { Root = oldRoot };

        FileTreeNode newRoot = new FileTreeNode(WorkspacePath.RootDirectory, "", 0);
        newRoot.AddFile("added.txt", new FileSignature(1, 1));
        FileTreeSnapshot newSnapshot = new FileTreeSnapshot { Root = newRoot };

        FileTreeChangeImpact impact = binding.AnalyzeChanges(oldSnapshot, newSnapshot);

        Assert.Empty(impact.EntitiesToReload);
        Assert.Empty(impact.EntitiesToUnload);
        Assert.Contains("added.txt", impact.NewRelativePaths);
        Assert.Contains("added.txt", impact.ModifiedRelativePaths);
    }
}
