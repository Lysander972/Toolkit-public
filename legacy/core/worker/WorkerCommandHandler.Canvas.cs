using Toolkit.Core.Canvas;
using Toolkit.Core.Refactor;

namespace Toolkit.Core.Worker;

/// <summary>
/// Worker 命令处理器 - Canvas 相关命令
///
/// Canvas 预览流程：
/// ============
/// 插件侧：
/// - Canvas 文件预览：读取 .canvas 二进制后转为 HTML
/// - 内联 Canvas 预览：传入解码后的旧版 canvas:名称=Base64 或新版 ==hjson:: 参数后转为 HTML
/// - Git 版本预览：通过 previewCanvasBinary 命令，传递二进制内容生成 HTML
/// </summary>
public sealed partial class WorkerCommandHandler
{
    /// <summary>
    /// 处理 canvas 二进制预览请求
    /// 用于支持 git:/ URI 等非文件系统路径的预览
    /// </summary>
    private WorkerResponse HandlePreviewCanvasBinary(WorkerRequest req)
    {
        string? base64 = req.CanvasBinaryBase64;
        if (string.IsNullOrWhiteSpace(base64))
        {
            return new WorkerResponse(Ok: false, Message: "缺少 canvasBinaryBase64 参数");
        }

        return Execute(() =>
        {
            byte[] canvasBytes;
            try
            {
                canvasBytes = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                return new WorkerResponse(Ok: false, Message: $"Base64 解码失败: {ex.Message}");
            }

            if (canvasBytes.Length == 0)
            {
                return new WorkerResponse(Ok: false, Message: "Canvas 二进制数据为空");
            }

            try
            {
                string html = CanvasConverter.BinaryToHtml(canvasBytes);
                return new WorkerResponse(Ok: true, CanvasPreviewHtml: html);
            }
            catch (InvalidOperationException ex)
            {
                return new WorkerResponse(Ok: false, Message: $"Canvas 解析失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new WorkerResponse(Ok: false, Message: $"Canvas 转换失败: {ex}");
            }
        });
    }

    /// <summary>
    /// 处理内联 Canvas 参数预览请求
    /// 输入必须为解码后的原始格式: 旧版 canvas:名称=Base64(...) 或新版 ==hjson:: 块
    /// </summary>
    private WorkerResponse HandlePreviewCanvasInlineArg(WorkerRequest req)
    {
        string? canvasArg = req.CanvasArg;
        if (string.IsNullOrWhiteSpace(canvasArg))
        {
            return new WorkerResponse(Ok: false, Message: "缺少 canvasArg 参数");
        }

        return Execute(() =>
        {
            try
            {
                CanvasNodeResult nodeResult = CanvasConverter.FromInline(canvasArg);
                if (!nodeResult.Ok || nodeResult.Root == null)
                {
                    return new WorkerResponse(Ok: false, Message: $"内联 Canvas 解析失败: {nodeResult.Diagnostics[0].Message}");
                }

                string html = CanvasConverter.ToHtml(nodeResult.Root, nodeResult.CanvasName);
                return new WorkerResponse(Ok: true, CanvasPreviewHtml: html);
            }
            catch (InvalidOperationException ex)
            {
                return new WorkerResponse(Ok: false, Message: $"内联 Canvas 转换失败: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new WorkerResponse(Ok: false, Message: $"内联 Canvas 预览失败: {ex}");
            }
        });
    }

    private WorkerResponse HandleCanvasExtract(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        int line = req.Line ?? throw new InvalidOperationException("缺少有效的 line 参数");

        return Execute(() =>
        {
            RefactorResult<CanvasExtractData> result = _services.CanvasRefactor.Extract(scriptPath, line);
            if (ToErrorResponse(result.Data, result.Errors, "Canvas 提取失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, CanvasExtract: result.Data);
        });
    }

    private WorkerResponse HandleCanvasImportByPath(string scriptPath, int line, string canvasRelativePath)
    {
        return Execute(() =>
        {
            RefactorResult<CanvasImportData> result = _services.CanvasRefactor.ImportByPath(scriptPath, line, canvasRelativePath);
            if (ToErrorResponse(result.Data, result.Errors, "Canvas 导入失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, CanvasImport: result.Data);
        });
    }

    private WorkerResponse HandleCanvasImportByGuid(string scriptPath, int line, string targetGuid)
    {
        return Execute(() =>
        {
            RefactorResult<CanvasImportData> result = _services.CanvasRefactor.ImportByGuid(scriptPath, line, targetGuid);
            if (ToErrorResponse(result.Data, result.Errors, "Canvas 导入失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, CanvasImport: result.Data);
        });
    }

    private WorkerResponse HandleCanvasInline(WorkerRequest req)
    {
        string scriptPath = RequirePath(req.ScriptRelativePath, "scriptPath");
        int line = req.Line ?? throw new InvalidOperationException("缺少有效的 line 参数");

        return Execute(() =>
        {
            RefactorResult<CanvasInlineData> result = _services.CanvasRefactor.Inline(scriptPath, line);
            if (ToErrorResponse(result.Data, result.Errors, "Canvas 内联失败") is { } opError)
            {
                return opError;
            }

            return new WorkerResponse(Ok: true, CanvasInline: result.Data);
        });
    }
}
