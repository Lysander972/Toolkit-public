using System.Text.Json;
using Toolkit.Core.Infra;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 工具注册表 — 资源生成域（Host 调度转接，D14 例外撤销）。
///
/// 工具 schema 权威回到 C# Host；执行经 ResourceGenScheduler → ComfyUIEngineProcessManager
/// → creatai-assets 引擎进程（原子执行，无状态）。调度（task_id/异步/跨操作）唯一在 Host。
/// </summary>
public sealed partial class McpToolRegistry
{
    private readonly ResourceGenScheduler? _resourceScheduler;

    /// <summary>资源生成域是否可用（引擎 python 路径注入且配置启用时）。</summary>
    internal bool ResourceGenEnabled => _resourceScheduler != null;

    private void RegisterResourceGenTools(ResourceGenScheduler scheduler)
    {
        RegisterGenerateTool("generate_ui_tile",
            "Generate a UI tile texture (nine_patch=false: uniform-scale square tile; nine_patch=true: nine-patch draft with candidate slice + stretch previews, NOT imported until confirmed). " +
            "wait=true blocks until ready and returns the candidate; wait=false (default) returns a task_id for resource_task_status/resource_task_result polling. Prompts MUST be in English.",
            new InputSchema
            {
                Properties = new()
                {
                    ["prompt"] = NonEmptyStringProperty("UI tile 视觉描述(英文)"),
                    ["width"] = new() { Type = "integer", Minimum = 1, Description = "生成宽度" },
                    ["height"] = new() { Type = "integer", Minimum = 1, Description = "生成高度;nine_patch=false 时须等于 width" },
                    ["nine_patch"] = new() { Type = "boolean", Default = false, Description = "true=九宫格候选(审查后经 confirm_ui_tile 确认);false=等比例贴图" },
                    ["seed"] = new() { Type = "integer", Default = 0, Description = "可选随机种子" },
                    ["wait"] = WaitProperty(),
                },
                Required = new[] { "prompt", "width", "height" }
            },
            scheduler);

        RegisterGenerateTool("generate_image",
            "Generate an image candidate at the requested size (deterministically validated: decodable + exact size). Never imports. " +
            "wait=true blocks until ready; wait=false (default) returns a task_id. Prompts MUST be in English.",
            new InputSchema
            {
                Properties = new()
                {
                    ["prompt"] = NonEmptyStringProperty("图像视觉描述(英文)"),
                    ["width"] = new() { Type = "integer", Minimum = 1 },
                    ["height"] = new() { Type = "integer", Minimum = 1 },
                    ["seed"] = new() { Type = "integer", Default = 0 },
                    ["wait"] = WaitProperty(),
                },
                Required = new[] { "prompt", "width", "height" }
            },
            scheduler);

        RegisterGenerateTool("generate_video",
            "Generate a video candidate from a start-frame image (Wan I2V workflow). output_form=mp4 returns the video; frames returns the PNG sequence. " +
            "wait=true blocks until ready; wait=false (default) returns a task_id. Prompts MUST be in English.",
            new InputSchema
            {
                Properties = new()
                {
                    ["prompt"] = NonEmptyStringProperty("视频运动/内容描述(英文)"),
                    ["first_image_base64"] = NonEmptyStringProperty("起始帧 PNG base64"),
                    ["width"] = new() { Type = "integer", Minimum = 32, Default = 640 },
                    ["height"] = new() { Type = "integer", Minimum = 32, Default = 640 },
                    ["output_form"] = new() { Type = "string", Enum = new[] { "mp4", "frames" }, Default = "mp4", Description = "mp4=直接视频;frames=PNG 序列帧" },
                    ["seed"] = new() { Type = "integer", Default = 0 },
                    ["wait"] = WaitProperty(),
                },
                Required = new[] { "prompt", "first_image_base64" }
            },
            scheduler);

        RegisterGenerateTool("generate_music",
            "Generate music (ACE-Step 1.5 workflow). Returns the audio candidate; never imports. " +
            "wait=true blocks until ready; wait=false (default) returns a task_id. Prompts MUST be in English.",
            new InputSchema
            {
                Properties = new()
                {
                    ["tags"] = NonEmptyStringProperty("音乐风格/配器/BPM 描述(英文);BGM 场景 lyrics 留空"),
                    ["duration_seconds"] = new() { Type = "number", Minimum = 1, Default = 120.0 },
                    ["lyrics"] = new() { Type = "string", Default = string.Empty, Description = "歌词(可选)" },
                    ["seed"] = new() { Type = "integer", Default = 0 },
                    ["wait"] = WaitProperty(),
                },
                Required = new[] { "tags" }
            },
            scheduler);

        RegisterGenerateTool("generate_sfx",
            "Generate a short sound effect (Stable Audio 3 Small-SFX workflow). Returns the audio candidate; never imports. " +
            "wait=true blocks until ready; wait=false (default) returns a task_id. Prompts MUST be in English.",
            new InputSchema
            {
                Properties = new()
                {
                    ["prompt"] = NonEmptyStringProperty("音效描述(英文)"),
                    ["seconds"] = new() { Type = "number", Minimum = 1, Default = 8.0 },
                    ["seed"] = new() { Type = "integer", Default = 0 },
                    ["wait"] = WaitProperty(),
                },
                Required = new[] { "prompt" }
            },
            scheduler);

        RegisterTaskQueryTools(scheduler);
        RegisterTaskContextTools(scheduler);
    }

    private void RegisterGenerateTool(string name, string description, InputSchema schema, ResourceGenScheduler scheduler)
    {
        Register(name, description, schema, async (arguments, cancellationToken) =>
        {
            JsonElement args = NormalizeArguments(arguments);
            bool wait = args.TryGetProperty("wait", out JsonElement waitProperty) && waitProperty.ValueKind == JsonValueKind.True;
            JsonElement callArgs = StripWait(args);
            ResourceGenSubmitResult submit = await scheduler.SubmitAsync(name, callArgs, wait, cancellationToken).ConfigureAwait(false);
            return submit.Result is { } result
                ? new { task_id = submit.TaskId, state = submit.State, output = result.Output }
                : new { task_id = submit.TaskId, state = submit.State };
        });
    }

    private void RegisterTaskQueryTools(ResourceGenScheduler scheduler)
    {
        Register("resource_task_status",
            "Query the status of a resource generation task (pending/running/ready/failed/canceled).",
            new InputSchema
            {
                Properties = new() { ["task_id"] = NonEmptyStringProperty("任务 id（生成工具返回）") },
                Required = new[] { "task_id" }
            },
            async (arguments, _) =>
            {
                JsonElement args = NormalizeArguments(arguments);
                ResourceGenTaskStatus status = scheduler.GetStatus(RequiredString(args, "task_id"));
                return new { task_id = status.TaskId, state = status.State, error = status.Error };
            });

        Register("resource_task_result",
            "Fetch the result of a ready resource generation task (candidate output). Throws if not ready.",
            new InputSchema
            {
                Properties = new() { ["task_id"] = NonEmptyStringProperty("任务 id（生成工具返回）") },
                Required = new[] { "task_id" }
            },
            async (arguments, _) =>
            {
                JsonElement args = NormalizeArguments(arguments);
                string taskId = RequiredString(args, "task_id");
                ResourceGenTaskResult result = scheduler.GetResult(taskId);
                return new { task_id = taskId, output = result.Output };
            });
    }

    private void RegisterTaskContextTools(ResourceGenScheduler scheduler)
    {
        Register("render_ui_tile_slice",
            "Re-render nine-patch stretch previews for a confirmed slice on a task candidate (review loop). Returns previews; does not import.",
            new InputSchema
            {
                Properties = new()
                {
                    ["task_id"] = NonEmptyStringProperty("候选任务 id"),
                    ["slice"] = new() { Type = "string", Pattern = @"^\d+,\d+,\d+,\d+,\d+,\d+,\d+,\d+$", Description = "8 值 slice 内缩" },
                    ["cases"] = new()
                    {
                        Type = "array",
                        MinItems = 1,
                        Items = new()
                        {
                            Type = "array",
                            MinItems = 2,
                            Items = new() { Type = "integer", Minimum = 1 }
                        }
                    },
                },
                Required = new[] { "task_id", "slice", "cases" }
            },
            async (arguments, cancellationToken) =>
            {
                JsonElement args = NormalizeArguments(arguments);
                string taskId = RequiredString(args, "task_id");
                using JsonDocument callArgs = JsonDocument.Parse("{}");
                JsonElement result = await scheduler.RunOnTaskAsync(taskId, "render_ui_tile_slice", callArgs.RootElement, cancellationToken).ConfigureAwait(false);
                return new { task_id = taskId, output = result };
            });

        Register("review_ui_tile",
            "Run VLM review on a task candidate's stretch previews (RH GLM image-to-text node). Returns per-case verdict text; pass/fail decision is the caller's.",
            new InputSchema
            {
                Properties = new()
                {
                    ["task_id"] = NonEmptyStringProperty("候选任务 id"),
                    ["prompt"] = new() { Type = "string", Default = string.Empty, Description = "审查指令(缺省=库内默认:拉伸变形检查)" },
                },
                Required = new[] { "task_id" }
            },
            async (arguments, cancellationToken) =>
            {
                JsonElement args = NormalizeArguments(arguments);
                string taskId = RequiredString(args, "task_id");
                JsonElement result = await scheduler.RunOnTaskAsync(taskId, "review_ui_tile", args, cancellationToken).ConfigureAwait(false);
                return new { task_id = taskId, output = result };
            });
    }

    private static JsonElement NormalizeArguments(JsonElement? arguments)
    {
        return arguments is { ValueKind: JsonValueKind.Object } element
            ? element.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();
    }

    private static JsonElement StripWait(JsonElement args)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in args.EnumerateObject())
            {
                if (property.Name != "wait")
                    property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static string RequiredString(JsonElement args, string name)
    {
        if (args.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            return property.GetString() ?? throw new InvalidOperationException("GetString returned null for String value");
        throw new ArgumentException($"参数必填: {name}");
    }

    private static PropertySchema WaitProperty()
    {
        return new() { Type = "boolean", Default = false, Description = "true=阻塞到就绪返回结果;false=立即返回 task_id 异步执行" };
    }
}
