using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using Toolkit.Core;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;
using Toolkit.Core.TsView;

namespace Toolkit.Mcp;

/// <summary>
/// MCP 工具注册表
/// 管理所有工具定义和执行
/// </summary>
public sealed partial class McpToolRegistry : IDisposable
{
    private readonly Dictionary<string, ToolEntry> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _handlers = new(StringComparer.Ordinal);
    private readonly ToolkitServices _services;
    private readonly string _projectRootFullPath;
    private readonly McpViewWorkspace? _viewWorkspace;
    private readonly ToolkitConfig? _toolkitConfig;

    private McpViewWorkspace ViewWorkspace => _viewWorkspace ?? throw new InvalidOperationException("McpViewWorkspace is required.");
    private readonly bool _registerHandlers;
    private bool _disposed;
    private static readonly SemaphoreSlim MCPWriteLock = new(1, 1);


    internal McpToolRegistry(
        ToolkitServices services,
        string projectRootFullPath,
        McpViewWorkspace? viewWorkspace,
        bool registerHandlers = true,
        ToolkitConfig? toolkitConfig = null,
        ResourceGenScheduler? resourceScheduler = null)
    {
        _services = services;
        _projectRootFullPath = projectRootFullPath;
        _viewWorkspace = viewWorkspace;
        _toolkitConfig = toolkitConfig;
        _registerHandlers = registerHandlers;
        _resourceScheduler = resourceScheduler;
        RegisterAllTools();
    }

    public IList<Tool> GetAllDefinitions()
    {
        return _tools.Values.Select(entry =>
        {
            return new Tool
            {
                Name = entry.Name,
                Description = entry.Description,
                InputSchema = JsonSerializer.SerializeToElement(entry.Schema, McpJsonOptions.Output)
            };
        }).ToList();
    }

    public Func<JsonElement?, CancellationToken, Task<object?>>? TryGetHandler(string name)
    {
        return _handlers.GetValueOrDefault(name);
    }

    public InputSchema? TryGetSchema(string name)
    {
        return _tools.GetValueOrDefault(name)?.Schema;
    }

    /// <summary>
    /// 根据 schema 验证参数
    /// </summary>
    /// <param name="schema">工具的输入 schema</param>
    /// <param name="arguments">传入的参数</param>
    /// <returns>验证错误列表，空表示验证通过</returns>
    public static List<string> ValidateArguments(InputSchema schema, JsonElement? arguments)
    {
        List<string> errors = [];

#pragma warning disable TK022 // Properties 是 JSON Schema 反序列化字段，边界 null 检查合法
        if (schema.Properties == null || arguments == null)
#pragma warning restore TK022
        {
            return errors;
        }

        JsonElement args = arguments.Value;
        if (args.ValueKind != JsonValueKind.Object)
        {
            return errors;
        }

        // 验证 required 字段
        if (schema.Required != null)
        {
            foreach (string req in schema.Required)
            {
                if (!args.TryGetProperty(req, out JsonElement prop) || prop.ValueKind == JsonValueKind.Undefined)
                {
                    errors.Add($"缺少必需参数: {req}");
                }
            }
        }

        // 验证每个属性
        foreach (KeyValuePair<string, PropertySchema> propDef in schema.Properties)
        {
            string propName = propDef.Key;
            PropertySchema propSchema = propDef.Value;

            if (!args.TryGetProperty(propName, out JsonElement propValue) || propValue.ValueKind == JsonValueKind.Undefined)
            {
                continue; // 可选字段未提供，跳过
            }

            ValidateProperty(propName, propSchema, propValue, errors);
        }

        return errors;
    }

    private static void ValidateProperty(string propName, PropertySchema schema, JsonElement value, List<string> errors)
    {
        string type = schema.Type;

        switch (type)
        {
            case "string":
                ValidateStringProperty(propName, schema, value, errors);
                break;
            case "number":
            case "integer":
                ValidateNumberProperty(propName, schema, value, errors);
                break;
            case "array":
                ValidateArrayProperty(propName, schema, value, errors);
                break;
            case "boolean":
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                    errors.Add($"参数 '{propName}' 类型错误：期望 boolean，实际 {value.ValueKind}");
                break;
            case "object":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"参数 '{propName}' 类型错误：期望 object，实际 {value.ValueKind}");
                    return;
                }

                if (schema.Required != null)
                {
                    foreach (string req in schema.Required)
                    {
                        if (!value.TryGetProperty(req, out JsonElement prop) || prop.ValueKind == JsonValueKind.Undefined)
                            errors.Add($"缺少必需参数: {propName}.{req}");
                    }
                }

#pragma warning disable TK022 // Properties 是 JSON Schema 反序列化字段，边界 null 检查合法
                if (schema.Properties != null)
#pragma warning restore TK022
                {
                    foreach (KeyValuePair<string, PropertySchema> propDef in schema.Properties)
                    {
                        string childName = propDef.Key;
                        if (value.TryGetProperty(childName, out JsonElement childValue) && childValue.ValueKind != JsonValueKind.Undefined)
                            ValidateProperty($"{propName}.{childName}", propDef.Value, childValue, errors);
                    }
                }
                break;
            default:
                errors.Add($"参数 '{propName}' 的 schema 类型无效: {type}");
                break;
        }
    }

    private static void ValidateStringProperty(string propName, PropertySchema schema, JsonElement value, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"参数 '{propName}' 类型错误：期望 string，实际 {value.ValueKind}");
            return;
        }

        if (value.GetString() is not string strValue)
            return;

        // 验证 enum
        if (schema.Enum != null && schema.Enum.Length > 0)
        {
            if (!Array.Exists(schema.Enum, e => string.Equals(e, strValue, StringComparison.Ordinal)))
            {
                errors.Add($"参数 '{propName}' 值无效：期望 [{string.Join(", ", schema.Enum)}]，实际 '{strValue}'");
                return;
            }
        }

        // 验证 minLength
        if (schema.MinLength.HasValue && strValue.Length < schema.MinLength.Value)
        {
            errors.Add($"参数 '{propName}' 长度不足：最小长度 {schema.MinLength.Value}，实际 {strValue.Length}");
            return;
        }

        // 验证 pattern
        if (!string.IsNullOrEmpty(schema.Pattern))
        {
#pragma warning disable TK025 // MCP schema 的 pattern 是单字段输入约束，不参与语法解析
            if (!Regex.IsMatch(strValue, schema.Pattern))
#pragma warning restore TK025
            {
                errors.Add($"参数 '{propName}' 格式无效：不符合 pattern '{schema.Pattern}'，实际值 '{strValue}'");
            }
        }
    }

    private static void ValidateNumberProperty(string propName, PropertySchema schema, JsonElement value, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"参数 '{propName}' 类型错误：期望 number，实际 {value.ValueKind}");
            return;
        }

        // 验证 minimum
        if (schema.Minimum.HasValue)
        {
            int numValue = value.GetInt32();
            if (numValue < schema.Minimum.Value)
            {
                errors.Add($"参数 '{propName}' 值过小：最小值 {schema.Minimum.Value}，实际 {numValue}");
            }
        }
    }

    private static void ValidateArrayProperty(string propName, PropertySchema schema, JsonElement value, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"参数 '{propName}' 类型错误：期望 array，实际 {value.ValueKind}");
            return;
        }

        // 验证 minItems
        if (schema.MinItems.HasValue)
        {
            int arrayLength = value.GetArrayLength();
            if (arrayLength < schema.MinItems.Value)
            {
                errors.Add($"参数 '{propName}' 元素不足：最少 {schema.MinItems.Value} 个，实际 {arrayLength}");
            }
        }

        // 验证数组元素（如果有 items 定义）
        if (schema.Items != null)
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateProperty($"{propName}[{index}]", schema.Items, item, errors);
                index++;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewWorkspace?.Dispose();
    }

    // DeviceScriptViewIndex + helpers moved to McpToolRegistry.CallGraph.cs

    // DeviceScriptViewIndex + helpers moved to McpToolRegistry.CallGraph.cs

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // Schema 辅助函数 - 与业务逻辑严格对齐
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 非空字符串属性。用于 Required 字段或业务逻辑拒绝空字符串的可选字段。
    /// 约束：minLength=1, pattern=\S（禁止全空白）
    /// 业务依据：
    ///   - Required 字段：必须提供，空字符串无意义
    ///   - 可选 GUID 字段：业务使用 GuidText.Validate 判断；格式无效的值不会作为关联处理
    ///   - SdkDocsQueryTool.cs:121,145,169 对 pattern/path 用 IsNullOrWhiteSpace 拒绝
    ///   - CanvasRefactor.cs:115,356 对 targetGuid/sourceCanvasPath 用 IsNullOrWhiteSpace
    ///   - RefactorHelper.cs:42 对 scriptGroup 用 IsNullOrWhiteSpace 判断默认目录
    /// </summary>
    private static PropertySchema NonEmptyStringProperty(string description)
    {
        return new PropertySchema
        {
            Type = "string",
            Description = description,
            MinLength = 1,
            Pattern = "\\S"
        };
    }

    /// <summary>
    /// 相对路径属性。禁止反斜杠、绝对路径、./ 和 .. 段。
    /// 在 Schema 层面拦截无效路径，避免运行时才发现错误。
    /// </summary>
    private static PropertySchema RelativePathProperty(string description)
    {
        return new PropertySchema
        {
            Type = "string",
            Description = description,
            MinLength = 1,
            // 禁止：反斜杠、以 / 或 X: 开头（绝对路径）、以 ./ 开头、包含 ..
            Pattern = "^(?!\\.?\\/)(?!.*\\.\\.)[^\\\\]+$"
        };
    }

    /// <summary>
    /// 脚本文件路径属性（严格 *.code）。禁止反斜杠。
    /// </summary>
    private static PropertySchema ScriptCodePathProperty(string description)
    {
        return new PropertySchema
        {
            Type = "string",
            Description = description,
            MinLength = 1,
            // 必须以 .code 结尾，且禁止反斜杠
            Pattern = "^[^\\\\]+\\.code$"
        };
    }

    /// <summary>
    /// 正整数属性。用于行号、数量等必须 >=1 的整数字段。
    /// 约束：minimum=1
    /// 业务依据：LintResultsTool.cs:103-114 拒绝 start/limit &lt;= 0
    /// </summary>
    private static PropertySchema PositiveIntegerProperty(string description)
    {
        return new PropertySchema
        {
            Type = "integer",
            Description = description,
            Minimum = 1
        };
    }

    /// <summary>
    /// GUID 属性。32 位小写十六进制字符串。
    /// 约束：pattern=^[0-9a-f]{32}$（锚点确保完整匹配）
    /// 业务依据：GuidText.Validate()（值校验）/ GuidText.HexPattern（JSON Schema 正则，同源）
    /// </summary>
    private static PropertySchema GuidProperty(string description)
    {
        return new PropertySchema
        {
            Type = "string",
            Description = $"{description} (pure 32-char lowercase hex; escape prefixes like @1 are forbidden)\n" +
                "deadbeefdeadbeefdeadbeefdeadbeef is a [format example] and MUST NOT be abused; using it for queries will fail 100% of the time! You MUST use a real GUID!\n" +
                "GUIDs MUST be obtained from entity query results; fabricating or using example values is forbidden!",
            Pattern = GuidText.HexPattern
        };
    }

    /// <summary>
    /// GUID 数组属性。元素为 32 位小写十六进制字符串。
    /// </summary>
    private static PropertySchema GuidArrayProperty(string description)
    {
        return new PropertySchema
        {
            Type = "array",
            Description = description + "\n" +
                "deadbeefdeadbeefdeadbeefdeadbeef is a [format example] and MUST NOT be abused; using it for queries will fail 100% of the time! You MUST use a real GUID!\n" +
                "GUIDs MUST be obtained from entity query results; fabricating or using example values is forbidden!",
            Items = new PropertySchema
            {
                Type = "string",
                Pattern = GuidText.HexPattern
            }
        };
    }

    /// <summary>
    /// 零件路径属性。必须符合 xxx.map_/数字 或 xxx.mech_/数字 格式。
    /// 倒数第二段必须以 .map_ 或 .mech_ 结尾，最后一段必须是纯数字 ID。
    /// 约束：禁止反斜杠、绝对路径、./ 开头、.. 段
    /// </summary>
    private static PropertySchema DevicePathProperty(string description)
    {
        return new PropertySchema
        {
            Type = "string",
            Description = description,
            MinLength = 1,
            // 倒数第二段以 .map_ 或 .mech_ 结尾，最后一段是纯数字
            Pattern = "^(?!\\.?\\/)(?!.*\\.\\.)[^\\\\]+\\.(?:map_|mech_)/[0-9]+$"
        };
    }

    /// <summary>
    /// 字符串数组属性。允许空数组（表示"不指定/不过滤"），元素必须非空。
    /// 约束：元素 minLength=1, pattern=\S；数组本身无 minItems（允许 []）
    /// 业务依据：
    ///   - LintResultsTool.cs:158,166,174 空数组跳过过滤（{ Length: &gt; 0 }）
    ///   - EntitySearchTool.cs:65 空数组视为没提供
    ///   - EntitySearchTool.cs:239 空数组返回 null（搜索所有类型）
    /// </summary>
    private static PropertySchema StringArrayProperty(string description)
    {
        return new PropertySchema
        {
            Type = "array",
            Description = description,
            // 注意：无 MinItems，允许空数组。空数组 = 不应用过滤 = 合法
            Items = new PropertySchema
            {
                Type = "string",
                MinLength = 1,
                Pattern = "\\S"
            }
        };
    }

    private void RegisterAllTools()
    {
        // ┌────────────────────────────────────────────────────────────────────────────────────────┐
        // │ 三层重构重命名机制规范                                                                   │
        // ├────────────────────────────────────────────────────────────────────────────────────────┤
        // │ 【层1】变量重命名 - refactor_rename_var                                                  │
        // │   • 脚本内局部变量重命名（支持批量，原子保存）                                           │
        // │   • 单文件内操作，不跨文件                                                               │
        // │   • 命名为 var 而非 identifier，避免与函数名混淆                                         │
        // │                                                                                        │
        // │ 【层2】脚本本体重命名 - refactor_rename_script                                           │
        // │   • 脚本文件本身的重命名（.code 文件的 name: 头）                                        │
        // │   • 自动处理零件内引用更新（list.json 级联）                                             │
        // │   • 自动更新所有外部 Act 调用处的脚本名引用                                              │
        // │                                                                                        │
        // │ 【层3】文件重命名 - refactor_rename_file                                                │
        // │   • 禁止目录：                                                                          │
        // │     - .git/ 目录（版本控制）                                                            │
        // │     - temp/ 目录（视图是自动生成的，移动本体则视图自动同步）                             │
        // │     - .map文件 或 .mech 文件及 .map_ 或 .mech_ 派生目录内的任何内容（会破坏项目结构）          │
        // │   • 对 .code：仅支持移动，不支持改名（改名需走层2）                                       │
        // │   • 其他有 guid 的文件：支持移动和改名                                                   │
        // │                                                                                        │
        // │ 【patch_mech_view 的重命名/删除容许】                                                        │
        // │   • patch_mech_view 只改补丁内内容，不触发额外修改，无静态引用同步刷新                       │
        // │   • 有外部静态 Act 调用时拒绝改名（需先用 refactor_rename_script 处理级联更新）          │
        // │   • 根据补丁决定是否触发删除：删除内联 code 文件，或断开 coderepo 链接                   │
        // │   • coderepo 不会自动删除，需通过 refactor_script_delete_repository 主动删除            │
        // │                                                                                        │
        // │ 【禁止】mech/map 的任何重命名操作 - 会破坏项目结构                                        │
        // └────────────────────────────────────────────────────────────────────────────────────────┘

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【层1】变量重命名
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_rename_var",
            description: "Rename local variables: batch-replace local variable names within the specified script. Operates within a single file; does not handle function names. If any rename fails, the entire operation is not saved.",
            executor: ExecuteRenameVar,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["renames"] = new()
                    {
                        Type = "array",
                        Description = "Batch rename list. Applied sequentially to the same script; if any entry fails, the entire operation is not saved.",
                        MinItems = 1,
                        Items = new()
                        {
                            Type = "object",
                            Properties = new()
                            {
                                ["old_name"] = NonEmptyStringProperty("Old variable name"),
                                ["new_name"] = NonEmptyStringProperty("New variable name")
                            },
                            Required = new[] { "old_name", "new_name" }
                        }
                    }
                },
                Required = new[] { "script_relative_path", "renames" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_rename_global_var",
            description: "Rename global variables: batch-rename global variables within the specified device and synchronize inline-script references inside that device. Does NOT cascade into coderepo scripts; running read_lint afterwards is recommended to detect broken references.",
            executor: ExecuteRenameGlobalVar,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["renames"] = new()
                    {
                        Type = "array",
                        Description = "Batch global-variable rename list. Only modifies the device vars.json and inline scripts of that device; coderepo scripts are NOT modified.",
                        MinItems = 1,
                        Items = new()
                        {
                            Type = "object",
                            Properties = new()
                            {
                                ["old_name"] = NonEmptyStringProperty("Old global variable name"),
                                ["new_name"] = NonEmptyStringProperty("New global variable name")
                            },
                            Required = new[] { "old_name", "new_name" }
                        }
                    }
                },
                Required = new[] { "device_relative_path", "renames" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【层2】脚本本体重命名
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_rename_script",
            description: "Rename a script: renames the script body (the name: header in the .code file), automatically updating references inside the device (list.json cascade) and script-name references at every external Act call site.",
            executor: ExecuteRenameScript,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["new_name"] = NonEmptyStringProperty("New script name")
                },
                Required = new[] { "script_relative_path", "new_name" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【层3】文件重命名/移动
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_rename_file",
            description: "Rename/move a file: .git/, temp/, and .map/.mech and their derived directories are FORBIDDEN; .code supports only moving (renaming requires refactor_rename_script); other files with a guid support both moving and renaming.\n\nThe following are NOT supported and will NOT happen:\n- Auto-creating directories: the target directory MUST already exist, otherwise the operation fails.\n- Overwriting a file: if a file with the same name already exists at the target, the operation fails.\n- Overwriting or merging a directory: if a directory with the same name already exists at the target, the operation fails.\n\nDuplicate-detection rule: always uses Windows case-insensitive comparison, regardless of the current platform.",
            executor: ExecuteRenameFile,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["old_relative_path"] = RelativePathProperty("File path, WorkspacePath"),
                    ["new_relative_path"] = RelativePathProperty("New path, WorkspacePath")
                },
                Required = new[] { "old_relative_path", "new_relative_path" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【代码仓库管理】脚本与代码仓库的关联操作
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_script_to_repository",
            description: "Export a device script to a code repository: exports a script inside the device as a standalone code-repository script file.",
            executor: ExecuteScriptToRepository,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["script_name"] = NonEmptyStringProperty("Script name"),
                    ["script_group"] = NonEmptyStringProperty("Script group name (scripts or event_XXX)")
                },
                Required = new[] { "device_relative_path", "script_name", "script_group" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_script_link_repository",
            description: "Link a device script to a code repository: binds the device script to an existing code-repository script; force can overwrite an existing binding.",
            executor: ExecuteScriptLinkRepository,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["script_name"] = NonEmptyStringProperty("Script name"),
                    ["script_group"] = NonEmptyStringProperty("Script group name (scripts or event_XXX)"),
                    ["target_guid"] = GuidProperty("Target code-repository script GUID"),
                    ["force"] = new() { Type = "boolean", Description = "Overwrite an existing binding", Default = false }
                },
                Required = new[] { "device_relative_path", "script_name", "script_group", "target_guid" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_script_delete_repository",
            description: "Delete a code-repository script: removes the script file and its GUID file in the code repository; if any device is still linked to this script, the deletion is refused and the link locations are listed.",
            executor: ExecuteScriptDeleteRepository,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["target_guid"] = GuidProperty("Code-repository script GUID")
                },
                Required = new[] { "target_guid" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【脚本删除】从零件脚本组中删除指定脚本
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_script_delete",
            description: "Delete a device script: removes the specified script from the device script group. For an inline script, the .code file and its list.json entry are deleted; for a linked script, only the list.json entry is detached (the CodeRepo file is NOT deleted). Reference integrity is checked by default; set force=true to skip the reference check (suitable for bulk-deletion scenarios).",
            executor: ExecuteScriptDelete,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["script_name"] = NonEmptyStringProperty("Script name"),
                    ["script_group"] = NonEmptyStringProperty("Script group name (scripts or event_XXX)"),
                    ["force"] = new() { Type = "boolean", Description = "Skip reference-integrity check (during bulk deletion, an intermediate state with incomplete references is acceptable)", Default = false }
                },
                Required = new[] { "device_relative_path", "script_name", "script_group" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【大纲写入】用大纲结构重排零件脚本和全局变量
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_apply_device_outline",
            description: "Reorder device scripts and global variables via an outline. Pass an outline with the same structure returned by read_device_script_outline (reorders only); omit outline to auto-reorder everything by name ascending.",
            executor: ExecuteScriptReorder,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["outline"] = new()
                    {
                        Type = "object",
                        Description = "Outline with the same structure as the read_device_script_outline output; omit to auto-reorder.",
                        Properties = new()
                        {
                            ["script_groups"] = new()
                            {
                                Type = "array",
                                Description = "Script-group list (each group MUST include every script name in that group)",
                                Items = new()
                                {
                                    Type = "object",
                                    Properties = new()
                                    {
                                        ["group_name"] = NonEmptyStringProperty("Script group name"),
                                        ["ordered_names"] = new()
                                        {
                                            Type = "array",
                                            Description = "New order of script names in this group",
                                            MinItems = 1,
                                            Items = new() { Type = "string" }
                                        }
                                    },
                                    Required = new[] { "group_name", "ordered_names" }
                                }
                            },
                            ["global_variables"] = new()
                            {
                                Type = "array",
                                Description = "New order of global-variable names (MUST include every global-variable name)",
                                MinItems = 1,
                                Items = new() { Type = "object", Properties = new() { ["name"] = new() { Type = "string" } }, Required = new[] { "name" } }
                            }
                        }
                    }
                },
                Required = new[] { "device_relative_path" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【Canvas 管理】内联/独立 Canvas 的转换操作
        // 注意：line 参数是原始 BeScript 代码文件（*.code）的行号，不是 TS 视图（temp/toolkit/views/*.ts）的行号
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_canvas_extract",
            description: "Extract an inline Canvas: extracts an inline Canvas from a script into a standalone .canvas file. Note: line is the line number in the raw .code file (NOT a TS view line number); on error, the list of valid create.canvas line numbers is returned.",
            executor: ExecuteCanvasExtract,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["line"] = PositiveIntegerProperty("Line of the inline Canvas (line number in the raw .code file, 1-based; NOT a TS view line number)")
                },
                Required = new[] { "script_relative_path", "line" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_canvas_import_by_path",
            description: "Link to an existing Canvas by path: replaces an inline Canvas in a script with a reference to the standalone Canvas at the specified path. Note: line is the line number in the raw .code file (NOT a TS view line number).",
            executor: ExecuteCanvasImportByPath,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["line"] = PositiveIntegerProperty("Line of the create.canvas call (line number in the raw .code file, 1-based; NOT a TS view line number)"),
                    ["source_canvas_relative_path"] = RelativePathProperty("Target Canvas path, WorkspacePath")
                },
                Required = new[] { "script_relative_path", "line", "source_canvas_relative_path" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_canvas_import_by_guid",
            description: "Link to an existing Canvas by GUID: replaces an inline Canvas in a script with a reference to the standalone Canvas with the specified GUID, or replaces an existing GUID reference with another GUID. Note: line is the line number in the raw .code file (NOT a TS view line number).",
            executor: ExecuteCanvasImportByGuid,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["line"] = PositiveIntegerProperty("Line of the create.canvas call (line number in the raw .code file, 1-based; NOT a TS view line number)"),
                    ["target_guid"] = GuidProperty("GUID of the target Canvas")
                },
                Required = new[] { "script_relative_path", "line", "target_guid" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_canvas_inline",
            description: "Inline a standalone Canvas: inlines a referenced standalone Canvas back into the script, and decides whether to delete the original file based on the references. Note: line is the line number in the raw .code file (NOT a TS view line number); on error, the list of valid create.canvas line numbers is returned.",
            executor: ExecuteCanvasInline,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_relative_path"] = ScriptCodePathProperty("Script file path (*.code, WorkspacePath)"),
                    ["line"] = PositiveIntegerProperty("Line of the Canvas reference (line number in the raw .code file, 1-based; NOT a TS view line number)")
                },
                Required = new[] { "script_relative_path", "line" }
            }
        );

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // 【变量重构】将局部变量提升为全局变量 - 只改 vars.json，不改任何 .code 文件
        // ═══════════════════════════════════════════════════════════════════════════════════════
        RegisterRefactorTool(
            name: "refactor_var_to_global",
            description: "Promote a local variable to a global variable: extracts the type from the first occurrence in the specified script, creates the global variable only in vars.json, and does NOT modify any .code file. After writing, views are automatically rebuilt and fully synchronized.",
            executor: ExecuteVarToGlobal,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["script_group_path"] = new() { Type = "string", Description = "Script-group path, WorkspacePath (e.g., xxx.map_/123/scripts or xxx.map_/123/event_OnUpdate)" },
                    ["script_name"] = NonEmptyStringProperty("Script name (without extension); the type is extracted from the first occurrence in this script"),
                    ["var_name"] = NonEmptyStringProperty("Variable name"),
                    ["allow_other_usages"] = new() { Type = "boolean", Description = "Whether other scripts may also declare a local variable with the same name (the type MUST match). Default false: the operation is refused when other same-named local variables exist.", Default = false }
                },
                Required = new[] { "script_group_path", "script_name", "var_name" }
            }
        );

        RegisterRefactorTool(
            name: "refactor_vars_to_local",
            description: "Delete or downgrade global variables: removes only the vars.json entries and does NOT modify any .code file. require_no_refs=true (default): the operation is refused if any reference exists, ensuring a fully clean deletion. require_no_refs=false: forces deletion; after view rebuild, reference sites surface as errors, and the caller must convert them to local variables one by one.",
            executor: ExecuteVarsToLocal,
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)"),
                    ["var_names"] = new() { Type = "array", Description = "List of global-variable names to delete", Items = new() { Type = "string" } },
                    ["require_no_refs"] = new() { Type = "boolean", Description = "Whether references MUST be absent to proceed (default true); set to false to skip the reference check and delete directly", Default = true }
                },
                Required = new[] { "device_relative_path", "var_names" }
            }
        );

        // read_sdk_docs
        Register(
            name: "read_sdk_docs",
            description: "Read the underlying SDK documentation of the Machinist (CreatAI) platform.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["mode"] = new()
                    {
                        Type = "string",
                        Description = "Mode; only listPaths, titleRegex, contentRegex, and path are supported. help, index, search, etc. are NOT supported.",
                        Enum = new[] { "listPaths", "titleRegex", "contentRegex", "path" }
                    },
                    ["pattern"] = NonEmptyStringProperty("Regex; only used with titleRegex or contentRegex"),
                    ["path"] = NonEmptyStringProperty("Path; only used with path mode"),
                    // area 允许空字符串（表示不按区域过滤）。业务依据：SdkDocsQueryTool.cs:417-418
                    ["area"] = new() { Type = "string", Description = "Area filter; optional" },
                    ["name_kind"] = new()
                    {
                        Type = "string",
                        Description = "Name match kind: ts / bes / both, default both (searches both BeScript names and TS view escaped names; results are the union)",
                        Enum = new[] { "ts", "bes", "both" }
                    }
                },
                Required = new[] { "mode" }
            },
            handler: HandleReadSdkDocs
        );

        // read_view（视图原文 + draft 合并,D14 单一事实来源；Python 适配器后处理哈希前缀/闭包）
        Register(
            name: "read_view",
            description: "Read the source text of a *.ts or *.html view as the single source of truth. Returns plain text only (no line-number prefix, no closure assembly — those are the Python adapter's job). This is the only way for an agent to read a view's current content.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["view_path"] = RelativePathProperty("Path relative to temp/toolkit/views (e.g., abc.map_/123.ts or ui/main.html). Other starting points are forbidden: bare 'views/', 'temp/toolkit/views/', './', '..', or absolute paths. Use '/' as separator, never '\\'. Only valid TS or HTML view paths are accepted.")
                },
                Required = new[] { "view_path" }
            },
            handler: HandleReadView
        );

        // read_lint
        Register(
            name: "read_lint",
            description: "Read lint results for the Machinist (CreatAI) workspace. Includes raw-file lint and TS syntax diagnostics.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["severity"] = new() { Type = "string", Description = "Severity", Enum = new[] { "error", "warning", "info" } },
                    ["ignore_recommended"] = new() { Type = "boolean", Description = "Ignore recommended items" },
                    ["start"] = new() { Type = "integer", Description = "Start (1-based)", Minimum = 1, Default = 1 },
                    ["limit"] = new() { Type = "integer", Description = "Number of entries", Minimum = 1, Default = 100 },
                    ["filter"] = new()
                    {
                        Type = "object",
                        Description = "Filter",
                        Properties = new()
                        {
                            ["paths"] = StringArrayProperty("Paths"),
                            ["entity_guids"] = GuidArrayProperty("Entity GUIDs (note: only maps/mechs; devices have no GUID)"),
                            ["codes"] = StringArrayProperty("code")
                        }
                    }
                }
            },
            handler: HandleReadLint
        );

        // read_call_graph_method_calls - 读取方法调用关系
        Register(
            name: "read_call_graph_method_calls",
            description: "Read method-call relationships in the call graph: given a method name, returns the callers and callees of every method with that name.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["method_name"] = NonEmptyStringProperty("Method name")
                },
                Required = new[] { "method_name" }
            },
            handler: HandleReadCallGraphMethodCalls
        );

        // read_call_graph_potential_dead_methods - 读取潜在死方法
        Register(
            name: "read_call_graph_potential_dead_methods",
            description: "Read potential dead methods in the call graph (unreachable methods; unless reached via variable-string-concatenation calls, they are completely unreachable).",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_path"] = new() { Type = "string", Description = "Device path filter (e.g., xxx.map_/123); optional" },
                    ["limit"] = new() { Type = "integer", Description = "Return limit, default 50, max 200", Minimum = 1, Default = 50 }
                }
            },
            handler: HandleReadCallGraphDeadMethods
        );

        // read_call_graph_entry_points - 读取入口点
        Register(
            name: "read_call_graph_entry_points",
            description: "Read all entry points in the call graph (e.g., script events, UI buttons, etc.).",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_path"] = new() { Type = "string", Description = "Device path filter (e.g., xxx.map_/123); optional" },
                    ["limit"] = new() { Type = "integer", Description = "Return limit, default 20, max 100", Minimum = 1, Default = 20 }
                }
            },
            handler: HandleReadCallGraphEntryPoints
        );

        // read_call_graph_stats - 读取统计
        Register(
            name: "read_call_graph_stats",
            description: "Read call-graph statistics.",
            schema: new InputSchema
            {
                Properties = []
            },
            handler: HandleReadCallGraphStats
        );

        // read_entities_by_name - 按名称模式查询
        Register(
            name: "read_entities_by_name",
            description: "Query entities by name pattern.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["name_pattern"] = NonEmptyStringProperty("Name regex"),
                    ["kinds"] = new() { Type = "array", Description = "Kind filter", Items = new() { Type = "string", Enum = new[] { "map", "mech", "device", "inlineScript", "codeRepoScript", "canvas", "excel" } } },
                    ["detail"] = new() { Type = "string", Description = "Level of detail", Enum = new[] { "summary", "full" }, Default = "summary" },
                    ["limit"] = new() { Type = "integer", Description = "Result limit", Minimum = 1, Default = 50 }
                },
                Required = new[] { "name_pattern" }
            },
            handler: HandleReadEntitiesByName
        );

        // read_entities_by_guid - 按 GUID 查询
        Register(
            name: "read_entities_by_guid",
            description: "Query entities by GUID. Only objects that have a GUID are supported, e.g., mech/map/canvas. Entity types without a GUID cannot be queried here, e.g., device.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["entity_guids"] = GuidArrayProperty("GUID list"),
                    ["detail"] = new() { Type = "string", Description = "Level of detail", Enum = new[] { "summary", "full" }, Default = "summary" }
                },
                Required = new[] { "entity_guids" }
            },
            handler: HandleReadEntitiesByGuid
        );

        // read_device_script_outline - 零件脚本大纲
        Register(
            name: "read_device_script_outline",
            description: "Get the device script outline: returns each script-group's script list and order, plus the global-variable list. The format matches what refactor_apply_device_outline expects.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["device_relative_path"] = DevicePathProperty("Device directory, WorkspacePath (format: xxx.map_/number or xxx.mech_/number)")
                },
                Required = new[] { "device_relative_path" }
            },
            handler: HandleReadDeviceScriptOutline
        );

        // read_player_log (windows only)
        if (OperatingSystem.IsWindows())
        {
            Register(
                name: "read_player_log",
                description: "Read Player.log.",
                schema: new InputSchema
                {
                    Properties = new()
                    {
                        ["mode"] = new() { Type = "string", Description = "Mode", Enum = new[] { "meta", "read", "search" } },
                        ["start_line"] = PositiveIntegerProperty("Start line (1-based, inclusive; required for read)"),
                        ["end_line"] = PositiveIntegerProperty("End line (1-based, inclusive; required for read)"),
                        ["keyword"] = NonEmptyStringProperty("Search keyword (required for search; case-sensitive)")
                    },
                    Required = new[] { "mode" }
                },
                handler: HandleReadPlayerLog
            );
        }

        // patch_mech_view（单编辑）
        Register(
            name: "patch_mech_view",
            description: "Entry point for editing *.ts and *.html view code in a Machinist (CreatAI) workspace. The tool validates syntax and propagates changes back to the underlying source files. Only patch_mech_view / patch_mech_view_multiedit correctly sync view edits back to original code or canvas files; using any other tool produces inconsistent or invalid modifications. To CREATE a new .html canvas, point view_path at a non-existent .html path and pass old_string: \"\" (empty string) with new_string as the full HTML.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["view_path"] = RelativePathProperty("Path relative to temp/toolkit/views (e.g., abc.map_/123.ts or ui/main.html). Other starting points are forbidden: bare 'views/', 'temp/toolkit/views/', './', '..', or absolute paths. Use '/' as separator, never '\\'. Only valid TS or HTML view paths are accepted."),
                    ["old_string"] = new() { Type = "string", Description = "Two modes. (1) Edit mode (view_path points to an EXISTING view): the exact text to replace — non-empty, must match file content verbatim including all whitespace and indentation, must NOT include any line-number prefix produced by read tools. When replace_all is false (default), old_string MUST be unique in the file — the edit FAILS on multiple matches. To disambiguate: include more surrounding lines, OR set replace_all: true. When an exact match yields zero hits, the tool retries once with leading indentation stripped per-line as a recovery for indentation-only differences; this fallback accepts ONLY a single hit — multiple fallback matches are treated identically to no-match (your old_string is wrong, re-read the file). When the indentation fallback also fails, the tool retries a Unicode escape swap: \\uXXXX escapes in old_string are decoded to characters, and non-ASCII characters are encoded to \\uXXXX, each whole-string variant matched exactly — again only a single hit succeeds, multiple hits are treated as no-match. (2) Create mode (view_path points to a NON-EXISTENT .html canvas): old_string MUST be the empty string \"\" (literally \"\"), and new_string is the full HTML of the new canvas. Creating is supported only for .html; .ts cannot be created." },
                    ["new_string"] = new() { Type = "string", Description = "The replacement text. Must obey the single-call-uniqueness rule per line. BeType.fromBeConst(...) is the constant form and must NOT stand alone on the right-hand side of an expression. Other than BeType.fromBeConst(...) as a parameter-position constant mapping, nested calls are entirely forbidden." },
                    ["replace_all"] = new() { Type = "boolean", Description = "If true, replace every occurrence of old_string in the file (batch). If false (default), old_string must match exactly once — multiple matches cause the edit to fail. Batch mode performs exact matching only, with no indentation or Unicode-escape fallbacks; newline normalization (CRLF→LF) is still applied.", Default = false },
                    ["base_fingerprint"] = new() { Type = "string", Description = "Optional view fingerprint returned by read_view (view_fingerprint). When provided, the edit is rejected if the view has changed since that read (any external modification invalidates it) — re-read the view and retry. When omitted, no fingerprint check is performed." }
                },
                Required = new[] { "view_path", "old_string", "new_string" }
            },
            handler: async (args, cancellationToken) =>
            {
                ApplyPatchRequest request = Deserialize<ApplyPatchRequest>(args);

                if (request.OldString == request.NewString)
                {
                    return BuildPatchResponse(null, [new ToolError(null, null, null, "old_string 和 new_string 不能相同")], Array.Empty<ToolError>(), false);
                }

                // 所有调用统一串行。
                await MCPWriteLock.WaitAsync(cancellationToken);
                try
                {
                    (PatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings, bool patchMatched) = BuildPatch(
                        request.ViewPath,
                        [new ViewEdit(request.OldString, request.NewString, request.ReplaceAll)],
                        request.BaseFingerprint);
                    return BuildPatchResponse(data, errors, warnings, patchMatched);
                }
                finally
                {
                    MCPWriteLock.Release();
                }
            }
        );

        // patch_mech_view_multiedit（多编辑，原子性）
        Register(
            name: "patch_mech_view_multiedit",
            description: "Batch entry point for editing *.ts and *.html view code in a Machinist (CreatAI) workspace. Submits multiple edits in one call; all must succeed or the whole batch is rolled back. Use for cross-position changes that must stay structurally atomic (e.g., variable declaration and first assignment must land together).",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["view_path"] = RelativePathProperty("Path relative to temp/toolkit/views (e.g., abc.map_/123.ts or ui/main.html). Other starting points are forbidden: bare 'views/', 'temp/toolkit/views/', './', '..', or absolute paths. Use '/' as separator, never '\\'. Only valid TS or HTML view paths are accepted."),
                    ["edits"] = new()
                    {
                        Type = "array",
                        Description = "Ordered list of edits, applied sequentially. Each old_string must be unique in the buffer produced by the preceding edit, unless replace_all is true for that edit.",
                        MinItems = 1,
                        Items = new()
                        {
                            Type = "object",
                            Properties = new()
                            {
                                ["old_string"] = NonEmptyStringProperty("The exact text to replace. Must match the in-progress buffer verbatim (after preceding edits in this same call) including all whitespace and indentation. Must NOT include any line-number prefix produced by read tools. Each old_string MUST be unique in the current buffer — the whole batch FAILS on multiple matches. To disambiguate, include more surrounding lines in old_string. When an exact match yields zero hits, the tool retries once with leading indentation stripped per-line as a recovery for indentation-only differences; this fallback accepts ONLY a single hit — multiple fallback matches are NOT reported as ambiguity, they are treated identically to no-match (your old_string is wrong, re-read the file). When the indentation fallback also fails, the tool retries a Unicode escape swap: \\uXXXX escapes in old_string are decoded to characters, and non-ASCII characters are encoded to \\uXXXX, each whole-string variant matched exactly — again only a single hit succeeds, multiple hits are treated as no-match. Creating a new canvas is NOT supported by multiedit; use single-edit patch_mech_view with old_string: \"\" instead."),
                                ["new_string"] = new() { Type = "string", Description = "The replacement text. Must obey the single-call-uniqueness rule per line. BeType.fromBeConst(...) is the constant form and must NOT stand alone on the right-hand side of an expression. Other than BeType.fromBeConst(...) as a parameter-position constant mapping, nested calls are entirely forbidden." },
                                ["replace_all"] = new() { Type = "boolean", Default = false, Description = "If true, replace every occurrence of old_string for THIS edit only (other edits unaffected). If false (default), old_string must match exactly once in the current buffer — multiple matches fail this edit." }
                            },
                            Required = new[] { "old_string", "new_string" }
                        }
                    },
                    ["base_fingerprint"] = new() { Type = "string", Description = "Optional view fingerprint returned by read_view (view_fingerprint). When provided, the whole batch is rejected if the view has changed since that read (any external modification invalidates it) — re-read the view and retry. When omitted, no fingerprint check is performed." }
                },
                Required = new[] { "view_path", "edits" }
            },
            handler: async (args, cancellationToken) =>
            {
                ApplyPatchMultiRequest request = Deserialize<ApplyPatchMultiRequest>(args);

                if (request.Edits.Length == 0)
                {
                    return BuildPatchResponse(null, [new ToolError(null, null, null, "缺少 edits")], Array.Empty<ToolError>(), false);
                }

                List<ToolError> preWarnings = new();
                List<ViewEdit> filtered = new();
                for (int i = 0; i < request.Edits.Length; i++)
                {
                    ViewEdit edit = request.Edits[i];

                    if (edit.OldString == edit.NewString)
                    {
                        preWarnings.Add(new ToolError(null, null, null, "old_string 和 new_string 相同，已跳过此项", ToolDiagnosticSeverity.Warning));
                        continue;
                    }
                    filtered.Add(edit);
                }

                if (filtered.Count == 0)
                {
                    return BuildPatchResponse(null, [new ToolError(null, null, null, "所有编辑项中 old_string 和 new_string 均相同")], preWarnings, false);
                }

                await MCPWriteLock.WaitAsync(cancellationToken);
                try
                {
                    (PatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings, bool patchMatched) = BuildPatch(
                        request.ViewPath,
                        filtered.ToArray(),
                        request.BaseFingerprint);
                    return BuildPatchResponse(data, errors, preWarnings.Concat(warnings).ToArray(), patchMatched);
                }
                finally
                {
                    MCPWriteLock.Release();
                }
            }
        );

        // --- commit_draft ---
        Register(
            name: "commit_draft",
            description: "Commit a .draft draft file previously saved due to a validation failure. draft_path is a path relative to the view root (e.g., ui/main.html.draft is the draft generated after a failed modification of ui/main.html).",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["draft_path"] = new PropertySchema
                    {
                        Type = "string",
                        Description = "Draft file path, relative to the view root (e.g., ui/main.html.draft); the suffix MUST be .ts.draft or .html.draft",
                        MinLength = 1,
                        Pattern = @"^(?!\.?\/)(?!.*\.\.)[^\\]+\.(ts|html)\.draft$"
                    }
                },
                Required = new[] { "draft_path" }
            },
            handler: async (args, cancellationToken) =>
            {
                await MCPWriteLock.WaitAsync(cancellationToken);
                try
                {
                    if (args == null
                        || args.Value.ValueKind != JsonValueKind.Object
                        || !args.Value.TryGetProperty("draft_path", out JsonElement draftPathElement)
                        || draftPathElement.ValueKind != JsonValueKind.String)
                    {
                        return BuildPatchResponse(null, [new ToolError(null, null, null, "缺少 draft_path")], Array.Empty<ToolError>(), false);
                    }

                    string draftPath = draftPathElement.GetString() ?? throw new InvalidOperationException("GetString returned null for String value");
#pragma warning disable TK008 // MCP 边界：用户输入验证
                    if (draftPath.Length == 0)
                    {
                        return BuildPatchResponse(null, [new ToolError(null, null, null, "缺少 draft_path")], Array.Empty<ToolError>(), false);
                    }
#pragma warning restore TK008

                    (bool ok, string draftViewRelativePath, _, string error) = TryValidateDraftPath(draftPath);
                    if (!ok)
                    {
                        return BuildPatchResponse(null, [new ToolError(null, null, null, error)], Array.Empty<ToolError>(), false);
                    }

                    string draftFullRelativePath = RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, draftViewRelativePath);
                    (string draftViewPath, string currentText, string draftText, IReadOnlyList<ToolError> readErrors) = ViewWorkspace.ReadViewDraft(draftFullRelativePath);
                    if (readErrors.Count > 0)
                    {
                        return BuildPatchResponse(null, readErrors, Array.Empty<ToolError>(), false);
                    }

                    (PatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings, bool matched) = BuildPatchFromPatchedText(
                        draftViewPath,
                        currentText,
                        draftText);
                    return BuildPatchResponse(data, errors, warnings, matched);
                }
                finally
                {
                    MCPWriteLock.Release();
                }
            }
        );

        // --- cleanup_drafts ---
        Register(
            name: "cleanup_drafts",
            description: "Clean up .draft files under the view root given a list of draft files. draft_path is a path relative to the view root. Usually no need to call this manually; drafts can be left as-is and will be invalidated automatically after a subsequent successful write. Use only when the user explicitly asks to delete draft files.",
            schema: new InputSchema
            {
                Properties = new()
                {
                    ["draft_paths"] = new()
                    {
                        Type = "array",
                        Description = "List of draft-file paths to clean up. Each item is a path relative to the view root (e.g., ui/main.html.draft).",
                        MinItems = 1,
                        Items = new PropertySchema
                        {
                            Type = "string",
                            Description = "Draft file path, relative to the view root (e.g., ui/main.html.draft)",
                            MinLength = 1,
                            Pattern = @"^(?!\.?\/)(?!.*\.\.)[^\\]+\.(ts|html)\.draft$"
                        }
                    }
                },
                Required = new[] { "draft_paths" }
            },
            handler: async (args, cancellationToken) =>
            {
                await MCPWriteLock.WaitAsync(cancellationToken);
                try
                {
                    if (args == null
                        || args.Value.ValueKind != JsonValueKind.Object
                        || !args.Value.TryGetProperty("draft_paths", out JsonElement draftPathsElement)
                        || draftPathsElement.ValueKind != JsonValueKind.Array)
                    {
                        return BuildResponse<object>(null, [new ToolError(null, null, null, "缺少 draft_paths")], Array.Empty<ToolError>());
                    }

                    if (draftPathsElement.GetArrayLength() == 0)
                    {
                        return BuildResponse<object>(null, [new ToolError(null, null, null, "draft_paths 不能为空")], Array.Empty<ToolError>());
                    }

                    List<string> draftFullRelativePaths = new(draftPathsElement.GetArrayLength());
                    foreach (JsonElement draftPathElement in draftPathsElement.EnumerateArray())
                    {
                        if (draftPathElement.ValueKind != JsonValueKind.String)
                        {
                            return BuildResponse<object>(null, [new ToolError(null, null, null, "draft_paths 中存在空路径")], Array.Empty<ToolError>());
                        }

                        string draftPath = draftPathElement.GetString() ?? throw new InvalidOperationException("GetString returned null for String value");
#pragma warning disable TK008 // MCP 边界：用户输入验证
                        if (draftPath.Length == 0)
                        {
                            return BuildResponse<object>(null, [new ToolError(null, null, null, "draft_paths 中存在空路径")], Array.Empty<ToolError>());
                        }
#pragma warning restore TK008

                        (bool ok, string validatedDraftPath, _, string error) = TryValidateDraftPath(draftPath);
                        if (!ok)
                        {
                            return BuildResponse<object>(null, [new ToolError(null, null, null, error)], Array.Empty<ToolError>());
                        }

                        draftFullRelativePaths.Add(RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, validatedDraftPath));
                    }

                    int removed = ViewWorkspace.CleanupViewDrafts(draftFullRelativePaths);
                    return BuildResponse(new CleanupDraftsResponse(removed), Array.Empty<ToolError>(), Array.Empty<ToolError>());
                }
                finally
                {
                    MCPWriteLock.Release();
                }
            }
        );

        // ── 资源生成域（Host 调度转接；引擎进程 + 调度器经构造注入，未注入时域整体不注册）──
        if (_resourceScheduler is { } scheduler)
        {
            RegisterResourceGenTools(scheduler);
        }
    }

    private (PatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, bool PatchMatched) BuildPatch(
        string? viewPath,
        ViewEdit[]? edits,
        string? baseFingerprint = null)
    {
        if (viewPath == null)
        {
            return FailPatch("缺少 view_path");
        }

        if (edits == null)
        {
            return FailPatch("缺少 edits");
        }

        if (ValidateViewPath(viewPath) is { } viewPathError)
            return FailPatch(viewPathError);

        if (viewPath.Equals("CodeRepo.ts", StringComparison.Ordinal))
        {
            return FailPatch("CodeRepo.ts 禁止 patch_mech_view（请使用重构）");
        }

        if (viewPath.EndsWith(".ts", StringComparison.Ordinal))
        {
            string devicePath = viewPath[..^".ts".Length];
            // TS 视图不支持新建：零件结构由用户在编辑器中创建，patch 仅负责脚本编辑。
            if (!_services.Model.EntityExists(devicePath))
            {
                return FailPatch($"TS 视图不存在: {devicePath}；patch_mech_view 不支持新建 TS 文件，请先在编辑器中创建零件后再编辑其脚本");
            }
            (string currentText, IReadOnlyList<ToolError> readErrors, IReadOnlyList<ToolError> readWarnings) = ViewWorkspace.ReadDeviceView(devicePath, forPatch: true);
            if (readErrors.Count > 0)
            {
                return (null, readErrors, readWarnings, false);
            }

            // 视图指纹校验：客户端声明 read_view 时的指纹，与当前脚本实体比对，不符 = 视图已变更
            if (baseFingerprint is not null && baseFingerprint != ComputeViewFingerprint(ViewWorkspace.GetDeviceSourceCode(devicePath)))
            {
                return FailPatch("视图已变更，请重新 read_view（base_fingerprint 不匹配）");
            }

            (string patchedText, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> patchWarnings) = ApplyStringEdits(currentText, edits);
            if (errors.Count > 0)
            {
                return (null, errors, readWarnings.Concat(patchWarnings).ToArray(), false);
            }

            if (ValidateCodeRepoDecoratorLines(currentText, patchedText, devicePath) is { } codeRepoError)
            {
                return FailValidateWithSavedDraft(
                    viewPath,
                    currentText,
                    patchedText,
                    [codeRepoError],
                    readWarnings.Concat(patchWarnings).ToArray());
            }

            (PatchData? data, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings2, bool patchMatched) = BuildPatchFromPatchedText(
                viewPath,
                currentText,
                patchedText);
            return (data, patchErrors, readWarnings.Concat(patchWarnings).Concat(patchWarnings2).ToArray(), patchMatched);
        }

        if (viewPath.EndsWith(".html", StringComparison.Ordinal))
        {
            string canvasRelativePath = viewPath[..^".html".Length] + ProjectEntityLayout.CanvasExt;
            string canvasViewPath = viewPath[..^".html".Length];

            // 修改分支：实体已存在，走 baseline diff。
            if (_services.Model.Find<CanvasEntity>(canvasRelativePath) is CanvasEntity canvasEntity)
            {
                // 视图指纹校验：客户端声明 read_view 时的指纹，与当前 canvas 二进制比对，不符 = 视图已变更
                if (baseFingerprint is not null && baseFingerprint != ComputeViewFingerprint(canvasEntity.Data))
                {
                    return FailPatch("视图已变更，请重新 read_view（base_fingerprint 不匹配）");
                }
                // HTML patch 的当前文本直接由模型层 Canvas→HTML 得到；禁止在静态校验前等待视图工作区同步或 PNG/CDP。
                string viewDirectoryRelativePath = RelativePathUtils.Combine(
                    ScriptPathHelper.ViewsPath,
                    RelativePathUtils.GetParentDirectory(canvasViewPath));
                string supportPrefix = CanvasHtmlRelativePath.BuildRelativePrefix(viewDirectoryRelativePath, ScriptPathHelper.CanvasRenderRootPath);
                string currentHtml = CanvasConverter.BinaryToHtml(canvasEntity.Data, supportPrefix, canvasRelativePath);
                (string patchedHtml, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> patchWarnings) = ApplyStringEdits(currentHtml, edits);
                if (errors.Count > 0)
                {
                    return (null, errors, patchWarnings, false);
                }

                (PatchData? data, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings2, bool patchMatched) = BuildPatchFromPatchedText(
                    viewPath,
                    currentHtml,
                    patchedHtml);
                return (data, patchErrors, patchWarnings.Concat(patchWarnings2).ToArray(), patchMatched);
            }

            // 新建模式下 multiedit 多 edit 顺序无法解释（"代替空白"在非空文件里无意义）；
            // 仅允许单 edit（multiedit 单点退化为 patch_mech_view 单编辑语义）。
            if (edits.Length != 1)
            {
                return FailPatch($"目标 Canvas 不存在: {canvasRelativePath}；新建模式仅支持单 edit（改用 patch_mech_view 单编辑，old_string 传 \"\"，new_string 为完整 HTML）");
            }
            ViewEdit createEdit = edits[0];
            if (createEdit.OldString.Length > 0)
            {
                return FailPatch($"目标 Canvas 不存在: {canvasRelativePath}；新建模式下 old_string 必须为空串 \"\"，new_string 须为完整 HTML");
            }
            string createPatchedHtml = TextLineEndings.ToLf(createEdit.NewString);

            // 父目录必须已存在（目录/零件结构由编辑器维护）。
            string parentDir = RelativePathUtils.GetParentDirectory(canvasRelativePath);
            if (_services.Model.Find<DirectoryEntity>(parentDir) is null)
            {
                return FailPatch($"Canvas 父目录不存在: {parentDir}；请先在编辑器中创建目录/零件结构后再新建 canvas");
            }

            (PatchData? createData, IReadOnlyList<ToolError> createErrors, IReadOnlyList<ToolError> createWarnings, bool createMatched) = BuildPatchFromPatchedText(
                viewPath,
                string.Empty,
                createPatchedHtml);
            return (createData, createErrors, createWarnings, createMatched);
        }

        return FailPatch($"path 后缀不正确（仅支持 .ts/.html）: {viewPath}");
    }

    private (PatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, bool PatchMatched) BuildPatchFromPatchedText(
        string viewPath,
        string currentText,
        string patchedText)
    {
        if (viewPath.EndsWith(".ts", StringComparison.Ordinal))
        {
            string devicePath = viewPath[..^".ts".Length];
            string[] currentLines = TextLines.SplitLines(currentText);
            string[] patchedLines = TextLines.SplitLines(patchedText);
            TsViewPatch[] patches = [new TsViewPatch(1, currentLines.Length, patchedLines)];
            DeviceTsView tool = _services.DeviceTsView;
            string viewFileRelativePath = RelativePathUtils.Combine(ScriptPathHelper.ViewsPath, viewPath);
            (DeviceTsViewPatchData? patchResult, IReadOnlyList<ToolError> patchErrors, IReadOnlyList<ToolError> patchWarnings) = ViewWorkspace.WaitUntilSynchronized(
                () => tool.Patch(devicePath, patches, currentText, viewFileRelativePath));
            if (patchErrors.Count > 0)
            {
                return FailValidateWithSavedDraft(viewPath, currentText, patchedText, patchErrors, patchWarnings);
            }

            if (patchResult == null)
            {
                return FailValidateWithSavedDraft(
                    viewPath,
                    currentText,
                    patchedText,
                    [new ToolError(null, null, null, $"写回 Device TS 视图失败：返回空结果: {devicePath}")],
                    patchWarnings);
            }

            HashSet<string> affectedPaths = new(patchResult.AffectedRelativePaths, StringComparer.Ordinal);

            bool applied = affectedPaths.Count > 0;
            string? cleanupNote = null;
            if (applied)
                cleanupNote = TryDetectDraft(viewPath);

            List<ToolError> warnings = new(patchWarnings);
            object? lintResults = null;
            if (applied)
                lintResults = GetPatchLintResults();
            return (new PatchData(
                    affectedPaths.ToArray(),
                    null,
                    lintResults),
                Array.Empty<ToolError>(),
                AddCleanupWarning(warnings, cleanupNote),
                true);
        }

        // Canvas HTML Patch 硬顺序：静态 HTML→Canvas 转码 → Canvas 根片段变更判断 → Layout Lint → CDP 渲染校验 → 写回持久化。
        // 未知属性、缺闭合标签、枚举值、向量格式等格式错误必须在静态转码阶段失败，绝对禁止进入 CDP。
        // HTML 视图只有 <BeCanvas>...</BeCanvas> 根片段会写回 Canvas；head 外壳变化不产生持久化内容。
        // CDP 只做格式合法后的补充渲染校验；WritePersist 只接收静态转码得到的 canvasBytes。
        // 【禁止在此流程中加入 try/catch Exception 吞异常、冗余 null 判空、完整 HTML 字节对比提前返回】
        // Validate/WritePersist 均通过 tuple 返回错误，吞异常即掩盖问题！
        // 【禁止对 currentText 做 Validate 再字节对比来判断无变化——多余的 Validate 调用纯粹浪费】
        if (viewPath.EndsWith(".html", StringComparison.Ordinal))
        {
            string canvasViewPath = viewPath[..^".html".Length];
            if (canvasViewPath.EndsWith(ProjectEntityLayout.CanvasExt, StringComparison.Ordinal))
            {
                return FailPatch($"Canvas 视图路径禁止包含 {ProjectEntityLayout.CanvasExt}: {canvasViewPath}", true);
            }

            string canvasRelativePath = canvasViewPath + ProjectEntityLayout.CanvasExt;
            CanvasHtmlView tool = _services.CanvasHtmlView;
            ColorPalette palette = _services.Logic.GetColorPalette();
            (bool validateSuccess, byte[] canvasBytes, IReadOnlyList<ToolError> validateErrors, IReadOnlyList<ToolError> validateWarnings) = tool.Validate(canvasRelativePath, patchedText);
            if (!validateSuccess)
            {
                return FailValidateWithSavedDraft(viewPath, currentText, patchedText, validateErrors, validateWarnings);
            }

            // isCreate：currentText 为空即新建模态（BuildPatch 新建分支传空串）。
            // 新建无 baseline，短路根片段对比（空 currentText 会让 ExtractCanvasRootFragment 抛异常）；
            // lint 直接以 patchedText 全量结果作为阻断条件，不计算 baseline 增量。
            // TK008：currentText.Length==0 是新建模态的约定标记，非防御性空值兜底。
#pragma warning disable TK008
            bool isCreate = currentText.Length == 0;
#pragma warning restore TK008
            if (!isCreate)
            {
                string currentCanvasRoot = CanvasHtmlViewCodec.ExtractCanvasRootFragment(currentText);
                string patchedCanvasRoot = CanvasHtmlViewCodec.ExtractCanvasRootFragment(patchedText);
                if (currentCanvasRoot == patchedCanvasRoot)
                {
                    return (new PatchData(
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null),
                        Array.Empty<ToolError>(),
                        validateWarnings,
                        true);
                }
            }

            List<string> patchedLayoutErrors = CanvasViewValidation.CollectHtmlLintErrors(patchedText, canvasRelativePath);
            List<string> newLayoutErrors;
            if (isCreate)
            {
                newLayoutErrors = patchedLayoutErrors;
            }
            else
            {
                List<string> baselineLayoutErrors = CanvasViewValidation.CollectHtmlLintErrors(currentText, canvasRelativePath);
                newLayoutErrors = CanvasViewValidation.ComputeNewErrors(baselineLayoutErrors, patchedLayoutErrors);
            }
            if (newLayoutErrors.Count > 0)
            {
                ToolError[] layoutErrors = newLayoutErrors
                    .Select(e => new ToolError(canvasViewPath, null, null, $"Canvas Layout Lint 失败: {e}"))
                    .ToArray();
                return FailValidateWithSavedDraft(
                    viewPath,
                    currentText,
                    patchedText,
                    layoutErrors,
                    validateWarnings);
            }

            IReadOnlyList<string> patchedRenderErrors = CanvasViewValidation.ValidateHtmlRender(
                patchedText,
                palette,
                _services.CanvasPreviewSettings,
                _services.Model,
                _services.Browser);
            if (patchedRenderErrors.Count > 0)
            {
                ToolError[] renderErrors = patchedRenderErrors
                    .Select(e => new ToolError(null, null, null, $"Canvas CDP 渲染校验失败: {e}"))
                    .ToArray();
                return FailValidateWithSavedDraft(
                    viewPath,
                    currentText,
                    patchedText,
                    renderErrors,
                    validateWarnings);
            }

            (bool writeSuccess, CanvasHtmlViewWriteData writeData, IReadOnlyList<ToolError> writeErrors, IReadOnlyList<ToolError> writeWarnings) = ViewWorkspace.WaitUntilSynchronized(
                () => tool.WritePersist(canvasRelativePath, canvasBytes));
            ToolError[] allWarnings = validateWarnings.Concat(writeWarnings).ToArray();
            if (!writeSuccess)
            {
                return FailValidateWithSavedDraft(viewPath, currentText, patchedText, writeErrors, allWarnings);
            }

            bool applied = writeData.Applied;
            string? cleanupNote = null;
            if (applied)
                cleanupNote = TryDetectDraft(viewPath);

            List<ToolError> warnings = new(allWarnings);
            object? lintResults = null;
            if (applied)
                lintResults = GetPatchLintResults();
            return (new PatchData(
                    writeData.AffectedPaths,
                    [canvasViewPath + ".png"],
                    lintResults,
                    writeData.CreatedGuid),
                Array.Empty<ToolError>(),
                AddCleanupWarning(warnings, cleanupNote),
                true);
        }

        return FailPatch($"path 后缀不正确（仅支持 .ts/.html）: {viewPath}");
    }

    internal static (string PatchedText, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings) ApplyStringEdits(
        string originalText,
        ViewEdit[] edits)
    {
        // 归一化原始文本,统一在归一化后的字符串上进行所有操作
        string result = TextLineEndings.ToLf(originalText);
        List<ToolError> errors = [];
        List<ToolError> warnings = [];

        for (int i = 0; i < edits.Length; i++)
        {
            ViewEdit edit = edits[i];
            string oldString = TextLineEndings.ToLf(edit.OldString);
            // old_string 为 0 长度会让下方 IndexOf 查找循环不前进（死循环）；修改模式禁止空 old_string。
            // 新建模式（实体不存在）不走 ApplyStringEdits，由 BuildPatch 新建分支处理空 old_string。
#pragma warning disable TK008
            if (oldString.Length == 0)
#pragma warning restore TK008
            {
                errors.Add(new ToolError(null, null, null, "old_string 不能为 0 长度"));
                return (string.Empty, errors, warnings);
            }
            string newString = TextLineEndings.ToLf(edit.NewString);

            // 查找所有匹配位置
            List<int> matches = [];
            int searchIndex = 0;
            while ((searchIndex = result.IndexOf(oldString, searchIndex, StringComparison.Ordinal)) >= 0)
            {
                matches.Add(searchIndex);
                searchIndex += oldString.Length;
            }

            if (matches.Count == 0)
            {
                string largePatchHint = " 大型补丁会显著降低匹配成功率，推荐拆分为多组更小的(能够通过校验的) patch 操作分别提交。但部分操作必须一次性完成比如变量的声明和首次赋值,标签的开始声明和关闭声明等等。若反复匹配失败可能是文件已经发生变化应重新读取而不是反复进行无意义的尝试。";

                // replace_all 必须精准匹配，不降级（per-edit：读当前 edit 的 ReplaceAll）
                if (!edit.ReplaceAll)
                {
                    // 降级：去掉行首缩进后匹配，换行作为结构分隔符保留
                    // 设计约束：降级是"精确匹配 0 命中"时的特例恢复（仅缩进不同的场景下给一次机会）。
                    // 降级 Count > 1 不视为"歧义"——精确匹配本就没命中，说明 old_string 已经写错。
                    // 这种情况一律归入"匹配失败"，禁止报告多个候选位置，避免误导调用方
                    // 以为"加几行上下文就能消歧"——实际应重新读取文件再构造 old_string。
                    string resultCollapsed = StripIndentation(result);
                    string oldCollapsed = StripIndentation(oldString);
                    int ci2 = 0;
                    int? singleHit = null;
                    while ((ci2 = resultCollapsed.IndexOf(oldCollapsed, ci2, StringComparison.Ordinal)) >= 0)
                    {
                        // 第二次命中即放弃降级，直接走匹配失败
                        if (singleHit.HasValue)
                        {
                            singleHit = null;
                            break;
                        }
                        singleHit = ci2;
                        ci2 += oldCollapsed.Length;
                    }

                    if (singleHit.HasValue)
                    {
                        int colStart = singleHit.Value;
                        int colEnd = colStart + oldCollapsed.Length;
                        int rawStart = MapStrippedIndexToRaw(result, colStart);
                        int rawEnd = MapStrippedIndexToRaw(result, colEnd);
                        result = result.Substring(0, rawStart) + newString + result.Substring(rawEnd);
                        continue;
                    }

                    // 第三级降级：unicode 转义交换（模仿 Claude Code "swapping \uXXXX escapes and their characters"）。
                    // 生成两个整体变体，覆盖两个方向：
                    //   A. 反转义：oldString 的 \uXXXX 解码为字符 —— 文件是真实字符、模型给了转义形式
                    //   B. 转义：oldString 的非 ASCII 字符编码为 \uXXXX —— 文件是 literal 转义文本、模型给了真实字符
                    // 唯一命中才替换；多点视为失败（与缩进降级同纪律：精确 0 命中说明调用方串已错）。
                    // 解码/编码后 oldString 与文件目标片段逐字符同构，命中索引直接可用，无需映射。
                    (bool swapSucceeded, string swappedResult) = TryUnicodeSwapMatch(result, oldString, newString);
                    if (swapSucceeded)
                    {
                        result = swappedResult;
                        continue;
                    }
                }

                errors.Add(new ToolError(null, null, null,
                    $"Edit {i + 1}: 匹配失败。{largePatchHint} 已尝试 unicode 转义交换(\\uXXXX ↔ 字符)。"));
                return (originalText, errors, warnings);
            }

            // 精确匹配多处 = 真歧义（old_string 正确但非唯一）：报告所有命中位置 + 行列号，
            // 让调用方在 old_string 上扩上下文消歧。per-edit replace_all 下每条 edit 均可
            // 设 replace_all: true 做"全部替换"旁路（patch_mech_view 与 multiedit 同权）。
            // 与降级路径（matches.Count == 0 后的去缩进匹配）严格区分：
            //   - 此处：精确命中 ≥2 → 调用方串正确 → 报位置帮助消歧
            //   - 降级：精确命中 0 → 调用方串已错 → 多点直接失败、不报位置（见上方 matches.Count==0 分支）
            if (matches.Count > 1 && !edit.ReplaceAll)
            {
                // 计算每个匹配位置的行列号，并提取上下文片段
                List<string> positions = new List<string>(matches.Count);
                List<string> snippets = new List<string>(matches.Count);
                foreach (int matchIndex in matches)
                {
                    (int line, int col) = GetLineColumn(result, matchIndex);
                    positions.Add($"({line},{col})");
                    snippets.Add($"  位置({line},{col}): \"{ExtractSnippet(result, matchIndex, 20)}\"");
                }

                string oldPreview = edit.OldString.Replace('\n', ' ');
                if (oldPreview.Length > 40) oldPreview = oldPreview.Substring(0, 40) + "...";

                // per-edit replace_all：patch_mech_view 与 multiedit 每条 edit 均可独立设 replace_all，
                // 统一提示两条出路（扩上下文消歧 / 对该 edit 设 replace_all: true）。
                string solution = "\n解决方案：1) 在 oldString 中增加唯一上下文（如前后各多取一行）使其唯一 2) 若确实要替换全部出现处，设置该 edit 的 replace_all: true。";

                errors.Add(new ToolError(
                    null,
                    null,
                    null,
                    $"Edit {i + 1}: oldString（\"{oldPreview}\"）在文件中有 {matches.Count} 处匹配，不唯一。各匹配位置:\n{string.Join("\n", snippets)}{solution}"));
                return (originalText, errors, warnings);
            }

            if (matches.Count == 1)
            {
                // 精确匹配且唯一,执行替换
                result = result.Substring(0, matches[0]) + newString + result.Substring(matches[0] + oldString.Length);
                continue;
            }

            // replace_all=true 且存在多处匹配，按位置重建
            StringBuilder sb = new System.Text.StringBuilder(result.Length + matches.Count * (newString.Length - oldString.Length));
            int lastIndex = 0;
            foreach (int matchIndex in matches)
            {
                sb.Append(result, lastIndex, matchIndex - lastIndex);
                sb.Append(newString);
                lastIndex = matchIndex + oldString.Length;
            }

            sb.Append(result, lastIndex, result.Length - lastIndex);
            result = sb.ToString();
        }

        return (result, errors, warnings);
    }

    /// <summary>
    /// 将字符索引转换为行号和列号（1-based）
    /// </summary>
    private static (int Line, int Column) GetLineColumn(string text, int index)
    {
        int line = 1;
        int column = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return (line, column);
    }

    /// <summary>
    /// 提取指定位置附近的文本片段（用于错误提示）
    /// </summary>
#pragma warning disable TK006 // 错误提示辅助方法
    private static string ExtractSnippet(string text, int index, int maxLength)
#pragma warning restore TK006
    {
        int start = Math.Max(0, index - maxLength / 2);
        int end = Math.Min(text.Length, index + maxLength / 2);
        int length = end - start;
        string snippet = TextLineEndings.ToLf(text.Substring(start, length)).Replace('\n', ' ');
        if (start > 0) snippet = "..." + snippet;
        if (end < text.Length) snippet = snippet + "...";
        return snippet;
    }

    private static ToolError? ValidateCodeRepoDecoratorLines(
        string baseText,
        string patchedText,
        string devicePath)
    {
        List<string> baseLines = ExtractCodeRepoDecoratorLines(baseText);
        List<string> patchedLines = ExtractCodeRepoDecoratorLines(patchedText);

        if (baseLines.Count != patchedLines.Count)
        {
            return new ToolError(devicePath, null, null,
                "禁止增删 @CodeRepo 装饰器行。如需调整代码仓库绑定关系，请使用 refactor_script_link_repository 或 refactor_script_delete_repository。");
        }

        for (int i = 0; i < baseLines.Count; i++)
        {
            if (!string.Equals(baseLines[i], patchedLines[i], StringComparison.Ordinal))
            {
                return new ToolError(devicePath, null, null,
                    "禁止修改 @CodeRepo 装饰器行。如需修改方法签名，请仅修改 BeScript 声明和方法签名，系统会自动处理代码仓库同步。");
            }
        }

        return null;
    }

    private static List<string> ExtractCodeRepoDecoratorLines(string text)
    {
        List<string> result = [];
        string[] lines = TextLines.SplitLines(text);
        foreach (string line in lines)
        {
#pragma warning disable TK010 // 去除缩进以匹配 @CodeRepo. 装饰行，行首空格是 BeScript 代码缩进而非数据内容
            string trimmed = line.Trim();
#pragma warning restore TK010
            if (trimmed.StartsWith("@CodeRepo.", StringComparison.Ordinal))
            {
                result.Add(trimmed);
            }
        }
        return result;
    }

    /// <summary>
    /// unicode 转义交换降级：把 oldString 的 \uXXXX 与字符形式互换后精确匹配文件（第三级回退）。
    /// 整体变体匹配（双向）：反转义变体 + 转义变体各尝试一次；唯一命中才替换，多点视为失败。
    /// 解码/编码后 oldString 与文件目标片段逐字符同构，命中索引直接可用，无需映射。
    /// </summary>
    private static (bool Success, string Swapped) TryUnicodeSwapMatch(string result, string oldString, string newString)
    {
        string decoded = DecodeUnicodeEscapes(oldString);
        string encoded = EncodeNonAsciiEscapes(oldString);
        if (decoded == oldString && encoded == oldString)
        {
            return (false, result); // 无 \uXXXX 也无非 ASCII，无转义可交换
        }
        foreach (string variant in new[] { decoded, encoded })
        {
            if (variant == oldString)
            {
                continue;
            }
            int? singleHit = null;
            int searchIndex = 0;
            while ((searchIndex = result.IndexOf(variant, searchIndex, StringComparison.Ordinal)) >= 0)
            {
                if (singleHit.HasValue)
                {
                    return (false, result); // 多点命中 → 视为失败（与缩进降级同纪律）
                }
                singleHit = searchIndex;
                searchIndex += variant.Length;
            }
            if (singleHit.HasValue)
            {
                return (true, result.Substring(0, singleHit.Value)
                    + newString
                    + result.Substring(singleHit.Value + variant.Length));
            }
        }
        return (false, result);
    }

    /// <summary>
    /// 把所有 \uXXXX 转义解码为对应字符；连续"高代理+低代理"合并为 BMP 外字符。
    /// 非法转义保留原样；孤立代理按原码点保留。仅处理 \u + 4 位十六进制。
    /// </summary>
    private static string DecodeUnicodeEscapes(string s)
    {
        if (s.IndexOf("\\u", StringComparison.Ordinal) < 0)
        {
            return s;
        }
        StringBuilder sb = new(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (i + 5 < s.Length && s[i] == '\\' && s[i + 1] == 'u'
                && ParseHex4(s, i + 2) is int code)
            {
                if (code is >= 0xD800 and <= 0xDBFF
                    && i + 11 < s.Length && s[i + 6] == '\\' && s[i + 7] == 'u'
                    && ParseHex4(s, i + 8) is int low && low is >= 0xDC00 and <= 0xDFFF)
                {
                    sb.Append(char.ConvertFromUtf32(0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00)));
                    i += 12;
                    continue;
                }
                sb.Append((char)code);
                i += 6;
                continue;
            }
            sb.Append(s[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 把所有非 ASCII 字符编码为 \uXXXX；高代理+低代理 pair 合并为 codepoint 后双转义。
    /// 仅处理 \u + 4 位十六进制（与 JSON 转义语义一致）。
    /// </summary>
    private static string EncodeNonAsciiEscapes(string s)
    {
        StringBuilder sb = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c <= 0x7F)
            {
                sb.Append(c);
                continue;
            }
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                int code = char.ConvertToUtf32(c, s[i + 1]);
                int high = ((code - 0x10000) >> 10) + 0xD800;
                int low = ((code - 0x10000) & 0x3FF) + 0xDC00;
                sb.Append("\\u").Append(high.ToString("X4")).Append("\\u").Append(low.ToString("X4"));
                i++;
                continue;
            }
            sb.Append("\\u").Append(((int)c).ToString("X4"));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 解析从 start 开始的 4 位十六进制为码点；非法返回 null。
    /// </summary>
    private static int? ParseHex4(string s, int start)
    {
        if (start + 4 > s.Length)
        {
            return null;
        }
        int code = 0;
        for (int j = 0; j < 4; j++)
        {
            char c = s[start + j];
            int v;
            if (c is >= '0' and <= '9') v = c - '0';
            else if (c is >= 'a' and <= 'f') v = c - 'a' + 10;
            else if (c is >= 'A' and <= 'F') v = c - 'A' + 10;
            else return null;
            code = (code << 4) | v;
        }
        return code;
    }

    /// <summary>
    /// 丢弃每行行首的缩进，保留换行和行内空格。用于降级匹配。
    /// 降级规则：只丢弃缩进，换行是结构分隔符必须保留。
    /// 注意：输入已先按统一换行规范转为 LF。
    /// </summary>
    private static string StripIndentation(string s)
    {
        System.Text.StringBuilder sb = new(s.Length);
        int i = 0;
        int len = s.Length;

        while (i < len)
        {
            // 跳过行首空白（缩进）
            while (i < len && (s[i] == ' ' || s[i] == '\t')) i++;

            // 复制非换行字符（保留行内空格）
            while (i < len && s[i] != '\n')
            {
                sb.Append(s[i]);
                i++;
            }

            // 保留换行符
            if (i < len && s[i] == '\n') { sb.Append(s[i]); i++; }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将 StripIndentation 后的索引映射回原始字符串的索引。
    /// 注意：输入已先按统一换行规范转为 LF。
    /// </summary>
    private static int MapStrippedIndexToRaw(string raw, int strippedIndex)
    {
        int si = 0;  // stripped 索引
        int ri = 0;  // raw 索引
        int len = raw.Length;

        while (ri < len && si < strippedIndex)
        {
            // 跳过行首空白
            while (ri < len && (raw[ri] == ' ' || raw[ri] == '\t')) ri++;

            // 非换行字符：stripped 和 raw 同步前进
            while (ri < len && raw[ri] != '\n' && si < strippedIndex)
            {
                si++;
                ri++;
            }

            // 换行符：两边都计入
            if (ri < len && raw[ri] == '\n' && si < strippedIndex) { si++; ri++; }
        }

        return ri;
    }

    private object? GetPatchLintResults()
    {
        LintResultsRequest request = new(null, null, null);
        (TsViewLintResultsData? data, _, _) = _services.LintTsviewResults.Query(request);
        object? tsDiagnostics = GetTscDiagnostics(timeoutMs: 10000, null, null, null, null);
        if (data == null)
            return tsDiagnostics;
        return new LintMergedData(data.Items, data.Truncated, tsDiagnostics);
    }

    /// <summary>
    /// 调用 tsc 获取 TS 视图的类型检查诊断
    /// </summary>
    internal object? GetTscDiagnostics(int timeoutMs)
    {
        return GetTscDiagnostics(timeoutMs, start: null, limit: null, severity: null, filter: null);
    }

    internal object? GetTscDiagnostics(int timeoutMs, int? start, int? limit, string? severity, LintResultsFilter? filter)
    {
        string tsconfigRelativePath = ScriptPathHelper.ToolkitTsConfigPath;

        // 使用 ProjectModel 检查 tsconfig.json 是否存在
        if (!_services.Model.EntityExists(tsconfigRelativePath))
        {
            return new TscStatusPayload("skipped", "tsconfig.json 不存在");
        }

        try
        {
            // 探测用户环境全局 TypeScript(npm 是 Node 官方自带安装器,全局装 tsc 的标准命令)。
            // 此处的 npm 是给外部用户环境的通用探测/提示,与仓库内部包管理(pnpm 唯一)无关,禁止篡改或当残留清理。
            ProcessStartInfo npmRootProcess = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "npm.cmd" : "npm",
                Arguments = "root --global",
                WorkingDirectory = _projectRootFullPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = TextEncoding.Utf8,
                StandardErrorEncoding = TextEncoding.Utf8
            };

            ProcessRunResult npmRootRun = ProcessRunner.Run(npmRootProcess, timeoutMs, CancellationToken.None);
            if (npmRootRun.Status != ProcessRunStatus.Completed || !npmRootRun.OutputDrained)
            {
                return new TscUnavailablePayload(
                    Status: "unavailable",
                    Reason: "tsc 未安装",
                    Hint: "请提醒用户：全局安装 TypeScript 可启用 TS 类型检查功能，命令：npm install -g typescript");
            }

            string globalNodeModulesFullPath = TextLineEndings.ToLf(npmRootRun.Stdout);
            if (globalNodeModulesFullPath.EndsWith('\n'))
                globalNodeModulesFullPath = globalNodeModulesFullPath[..^1];
            string typescriptModuleFullPath = Path.Combine(globalNodeModulesFullPath, "typescript", "lib", "typescript.js");
            if (!File.Exists(typescriptModuleFullPath))
            {
                return new TscUnavailablePayload(
                    Status: "unavailable",
                    Reason: "tsc 未安装",
                    Hint: "请提醒用户：全局安装 TypeScript 可启用 TS 类型检查功能，命令：npm install -g typescript");
            }

            string tsconfigFullPath = Path.Combine(
                _projectRootFullPath,
                tsconfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = _projectRootFullPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = TextEncoding.Utf8,
                StandardErrorEncoding = TextEncoding.Utf8
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("""
const path = require('node:path');
const ts = require(process.argv[1]);
const configPath = process.argv[2];
const configFile = ts.readConfigFile(configPath, ts.sys.readFile);
const diagnostics = [];
if (configFile.error) {
  diagnostics.push(configFile.error);
} else {
  const parsed = ts.parseJsonConfigFileContent(configFile.config, ts.sys, path.dirname(configPath), undefined, configPath);
  const program = ts.createProgram({ rootNames: parsed.fileNames, options: parsed.options });
  diagnostics.push(...parsed.errors, ...ts.getPreEmitDiagnostics(program));
}
const payload = diagnostics.map((diagnostic) => {
  let filePath = '';
  let line = 0;
  let column = 0;
  if (diagnostic.file && diagnostic.start !== undefined) {
    const position = diagnostic.file.getLineAndCharacterOfPosition(diagnostic.start);
    filePath = diagnostic.file.fileName;
    line = position.line + 1;
    column = position.character + 1;
  }
  return {
    Severity: ts.DiagnosticCategory[diagnostic.category].toLowerCase(),
    Code: `TS${diagnostic.code}`,
    Message: ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'),
    FilePath: filePath,
    Line: line,
    Column: column,
  };
});
process.stdout.write(JSON.stringify(payload));
""");
            psi.ArgumentList.Add(typescriptModuleFullPath);
            psi.ArgumentList.Add(tsconfigFullPath);

            ProcessRunResult run = ProcessRunner.Run(psi, timeoutMs, CancellationToken.None);
            if (run.Status == ProcessRunStatus.FailedToStart)
            {
                return new TscUnavailablePayload(
                    Status: "unavailable",
                    Reason: "tsc 未安装",
                    Hint: "请提醒用户：全局安装 TypeScript 可启用 TS 类型检查功能，命令：npm install -g typescript");
            }
            if (run.Status != ProcessRunStatus.Completed)
            {
                // TimedOut / Canceled：主进程已被 drain deadline 兜底回收，绝不卡死，降级为可读状态。
                return new TscStatusPayload("error", $"tsc 执行未正常完成（{run.Status}，超时上限 {timeoutMs}ms）");
            }
            if (!run.OutputDrained)
            {
                // 输出被残留持管道进程截断：禁止拿不完整输出冒充完整诊断结果（会静默漏报错误）。
                return new TscStatusPayload("error", $"tsc 输出读取被截断（有残留进程持有管道，退出码 {run.ExitCode}），诊断结果不可用");
            }

            List<RawTscDiagnosticItem> diagnostics = JsonSerializer.Deserialize<List<RawTscDiagnosticItem>>(run.Stdout)
                ?? throw new InvalidDataException("TypeScript 诊断输出不是有效 JSON");
            diagnostics = diagnostics.Select(item =>
            {
                if (!Path.IsPathRooted(item.FilePath))
                    return item;
                string relativePath = Path.GetRelativePath(_projectRootFullPath, item.FilePath);
                return item with { FilePath = WorkspacePath.ToUnixSeparator(relativePath) };
            }).ToList();
            int totalCount = diagnostics.Count;
            List<RawTscDiagnosticItem> filtered = ApplyTscFilters(diagnostics, severity, filter).ToList();
            int filteredCount = filtered.Count;

            int skipCount = Math.Max(start.GetValueOrDefault(1) - 1, 0);
            int takeCount = limit.GetValueOrDefault(100);

            List<RawTscDiagnosticItem> sliced = filtered
                .Skip(skipCount)
                .Take(takeCount)
                .ToList();
            bool truncated = skipCount + sliced.Count < filteredCount;
            List<TscDiagnosticItem> outputItems = sliced
                .Select(item => new TscDiagnosticItem(
                    $"{item.FilePath}({item.Line},{item.Column})",
                    $"[{item.Severity}] {item.Code} {item.Message}"))
                .ToList();

            return new TscOkPayload(
                Truncated: truncated,
                Items: outputItems);
        }
        catch (Exception ex)
        {
            string errorText = $"{ex.GetType().Name}: {ex.Message}";
            if (ex is AggregateException aggEx && aggEx.InnerExceptions.Count > 0)
                errorText = $"{ex.GetType().Name}: {string.Join("; ", aggEx.InnerExceptions.Select(e => $"[{e.GetType().Name}] {e.Message}"))}";
            return new TscStatusPayload("error", $"执行 tsc 失败: {errorText}");
        }
    }

    internal sealed record TscDiagnosticItem(
        string Location,
        string Message);

    private sealed record RawTscDiagnosticItem(
        string Severity,
        string Code,
        string Message,
        string FilePath,
        int Line,
        int Column);

    private static IEnumerable<RawTscDiagnosticItem> ApplyTscFilters(
        IEnumerable<RawTscDiagnosticItem> diagnostics,
        string? severity,
        LintResultsFilter? filter)
    {
        IEnumerable<RawTscDiagnosticItem> result = diagnostics;

        if (severity != null)
        {
            result = result.Where(item => item.Severity == severity);
        }

        if (filter == null)
        {
            return result;
        }

        // TS 诊断不支持 EntityGuids 过滤：仅有视图路径，没有可用的 GUID 映射
        if (filter.Paths is { Length: > 0 })
        {
            HashSet<string> pathSet = new HashSet<string>(
                filter.Paths.Select(WorkspacePath.ToUnixSeparator),
                StringComparer.Ordinal);
            result = result.Where(item =>
                pathSet.Any(p => item.FilePath.Contains(p, StringComparison.Ordinal)));
        }

        if (filter.Codes is { Length: > 0 })
        {
            HashSet<string> codeSet = new HashSet<string>(filter.Codes, StringComparer.Ordinal);
            result = result.Where(item => codeSet.Contains(item.Code));
        }

        return result;
    }

#pragma warning disable TK006 // 工具注册统一入口，所有公开 Register 方法的最终归口
    private void Register(
        string name,
        string description,
        InputSchema schema,
        Func<JsonElement?, CancellationToken, Task<object?>> handler)
    {
        _tools[name] = new ToolEntry(name, description, schema);
        if (_registerHandlers)
        {
            _handlers[name] = handler;
        }
    }
#pragma warning restore TK006

    private sealed record ToolEntry(string Name, string Description, InputSchema Schema);

    private static T Deserialize<T>(JsonElement? element)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Null || element.Value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Cannot deserialize null element");

        return JsonSerializer.Deserialize<T>(element.Value.GetRawText(), McpJsonOptions.Standard)
            ?? throw new JsonException($"Deserialization of {typeof(T).Name} returned null");
    }

    private static string? GetStringProperty(JsonElement? element, string propertyName)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (element.Value.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();

        return null;
    }

    private static int? GetIntProperty(JsonElement? element, string propertyName)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (element.Value.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();

        return null;
    }

#pragma warning disable TK006 // 与 GetIntProperty/GetStringProperty 对称
    private static bool? GetBoolProperty(JsonElement? element, string propertyName)
#pragma warning restore TK006
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (element.Value.TryGetProperty(propertyName, out JsonElement prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            return prop.GetBoolean();

        return null;
    }

    private static string[]? GetStringArrayProperty(JsonElement? element, string propertyName)
    {
        if (element == null || element.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.Value.TryGetProperty(propertyName, out JsonElement prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? throw new InvalidOperationException("GetString returned null for String value"))
            .ToArray();
    }

    private static string[]? ExtractStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? throw new InvalidOperationException("GetString returned null for String value"))
            .ToArray();
    }

    private static object BuildResponse<T>(T? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings)
    {
        return new PatchToolResponse(errors, warnings, Array.Empty<ToolError>(), data, false);
    }

    /// <summary>
    /// read_view handler:读视图原文(TS/HTML,D14 单一事实来源)+ R13 闭包。
    /// R13 闭包:.ts 附 device 内联脚本原始 code 全文(TS→code 1:1);.html 无闭包(本身即预览)。
    /// </summary>
    private Task<object?> HandleReadView(JsonElement? args, CancellationToken _)
    {
        string? viewPath = GetStringProperty(args, "view_path");
        if (viewPath == null)
        {
            ToolError err = new(null, null, null, "必须提供 view_path");
            return Task.FromResult<object?>(BuildResponse<object>(null, [err], []));
        }

        if (viewPath.EndsWith(".ts", StringComparison.Ordinal))
        {
            string devicePath = viewPath[..^".ts".Length];
            (string text, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = ViewWorkspace.ReadDeviceView(devicePath, forPatch: false);
            if (errors.Count > 0)
            {
                return Task.FromResult<object?>(BuildResponse<object>(null, errors, []));
            }
            IReadOnlyList<(string ScriptName, string? EventName, string SourceText)> sourceCode = ViewWorkspace.GetDeviceSourceCode(devicePath);
            return BuildReadViewResult(viewPath, text, ComputeViewFingerprint(sourceCode), sourceCode);
        }
        if (viewPath.EndsWith(".html", StringComparison.Ordinal))
        {
            // _canvasViews 的 key 是含 .canvas 的实体路径（HookPatchService.cs:236、McpViewWorkspace.cs:1614 先例）
            string canvasPath = viewPath[..^".html".Length] + ".canvas";
            (string html, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> _) = ViewWorkspace.ReadCanvasHtmlView(canvasPath, forPatch: false);
            if (errors.Count > 0)
            {
                return Task.FromResult<object?>(BuildResponse<object>(null, errors, []));
            }
            // HTML 指纹基准 = canvas 二进制哈希（read 缓存文本与 patch 实时文本可能不一致，二进制是稳定基准）
            CanvasEntity? canvasEntity = _services.Model.Find<CanvasEntity>(canvasPath);
            if (canvasEntity is null)
            {
                ToolError missingErr = new(null, null, null, $"Canvas 实体不存在: {canvasPath}");
                return Task.FromResult<object?>(BuildResponse<object>(null, [missingErr], []));
            }
            return BuildReadViewResult(viewPath, html, ComputeViewFingerprint(canvasEntity.Data), null);
        }

        string suffixMsg = $"path 后缀不正确（仅支持 .ts/.html）: {viewPath}";
        ToolError suffixErr = new(null, null, null, suffixMsg);
        return Task.FromResult<object?>(BuildResponse<object>(null, [suffixErr], []));
    }

    private Task<object?> BuildReadViewResult(
        string viewPath, string mainText, string viewFingerprint,
        IReadOnlyList<(string ScriptName, string? EventName, string SourceText)>? sourceCode = null)
    {
        // R13 闭包(TS→code 1:1):.ts 附 device 内联脚本原始 code 全文;
        // .html 无闭包(HTML 视图本身即预览,Canvas_View_Spec:7)。
        if (sourceCode is null || sourceCode.Count == 0)
        {
            return Task.FromResult<object?>(BuildResponse<object>(
                new { view_path = viewPath, text = mainText, view_fingerprint = viewFingerprint }, [], []));
        }
        return Task.FromResult<object?>(BuildResponse<object>(
            new { view_path = viewPath, text = mainText, view_fingerprint = viewFingerprint, closures = BuildScriptClosures(sourceCode) },
            [], []));
    }

    /// <summary>
    /// TS 视图指纹：device 全部脚本规范化拼接的 SHA-256（read 与 patch 同口径的实体级基准）。
    /// 基于脚本实体而非视图文本——内部 Agent 读精简视图、外部读完整视图，文本形态不同但实体同源；
    /// draft/视图重建均不参与，只有脚本内容变更才改变指纹。
    /// </summary>
    private static string ComputeViewFingerprint(IReadOnlyList<(string ScriptName, string? EventName, string SourceText)> scripts)
    {
        StringBuilder sb = new();
        foreach ((string scriptName, string? eventName, string sourceText) in scripts)
        {
            // Append(string?) 对 null 追加空串,与 eventName 为空归一一致
            sb.Append(scriptName).Append('\u0000').Append(eventName).Append('\u0000').Append(sourceText).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(TextEncoding.Utf8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// HTML 视图指纹：canvas 二进制数据的 SHA-256（read 与 patch 同从模型实体取 Data 计算）。
    /// </summary>
    private static string ComputeViewFingerprint(byte[] canvasData)
    {
        return Convert.ToHexString(SHA256.HashData(canvasData));
    }

    private static object[] BuildScriptClosures(IReadOnlyList<(string ScriptName, string? EventName, string SourceText)> sourceCode)
    {
        object[] result = new object[sourceCode.Count];
        for (int i = 0; i < sourceCode.Count; i++)
        {
            (string name, string? evt, string code) = sourceCode[i];
            result[i] = new
            {
                script_name = name,
                event_name = evt,
                code = code,
            };
        }
        return result;
    }

    private static PatchToolResponse BuildPatchResponse(PatchData? data, IReadOnlyList<ToolError> errors, IReadOnlyList<ToolError> warnings, bool patchMatched)
    {
        IReadOnlyList<ToolError> infos = Array.Empty<ToolError>();
        if (errors.Count > 0)
        {
            List<ToolError> merged = [new ToolError(null, null, null, "本次校验未通过，修改完全不生效", ToolDiagnosticSeverity.Info), .. errors];
            errors = merged;
        }
        else if (patchMatched)
        {
            infos = [new ToolError(null, null, null, "修改成功!", ToolDiagnosticSeverity.Info)];
        }
        return new(errors, warnings, infos, data, patchMatched);
    }

    private static (PatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, bool PatchMatched) FailPatch(string message, bool patchMatched = false)
    {
        return (null, [new ToolError(null, null, null, message)], Array.Empty<ToolError>(), patchMatched);
    }

    private (PatchData? Data, IReadOnlyList<ToolError> Errors, IReadOnlyList<ToolError> Warnings, bool PatchMatched) FailValidateWithSavedDraft(
        string viewPath,
        string currentText,
        string draftText,
        IReadOnlyList<ToolError> errors,
        IReadOnlyList<ToolError> warnings)
    {
        string draftPath = ViewWorkspace.WriteViewDraft(viewPath, currentText, draftText);
        string viewsPrefix = ScriptPathHelper.ViewsPath + "/";
        string displayDraftPath = draftPath;
        if (draftPath.StartsWith(viewsPrefix, StringComparison.Ordinal))
            displayDraftPath = draftPath[viewsPrefix.Length..];
        string draftHint = $"本次失败的修改已经被保存为草稿（{displayDraftPath}），可再次使用原生工具（如 Edit/apply_patch）编辑草稿，然后通过 commit_draft 尝试重新提交(对于大型修改推荐编辑草稿二次提交避免反复失败)；若放弃本次修改可直接忽略该草稿，草稿不会干扰 patch_mech_view / patch_mech_view_multiedit 的正常使用；若需主动清理可使用 cleanup_drafts";
        ToolError[] resultErrors = [new ToolError(null, null, null, "校验未通过")];
        if (errors.Count > 0)
            resultErrors = errors.ToArray();
        List<ToolError> resultWarnings = new(warnings)
        {
            new ToolError(null, null, null, draftHint, ToolDiagnosticSeverity.Info)
        };
        return (null, resultErrors, resultWarnings, true);
    }

    /// <summary>
    /// 检查是否存在待提交的草稿文件。
    /// 不执行删除——视图刷新会因基线变化自动丢弃过期的草稿。
    /// 仅返回视图根相对路径用于提示。
    /// </summary>
    private static string? TryDetectDraft(string viewPath)
    {
        string draftPath = DraftStore.DraftPathFor(ScriptPathHelper.ViewsPath, viewPath);
        if (DraftStore.Exists(draftPath))
            return viewPath + ".draft";
        return null;
    }

    private static IReadOnlyList<ToolError> AddCleanupWarning(List<ToolError> warnings, string? cleanupNote)
    {
        if (cleanupNote != null)
        {
            warnings.Add(new ToolError(null, null, null, $"原草稿文件 {cleanupNote} 已因本次成功写入而自动失效", ToolDiagnosticSeverity.Info));
        }
        return warnings;
    }
}
