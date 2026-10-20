using System.Text.Json;
using System.Text.RegularExpressions;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpBrowserTests")]
public sealed class PatchMechViewHtmlValidationTests
{
    private readonly McpBrowserTestFixture _fixture;
    private const string CanvasOpenTag = "<BeCanvas";
    private const string CanvasCloseTag = "</BeCanvas>";

    public PatchMechViewHtmlValidationTests(McpBrowserTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static (string viewRelativePath, string currentHtml)? BuildCanvasPatchTarget(ToolkitServices services)
    {
        CanvasEntity? canvas = services.Model.ListCanvases().FirstOrDefault();
        if (canvas == null) return null;

        string canvasRelativePath = WorkspacePath.RequireRelativePath(canvas.实体_完整相对路径标识);

        (CanvasHtmlViewReadData? readData, IReadOnlyList<ToolError> readErrors, _) = services.CanvasHtmlView.Read(canvasRelativePath);
        Assert.Empty(readErrors);
        Assert.NotNull(readData);

        return ($"{canvasRelativePath[..^ProjectEntityLayout.CanvasExt.Length]}.html", readData.Html);
    }

    private static async Task<JsonElement> InvokePatchMechViewAsync(
        McpToolRegistry registry,
        string viewPath,
        string oldString,
        string newString)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler("patch_mech_view");
        Assert.NotNull(handler);

        using JsonDocument requestDoc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            view_path = viewPath,
            old_string = oldString,
            new_string = newString
        }));
        object? response = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));
        return responseDoc.RootElement.Clone();
    }

    private static async Task AssertPatchFailedWithMessageAsync(
        McpToolRegistry registry,
        string viewPath,
        string currentHtml,
        string invalidHtml,
        params string[] expectedMessageFragments)
    {
        Assert.NotEqual(currentHtml, invalidHtml);

        JsonElement response = await InvokePatchMechViewAsync(
            registry,
            viewPath,
            currentHtml,
            invalidHtml);

        Assert.True(response.TryGetProperty("errors", out JsonElement errorsElement));
        Assert.Equal(JsonValueKind.Array, errorsElement.ValueKind);

        string[] messages = errorsElement.EnumerateArray()
            .Select(item =>
            {
                string? message = item.GetProperty("message").GetString();
                if (message is null)
                {
                    throw new InvalidOperationException("错误缺少 message");
                }

                return message;
            })
            .ToArray();

        Assert.NotEmpty(messages);
        foreach (string fragment in expectedMessageFragments)
        {
            Assert.Contains(messages, message => message.Contains(fragment, StringComparison.Ordinal));
        }
    }

    private static async Task AssertPatchSucceededWithoutErrorsAsync(
        McpToolRegistry registry,
        string viewPath,
        string currentHtml,
        string patchedHtml)
    {
        Assert.NotEqual(currentHtml, patchedHtml);

        JsonElement response = await InvokePatchMechViewAsync(
            registry,
            viewPath,
            currentHtml,
            patchedHtml);

        if (!response.TryGetProperty("errors", out JsonElement errorsElement))
        {
            return;
        }

        Assert.Equal(JsonValueKind.Array, errorsElement.ValueKind);
        string[] messages = errorsElement.EnumerateArray()
            .Select(item =>
            {
                string? message = item.GetProperty("message").GetString();
                if (message is null)
                {
                    throw new InvalidOperationException("错误缺少 message");
                }

                return message;
            })
            .ToArray();
        Assert.Empty(messages);
    }

    private static string? ReplaceFirstByRegex(string source, string pattern, string replacement)
    {
        Match match = Regex.Match(source, pattern, RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return source[..match.Index] + replacement + source[(match.Index + match.Length)..];
    }

    private static string? RewriteCanvasFragment(string html, Func<string, string> rewrite)
    {
        int openIndex = html.IndexOf(CanvasOpenTag, StringComparison.Ordinal);
        if (openIndex < 0) return null;

        int closeIndex = html.IndexOf(CanvasCloseTag, openIndex, StringComparison.Ordinal);
        if (closeIndex < 0) return null;

        int fragmentEndIndex = closeIndex + CanvasCloseTag.Length;
        string fragment = html[openIndex..fragmentEndIndex];
        string rewrittenFragment = rewrite(fragment);
        if (string.Equals(fragment, rewrittenFragment, StringComparison.Ordinal)) return null;

        return html[..openIndex] + rewrittenFragment + html[fragmentEndIndex..];
    }

    [Fact]
    public async Task PatchCanvasHtml_UnknownAttribute_ShouldReturnValidationError()
    {
        if (!_fixture.IsAvailable) return;

        if (BuildCanvasPatchTarget(_fixture.Services) is not (string viewRelativePath, string currentHtml)) return;

        string invalidHtml = currentHtml.Replace("<BeCanvas ", "<BeCanvas invalid_attr=\"1\" ", StringComparison.Ordinal);
        await AssertPatchFailedWithMessageAsync(
            _fixture.Registry,
            viewRelativePath,
            currentHtml,
            invalidHtml,
            "HTML 视图转 Canvas 失败",
            "HTML视图存在未知属性",
            "CanvasTypesNodes.ts");
    }

    [Fact]
    public async Task PatchCanvasHtml_MissingCanvasCloseTag_ShouldReturnValidationError()
    {
        if (!_fixture.IsAvailable) return;

        if (BuildCanvasPatchTarget(_fixture.Services) is not (string viewRelativePath, string currentHtml)) return;

        string invalidHtml = currentHtml.Replace("</BeCanvas>", "</BrokenCanvas>", StringComparison.Ordinal);
        await AssertPatchFailedWithMessageAsync(
            _fixture.Registry,
            viewRelativePath,
            currentHtml,
            invalidHtml,
            "HTML 视图转 Canvas 失败",
            "Canvas XML解析失败",
            "does not match the end tag");
    }

    [Fact]
    public async Task PatchCanvasHtml_InvalidEnumValue_ShouldReturnValidationError()
    {
        if (!_fixture.IsAvailable) return;

        if (BuildCanvasPatchTarget(_fixture.Services) is not (string viewRelativePath, string currentHtml)) return;

        string? invalidHtml = RewriteCanvasFragment(
            currentHtml,
            static fragment =>
            {
                if (ReplaceFirstByRegex(fragment, "parentanchor=\"[^\"]+\"", "parentanchor=\"NOT_A_REAL_ENUM\"") is string patchedByParentAnchor)
                {
                    return patchedByParentAnchor;
                }

                if (ReplaceFirstByRegex(fragment, "anchor=\"[^\"]+\"", "anchor=\"NOT_A_REAL_ENUM\"") is string patchedByAnchor)
                {
                    return patchedByAnchor;
                }

                return fragment.Replace(
                    CanvasCloseTag,
                    "  <BeUIRect name=\"bad_enum\" parentanchor=\"NOT_A_REAL_ENUM\" anchor=\"MIDDLE_CENTER\" pos=\"0,0,0\" size=\"1,1,1\"></BeUIRect>\n" + CanvasCloseTag,
                    StringComparison.Ordinal);
            });

        if (invalidHtml == null) return;

        await AssertPatchFailedWithMessageAsync(
            _fixture.Registry,
            viewRelativePath,
            currentHtml,
            invalidHtml,
            "HTML 视图转 Canvas 失败",
            "需要有效的枚举名称");
    }

    [Fact]
    public async Task PatchCanvasHtml_InvalidVectorFormat_ShouldReturnValidationError()
    {
        if (!_fixture.IsAvailable) return;

        if (BuildCanvasPatchTarget(_fixture.Services) is not (string viewRelativePath, string currentHtml)) return;

        string? invalidHtml = RewriteCanvasFragment(
            currentHtml,
            static fragment =>
            {
                if (ReplaceFirstByRegex(fragment, "size=\"[^\"]+\"", "size=\"1,2\"") is string patchedBySize)
                {
                    return patchedBySize;
                }

                if (ReplaceFirstByRegex(fragment, "pos=\"[^\"]+\"", "pos=\"1,2\"") is string patchedByPos)
                {
                    return patchedByPos;
                }

                return fragment.Replace(
                    CanvasCloseTag,
                    "  <BeUIRect name=\"bad_vec\" parentanchor=\"MIDDLE_CENTER\" anchor=\"MIDDLE_CENTER\" pos=\"1,2\" size=\"1,1,1\"></BeUIRect>\n" + CanvasCloseTag,
                    StringComparison.Ordinal);
            });

        if (invalidHtml == null) return;

        await AssertPatchFailedWithMessageAsync(
            _fixture.Registry,
            viewRelativePath,
            currentHtml,
            invalidHtml,
            "HTML 视图转 Canvas 失败",
            "需要向量格式 x,y,z");
    }

    [Fact]
    public async Task PatchCanvasHtml_HeadOnlyChange_ShouldNotProduceValidationError()
    {
        if (!_fixture.IsAvailable) return;

        if (BuildCanvasPatchTarget(_fixture.Services) is not (string viewRelativePath, string currentHtml)) return;

        const string oldHead = "<meta charset=\"UTF-8\">";
        if (!currentHtml.Contains(oldHead, StringComparison.Ordinal)) return;

        string patchedHtml = currentHtml.Replace(
            oldHead,
            "<meta charset=\"UTF-8\">\n<meta name=\"head-only-test\" content=\"1\">",
            StringComparison.Ordinal);

        await AssertPatchSucceededWithoutErrorsAsync(
            _fixture.Registry,
            viewRelativePath,
            currentHtml,
            patchedHtml);
    }
}
