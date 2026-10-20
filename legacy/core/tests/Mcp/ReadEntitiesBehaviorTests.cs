using System.Text.Json;
using System.Text.RegularExpressions;
using Toolkit.Mcp;
using Xunit;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Mcp;

[Collection("McpTests")]
public sealed class ReadEntitiesBehaviorTests
{
    private readonly McpTestFixture _fixture;

    public ReadEntitiesBehaviorTests(McpTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task<JsonElement> InvokeReadEntitiesAsync(McpToolRegistry registry, object args)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler("read_entities_by_name");
        Assert.NotNull(handler);

        string rawJson = JsonSerializer.Serialize(args);
        using JsonDocument requestDoc = JsonDocument.Parse(rawJson);
        object? response = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));
        return responseDoc.RootElement.Clone();
    }

    private static async Task<JsonElement> InvokeReadEntitiesByGuidAsync(McpToolRegistry registry, object args)
    {
        Func<JsonElement?, CancellationToken, Task<object?>>? handler = registry.TryGetHandler("read_entities_by_guid");
        Assert.NotNull(handler);

        string rawJson = JsonSerializer.Serialize(args);
        using JsonDocument requestDoc = JsonDocument.Parse(rawJson);
        object? response = await handler(requestDoc.RootElement, CancellationToken.None);
        using JsonDocument responseDoc = JsonDocument.Parse(JsonSerializer.Serialize(response, CamelCaseOptions));
        return responseDoc.RootElement.Clone();
    }

    private static JsonElement GetPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value))
        {
            return value;
        }

        string pascalName = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        if (element.TryGetProperty(pascalName, out value))
        {
            return value;
        }

        throw new KeyNotFoundException($"属性不存在: {propertyName}");
    }

    [Fact]
    public async Task ReadEntities_Should_Filter_By_EntityGuids()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        string? targetMapGuid = _fixture.Services.Model.ListMaps()
            .Select(m => m.Guid)
            .First(g => GuidText.Validate(g));

        JsonElement response = await InvokeReadEntitiesByGuidAsync(_fixture.Registry, new
        {
            entity_guids = new[] { targetMapGuid }
        });

        JsonElement errors = GetPropertyIgnoreCase(response, "errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Empty(errors.EnumerateArray());
        JsonElement data = GetPropertyIgnoreCase(response, "data");
        JsonElement entities = GetPropertyIgnoreCase(data, "entities");
        Assert.Contains(entities.EnumerateArray(), item =>
            string.Equals(GetPropertyIgnoreCase(item, "guid").GetString(), targetMapGuid, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadEntities_ScriptKind_Should_Include_InlineScriptPath()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        JsonElement response = await InvokeReadEntitiesAsync(_fixture.Registry, new
        {
            kinds = new[] { "inlineScript" },
            name_pattern = @".*\.code$",
            limit = 200
        });

        JsonElement errors = GetPropertyIgnoreCase(response, "errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Empty(errors.EnumerateArray());
        JsonElement data = GetPropertyIgnoreCase(response, "data");
        JsonElement entities = GetPropertyIgnoreCase(data, "entities");
        Assert.NotEmpty(entities.EnumerateArray());

        Assert.Contains(entities.EnumerateArray(), static item =>
        {
            string? nameValue = item.GetProperty("name").GetString();
            if (nameValue is null)
            {
                throw new InvalidOperationException("实体缺少 name");
            }
            string name = nameValue;
            if (!name.EndsWith(".code", StringComparison.Ordinal))
                return false;

            string? viewValue = item.GetProperty("view").GetString();
            if (viewValue is null)
            {
                throw new InvalidOperationException("实体缺少 view");
            }
            string view = viewValue;
            if (!view.StartsWith("temp/toolkit/views/", StringComparison.Ordinal))
            {
                return false;
            }

            return Regex.IsMatch(
                view,
                @"^temp/toolkit/views/.+\.ts(?::\d+-\d+)?$",
                RegexOptions.None,
                TimeSpan.FromMilliseconds(100));
        });
    }

    [Fact]
    public async Task ReadEntities_FullMode_On_Device_Result_Should_Not_Fail_When_NoGuid()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        string deviceRelativePath = _fixture.Services.Model.ListDevices().First().device目录_完整相对路径;
        string escapedPattern = "^" + Regex.Escape(deviceRelativePath) + "$";

        JsonElement response = await InvokeReadEntitiesAsync(_fixture.Registry, new
        {
            kinds = new[] { "device" },
            name_pattern = escapedPattern,
            detail = "full",
            limit = 1
        });

        JsonElement errors = GetPropertyIgnoreCase(response, "errors");
        Assert.Equal(JsonValueKind.Array, errors.ValueKind);
        Assert.Empty(errors.EnumerateArray());
        JsonElement data = GetPropertyIgnoreCase(response, "data");
        JsonElement entities = GetPropertyIgnoreCase(data, "entities");
        Assert.Equal(JsonValueKind.Array, entities.ValueKind);
    }
}
