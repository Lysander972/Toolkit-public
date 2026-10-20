// 工程逻辑上下文 - Logic 层（逻辑/行为层）
// 职责：实体间高层次关联分析（符号引用、调用图、链接关系）
// 数据来源：Persistence 层提供的 ProjectModel（只读）
// 原 BescriptProjectIndex、CodeRepoLinkIndex、DeviceScriptPathResolver 的功能已迁移至此

using System.Security.Cryptography;
using System.Text;
using Toolkit.Core.Infra;

using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Analysis;

/// <summary>
/// 工程逻辑上下文 - Logic 层（逻辑/行为层）。
///
/// 核心职责：实体间高层次关联分析
/// - 符号引用索引：方法/变量被哪里引用
/// - 调用图构建：脚本间的调用关系
/// - 链接关系分析：零件对代码仓库的引用
/// - 脚本归属判断：脚本属于零件还是代码仓库
///
/// 缓存管理：
/// - AST 解析缓存（由 ScriptAnalysisService 管理，此处统一清除）
/// - 所有缓存与 ProjectModel 生命周期绑定，通过 RebuildIndexes() 统一失效
///
/// 数据来源：
/// - 从 Persistence 层（ProjectModel）读取内存实体
/// - 不直接操作文件系统
///
/// 禁止：
/// - 直接文件 IO（由 Persistence 层负责）
/// - 实体序列化（由 Persistence 层负责）
/// - 实体状态修改（查询时只读）
/// - 引用 Persistence 层的具体实现类型（如 FileIO）
/// </summary>
public sealed class ProjectLogicContext
{
    private readonly ProjectModel _data;
    private readonly ScriptAnalysisService _analysisService;

    /// <summary>
    /// 获取分析服务实例（用于脚本 AST 解析）。
    /// </summary>
    public ScriptAnalysisService AnalysisService => _analysisService;

    /// <summary>
    /// 获取关联的 ProjectModel 实例。
    /// </summary>
    public ProjectModel Model => _data;

    // 逻辑索引（基于 _data 中的实体构建）
    private readonly Dictionary<string, HashSet<string>> _scriptPathsByName;
    private readonly Dictionary<string, List<string>> _linkReverseIndex;

    /// <summary>GUID → 零件路径列表的反向索引（内部使用）</summary>
    internal Dictionary<string, List<string>> LinkReverseIndex => _linkReverseIndex;
    private readonly Dictionary<string, ScriptOwnership> _ownershipIndex;

    // 脚本 AST 代际缓存：视图构建路径共享，内容寻址命中 + 代际淘汰。
    // 失效由 RebuildIndexes 推进代际（不清表），淘汰由 PruneScriptCache 在重建轮末尾驱动。
    private readonly ScriptAstCache _astCache;

    /// <summary>脚本 AST 代际缓存（视图构建路径共享）。</summary>
    internal ScriptAstCache ScriptAstCache => _astCache;

    public ProjectLogicContext(ProjectModel data)
    {
        _data = data;
        _analysisService = new ScriptAnalysisService(data);
        _astCache = new ScriptAstCache();

        _scriptPathsByName = new(StringComparer.Ordinal);
        _linkReverseIndex = new(StringComparer.Ordinal);
        _ownershipIndex = new(StringComparer.Ordinal);

        RebuildIndexes();
    }

    #region 索引重建

    /// <summary>
    /// 重新构建所有逻辑索引。
    /// 在数据层发生变更后调用。
    ///
    /// 注意：这是统一缓存失效入口，会清除 Analysis 层缓存与逻辑索引。
    /// 例外：ScriptAstCache 代际缓存不清表（内容寻址保证正确性），仅推进代际驱动淘汰。
    /// </summary>
    public void RebuildIndexes()
    {
        _scriptPathsByName.Clear();
        _linkReverseIndex.Clear();
        _ownershipIndex.Clear();

        // 统一清除 Analysis 层所有缓存
        _analysisService.ClearCache();
        // AST 代际缓存：推进代际（不清表）。内容寻址保证正确性，代际只驱动淘汰。
        _astCache.AdvanceGeneration(_data.RefreshVersion);

        // 索引代码仓库脚本（仅元数据，不含 AST）
        foreach (CodeRepoScriptEntity script in _data.ListCodeRepoScripts())
        {
            IndexCodeRepoScriptMetadata(script);
        }

        // 索引零件（建立归属和链接反向索引）
        foreach (DeviceEntity device in _data.List<DeviceEntity>())
        {
            IndexDevice(device);
        }
    }

    #endregion

    #region 颜色表（统一缓存管理）

    /// <summary>
    /// 获取颜色表（带缓存）。
    /// 缓存由 RebuildIndexes() 统一失效。
    /// </summary>
    public ColorPalette GetColorPalette()
    {
        ColorPalette palette = new();

        try
        {
            palette.LoadFromProject(_data);
        }
        catch (Exception ex)
        {
            // 颜色表加载失败时返回空调色板（不阻止预览功能）这是原版特性可以没有颜色表
            // Offensive: 即使是可容忍的错误也要收集，不能静默吃掉
            string? paletteExcel = _data.Root.SettingJson?.PaletteExcel;
            Log.For(LogModule.Core).Warning($"颜色表加载失败 ({paletteExcel}): {ex.Message}");
        }

        return palette;
    }

    #endregion

    #region 索引构建（私有）

    /// <summary>
    /// 索引代码仓库脚本元数据（仅从实体属性，不涉及 AST 解析）。
    /// </summary>
    private void IndexCodeRepoScriptMetadata(CodeRepoScriptEntity script)
    {
        // 按名称索引（从路径推断名称）
        string name = script.ScriptName;
        if (name.Length != 0)
        {
            if (!_scriptPathsByName.ContainsKey(name))
                _scriptPathsByName[name] = new HashSet<string>(StringComparer.Ordinal);
            _scriptPathsByName[name].Add(script.实体_完整相对路径标识);
        }
    }

    private void IndexDevice(DeviceEntity device)
    {
        // 索引自定义方法脚本（仅非事件脚本组）：_scriptPathsByName 用于 act 调用 →
        // 脚本定义解析（LSP goto-definition / hover / 链接扫描）。事件脚本不能被作为
        // 自定义方法调用（act 设备.事件脚本名() 非法），所以禁止纳入此索引；如需扫
        // 描事件脚本的内容（字符校验、签名校验、引用查找等），请改用 device.AllScripts。
        foreach (InlineScriptEntityBase script in device.Scripts)
        {
            ScriptOwnership ownership = new(
                true,
                device.device目录_完整相对路径,
                script.EventName,
                ScriptSource.Device
            );
            _ownershipIndex[script.实体_完整相对路径标识] = ownership;

            // 按脚本名称索引（内联脚本）
            string name = script.ScriptName;
            if (name.Length != 0)
            {
                if (!_scriptPathsByName.ContainsKey(name))
                {
                    _scriptPathsByName[name] = new HashSet<string>(StringComparer.Ordinal);
                }
                _scriptPathsByName[name].Add(script.实体_完整相对路径标识);
            }
        }

        // 建立链接反向索引 + 链接脚本名称索引
        foreach (DeviceLinkInfo link in device.Links)
        {
            // 反向索引：GUID -> 零件路径列表
            if (!_linkReverseIndex.ContainsKey(link.Guid))
            {
                _linkReverseIndex[link.Guid] = [];
            }
            List<string> list = _linkReverseIndex[link.Guid];
            if (!list.Contains(device.device目录_完整相对路径))
            {
                list.Add(device.device目录_完整相对路径);
            }

            // 名称索引：链接脚本名 -> 代码仓库脚本路径
            // 通过 GUID 查找代码仓库脚本实体，获取其路径
            if (link.Name.Length != 0 &&
                _data.FindByGuid<CodeRepoScriptEntity>(link.Guid) is CodeRepoScriptEntity codeRepoScript)
            {
                if (!_scriptPathsByName.ContainsKey(link.Name))
                {
                    _scriptPathsByName[link.Name] = new HashSet<string>(StringComparer.Ordinal);
                }
                _scriptPathsByName[link.Name].Add(codeRepoScript.实体_完整相对路径标识);
            }
        }
    }

    #endregion

    #region 查询 API（原 BescriptProjectIndex）

    /// <summary>
    /// 按脚本名查找所有匹配的脚本路径。
    /// </summary>
    public IReadOnlyList<string> FindScriptPathsByName(string name)
    {
        if (_scriptPathsByName.ContainsKey(name))
        {
            HashSet<string> set = _scriptPathsByName[name];
            lock (set)
            {
                return set.ToArray();
            }
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// 按符号名查找所有引用位置。
    /// 按需解析脚本获取 AST 进行搜索（包含代码仓库脚本和零件内联脚本）。
    /// </summary>
    public IReadOnlyList<SymbolReference> FindSymbolReferences(string symbolName)
    {
        List<SymbolReference> references = [];

        // 遍历所有代码仓库脚本进行按需解析
        foreach (CodeRepoScriptEntity script in _data.ListCodeRepoScripts())
        {
            string scriptPath = script.实体_完整相对路径标识;
            BescriptParseData? parseData = _analysisService.ParseScript(scriptPath);
            if (parseData != null && parseData.Ast.RootNodes.Count > 0)
            {
                FindSymbolInNodes(scriptPath, parseData.Ast.RootNodes, symbolName, references);
            }
        }

        // 遍历所有零件的内联脚本进行按需解析（含事件脚本组与非事件脚本组）
        foreach (DeviceEntity device in _data.List<DeviceEntity>())
        {
            foreach (InlineScriptEntityBase script in device.AllScripts)
            {
                string scriptPath = script.实体_完整相对路径标识;
                BescriptParseData? parseData = _analysisService.ParseScript(scriptPath);
                if (parseData != null && parseData.Ast.RootNodes.Count > 0)
                {
                    FindSymbolInNodes(scriptPath, parseData.Ast.RootNodes, symbolName, references);
                }
            }
        }

        return references;
    }

    /// <summary>
    /// 在 AST 节点中递归查找符号引用。
    /// </summary>
    private static void FindSymbolInNodes(
        string scriptRelativePath,
        IReadOnlyList<BescriptAstNode> nodes,
        string symbolName,
        List<SymbolReference> results)
    {
        foreach (BescriptAstNode node in nodes)
        {
            // 检查当前节点是否匹配
            if (node.Kind == BescriptStatementKind.Call &&
                node is IHasCall { Call: var call } &&
                string.Equals(call.FunctionName, symbolName, StringComparison.Ordinal))
            {
                results.Add(new SymbolReference(
                    symbolName,
                    scriptRelativePath,
                    node.Line,
                    node.Depth,
                    node.Kind,
                    node.Text,
                    ((IHasCall)node).Call.Kind,
                    ((IHasCall)node).Call switch
                    {
                        VarfCallSignature v => v.OwnerName,
                        ActCallSignature a => a.OwnerName,
                        DvfCallSignature d => d.OwnerName,
                        _ => null
                    },
                    ((IHasCall)node).Call switch
                    {
                        VarfCallSignature v => v.OwnerNameIsAlias,
                        ActCallSignature a => a.OwnerNameIsAlias,
                        DvfCallSignature d => d.OwnerNameIsAlias,
                        _ => false
                    }
                ));
            }

            // 递归处理子节点
            if (node.Children.Count > 0)
            {
                FindSymbolInNodes(scriptRelativePath, node.Children, symbolName, results);
            }
        }
    }

    /// <summary>
    /// 获取所有脚本文档（按需解析 AST）。
    /// 包含代码仓库脚本和零件内联脚本。
    /// </summary>
    public IReadOnlyList<ScriptDocumentInfo> GetAllScriptDocuments()
    {
        List<ScriptDocumentInfo> documents = [];

        // 代码仓库脚本
        foreach (CodeRepoScriptEntity script in _data.ListCodeRepoScripts())
        {
            string scriptPath = script.实体_完整相对路径标识;
            BescriptParseData? parseData = _analysisService.ParseScript(scriptPath);
            if (parseData != null)
            {
                documents.Add(new ScriptDocumentInfo(
                    scriptPath,
                    BescriptHeaderFromParseData(parseData),
                    parseData.Ast,
                    DateTimeOffset.UtcNow.Ticks,
                    DateTimeOffset.UtcNow
                ));
            }
        }

        // 零件内联脚本（含事件脚本组与非事件脚本组）
        foreach (DeviceEntity device in _data.List<DeviceEntity>())
        {
            foreach (InlineScriptEntityBase script in device.AllScripts)
            {
                string scriptPath = script.实体_完整相对路径标识;
                BescriptParseData? parseData = _analysisService.ParseScript(scriptPath);
                if (parseData != null)
                {
                    documents.Add(new ScriptDocumentInfo(
                        scriptPath,
                        BescriptHeaderFromParseData(parseData),
                        parseData.Ast,
                        DateTimeOffset.UtcNow.Ticks,
                        DateTimeOffset.UtcNow
                    ));
                }
            }
        }

        return documents;
    }

    /// <summary>
    /// 从 BescriptParseData 构建 BescriptHeader（用于兼容）。
    /// </summary>
    private static BescriptHeader BescriptHeaderFromParseData(BescriptParseData data)
    {
        return new BescriptHeader(
            data.Name,
            data.Comment,
            data.Debug,
            data.Skip,
            data.Private,
            data.Guid,
            data.Color,
            data.Parameters,
            data.ReturnParameter
        );
    }

    #endregion

    #region 查询 API（原 DeviceScriptPathResolver）

    /// <summary>
    /// 查询脚本归属信息。
    /// 索引中存在则属于零件，不存在则属于代码仓库（代码仓库脚本不进入归属索引）。
    /// </summary>
    public ScriptOwnership GetScriptOwnership(string scriptRelativePath)
    {
        string validatedScriptPath = WorkspacePath.RequireRelativePath(scriptRelativePath);

        if (_ownershipIndex.ContainsKey(validatedScriptPath))
        {
            return _ownershipIndex[validatedScriptPath];
        }

        // 代码仓库脚本不在归属索引中，查不到即属于代码仓库
        return new ScriptOwnership(
            false,
            null,
            null,
            ScriptSource.CodeRepo
        );
    }

    /// <summary>
    /// 获取全部代码仓库脚本（走 AST 代际缓存）。
    /// 同一轮刷新内同内容只解析一次，消除视图构建路径对 CodeRepo 的重复扫描。
    /// </summary>
    public IReadOnlyList<ScriptFileInfo> GetCodeRepoScripts(List<ToolError> warnings)
    {
        return CodeRepoScripts.ScanOrCache(_data, _astCache, warnings);
    }

    /// <summary>
    /// 淘汰 AST 缓存中未被当前代复用的条目。由 McpViewWorkspace 重建轮阶段四调用。
    /// </summary>
    public void PruneScriptCache()
    {
        _astCache.PruneToCurrentGeneration();
    }

    /// <summary>
    /// 逻辑内容指纹：覆盖 custom-logic.d.ts / CodeRepo.ts 等逻辑产物的全部输入——
    /// 设备集、device.json 内容、载体 kind+guid、各脚本组 list.json、全部内联 .code、
    /// 代码仓库脚本内容与 GUID。不覆盖 vars.json/canvas 等非逻辑内容。
    /// 文件哈希走 FileSystemCache（零 IO，内容与文件一致即指纹一致）。
    /// 用途：产物缓存键——逻辑内容不变则产物不变，无关改动（canvas 等）不触发重算。
    /// </summary>
    public string ComputeLogicFingerprint()
    {
        _data.EnterReadLock();
        try
        {
            StringBuilder sb = new();
            foreach (CodeRepoScriptEntity script in _data.ListCodeRepoScripts())
            {
                sb.Append(script.实体_完整相对路径标识).Append('\n');
                sb.Append(script.Guid).Append('\n');
                AppendFileHash(sb, script.实体_完整相对路径标识);
            }
            foreach (DeviceEntity device in _data.List<DeviceEntity>())
            {
                sb.Append(device.实体_完整相对路径标识).Append('\n');
                sb.Append(device.CarrierKind).Append('/').Append(device.CarrierGuid).Append('\n');
                AppendFileHash(sb, device.device配置_完整相对路径);
                foreach (ScriptGroupEntityBase group in device.ScriptGroups)
                {
                    AppendFileHash(sb, group.listJson_完整相对路径);
                    foreach (InlineScriptEntityBase inline in group.Scripts)
                    {
                        AppendFileHash(sb, inline.实体_完整相对路径标识);
                    }
                }
            }
            return Convert.ToHexString(SHA256.HashData(TextEncoding.Utf8.GetBytes(sb.ToString())));
        }
        finally
        {
            _data.ExitReadLock();
        }
    }

    /// <summary>
    /// 把文件内容哈希拼入指纹；文件允许缺失（如 list.json 缺省视为空清单），以固定标记参与。
    /// </summary>
    private static void AppendFileHash(StringBuilder sb, string fileRelativePath)
    {
        sb.Append(fileRelativePath).Append('=');
        sb.Append(FileIO.FileExists(fileRelativePath)
            ? Convert.ToHexString(FileIO.GetFileHash(fileRelativePath))
            : "<missing>");
        sb.Append('\n');
    }

    #endregion
}

#region 数据模型

/// <summary>
/// 脚本来源
/// </summary>
public enum ScriptSource
{
    Device,
    CodeRepo
}

/// <summary>
/// 脚本归属信息 - 判断脚本属于零件还是代码仓库
/// </summary>
public sealed record ScriptOwnership(
    bool BelongsToDevice,
    string? DeviceRelativePath,
    string? EventName,
    ScriptSource Source
);

/// <summary>
/// 脚本文档信息（从原 BescriptProjectIndex 迁移）
/// </summary>
public sealed record ScriptDocumentInfo(
    string RelativePath,
    BescriptHeader Header,
    BescriptAst? Ast,
    long Version,
    DateTimeOffset LastUpdatedUtc
);

/// <summary>
/// 符号引用位置（从原 BescriptProjectIndex 迁移）
/// </summary>
public sealed record SymbolReference(
    string SymbolName,
    string ScriptRelativePath,
    int Line,
    int Depth,
    BescriptStatementKind Kind,
    string Text,
    BescriptCallKind? CallKind,
    string? OwnerName,
    bool OwnerNameIsAlias
);

#endregion
