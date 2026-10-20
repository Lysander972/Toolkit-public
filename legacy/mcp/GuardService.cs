using System.Text.Json.Nodes;
using Toolkit.Core.Infra;

namespace Toolkit.Mcp;

/// <summary>
/// Guard 请求：平台标识 + 原始 hook stdin JSON
/// </summary>
public sealed record GuardRequest(string Platform, string Raw);

/// <summary>
/// Guard 响应：允许/拒绝 + 拒绝原因
/// </summary>
public sealed record GuardResponse(bool Allow, string Reason);

/// <summary>
/// Hook guard 统一判断服务。接收各平台 guard 脚本转发的原始 hook JSON，
/// 根据 platform 提取工具名、命令、文件路径，执行 shell 命令阻断和受保护文件路径拦截。
/// </summary>
public sealed class GuardService
{
    private static readonly Toolkit.Core.Infra.Log _log = Toolkit.Core.Infra.Log.For(LogModule.Host);

    private static readonly HashSet<string> ShellDeny = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "python3", "pip", "pip3", "sed", "awk", "perl", "ruby",
        "node", "npm", "npx", "cat", "tee", "powershell", "pwsh"
    };

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".guid", ".code", ".canvas", ".ts", ".html"
    };

    private static readonly HashSet<string> ProtectedJsonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "setting.json", "map.json", "mech.json", "vars.json", "list.json", "device.json"
    };

    /// <inheritdoc/>
    public GuardResponse Check(GuardRequest request)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(request.Raw);
        }
        catch
        {
            _log.Warning("[GUARD] JSON解析失败, platform=unknown");
            return new GuardResponse(false, "guard: invalid JSON");
        }

        if (root == null)
        {
            _log.Warning("[GUARD] JSON为空, platform=unknown");
            return new GuardResponse(false, "guard: empty JSON");
        }

        // 平台差异仅在 JSON 提取路径，检查逻辑统一
        string? toolName = null;
        string? cmd = null;
        string? fp = null;

        switch (request.Platform)
        {
            // Claude Code: tool_name → tool_input.command / tool_input.file_path
            case "claude":
                {
                    string t = GetString(root, "tool_name");
                    toolName = t;
                    if (t == "Bash") cmd = GetNestedString(root, "tool_input", "command");
                    else if (t is "Write" or "Edit" or "MultiEdit") fp = GetNestedString(root, "tool_input", "file_path");
                    break;
                }
            // Cursor: hook_event_name → root.command / root.file_path
            case "cursor":
                {
                    string t = GetString(root, "hook_event_name");
                    toolName = t;
                    if (t == "beforeShellExecution") cmd = GetStringOrNull(root, "command");
                    else if (t == "beforeReadFile") fp = GetStringOrNull(root, "file_path");
                    break;
                }
            // Factory Droid: hook_event_name=PreToolUse → tool_input.command / tool_input.file_path
            case "factory":
                {
                    string t = GetString(root, "hook_event_name");
                    toolName = t;
                    if (t == "PreToolUse")
                    {
                        string tn = GetString(root, "tool_name");
                        toolName = tn;
                        if (tn == "Execute") cmd = GetNestedString(root, "tool_input", "command");
                        else if (tn is "Edit" or "Create") fp = GetNestedString(root, "tool_input", "file_path");
                    }
                    break;
                }
            // Kimi: tool_name → tool_input.command / tool_input.path
            case "kimi":
                {
                    string t = GetString(root, "tool_name");
                    toolName = t;
                    if (t == "Shell") cmd = GetNestedString(root, "tool_input", "command");
                    else if (t is "WriteFile" or "StrReplaceFile") fp = GetNestedString(root, "tool_input", "path");
                    break;
                }
            // Cline: preToolUse.toolName → preToolUse.parameters.command / path
            case "cline":
                {
                    JsonNode? preToolUse = root["preToolUse"];
                    if (preToolUse != null)
                    {
                        string? t = GetStringOrNull(preToolUse, "toolName");
                        toolName = t;
                        if (t == "execute_command") cmd = GetNestedString(preToolUse, "parameters", "command");
                        else if (t is "write_to_file" or "replace_in_file") fp = GetNestedString(preToolUse, "parameters", "path");
                    }
                    break;
                }
            // Crush CLI: tool_name → tool_input.command / tool_input.file_path (与 Claude 格式一致)
            case "crush":
                {
                    string t = GetString(root, "tool_name");
                    toolName = t;
                    if (t == "bash") cmd = GetNestedString(root, "tool_input", "command");
                    else if (t is "write" or "edit" or "multiedit") fp = GetNestedString(root, "tool_input", "file_path");
                    break;
                }
            // Gemini CLI: tool_name → tool_input.command / tool_input.file_path
            case "gemini":
                {
                    string t = GetString(root, "tool_name");
                    toolName = t;
                    if (t == "run_shell_command") cmd = GetNestedString(root, "tool_input", "command");
                    else if (t is "write_file" or "replace") fp = GetNestedString(root, "tool_input", "file_path");
                    break;
                }
            // Codex CLI: hook_event_name → tool_name → tool_input.command
            case "codex":
                {
                    string eventName = GetString(root, "hook_event_name");
                    string t = GetString(root, "tool_name");
                    toolName = t;
                    if ((eventName == "PreToolUse" || eventName == "PermissionRequest") && t == "Bash")
                        cmd = GetNestedString(root, "tool_input", "command");
                    break;
                }
            default:
                return new GuardResponse(true, "ALLOW");
        }

        if (cmd != null)
        {
            string? deniedCmd = FindDeniedCommand(cmd);
            if (deniedCmd != null)
            {
                string cmdPreview = cmd;
                if (cmd.Length > 120)
                    cmdPreview = cmd[..120] + "...";
                _log.Warning($"[GUARD] BLOCK platform={request.Platform} tool={toolName} denied={deniedCmd} cmd=\"{cmdPreview}\"");
                return new GuardResponse(false, "用户明确反对使用外部工具直接修改文件，请使用原生编辑工具或 MCP");
            }
        }
        if (fp != null && IsProtectedFilePath(fp))
        {
            _log.Warning($"[GUARD] BLOCK platform={request.Platform} tool={toolName} path=\"{fp}\"");
            return new GuardResponse(false, "禁止直接操作受保护的工程文件，请通过 MCP 工具操作");
        }

        string toolNameText = "(untracked)";
        if (toolName != null)
            toolNameText = toolName;
        string cmdText = "no";
        if (cmd != null)
            cmdText = "yes";
        string pathText = "(none)";
        if (fp != null)
            pathText = fp;
        _log.Info($"[GUARD] PASS platform={request.Platform} tool={toolNameText} cmd={cmdText} path={pathText}");
        return new GuardResponse(true, "ALLOW");
    }

    // ── 共用判断 ─────────────────────────────────────────

    /// <summary>
    /// 检查命令字符串中是否包含被禁止的工具名（词界匹配，不用正则）
    /// </summary>
    private static string? FindDeniedCommand(string cmd)
    {
        // 统一换行符为空格，避免跨行命令漏检
        string normalized = cmd.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        foreach (string deny in ShellDeny)
        {
            int idx = 0;
            while (idx <= normalized.Length - deny.Length)
            {
                int found = normalized.IndexOf(deny, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;

                int end = found + deny.Length;
                bool beforeOk = found == 0 || !IsWordChar(normalized[found - 1]);
                bool afterOk = end >= normalized.Length || !IsWordChar(normalized[end]);
                if (beforeOk && afterOk)
                    return deny;

                idx = found + 1;
            }
        }
        return null;
    }

    internal static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// 检查文件路径是否受保护（特定 JSON 文件名或受保护扩展名）
    /// </summary>
    private static bool IsProtectedFilePath(string filePath)
    {
        string f = filePath.Replace('\\', '/');
        // 取 basename
        int lastSlash = f.LastIndexOf('/');
        string baseName = f;
        if (lastSlash >= 0)
            baseName = f[(lastSlash + 1)..];

        if (ProtectedJsonNames.Contains(baseName)) return true;

        int dotIdx = baseName.LastIndexOf('.');
        if (dotIdx >= 0)
        {
            string ext = baseName[dotIdx..];
            if (ProtectedExtensions.Contains(ext)) return true;
        }

        return false;
    }

    // ── JSON 提取辅助 ────────────────────────────────────

    private static string GetString(JsonNode node, string key)
    {
        JsonNode valueNode = node[key] ?? throw new InvalidOperationException($"Guard JSON 缺少字段: {key}");
        return valueNode.GetValue<string>();
    }

    private static string? GetStringOrNull(JsonNode node, string key)
        => node[key]?.GetValue<string>();

    private static string? GetNestedString(JsonNode node, string objectKey, string valueKey)
    {
        JsonNode? obj = node[objectKey];
        if (obj == null) return null;
        return obj[valueKey]?.GetValue<string>();
    }
}
