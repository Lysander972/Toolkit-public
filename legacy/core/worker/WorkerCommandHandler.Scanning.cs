using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 脚本扫描相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleScanActCalls(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        string scriptText = ResolveScriptText(req.ScriptText, scriptPath);

        return Execute(() =>
        {
            IReadOnlyList<BescriptActCallRange> ranges = _services.BescriptScanner.ScanActCalls(scriptText);
            List<ActCallLink> links = new(ranges.Count);

            // 预加载所有脚本文档（用于查找脚本名）
            IReadOnlyList<ScriptDocumentInfo> allDocs = _services.Logic.GetAllScriptDocuments();

            foreach (BescriptActCallRange range in ranges)
            {
                SymbolLocation? definition = null;
                IReadOnlyList<SymbolLocation>? candidates = null;

                // 查找所有同名脚本路径
                IReadOnlyList<string> paths = _services.Logic.FindScriptPathsByName(range.ScriptName);

                if (paths.Count == 0)
                {
                    // 真正的悬空：找不到任何同名脚本
                    // definition 和 candidates 都为 null
                }
                else if (paths.Count == 1)
                {
                    // 唯一目标：直接跳转
                    ScriptDocumentInfo? docInfo = allDocs.FirstOrDefault(d => d.RelativePath == paths[0]);
                    if (docInfo != null)
                    {
                        definition = CreateSymbolLocation(paths[0], docInfo);
                    }
                }
                else
                {
                    // 多个同名目标：构建候选列表
                    // 候选包括：1.内联的 scripts 脚本组  2.独立脚本（代码仓库）
                    List<SymbolLocation> allCandidates = [];
                    foreach (string p in paths)
                    {
                        ScriptDocumentInfo? docInfo = allDocs.FirstOrDefault(d => d.RelativePath == p);
                        if (docInfo != null)
                        {
                            allCandidates.Add(CreateSymbolLocation(p, docInfo));
                        }
                    }

                    if (allCandidates.Count == 1)
                    {
                        // 过滤后只剩一个，直接跳转
                        definition = allCandidates[0];
                    }
                    else if (allCandidates.Count > 1)
                    {
                        // 多个候选，返回列表
                        candidates = allCandidates;
                    }
                }

                links.Add(new ActCallLink(range.ScriptName, range.Range, definition, candidates));
            }

            return new WorkerResponse(Ok: true, ActCallLinks: links);
        });
    }

    private static SymbolLocation CreateSymbolLocation(string path, ScriptDocumentInfo doc)
    {
        return new SymbolLocation(
            RelativePath: path,
            Line: 1,
            Column: 0,
            Name: doc.Header.Name,
            Text: $"脚本定义: {doc.Header.Name}");
    }

    private WorkerResponse HandleScanBuiltinMethodCalls(WorkerRequest req)
    {
        string scriptPath = WorkspacePath.RequireRelativePathOrEmpty(req.ScriptRelativePath);
        string scriptText = ResolveScriptText(req.ScriptText, scriptPath);

        return Execute(() =>
        {
            IReadOnlyList<BescriptBuiltinMethodRange> ranges = _services.BescriptScanner.ScanBuiltinMethodCalls(scriptText);
            return new WorkerResponse(Ok: true, BuiltinMethodRanges: ranges);
        });
    }

    private WorkerResponse HandleScanCanvasArgs(WorkerRequest req)
    {
        string scriptPath = WorkspacePath.RequireRelativePathOrEmpty(req.ScriptRelativePath);
        string scriptText = ResolveScriptText(req.ScriptText, scriptPath);

        return Execute(() =>
        {
            IReadOnlyList<BescriptCanvasArgRange> ranges = _services.BescriptScanner.ScanCanvasArgs(scriptText);
            return new WorkerResponse(Ok: true, CanvasArgRanges: ranges);
        });
    }

    private WorkerResponse HandleScanGlobalVarRefs(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        (string devicePath, string? _) = RequireDeviceOwnership(scriptPath);
        string scriptText = ResolveScriptText(req.ScriptText, scriptPath);

        return Execute(() =>
        {
            RefactorResult<GlobalVarListData> result = _services.GlobalVarOperation.List(devicePath);
            if (ToErrorResponse(result.Data, result.Errors, "获取全局变量列表失败") is { } opError)
            {
                return opError;
            }

            string[] names = (result.Data ?? throw new InvalidOperationException()).Variables.Select(v => v.Name).ToArray();
            IReadOnlyList<BescriptGlobalVarRange> ranges = _services.BescriptScanner.ScanGlobalVarRefs(scriptText, names);
            return new WorkerResponse(Ok: true, GlobalVarRanges: ranges);
        });
    }
}
