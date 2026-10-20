using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Entity;

// Spine 实体构建测试：覆盖识别逻辑与边缘场景，不依赖磁盘 IO。
// SpineEntityBuilder 仅按 .spine_ 派生目录后缀识别（Ordinal），目录内不放带 .guid 的文件，
// 避免 ResourceEntityBuilder.LoadSelf 触发磁盘读取；ResourceEntity 子实体的真实加载属其既有测试范围。
[Collection("WorkspaceState")]
public sealed class SpineEntityBuildTests
{
    // ============================================================================
    // 识别：构建器集在工程根/常规目录下识别 .spine_ 派生目录
    // ============================================================================

    [Fact]
    public void ForProjectRoot_Identifies_SpineSuffix_AsSpineEntity()
    {
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("hero.spine_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        SpineEntity spine = Assert.Single(children.OfType<SpineEntity>());
        Assert.Equal("hero.spine_", spine.DirectoryName);
        Assert.Equal(ProjectEntityKind.Spine, spine.Kind);
    }

    [Fact]
    public void ForRegularDirectory_Identifies_SpineSuffix_AsSpineEntity()
    {
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("hero.spine_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForRegularDirectory());

        Assert.Single(children.OfType<SpineEntity>());
    }

    [Fact]
    public void NonSpineSuffix_NotIdentified_AsSpineEntity()
    {
        // 含 "spine" 字样但不是 .spine_ 后缀 → 普通 DirectoryEntity
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("spine_assets");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Empty(children.OfType<SpineEntity>());
        Assert.Single(children.OfType<DirectoryEntity>());
    }

    [Theory]
    [InlineData("hero.Spine_")]   // 首字母大写
    [InlineData("hero.SPINE_")]   // 全大写
    [InlineData("hero.spine")]    // 无尾下划线
    public void DifferentCase_Or_MissingUnderscore_NotIdentified_AsSpineEntity(string dirName)
    {
        // Ordinal 匹配，大小写敏感；后缀必须精确为 .spine_
        FileTreeNode root = NewRoot();
        root.AddChildDirectory(dirName);

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Empty(children.OfType<SpineEntity>());
    }

    [Fact]
    public void MultipleSpineDirs_AllIdentified_Independently()
    {
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("a.spine_");
        root.AddChildDirectory("b.spine_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Equal(2, children.OfType<SpineEntity>().Count());
    }

    [Fact]
    public void SpineDir_Coexists_With_RegularDirectory()
    {
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("hero.spine_");
        root.AddChildDirectory("regular");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Single(children.OfType<SpineEntity>());
        Assert.Single(children.OfType<DirectoryEntity>());
    }

    [Fact]
    public void EmptyRoot_Produces_NoSpineEntity()
    {
        FileTreeNode root = NewRoot();

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Empty(children.OfType<SpineEntity>());
    }

    // ============================================================================
    // 不递归：ForSpineDirectory 不含 SpineEntityBuilder，Spine 内部不再识别 Spine
    // ============================================================================

    [Fact]
    public void ForSpineDirectory_DoesNotContain_SpineBuilder()
    {
        Type[] builderTypes = EntityBuilderSets.ForSpineDirectory()
            .Select(static b => b.GetType())
            .ToArray();

        Assert.DoesNotContain(typeof(SpineEntityBuilder), builderTypes);
    }

    [Fact]
    public void ForSpineDirectory_Contains_Resource_And_Directory_Builder()
    {
        Type[] builderTypes = EntityBuilderSets.ForSpineDirectory()
            .Select(static b => b.GetType())
            .ToArray();

        Assert.Equal(
            new[] { typeof(ResourceEntityBuilder), typeof(DirectoryEntityBuilder) },
            builderTypes);
    }

    [Fact]
    public void Nested_SpineDir_Loaded_As_DirectoryEntity_Not_Spine()
    {
        // hero.spine_ 内含 inner.spine_：因 ForSpineDirectory 无 SpineBuilder，
        // inner.spine_ 必须被识别为 DirectoryEntity，而非 SpineEntity（不递归）。
        FileTreeNode root = NewRoot();
        FileTreeNode spineDir = root.AddChildDirectory("hero.spine_");
        spineDir.AddChildDirectory("inner.spine_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        SpineEntity spine = Assert.Single(children.OfType<SpineEntity>());
        Assert.Empty(spine.Children.OfType<SpineEntity>());
        Assert.Single(spine.Children.OfType<DirectoryEntity>());
    }

    [Fact]
    public void Empty_SpineDir_Has_NoChildren()
    {
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("hero.spine_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        SpineEntity spine = Assert.Single(children.OfType<SpineEntity>());
        Assert.Empty(spine.Children);
    }

    // ============================================================================
    // 构建器集注册：工程根与常规目录必须注册 SpineEntityBuilder
    // ============================================================================

    [Fact]
    public void ForProjectRoot_Contains_SpineBuilder()
    {
        bool contains = EntityBuilderSets.ForProjectRoot().Any(b => b is SpineEntityBuilder);
        Assert.True(contains);
    }

    [Fact]
    public void ForRegularDirectory_Contains_SpineBuilder()
    {
        bool contains = EntityBuilderSets.ForRegularDirectory().Any(b => b is SpineEntityBuilder);
        Assert.True(contains);
    }

    // ============================================================================
    // 优先级：SpineBuilder(250) 必须在 DirectoryBuilder(1000) 之前、MechBuilder(200) 之后
    // ============================================================================

    [Fact]
    public void SpineBuilder_Priority_IsBetween_Mech_And_Directory()
    {
        SpineEntityBuilder spine = new();
        MechEntityBuilder mech = new();
        CanvasEntityBuilder canvas = new();
        DirectoryEntityBuilder directory = new();

        Assert.Equal(250, spine.Priority);
        Assert.True(mech.Priority < spine.Priority, "Spine 必须在 Mech 之后识别");
        Assert.True(spine.Priority < canvas.Priority, "Spine 必须在 Canvas 之前识别");
        Assert.True(spine.Priority < directory.Priority, "Spine 必须在常规目录兜底之前识别");
    }

    // ============================================================================
    // SpineEntity 自身行为
    // ============================================================================

    [Fact]
    public void Kind_Is_Spine()
    {
        SpineEntity spine = NewSpine("hero.spine_");

        Assert.Equal(ProjectEntityKind.Spine, spine.Kind);
    }

    [Fact]
    public void GetSelfCoverage_CoversDirectory_Only_NoAnchorFile()
    {
        // SpineEntity 无锚点文件、无实体级 GUID，仅覆盖派生目录本身（与 Map 覆盖多文件形成对比）。
        SpineEntity spine = NewSpine("hero.spine_");

        EntityCoverage coverage = spine.GetSelfCoverage();

        Assert.Single(coverage.Directories);
        Assert.Contains("hero.spine_", coverage.Directories.Keys);
        Assert.Empty(coverage.Files);
    }

    [Fact]
    public void Validate_Accepts_SpineSuffix()
    {
        SpineEntity spine = NewSpine("hero.spine_");
        spine.Validate();  // 不抛
    }

    [Theory]
    [InlineData("hero")]
    [InlineData("hero.spine")]
    [InlineData("hero.map_")]
    public void Validate_Rejects_NonSpineSuffix(string dirName)
    {
        SpineEntity spine = NewSpine(dirName);

        Assert.Throws<InvalidOperationException>(spine.Validate);
    }

    [Fact]
    public void WritableSpineEntity_Rename_Throws_NotSupported()
    {
        WritableSpineEntity writable = new("hero.spine_") { Parent = new ProjectRootEntity() };

        Assert.Throws<NotSupportedException>(() => writable.Rename("enemy"));
    }

    [Fact]
    public void GetEntityBuilders_Returns_ForSpineDirectory()
    {
        SpineEntity spine = NewSpine("hero.spine_");

        Type[] builderTypes = spine.GetEntityBuilders()
            .Select(static b => b.GetType())
            .ToArray();

        Assert.Equal(
            new[] { typeof(ResourceEntityBuilder), typeof(DirectoryEntityBuilder) },
            builderTypes);
        Assert.DoesNotContain(typeof(SpineEntityBuilder), builderTypes);
    }

    // ============================================================================
    // 与其他实体交互：SpineBuilder 不误抢其他派生目录后缀，DirectoryEntityBuilder 兜底仍健壮
    // ============================================================================

    [Fact]
    public void MixedTree_EachSuffix_Routed_To_Correct_Builder()
    {
        // .spine_ → SpineEntity；.map_/.mech_（无锚点）+ 普通目录 → DirectoryEntityBuilder 兜底。
        // 验证 SpineBuilder 仅认 .spine_，不与 .map_/.mech_ 后缀家族混淆。
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("hero.spine_");
        root.AddChildDirectory("orphan.map_");   // 无 .map.guid 锚点 → MapEntityBuilder 失败
        root.AddChildDirectory("orphan.mech_");  // 无 .mech.guid 锚点 → MechEntityBuilder 失败
        root.AddChildDirectory("regular");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Single(children.OfType<SpineEntity>());
        // 三个非 Spine 目录全部兜底为 DirectoryEntity（Map/Mech 无锚点，未被其 builder 抢占）
        Assert.Equal(3, children.OfType<DirectoryEntity>().Count());
    }

    [Fact]
    public void SpineBuilder_DoesNot_Consume_MapMech_Suffix()
    {
        // 直接验证后缀区分：只有 .spine_ 被识别为 SpineEntity，.map_/.mech_ 不是。
        FileTreeNode root = NewRoot();
        root.AddChildDirectory("only.map_");

        List<ProjectEntity> children = BuildAtRoot(root, EntityBuilderSets.ForProjectRoot());

        Assert.Empty(children.OfType<SpineEntity>());
        Assert.Single(children.OfType<DirectoryEntity>());
    }

    // ============================================================================
    // Helpers
    // ============================================================================

    private static FileTreeNode NewRoot()
        => new(WorkspacePath.RootDirectory, string.Empty, 0);

    private static SpineEntity NewSpine(string directoryName)
    {
        ProjectRootEntity root = new();
        return new SpineEntity(directoryName) { Parent = root };
    }

    private static List<ProjectEntity> BuildAtRoot(FileTreeNode root, IEnumerable<ISubEntityBuilder> builders)
    {
        // FileTreeBinding 单例需绑定 root，供 SpineEntity.LoadChildren 的 FindNode 使用。
        FileTreeBinding.Instance.RefreshFilePaths(root);

        EntityLoadErrorCollector errors = new();
        EntityBuildContext context = EntityBuildContext.CreateForChild(root, errors, 0, new ProjectRootEntity());

        EntityBuildCoordinator coordinator = new();
        foreach (ISubEntityBuilder builder in builders)
        {
            coordinator.Register(builder);
        }
        return coordinator.BuildAll(context);
    }
}
