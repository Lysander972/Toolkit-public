// 实体搜索工具 - Logic 层
// 基于 Persistence 层提供的 ProjectModel 进行实体搜索
// 明确规则搜索，拒绝模糊"智能"逻辑

using System.Text.RegularExpressions;

using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.TsView;

namespace Toolkit.Core.Persistence;

/// <summary>
/// 实体搜索参数
/// </summary>
public sealed record EntitySearchArgs(
    string? NamePattern,
    string[]? EntityGuids,
    string[]? Kinds,
    int Limit = 50
);

/// <summary>
/// 实体搜索结果项（包含视图位置）
/// </summary>
public sealed record EntitySearchResultItem(
    string? Guid,
    string Kind,
    string Name,
    string? View,
    IReadOnlyList<string>? PreviewPngs
);

/// <summary>
/// 实体搜索响应
/// </summary>
public sealed record EntitySearchResponse(
    IReadOnlyList<EntitySearchResultItem> Entities
);

/// <summary>
/// 实体搜索工具 - Logic 层。
/// 基于 Persistence 层数据进行实体搜索。
/// 明确规则，无"智能"模糊匹配。
/// </summary>
public sealed class EntitySearchTool
{
    private readonly ProjectModel _model;
    private readonly CodeRepoTsView _codeRepoTsView;
    private readonly DeviceTsView _deviceTsView;

    public EntitySearchTool(ProjectLogicContext context, CodeRepoTsView codeRepoTsView, DeviceTsView deviceTsView)
    {
        _model = context.Model;
        _codeRepoTsView = codeRepoTsView;
        _deviceTsView = deviceTsView;
    }

    /// <summary>
    /// 搜索实体，返回带视图位置的结果
    /// </summary>
    public (EntitySearchResponse? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) Search(
        EntitySearchArgs args)
    {
        List<EntitySearchResultItem> results = [];
        List<ToolError> errors = [];
        List<ToolError> warnings = [];

        string? namePattern = args.NamePattern;
        HashSet<string>? guidFilters = BuildGuidFilters(args.EntityGuids);
        HashSet<string>? kindFilters = BuildKindFilters(args.Kinds);

        int limit = args.Limit;

        List<(ProjectEntity Entity, string Kind)> items = [];

        if (kindFilters == null || kindFilters.Contains("map"))
            SearchByType<MapEntity>(_model.ListMaps(), namePattern, guidFilters, limit, items, "map");
        if (items.Count < limit && (kindFilters == null || kindFilters.Contains("mech")))
            SearchByType<MechEntity>(_model.ListMechs(), namePattern, guidFilters, limit, items, "mech");
        if (items.Count < limit && (kindFilters == null || kindFilters.Contains("device")))
            SearchByType<DeviceEntity>(_model.ListDevices(), namePattern, guidFilters, limit, items, "device");
        if (items.Count < limit && (kindFilters == null || kindFilters.Contains("inlineScript")))
            SearchByType<InlineScriptEntity>(_model.List<InlineScriptEntity>(), namePattern, guidFilters, limit, items, "inlineScript");
        if (items.Count < limit && (kindFilters == null || kindFilters.Contains("codeRepoScript")))
            SearchByType<CodeRepoScriptEntity>(_model.ListCodeRepoScripts(), namePattern, guidFilters, limit, items, "codeRepoScript");
        if (items.Count < limit && (kindFilters == null || kindFilters.Contains("canvas")))
            SearchByType<CanvasEntity>(_model.ListCanvases(), namePattern, guidFilters, limit, items, "canvas");

        // 代码仓库索引缓存（用于构建script的view）
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByPath = new(StringComparer.Ordinal);
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByGuid = new(StringComparer.Ordinal);
        Dictionary<string, DeviceScriptViewIndex?> deviceScriptViews = new(StringComparer.Ordinal);

        if (items.Any(static x => x.Kind == "codeRepoScript"))
            LoadCodeRepoIndexes(codeRepoByPath, codeRepoByGuid);

        foreach ((ProjectEntity entity, string kind) in items.Take(limit))
        {
            string? view = BuildView(kind, entity, codeRepoByPath, codeRepoByGuid, deviceScriptViews);
            IReadOnlyList<string>? previewPngs = BuildPreviewPngs(kind, entity);
            string? guid = null;
            if (entity is IHasGuid hasGuid)
                guid = hasGuid.Guid;
            results.Add(new EntitySearchResultItem(guid, kind, entity.实体_完整相对路径标识, view, previewPngs));
        }

        AddSearchWarnings(namePattern, warnings);
        return (new EntitySearchResponse(results), errors, warnings);
    }


    private string? BuildView(
        string kind,
        ProjectEntity entity,
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByPath,
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByGuid,
        Dictionary<string, DeviceScriptViewIndex?> deviceScriptViews)
    {
        if (entity is DeviceEntityBase device)
            return EntityViewPath.GetDeviceTsViewPath(device.device目录_完整相对路径);

        if (entity is CanvasEntityBase canvas)
            return EntityViewPath.GetCanvasHtmlViewPath(canvas.实体_完整相对路径标识);

        if (kind == "inlineScript" || kind == "codeRepoScript")
        {
            if (entity is InlineScriptEntity inlineScript)
                return BuildInlineScriptView(inlineScript, deviceScriptViews);

            if (entity is CodeRepoScriptEntity codeRepoScript)
                return BuildCodeRepoScriptView(codeRepoScript.Guid, codeRepoScript.实体_完整相对路径标识, codeRepoByPath, codeRepoByGuid);
            return null;
        }

        return null;
    }

    /// <summary>
    /// 构建实体的 PNG 预览图路径列表。
    /// 仅对独立 Canvas 返回 PNG 路径；内联 Canvas 不再生成静态预览图，返回 null。
    /// </summary>
    private IReadOnlyList<string>? BuildPreviewPngs(string kind, ProjectEntity entity)
    {
        string canvasViewsRoot = ScriptPathHelper.ViewsPath;

        // 独立 Canvas: 一个 PNG
        if (entity is CanvasEntityBase canvas)
        {
            string canvasPath = canvas.实体_完整相对路径标识;
            if (canvasPath.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
            {
                string withoutExt = canvasPath[..^ProjectEntityLayout.CanvasExt.Length];
                return new[] { $"{canvasViewsRoot}/{withoutExt}.png" };
            }
        }

        // 内联 Canvas 不再生成静态 PNG 预览图
        return null;
    }

    private string? BuildCodeRepoScriptView(
        string guid,
        string relativePath,
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByPath,
        Dictionary<string, (int StartLine, int EndLine)> codeRepoByGuid)
    {
        string codeRepoViewPath = EntityViewPath.GetCodeRepoTsViewPath();

        if (codeRepoByPath.TryGetValue(relativePath, out (int StartLine, int EndLine) spanByPath))
            return EntityViewPath.FormatViewLocation(codeRepoViewPath, spanByPath.StartLine, spanByPath.EndLine);

        if (codeRepoByGuid.TryGetValue(guid, out (int StartLine, int EndLine) spanByGuid))
            return EntityViewPath.FormatViewLocation(codeRepoViewPath, spanByGuid.StartLine, spanByGuid.EndLine);

        return codeRepoViewPath;
    }

    private string? BuildInlineScriptView(
        InlineScriptEntity inlineScript,
        Dictionary<string, DeviceScriptViewIndex?> deviceScriptViews)
    {
        string scriptRelativePath = inlineScript.实体_完整相对路径标识;
        if (ScriptPathHelper.ExtractDevicePathFromSourcePath(scriptRelativePath) is not { } deviceRelativePath)
        {
            return null;
        }

        DeviceScriptViewIndex? deviceScriptView = TryGetDeviceScriptViewIndex(deviceRelativePath, deviceScriptViews);
        if (deviceScriptView == null)
        {
            return EntityViewPath.GetDeviceTsViewPath(deviceRelativePath);
        }

        if (deviceScriptView.ScriptRangesByPath.TryGetValue(scriptRelativePath, out (int StartLine, int EndLine) span))
        {
            return EntityViewPath.FormatViewLocation(deviceScriptView.DeviceViewPath, span.StartLine, span.EndLine);
        }

        return deviceScriptView.DeviceViewPath;
    }

    private DeviceScriptViewIndex? TryGetDeviceScriptViewIndex(
        string deviceRelativePath,
        Dictionary<string, DeviceScriptViewIndex?> deviceScriptViews)
    {
        if (deviceScriptViews.TryGetValue(deviceRelativePath, out DeviceScriptViewIndex? cached))
        {
            return cached;
        }

        (DeviceTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _deviceTsView.Read(deviceRelativePath);
        if (errors.Count > 0 || data == null)
        {
            deviceScriptViews[deviceRelativePath] = null;
            return null;
        }

        Dictionary<string, (int StartLine, int EndLine)> scriptRangesByPath = new(StringComparer.Ordinal);
        foreach (DeviceTsViewScriptIndex scriptIndex in data.ScriptIndexes)
        {
            if (string.IsNullOrWhiteSpace(scriptIndex.SourceRelativePath))
                continue;
            scriptRangesByPath[scriptIndex.SourceRelativePath] = (scriptIndex.StartLine, scriptIndex.EndLine);
        }

        DeviceScriptViewIndex built = new(
            EntityViewPath.GetDeviceTsViewPath(deviceRelativePath),
            scriptRangesByPath);
        deviceScriptViews[deviceRelativePath] = built;
        return built;
    }

    private void LoadCodeRepoIndexes(
        Dictionary<string, (int StartLine, int EndLine)> byPath,
        Dictionary<string, (int StartLine, int EndLine)> byGuid)
    {
        (CodeRepoTsViewReadData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = _codeRepoTsView.Read(_model);

        if (errors.Count > 0 || data == null)
        {
            return;
        }

        foreach (CodeRepoTsViewScriptIndex script in data.Scripts)
        {
            if (script.SourceRelativePath.Length != 0)
                byPath[script.SourceRelativePath] = (script.StartLine, script.EndLine);

            if (GuidText.Validate(script.Guid))
                byGuid[script.Guid] = (script.StartLine, script.EndLine);
        }
    }

    private void SearchByType<T>(
        IReadOnlyList<T> entities,
        string? namePattern,
        HashSet<string>? guidFilters,
        int maxResults,
        List<(ProjectEntity Entity, string Kind)> results,
        string kind) where T : ProjectEntity
    {
        foreach (T entity in entities)
        {
            if (results.Count >= maxResults) break;

            if (!MatchesNamePattern(entity, namePattern))
                continue;

            if (!MatchesGuidFilter(entity, guidFilters))
                continue;

            results.Add((entity, kind));
        }
    }

#pragma warning disable TK006 // 过滤条件判定，各自封装独立匹配逻辑
    private static bool MatchesNamePattern(ProjectEntity entity, string? namePattern)
    {
        if (string.IsNullOrWhiteSpace(namePattern))
            return true;

        // namePattern 为 .NET 正则（MCP 接口定义），精确匹配用 ^xxx$
        return Regex.IsMatch(entity.实体_完整相对路径标识, namePattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }

    private static bool MatchesGuidFilter(ProjectEntity entity, HashSet<string>? guidFilters)
    {
        if (guidFilters == null)
            return true;

        if (entity is not IHasGuid hasGuid)
            return false;

        return guidFilters.Contains(hasGuid.Guid);
    }
#pragma warning restore TK006

    private static HashSet<string>? BuildGuidFilters(string[]? entityGuids)
    {
        if (entityGuids == null || entityGuids.Length == 0)
            return null;

        HashSet<string> built = new(StringComparer.Ordinal);
        foreach (string entityGuid in entityGuids)
        {
            if (GuidText.Validate(entityGuid))
                built.Add(entityGuid);
        }

        return built;
    }

    private static HashSet<string>? BuildKindFilters(string[]? kinds)
    {
        if (kinds == null || kinds.Length == 0)
            return null;

        HashSet<string> built = new(StringComparer.Ordinal);
        foreach (string kind in kinds)
        {
            if (kind.Length != 0)
                built.Add(kind);
        }

        return built;
    }

    private sealed record DeviceScriptViewIndex(
        string DeviceViewPath,
        Dictionary<string, (int StartLine, int EndLine)> ScriptRangesByPath
    );

    /// <summary>
    /// 添加搜索相关的警告（滥用数字ID、滥用"设备"一词）。
    /// 这是用户明确需求的滥用问题专项处理。
    /// </summary>
    private static void AddSearchWarnings(string? namePattern, List<ToolError> warnings)
    {
        if (string.IsNullOrWhiteSpace(namePattern)) return;

#pragma warning disable TK010 // 系统边界：用户输入的搜索词可能带有前后空白，Trim 是标准预处理
        string trimmed = namePattern.Trim();
#pragma warning restore TK010
        while (trimmed.StartsWith("^", StringComparison.Ordinal)) trimmed = trimmed[1..];
        while (trimmed.EndsWith("$", StringComparison.Ordinal)) trimmed = trimmed[..^1];

        // 纯数字警告：零件数字 ID 仅在所属地图/机械内有效
        if (Regex.IsMatch(trimmed, @"^\d+$"))
        {
            warnings.Add(new ToolError(null, null, null,
                "namePattern 是纯数字：零件数字 ID 仅在所属地图/机械内有效；底层 SDK 方法名不是数字。零件：先找到地图/机械再在零件视图路径或 Device_*_<id> 按目录名/ID 查；自定义/SDK 方法：按名称或命名空间在对应 TS 视图或 d.ts 中查找，不要把数字当作全局名称交给 read_entities。",
                ToolDiagnosticSeverity.Warning));
        }

        // "设备"用词警告：原版概念是「零件(Device)」
        if (namePattern.Contains("设备", StringComparison.Ordinal))
        {
            warnings.Add(new ToolError(null, null, null,
                "请不要使用「设备」一词，原版概念是「零件(Device)」，避免翻译混用导致定位失败。",
                ToolDiagnosticSeverity.Warning));
        }
    }
}
