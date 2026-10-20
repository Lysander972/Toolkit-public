using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - 文档查询相关命令
/// </summary>
public sealed partial class WorkerCommandHandler
{
    private WorkerResponse HandleGetBuiltinMethodDoc(WorkerRequest req)
    {
        string methodName = RequireParam(req.SymbolName, "symbolName");

        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        // 第1层：精确路径匹配（无category限制，支持任意完整路径）
        MethodSignature? sig = registry.GetByPath(methodName);

        if (sig is null && OwnerPath.SplitOwnerPath(methodName) is { } ownerSplit)
        {
            // 第2层：继承链查询，VarFunc和DeviceAction方法名不重叠，优先级无歧义
            sig = registry.GetByPathWithInheritance(methodName, MethodCategory.VarFunc);
        }

        if (sig is null && !methodName.Contains('.', StringComparison.Ordinal))
        {
            // 第3层：act调用的ownerType是数字ID，Scanner无法生成完整路径，需全局唯一匹配
            sig = FindUniqueDeviceActionByMethodName(registry, methodName);
        }
        if (sig is null)
        {
            return new WorkerResponse(Ok: false, Message: $"未知内置方法: {methodName}");
        }

        // 继承链查询时OwnerType可能需要覆盖为调用方的ownerType
        MethodSignature displaySig = sig;
        if (OwnerPath.SplitOwnerPath(methodName) is { } split && sig.OwnerType != split.ownerType)
        {
            displaySig = sig with { OwnerType = split.ownerType };
        }

        (DocRef? HumanDoc, DocRef? AgentDoc) refs = BuiltinMethodDocRefs.Get(displaySig);

        BuiltinMethodDoc doc = new(
            Name: displaySig.GetFullPath(),
            Signature: displaySig.ToDisplayString(),
            Documentation: displaySig.GetDocumentation(),
            HumanDoc: refs.HumanDoc,
            AgentDoc: refs.AgentDoc
        );

        return new WorkerResponse(Ok: true, BuiltinMethodDoc: doc);
    }

    private WorkerResponse HandleGetEventDocs(WorkerRequest req)
    {
        string eventName = RequireParam(req.EventName, "eventName");

        return Execute(() =>
        {
            IReadOnlyList<EventDoc> docs = ListEventDocs(eventName, req.DeviceType);
            if (docs.Count == 0)
            {
                return new WorkerResponse(Ok: false, Message: $"未知事件: {eventName}");
            }

            return new WorkerResponse(Ok: true, EventDocs: docs);
        });
    }

    private WorkerResponse HandleFindReferences(WorkerRequest req)
    {
        string symbolName = RequireParam(req.SymbolName, "symbolName");

        return Execute(() =>
        {
            // 内联：查找符号引用位置
            List<SymbolLocation> results = [];
            IReadOnlyList<SymbolReference> refs = _services.Logic.FindSymbolReferences(symbolName);
            foreach (SymbolReference r in refs)
            {
                results.Add(new SymbolLocation(
                    RelativePath: r.ScriptRelativePath,
                    Line: r.Line,
                    Column: 0,
                    Name: r.SymbolName,
                    Text: r.Text));
            }

            return new WorkerResponse(Ok: true, References: results);
        });
    }

    #region 文档查询辅助方法

    private static IReadOnlyList<EventDoc> ListEventDocs(string eventName, string? deviceType)
    {
        EventSignatureRegistry registry = EventSignatureRegistry.Instance;

        IReadOnlyList<EventSignature> signatures;

        if (!string.IsNullOrWhiteSpace(deviceType))
        {
            EventSignature? sig = registry.Get(deviceType, eventName);
            signatures = Array.Empty<EventSignature>();
            if (sig != null)
            {
                signatures = new[] { sig };
            }
        }
        else
        {
            signatures = registry.GetByName(eventName);
        }

        if (signatures.Count == 0)
        {
            return Array.Empty<EventDoc>();
        }

        List<EventDoc> list = new(signatures.Count);
        foreach (EventSignature? sig in signatures.OrderBy(s => s.DeviceType, StringComparer.Ordinal))
        {
            EventParameterDoc[] parameters = sig.Parameters
                .Select(p => new EventParameterDoc(p.EffectiveName, p.DisplayTypeName, p.Description))
                .ToArray();

            list.Add(new EventDoc(
                EventName: sig.EventName,
                DeviceType: sig.DeviceType,
                Signature: sig.ToDisplayString(),
                Documentation: sig.GetDocumentation(),
                ParamsLine: sig.ToParamsLineString(),
                Parameters: parameters
            ));
        }

        return list;
    }

    private static MethodSignature? FindUniqueDeviceActionByMethodName(BuiltinMethodRegistry registry, string methodName)
    {
        const int pageSize = 1000;
        int skip = 0;

        MethodSignature? found = null;

        while (true)
        {
            MethodQueryResult page = registry.Query(
                namePattern: null,
                category: MethodCategory.DeviceAction,
                ownerType: null,
                skip: skip,
                take: pageSize);

            foreach (MethodSignature sig in page.Items)
            {
                if (!sig.MethodName.Equals(methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (found is not null)
                {
                    return null;
                }

                found = sig;
            }

            if (page.Items.Count == 0 || skip + page.Items.Count >= page.Total)
            {
                break;
            }

            skip += page.Items.Count;
        }

        return found;
    }

    #endregion
}
