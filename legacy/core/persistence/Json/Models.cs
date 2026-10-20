namespace Toolkit.Core.Persistence;

// =============================================================================
// 核心设计警告 / CORE DESIGN WARNING
// =============================================================================
//
// 本文件包含 Hjson 数据模型。这些是 internal 类型，因为工作区应通过
// 实体模型操作文件内容，这些模型仅用于实体内部的 Hjson 处理。
//
// 禁止：
// 1. 将这些类型改为 public
// 2. 在其他模块直接使用这些模型处理文件内容
//
// =============================================================================

/// <summary>
/// list.json 条目（list.json 中单个条目的序列化/反序列化表示）。
///
/// 【职责边界】ListJsonItem 只是 list.json 文件中的原始数据格式，不代表脚本实体：
/// - 内联脚本：对应 .code 文件（由 InlineScriptEntity 管理）
/// - CodeRepo：对应代码仓库中的 *.code 文件（由 CodeRepoScriptEntity 独立管理）
///
/// 格式对照：
/// - 内联脚本: "脚本名" → IsCodeRepo=false
/// - CodeRepo:  {guid:"xxx", name:"脚本名", private:true} → IsCodeRepo=true, Guid="xxx"
///
/// 【重要】IsCodeRepo 仅表示 list.json 中的条目格式：
/// - false = 字符串格式（内联脚本名称）
/// - true  = 对象格式（CodeRepo 引用，含 guid 和 name）
///
/// 注意：IsCodeRepo 与 ScriptEntryInfo.IsCodeRepo 语义完全一致，统一命名避免混淆。
/// </summary>
/// <param name="Name">脚本名称</param>
/// <param name="Guid">CodeRepo GUID；仅 IsCodeRepo=true 时有业务含义</param>
/// <param name="IsCodeRepo">true=CodeRepo引用(对象格式), false=内联脚本(字符串格式)</param>
/// <param name="IsPrivate">list.json 条目的 private 标记（缺省为 false）</param>
internal sealed record ListJsonItem(string Name, string Guid, bool IsCodeRepo, bool IsPrivate = false);

/// <summary>
/// vars.json 条目（vars.json 中单个变量的序列化/反序列化表示）。
///
/// 警告：这是内部类型，工作区应通过实体模型操作文件。
/// </summary>
internal sealed record VarsJsonItem(string Name, string TypeName, string? Comment = null);
