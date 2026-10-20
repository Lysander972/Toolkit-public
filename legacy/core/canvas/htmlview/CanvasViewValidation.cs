using Toolkit.Core.Canvas.Render;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Canvas;

/// <summary>
/// Canvas 视图统一校验入口。
/// TS 视图内联 Canvas 和 HTML 独立 Canvas 视图共用同一套校验标准。
///
/// 写回入口必须先完成静态 HTML→Canvas 转码；格式错误不得进入 CDP。
/// CollectHtmlLintErrors 内部解析 HTML 只为取得 CanvasNode 做 Layout Lint，不得替代写回入口的转码顺序。
/// CDP 只允许在静态转码和 Layout Lint 通过后调用。
/// </summary>
public static class CanvasViewValidation
{
    private static readonly Log _log = Log.For(LogModule.Core);
    public static List<string> ComputeNewErrors(IReadOnlyList<string> baselineErrors, IReadOnlyList<string> patchedErrors)
    {
        HashSet<string> baseline = new(baselineErrors, StringComparer.Ordinal);
        List<string> result = [];
        foreach (string error in patchedErrors)
        {
            if (baseline.Contains(error))
            {
                continue;
            }

            result.Add(error);
        }

        return result;
    }

    public static List<string> ValidateLayout(CanvasNode root)
    {
        return CanvasLayoutValidator.ValidateTree(root);
    }

    public static List<string> CollectHtmlLintErrors(string html, string sourceRelativePath)
    {
        try
        {
            string bodyHtml = CanvasHtmlViewCodec.ExtractCanvasRootFragment(html);
            (CanvasNode rootNode, _) = CanvasConverter.FromHtmlInline(bodyHtml);
            List<string> errors = ValidateLayout(rootNode);
            errors.AddRange(LintEngine.ValidateCanvasTextCharacters(rootNode, sourceRelativePath, 1).Select(static d => d.Message));
            return errors;
        }
        catch (Exception ex)
        {
            _log.Exception(ex, "Canvas HTML Lint 异常");
            return [$"HTML 视图转 Canvas 失败: {ex.Message}"];
        }
    }

    public static List<string> CollectInlineLintErrors(string rawCanvas, string sourceRelativePath, int line)
    {
        CanvasNodeResult parseResult = CanvasPreview.ParseInlineStatic(rawCanvas);
        if (!parseResult.Ok || parseResult.Root is null)
        {
            return [$"Canvas 解析失败: {parseResult.Diagnostics[0].Message}"];
        }

        List<string> errors = ValidateLayout(parseResult.Root);
        errors.AddRange(LintEngine.ValidateCanvasTextCharacters(parseResult.Root, sourceRelativePath, line).Select(static d => d.Message));
        return errors;
    }

    public static IReadOnlyList<string> ValidateHtmlRender(
        string html,
        ColorPalette palette,
        CanvasPreviewSettings settings,
        ProjectModel model,
        BrowserRenderService browser)
    {
        try
        {
            string bodyHtml = CanvasHtmlViewCodec.ExtractCanvasRootFragment(html);
            return ValidateRender(bodyHtml, palette, settings, model, browser);
        }
        catch (Exception ex)
        {
            _log.Exception(ex, "Canvas HTML 视图转码异常");
            return [$"HTML 视图转 Canvas 失败: {ex.Message}"];
        }
    }

    public static IReadOnlyList<string> ValidateRender(
        string bodyHtml,
        ColorPalette palette,
        CanvasPreviewSettings settings,
        ProjectModel model,
        BrowserRenderService browser)
    {
        string selfContainedHtml = CanvasHtmlViewAssets.BuildSelfContainedHtml(
            bodyHtml,
            palette.GetAllColors(),
            settings,
            model);

        try
        {
            browser.ValidateHtmlRender(selfContainedHtml, settings.DefaultWidth, settings.DefaultHeight);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _log.Exception(ex, "Canvas 渲染校验异常");
            return [$"渲染校验失败: {ex.Message}"];
        }
    }
}
