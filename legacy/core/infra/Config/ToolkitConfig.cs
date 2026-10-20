using Tomlyn;
using Tomlyn.Model;

namespace Toolkit.Core.Infra;

/// <summary>
/// 统一配置读取（toolkit.config.toml）。规范唯一权威：docs/Common_ToolkitConfig_Spec.md。
/// 文件缺失时按 §3 默认值物化全量文件（构造函数内完成，写前再查防并发双写）。
/// 读取按扁平点分路径；文件缺条目时回退默认值（默认值唯一来源 = DefaultToml，解析同一份文本，禁止第二来源）。
/// 损坏文件解析失败直接抛异常，不做自动修复。
/// </summary>
public sealed class ToolkitConfig
{
    // 规范 §3 默认值全量物化模板（同时也是缺条目回读的唯一默认值来源）
    private const string DefaultToml = """
        # Toolkit 统一配置文件
        mcp.port = 5050
        lint.root = "."
        lint.run_on_startup = true
        canvas_preview.default_width = 1080
        canvas_preview.default_height = 2160
        canvas_preview.scale = 1.0
        browser.render_worker_count = 2
        browser.executable_path = ""
        code_lens.show_doc_buttons = true
        resource_gen.comfyui.base_url = ""
        resource_gen.comfyui.api_key = ""
        resource_gen.comfyui.tripo_api_key = ""
        resource_gen.comfyui.model.text_to_image = "千问2.0-文生图"
        resource_gen.comfyui.model.image_to_image = "千问2.0-图像编辑"
        resource_gen.comfyui.model.image_to_video = "万相2.7-图生视频"
        resource_gen.comfyui.model.text_to_video = "万相2.5 Preview 文生视频"
        resource_gen.comfyui.model.text_to_music = "suno-single-v5.5"
        resource_gen.comfyui.model.image_to_3d = "hitem3d-v15/image-to-3d"
        llm.roles.planner = "deepseek/deepseek-chat"
        llm.roles.coder = "deepseek/deepseek-chat"
        llm.roles.resource = "deepseek/deepseek-chat"
        llm.keys.deepseek = ""
        llm.keys.zhipu = ""
        llm.keys.openai = ""
        llm.keys.gemini = ""
        llm.keys.local_llm = ""
        llm.extra_sources = []
        """;

    private static readonly TomlTable DefaultModel = Parse(DefaultToml);

    private readonly TomlTable _model;

    public ToolkitConfig(string configFileFullPath)
    {
        if (!SystemIOWrapper.FileExists(configFileFullPath))
        {
            SystemIOWrapper.CreateDirectory(SystemIOWrapper.GetParentDirectory(configFileFullPath));
            // 多进程并发物化须容忍已存在：写前再查
            if (!SystemIOWrapper.FileExists(configFileFullPath))
                WriteAtomic(configFileFullPath, DefaultToml);
        }
        _model = Parse(SystemIOWrapper.ReadAllText(configFileFullPath));
    }

    public int GetInt(string key) => (int)(long)Lookup(key);

    public string GetString(string key) => (string)Lookup(key);

    public bool GetBool(string key) => (bool)Lookup(key);

    /// <summary>原子写：临时文件 + 替换，防止半写状态被对端读到（规范 §5）。供写入方使用。</summary>
    public static void WriteAtomic(string fileFullPath, string content)
    {
        string tempFileFullPath = fileFullPath + ".tmp";
        SystemIOWrapper.WriteAllText(tempFileFullPath, content);
        SystemIOWrapper.MoveFile(tempFileFullPath, fileFullPath, overwrite: true);
    }

    private object Lookup(string key)
    {
        object? value = Navigate(_model, key);
        if (value != null)
            return value;
        value = Navigate(DefaultModel, key);
        if (value == null)
            throw new KeyNotFoundException($"未知配置条目: {key}");
        return value;
    }

    private static object? Navigate(TomlTable table, string key)
    {
        object? current = table;
        foreach (string segment in key.Split('.'))
        {
            if (current is not TomlTable currentTable || !currentTable.TryGetValue(segment, out object? next))
                return null;
            current = next;
        }
        return current;
    }

    private static TomlTable Parse(string content)
    {
        TomlTable? table = TomlSerializer.Deserialize<TomlTable>(content);
        return table != null ? table : new TomlTable();
    }
}
