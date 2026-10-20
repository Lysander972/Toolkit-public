using Toolkit.Core.Bescript;

namespace Toolkit.Core.Sdk;

/// <summary>
/// 事件参数定义。
/// 【设计原则】
/// 1. 本名（Name）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 本名一致即说明 SDK 与底层对齐，必须保持精准一致性。
/// 3. 别名（Alias）仅用于视图生成，不参与注册匹配。
/// 4. 本名与别名的映射关系在注册时手动指定，依赖手动管理维护。
/// 5. 若本名形如 A@B，则 A@B 整体仍是本名；B 或按语义整理出的读名只能放在 Alias。
/// 别名是显示专用的美化,集中在SDK注册处手动指定完成
/// 方法名级别必须是全程本名!无别名概念
/// 别名仅针对参数位
/// </summary>
public sealed record EventParameter(
    /// <summary>
    /// 本名：必须与底层反射名称完全一致，用于注册匹配。（含 $ 前缀）
    /// 【严禁！严禁！严禁！】直接访问此字段！
    /// 显示名称通过本记录的 EffectiveName 属性读取。
    /// 除此之外任何地方直接访问 _internal_raw_name 都是绝对禁止的破坏行为！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// 严禁！严禁！严禁！添加任何所谓"RawName"转发属性！那是最愚蠢的绕过！
    /// </summary>
    string _internal_raw_name,
    string TypeName,    // 类型名
    string? Description = null,
    MethodEnumType? EnumType = null,
    /// <summary>别名：用于视图生成时替代本名，不参与注册匹配。</summary>
    string? Alias = null
)
{
    public string DisplayTypeName => EnumType is not null ? EnumType.DisplayTypeName : TypeName;

    /// <summary>
    /// 对外读取名称唯一入口：优先取别名，无别名用本名。
    /// 【使用场景】视图生成（BescriptDtsGenerator 等），不用于注册匹配。
    /// </summary>
#pragma warning disable TK005 // 唯一名称入口：别名可选，无别名用本名（非多源，而是单层覆写）
    public string EffectiveName => Alias ?? _internal_raw_name;
#pragma warning restore TK005

    /// <summary>验证参数定义的有效性</summary>
    public bool IsValid()
    {
        // 强耦合：TypeName="Enum" 必须有 EnumType；反之亦然
        bool isEnum = string.Equals(TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal);
        return isEnum == (EnumType is not null);
    }
}

/// <summary>
/// 事件签名定义。
/// 【设计原则】
/// 1. 事件名（EventName）必须与底层反射名称完全一致，用于注册匹配。
/// 2. 事件名级别必须是全程本名！无别名概念！
/// 3. 别名仅针对：参数位。
/// </summary>
public sealed record EventSignature(
    /// <summary>事件名：必须与底层反射名称完全一致，全程本名！</summary>
    string EventName,
    /// <summary>所属零件类型名：必须与底层反射名称完全一致。</summary>
    string DeviceType,
    IReadOnlyList<EventParameter> Parameters,
    string Summary,
    string? Remarks = null,
    IReadOnlyList<int>? AllowedDeviceTypeIds = null,  // 允许使用此事件的零件类型 ID 列表（包含模式）
    IReadOnlyList<int>? ExcludedDeviceTypeIds = null,  // 排除的零件类型 ID 列表（排除模式，用于通用事件）
    string? Notes = null
)
{
    /// <summary>验证事件名是否符合命名规则</summary>
    public static bool IsValidEventName(string name)
    {
        return BeScriptNaming.Identifier.IsMatch(name);
    }

    /// <summary>验证事件签名的有效性</summary>
    public bool IsValid()
    {
        if (!IsValidEventName(EventName))
            return false;

#pragma warning disable TK008 // DeviceType 来自外部SDK注册数据，空值为非法输入，属系统边界校验
        if (DeviceType.Length == 0)
            return false;
#pragma warning restore TK008

        // 验证所有参数
        foreach (EventParameter param in Parameters)
        {
            if (!param.IsValid())
                return false;
        }

        return true;
    }

    /// <summary>验证事件签名，如果无效则抛出异常</summary>
    public void Validate()
    {
        if (!IsValidEventName(EventName))
            throw new ArgumentException($"事件名 '{EventName}' 不符合命名规则（字母或下划线开头，仅包含字母、数字、下划线）", nameof(EventName));

#pragma warning disable TK008 // DeviceType 来自外部SDK注册数据，空值为非法输入，属系统边界校验
        if (DeviceType.Length == 0)
            throw new ArgumentException("零件类型不能为空", nameof(DeviceType));
#pragma warning restore TK008

        for (int i = 0; i < Parameters.Count; i++)
        {
            EventParameter param = Parameters[i];
            if (param.EnumType is not null)
            {
                if (!string.Equals(param.TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
                    throw new ArgumentException($"事件参数 '{param.EffectiveName}' 只有在类型为 Enum 时才能声明枚举分组");
            }
        }
    }

    /// <summary>
    /// 检查指定的零件类型 ID 是否允许此事件。
    /// 双模式优先级固定：排除模式存在时优先按排除列表判定；否则按包含列表判定；都未配置则拒绝。
    /// </summary>
    public bool IsAllowedForDeviceType(int deviceTypeId)
    {
        // 排除模式：只要未命中排除列表就允许（允许空列表）
        // null 表示未配置排除列表（与空列表"配置了但无排除项"语义不同），null 检查合法
#pragma warning disable TK022
        if (ExcludedDeviceTypeIds != null)
            return !ExcludedDeviceTypeIds.Contains(deviceTypeId);

        // 包含模式：必须在允许列表中（允许空列表，表示不允许）
        if (AllowedDeviceTypeIds != null)
            return AllowedDeviceTypeIds.Contains(deviceTypeId);
#pragma warning restore TK022

        // 未配置允许/排除列表时，拒绝
        return false;
    }
}
