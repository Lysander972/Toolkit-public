using Toolkit.Core.Infra;

namespace Toolkit.Core.Persistence;

// =============================================================================
// 实体构建策略与错误处理政策
// =============================================================================
//
// 【用户确定性要求】以下错误处理策略是用户明确要求的设计，不是防御性fallback：
//
// 1. 构建器失败处理：
//    - 各builder在检测到结构性错误时直接抛异常（Offensive）
//    - 协调器作为边界，捕获异常并转换为Failed，防止崩溃
//    - 失败的builder返回Failed或抛异常，协调器继续尝试下一个builder
//
// 2. 降级尝试机制：
//    - 按优先级依次尝试各builder
//    - 某个builder失败后，继续尝试优先级更低的builder
//    - 最终无法识别的文件/目录作为未覆盖项，由FileTreeDiagnostics报告
//
// 3. 不崩溃原则：
//    - 单个实体构建失败不应导致整个工程加载崩溃
//    - 畸形结构（如map.map_内含device.json）会被降级处理或标记为未处理
//    - 错误收集到EntityLoadErrorCollector，继续处理其他实体
//    - 最终由用户决定是否接受带错误的工程状态
//
// 4. 边界清晰：
//    - 防御仅在系统边界（协调器、文件读写、MCP入口）
//    - 内部逻辑保持攻击性，错误直接抛出
//    - 禁止内部防御逻辑堆叠和自动修复
//
// =============================================================================

/// <summary>
/// 实体构建上下文。
/// 用 CandidatePaths 维护待处理的候选路径集合。
/// </summary>
internal sealed class EntityBuildContext
{
    public FileTreeNode CurrentNode { get; }
    public EntityLoadErrorCollector Errors { get; }
    public int Depth { get; }

    /// <summary>候选路径集合（待处理的文件和目录路径）</summary>
    public HashSet<string> CandidatePaths { get; }

    public ProjectEntity ParentEntity { get; }

    public EntityBuildContext(
        FileTreeNode currentNode,
        EntityLoadErrorCollector errors,
        int depth,
        HashSet<string> candidatePaths,
        ProjectEntity parentEntity)
    {
        CurrentNode = currentNode;
        Errors = errors;
        Depth = depth;
        CandidatePaths = candidatePaths;
        ParentEntity = parentEntity;
    }

    /// <summary>
    /// 创建子级上下文：收集子节点下的全部候选，排除父实体覆盖范围。
    /// </summary>
    public static EntityBuildContext CreateForChild(
        FileTreeNode childNode,
        EntityLoadErrorCollector errors,
        int depth,
        ProjectEntity parentEntity)
    {
        HashSet<string> candidatePaths = new(StringComparer.Ordinal);

        // 收集全部直接子文件和子目录
        foreach (string file in childNode.Files)
            candidatePaths.Add(childNode.GetFileRelativePath(file));
        foreach (FileTreeNode child in childNode.Children)
            candidatePaths.Add(child.RelativePath);

        // 排除父实体本身的覆盖范围
        foreach (string path in parentEntity.GetSelfCoverage().AllPaths)
            candidatePaths.Remove(path);

        return new EntityBuildContext(childNode, errors, depth, candidatePaths, parentEntity);
    }

    /// <summary>获取候选文件名</summary>
    public IEnumerable<string> GetCandidateFileNames()
    {
        return CurrentNode.Files.Where(f => CandidatePaths.Contains(CurrentNode.GetFileRelativePath(f)));
    }

    /// <summary>获取候选子目录</summary>
    public IEnumerable<FileTreeNode> GetCandidateChildren()
    {
        return CurrentNode.Children.Where(c => CandidatePaths.Contains(c.RelativePath));
    }

    /// <summary>从候选集中移除指定路径</summary>
    public void RemoveFromCandidates(IEnumerable<string> paths)
    {
        foreach (string path in paths)
            CandidatePaths.Remove(path);
    }
}

/// <summary>
/// 子实体构建结果。
/// </summary>
internal readonly struct SubEntityBuildResult
{
    public ProjectEntity? Entity { get; }
    public SubEntityBuildResult(ProjectEntity? entity) => Entity = entity;
    public static SubEntityBuildResult Failed { get; } = new(null);
}

/// <summary>
/// 子实体构建器接口。
/// </summary>
internal interface ISubEntityBuilder
{
    int Priority { get; }
    SubEntityBuildResult TryBuild(EntityBuildContext context);
}

// =============================================================================
// 根级实体构建器（在工程根和常规目录注册）
// 优先级: 100-199
// 绝对禁止滥用：地图/机械的父级只能是工程根或常规目录！
// 禁止在地图/机械派生目录、零件目录等其他层级识别地图/机械！
// =============================================================================

internal sealed class MapEntityBuilder : ISubEntityBuilder
{
    public int Priority => 100;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        string? guidFileName = context.GetCandidateFileNames()
            .FirstOrDefault(f => f.EndsWith(ProjectEntityLayout.MapGuidExt, StringComparison.Ordinal));
        if (guidFileName == null) return SubEntityBuildResult.Failed;

        string baseName = guidFileName[..^ProjectEntityLayout.MapGuidExt.Length];
        string mapFileName = baseName + ProjectEntityLayout.MapExt;
        string derivedDirName = baseName + ProjectEntityLayout.MapDirExt;

        // Offensive: 检测违反"三元素平级"规则的错误结构
        if (node.Name.Equals(derivedDirName, StringComparison.Ordinal))
        {
            string mapDirRelativePath = node.GetDirectoryRelativePath();
            throw new InvalidOperationException(
                $"违反三元素平级规则: 在地图派生目录 '{mapDirRelativePath}' 内发现 GUID 文件 '{guidFileName}'。 " +
                $"地图三元素 (map文件、GUID、派生目录) 必须在同一父目录下平级放置，" +
                $"派生目录内不应包含额外的 .map.guid 文件。");
        }

        if (!node.Files.Contains(mapFileName))
            throw new InvalidOperationException($"地图缺少 map 文件: {mapFileName}");

        FileTreeNode? derivedNode = node.Children.FirstOrDefault(c => c.Name == derivedDirName);
        if (derivedNode == null)
        {
            string mapDirRelativePath = node.GetDirectoryRelativePath();
            string derivedDirRelativePath = RelativePathUtils.Combine(mapDirRelativePath, derivedDirName);
            throw new InvalidOperationException($"地图缺少派生目录: {derivedDirRelativePath}");
        }

        MapEntity map = new()
        {
            Parent = context.ParentEntity
        };
        map.SetIdentifierInternal(baseName + ProjectEntityLayout.MapDirExt);  // "main.map_"

        // 先加载实体自身信息（GUID、配置文件等）
        int errorCountBefore = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        map.LoadSelf(context.Errors);

        // 检查实体自身加载是否失败（只检查 Error 级别，Warning 不阻止构建）
        int errorCountAfter = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        if (errorCountAfter > errorCountBefore)
        {
            return SubEntityBuildResult.Failed;
        }

        // 实体自身加载成功，继续加载子实体
        // 子实体加载失败不影响父实体的存在
        map.LoadChildren(context.Errors);

        return new SubEntityBuildResult(map);
    }
}

internal sealed class MechEntityBuilder : ISubEntityBuilder
{
    public int Priority => 200;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        string? guidFileName = context.GetCandidateFileNames()
            .FirstOrDefault(f => f.EndsWith(ProjectEntityLayout.MechGuidExt, StringComparison.Ordinal));
        if (guidFileName == null) return SubEntityBuildResult.Failed;

        string baseName = guidFileName[..^ProjectEntityLayout.MechGuidExt.Length];
        string mechFileName = baseName + ProjectEntityLayout.MechExt;
        string derivedDirName = baseName + ProjectEntityLayout.MechDirExt;

        // Offensive: 检测违反"三元素平级"规则的错误结构
        if (node.Name.Equals(derivedDirName, StringComparison.Ordinal))
        {
            string mechDirRelativePath = node.GetDirectoryRelativePath();
            throw new InvalidOperationException(
                $"违反三元素平级规则: 在机械派生目录 '{mechDirRelativePath}' 内发现 GUID 文件 '{guidFileName}'。 " +
                $"机械三元素 (mech文件、GUID、派生目录) 必须在同一父目录下平级放置，" +
                $"派生目录内不应包含额外的 .mech.guid 文件。");
        }

        if (!node.Files.Contains(mechFileName))
            throw new InvalidOperationException($"机械缺少 mech 文件: {mechFileName}");

        FileTreeNode? derivedNode = node.Children.FirstOrDefault(c => c.Name == derivedDirName);
        if (derivedNode == null)
        {
            string mechDirRelativePath = node.GetDirectoryRelativePath();
            string derivedDirRelativePath = RelativePathUtils.Combine(mechDirRelativePath, derivedDirName);
            throw new InvalidOperationException($"机械缺少派生目录: {derivedDirRelativePath}");
        }

        MechEntity mech = new()
        {
            Parent = context.ParentEntity
        };
        mech.SetIdentifierInternal(baseName + ProjectEntityLayout.MechDirExt);  // "sub.mech_"

        // 先加载实体自身信息（GUID、配置文件等）
        int errorCountBefore = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        mech.LoadSelf(context.Errors);

        // 检查实体自身加载是否失败（只检查 Error 级别，Warning 不阻止构建）
        int errorCountAfter = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        if (errorCountAfter > errorCountBefore)
        {
            return SubEntityBuildResult.Failed;
        }

        // 实体自身加载成功，继续加载子实体
        // 子实体加载失败不影响父实体的存在
        mech.LoadChildren(context.Errors);

        return new SubEntityBuildResult(mech);
    }
}

// =============================================================================
// Spine 实体构建器（在工程根和常规目录注册）
// 优先级: 250
// 绝对禁止滥用：Spine 资产的父级只能是工程根或常规目录！
// 仅按 .spine_ 派生目录后缀识别（Ordinal），不依赖文件内容。
// =============================================================================

internal sealed class SpineEntityBuilder : ISubEntityBuilder
{
    public int Priority => 250;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode? childNode = context.GetCandidateChildren()
            .FirstOrDefault(c => c.Name.EndsWith(ProjectEntityLayout.SpineDirExt, StringComparison.Ordinal));
        if (childNode == null) return SubEntityBuildResult.Failed;

        SpineEntity spine = new(childNode.Name)
        {
            Parent = context.ParentEntity
        };
        spine.Rebuild(context.Errors);

        return new SubEntityBuildResult(spine);
    }
}

// =============================================================================
// 独立文件实体构建器（在工程根和常规目录注册）
// 优先级: 300-499
// 绝对禁止滥用：独立文件的父级只能是工程根或常规目录！
// 禁止在地图/机械派生目录、零件目录等其他层级识别独立文件！
// =============================================================================

internal sealed class CanvasEntityBuilder : ISubEntityBuilder
{
    public int Priority => 300;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        string? canvasFileName = context.GetCandidateFileNames()
            .FirstOrDefault(static f => f.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal));
        if (canvasFileName == null) return SubEntityBuildResult.Failed;

        string guidFileName = canvasFileName + ProjectEntityLayout.GuidExt;
        if (!node.Files.Contains(guidFileName))
            return SubEntityBuildResult.Failed;

        CanvasEntity canvas = new() { Parent = context.ParentEntity };
        canvas.SetIdentifierInternal(canvasFileName);

        // 加载实体自身信息（文件内容、GUID）
        int errorCountBefore = context.Errors.Errors.Count(static e => e.Severity == ToolDiagnosticSeverity.Error);
        canvas.LoadSelf(context.Errors);

        // 检查实体自身加载是否失败（只检查 Error 级别，Warning 不阻止构建）
        int errorCountAfter = context.Errors.Errors.Count(static e => e.Severity == ToolDiagnosticSeverity.Error);
        if (errorCountAfter > errorCountBefore)
        {
            return SubEntityBuildResult.Failed;
        }

        return new SubEntityBuildResult(canvas);
    }
}

internal sealed class CodeRepoEntityBuilder : ISubEntityBuilder
{
    public int Priority => 400;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        string? codeFileName = context.GetCandidateFileNames()
            .FirstOrDefault(static f => f.EndsWith(ProjectEntityLayout.CodeExt, StringComparison.Ordinal));
        if (codeFileName == null) return SubEntityBuildResult.Failed;

        string guidFileName = codeFileName + ProjectEntityLayout.GuidExt;
        if (!node.Files.Contains(guidFileName))
            return SubEntityBuildResult.Failed;

        CodeRepoScriptEntity codeRepo = new() { Parent = context.ParentEntity };
        codeRepo.SetIdentifierInternal(codeFileName);

        int errorCountBefore = context.Errors.Errors.Count(static e => e.Severity == ToolDiagnosticSeverity.Error);
        codeRepo.Rebuild(context.Errors);

        int errorCountAfter = context.Errors.Errors.Count(static e => e.Severity == ToolDiagnosticSeverity.Error);
        if (errorCountAfter > errorCountBefore)
            return SubEntityBuildResult.Failed;

        return new SubEntityBuildResult(codeRepo);
    }
}

internal sealed class ResourceEntityBuilder : ISubEntityBuilder
{
    public int Priority => 500;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 【优先级隔离】不过滤 code，由优先级保证 CodeRepoEntityBuilder(400) 先于 ResourceEntityBuilder(500) 处理 code
        string[] processedExts = new[] { ProjectEntityLayout.CanvasExt };

        string? fileName = context.GetCandidateFileNames()
            .FirstOrDefault(f =>
            {
                string ext = RelativePathUtils.GetExtension(f);
                if (processedExts.Contains(ext, StringComparer.Ordinal)
                    || ext.Equals(ProjectEntityLayout.GuidExt, StringComparison.Ordinal))
                    return false;
                string guidFileName = f + ProjectEntityLayout.GuidExt;
                return node.Files.Contains(guidFileName);
            });
        if (fileName == null) return SubEntityBuildResult.Failed;

        ResourceEntity resource = new() { Parent = context.ParentEntity };
        resource.SetIdentifierInternal(fileName);

        // 加载实体自身信息（文件内容、GUID）
        int errorCountBefore = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        resource.LoadSelf(context.Errors);

        // 检查实体自身加载是否失败或 GUID 加载失败（只检查 Error 级别，Warning 不阻止构建）
        int errorCountAfter = context.Errors.Errors.Count(e => e.Severity == ToolDiagnosticSeverity.Error);
        if (errorCountAfter > errorCountBefore)
        {
            return SubEntityBuildResult.Failed;
        }

        return new SubEntityBuildResult(resource);
    }
}

// =============================================================================
// 常规目录构建器（兜底，最后处理）
// 优先级: 1000
// 绝对禁止滥用：此构建器只能在工程根或常规目录注册！
// 禁止在地图/机械派生目录、零件目录等其他层级使用！
// =============================================================================

internal sealed class DirectoryEntityBuilder : ISubEntityBuilder
{
    public int Priority => 1000;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        FileTreeNode? childNode = context.GetCandidateChildren().FirstOrDefault();
        if (childNode == null) return SubEntityBuildResult.Failed;

        DirectoryEntity dir = new(childNode.Name)
        {
            Parent = context.ParentEntity
        };
        dir.Rebuild(context.Errors);

        return new SubEntityBuildResult(dir);
    }
}

// =============================================================================
// 特殊目录构建器（只能直属于工程根）
// 优先级: 50-99
// =============================================================================

internal sealed class TempRootEntityBuilder : ISubEntityBuilder
{
    public int Priority => 50;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        FileTreeNode? tempNode = context.GetCandidateChildren()
            .FirstOrDefault(static c => c.Name.Equals("temp", StringComparison.Ordinal));
        if (tempNode == null) return SubEntityBuildResult.Failed;

        TempRootEntity temp = new() { Parent = context.ParentEntity };
        temp.Rebuild(context.Errors);

        return new SubEntityBuildResult(temp);
    }
}

internal sealed class ModDirectoryEntityBuilder : ISubEntityBuilder
{
    public int Priority => 60;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        FileTreeNode? modNode = context.GetCandidateChildren()
            .FirstOrDefault(static c => ScriptPathHelper.IsEditorSegment(c.Name));
        if (modNode == null) return SubEntityBuildResult.Failed;

        ModDirectoryEntity mod = new(modNode.Name) { Parent = context.ParentEntity };
        mod.Rebuild(context.Errors);

        return new SubEntityBuildResult(mod);
    }
}

// =============================================================================
// Temp 内部构建器
// 优先级: 100-199
// =============================================================================

internal sealed class ToolkitRootEntityBuilder : ISubEntityBuilder
{
    public int Priority => 100;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 只在 temp 目录下识别
        if (!node.RelativePath.Equals("temp", StringComparison.Ordinal))
            return SubEntityBuildResult.Failed;

        FileTreeNode? toolkitNode = context.GetCandidateChildren()
            .FirstOrDefault(static c => c.Name.Equals("toolkit", StringComparison.Ordinal));
        if (toolkitNode == null) return SubEntityBuildResult.Failed;

        ToolkitRootEntity toolkit = new() { Parent = context.ParentEntity };
        toolkit.Rebuild(context.Errors);

        return new SubEntityBuildResult(toolkit);
    }
}

internal sealed class OtherTempFileEntityBuilder : ISubEntityBuilder
{
    public int Priority => 110;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 只在 temp 目录下识别（不包括 toolkit 子目录）
        if (!node.RelativePath.Equals("temp", StringComparison.Ordinal))
            return SubEntityBuildResult.Failed;

        string? fileName = context.GetCandidateFileNames().FirstOrDefault();
        if (fileName == null) return SubEntityBuildResult.Failed;

        OtherTempFileEntity tempFile = new(fileName) { Parent = context.ParentEntity };
        tempFile.Rebuild(context.Errors);

        return new SubEntityBuildResult(tempFile);
    }
}

// =============================================================================
// Toolkit 内部构建器
// 优先级: 200-299
// =============================================================================

internal sealed class ToolkitInternalDirectoryEntityBuilder : ISubEntityBuilder
{
    public int Priority => 200;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 只在 toolkit 内部识别
        if (!node.RelativePath.StartsWith("temp/toolkit", StringComparison.Ordinal))
            return SubEntityBuildResult.Failed;

        FileTreeNode? childNode = context.GetCandidateChildren().FirstOrDefault();
        if (childNode == null) return SubEntityBuildResult.Failed;

        ToolkitInternalDirectoryEntity dir = new(childNode.Name) { Parent = context.ParentEntity };
        dir.Rebuild(context.Errors);

        return new SubEntityBuildResult(dir);
    }
}

internal sealed class ToolkitFileEntityBuilder : ISubEntityBuilder
{
    public int Priority => 210;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 只在 toolkit 内部识别
        if (!node.RelativePath.StartsWith("temp/toolkit", StringComparison.Ordinal))
            return SubEntityBuildResult.Failed;

        string? fileName = context.GetCandidateFileNames().FirstOrDefault();
        if (fileName == null) return SubEntityBuildResult.Failed;

        ToolkitFileEntity toolkitFile = new(fileName) { Parent = context.ParentEntity, Content = string.Empty };
        toolkitFile.Rebuild(context.Errors);

        return new SubEntityBuildResult(toolkitFile);
    }
}

internal sealed class ToolkitBinaryFileEntityBuilder : ISubEntityBuilder
{
    public int Priority => 205;

    public SubEntityBuildResult TryBuild(EntityBuildContext context)
    {
        FileTreeNode node = context.CurrentNode;

        // 只在 toolkit 内部识别
        if (!node.RelativePath.StartsWith("temp/toolkit", StringComparison.Ordinal))
            return SubEntityBuildResult.Failed;

        // 目前仅将 PNG 视为二进制实体，避免文本加载污染
        string? fileName = context.GetCandidateFileNames()
            .FirstOrDefault(static f => f.EndsWith(".png", StringComparison.Ordinal));
        if (fileName == null) return SubEntityBuildResult.Failed;

        ToolkitBinaryFileEntity toolkitFile = new(fileName) { Parent = context.ParentEntity };
        toolkitFile.Rebuild(context.Errors);

        return new SubEntityBuildResult(toolkitFile);
    }
}

// =============================================================================
// 构建协调器
// =============================================================================

internal sealed class EntityBuildCoordinator
{
    private readonly List<ISubEntityBuilder> _builders = [];

    public void Register(ISubEntityBuilder builder)
    {
        _builders.Add(builder);
        _builders.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>
    /// 按优先级依次尝试构建实体，失败时降级到下一个builder。
    ///
    /// 【用户确定性要求】错误处理策略：
    /// 1. 边界防御：协调器捕获builder抛出的异常，防止单个实体构建失败导致整个工程崩溃
    /// 2. 降级尝试：某builder返回Failed或抛异常时，继续尝试优先级更低的builder
    /// 3. 未覆盖处理：所有builder都失败的文件/目录保留在CandidatePaths中，
    ///    由FileTreeDiagnostics作为未覆盖项报告警告
    /// 4. 不崩溃：单个实体构建失败不影响其他实体的构建，畸形结构会被降级处理
    /// 5. 错误记录：builder抛出的异常记录到EntityLoadErrorCollector，供用户查看
    /// </summary>
    public List<ProjectEntity> BuildAll(EntityBuildContext context)
    {
        List<ProjectEntity> entities = [];
        int builderIndex = 0;

        while (context.CandidatePaths.Count > 0 && builderIndex < _builders.Count)
        {
            SubEntityBuildResult result;

            try
            {
                // 【边界防御】捕获builder抛出的异常，防止崩溃
                // builder内部保持Offensive，直接抛异常
                // 协调器作为边界，捕获异常并转换为Failed，继续降级尝试
                result = _builders[builderIndex].TryBuild(context);
            }
            catch (Exception ex)
            {
                // 记录错误信息，继续尝试下一个builder
                string candidatePath = context.CandidatePaths.OrderBy(static p => p, StringComparer.Ordinal).First();
                context.Errors.AddError(
                    candidatePath,
                    $"实体构建器 {_builders[builderIndex].GetType().Name} 处理失败: {ex.Message}",
                    ToolDiagnosticSeverity.Warning);

                result = SubEntityBuildResult.Failed;
            }

            if (result.Entity != null)
            {
                // 成功：从候选集移除覆盖范围，重置序号
                context.RemoveFromCandidates(result.Entity.GetSelfCoverage().AllPaths);
                entities.Add(result.Entity);
                builderIndex = 0;
            }
            else
            {
                // 失败：序号++，继续尝试下一个builder（降级）
                builderIndex++;
            }
        }

        return entities;
    }
}

// =============================================================================
// 预配置的构建器集合（按场景）
// =============================================================================
// 绝对禁止滥用"常规目录"概念！
// - 常规目录的父级只能是：工程根 或 常规目录
// - 独立文件的父级只能是：工程根 或 常规目录
// - 零件的父级只能是：地图/机械派生目录
// - 脚本组的父级只能是：零件目录
// - 内联脚本的父级只能是：脚本组目录
// =============================================================================

internal static class EntityBuilderSets
{
    /// <summary>
    /// 工程根目录构建器集合。
    /// 预期子实体：地图、机械、独立文件实体、特殊目录(temp/mod)、常规目录。
    /// 注意：工程根扫描时会主动跳过地图/机械的派生目录（这些由地图/机械自己处理）。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForProjectRoot()
    {
        yield return new TempRootEntityBuilder();
        yield return new ModDirectoryEntityBuilder();
        yield return new MapEntityBuilder();
        yield return new MechEntityBuilder();
        yield return new SpineEntityBuilder();
        yield return new CanvasEntityBuilder();
        yield return new CodeRepoEntityBuilder();
        yield return new ResourceEntityBuilder();
        yield return new DirectoryEntityBuilder();
    }

    /// <summary>
    /// 常规目录构建器集合。
    /// 预期子实体：地图、机械、独立文件实体、常规目录。
    /// 与工程根完全相同！唯一的区别是不会跳过任何子目录。
    /// 绝对禁止：将"常规目录"概念滥用到其他层级！
    /// 常规目录的父级只能是工程根或常规目录！
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForRegularDirectory()
    {
        yield return new MapEntityBuilder();
        yield return new MechEntityBuilder();
        yield return new SpineEntityBuilder();
        yield return new CanvasEntityBuilder();
        yield return new CodeRepoEntityBuilder();
        yield return new ResourceEntityBuilder();
        yield return new DirectoryEntityBuilder();
    }

    /// <summary>
    /// Temp 根目录构建器集合。
    /// 预期子实体：ToolkitRoot、OtherTempFile。
    /// 只能直属于工程根。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForTempRoot()
    {
        yield return new ToolkitRootEntityBuilder();
        yield return new OtherTempFileEntityBuilder();
    }

    /// <summary>
    /// Toolkit 根目录构建器集合。
    /// 预期子实体：ToolkitInternalDirectory、ToolkitFile。
    /// 只能直属于 temp 目录。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForToolkitRoot()
    {
        yield return new ToolkitInternalDirectoryEntityBuilder();
        yield return new ToolkitBinaryFileEntityBuilder();
        yield return new ToolkitFileEntityBuilder();
    }

    /// <summary>
    /// Toolkit 内部目录构建器集合（递归）。
    /// 预期子实体：ToolkitInternalDirectory、ToolkitFile。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForToolkitInternal()
    {
        yield return new ToolkitInternalDirectoryEntityBuilder();
        yield return new ToolkitBinaryFileEntityBuilder();
        yield return new ToolkitFileEntityBuilder();
    }

    /// <summary>
    /// Mod 目录构建器集合。
    /// 预期子实体：常规资源实体（ResourceEntity）。
    /// 【根源隔离】Mod 目录下的 code 被识别为 ResourceEntity，而非 CodeRepoScriptEntity。
    /// 优先级机制：CodeRepoEntityBuilder(400) 不在 Mod 目录注册，ResourceEntityBuilder(500) 处理所有文件。
    /// 只能直属于工程根。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForModDirectory()
    {
        yield return new ResourceEntityBuilder();
    }

    /// <summary>
    /// Spine 目录构建器集合。
    /// 预期子实体：资源文件（ResourceEntity）、常规目录（DirectoryEntity）。
    /// 不递归识别 Spine（SpineEntityBuilder 不在此集合），避免无限嵌套。
    /// </summary>
    public static IEnumerable<ISubEntityBuilder> ForSpineDirectory()
    {
        yield return new ResourceEntityBuilder();
        yield return new DirectoryEntityBuilder();
    }

}
