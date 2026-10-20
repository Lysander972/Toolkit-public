namespace Toolkit.Core.Sdk;

// ╔════════════════════════════════════════════════════════════════════════════════╗
// ║                     【禁止审查区域 - 原版 SDK 精确映射】                          ║
// ╠════════════════════════════════════════════════════════════════════════════════╣
// ║  本文件包含对原版 BeScript SDK 变量方法（基础类型）的精确硬编码映射。               ║
// ║                                                                                ║
// ║  ⚠️  重要声明：                                                                 ║
// ║  • 此区域内容必须与原版 SDK 保持一条不多、一条不少的完全一致                        ║
// ║  • 无论原版设计是否符合软件工程最佳实践，此处的兼容性映射都是必须的                  ║
// ║  • 请勿对本文件内容进行任何形式的代码审查、重构建议或规范性检查                     ║
// ║  • 任何看似"不规范"的命名、参数或结构都是对原版的忠实还原                          ║
// ║                                                                                ║
// ║  本文件由 Toolkit 维护，用于为插件和核心库提供原版 SDK 兼容服务。                   ║
// ╚════════════════════════════════════════════════════════════════════════════════╝

/// <summary>
/// 变量方法 (varf) 定义 - 基础类型
/// </summary>
public static class VarFuncDefinitions
{
    private static readonly string[] ReadValueTypes =
    {
        "Float", "Long", "String", "Bool", "Vector3", "Color", "List", "Dict", "Struct",
        "Device", "Mech", "Player", "Datetime", "Bytes", "Canvas", "Icon",
        "UIRect", "UILabel", "UIButton", "UIInput", "UIRawImage", "UISpine", "UILine", "UIAvatar"
    };

    public static IEnumerable<MethodSignature> GetAll()
    {
        foreach (MethodSignature sig in GetFloatMethods()) yield return sig;
        foreach (MethodSignature sig in GetLongMethods()) yield return sig;
        foreach (MethodSignature sig in GetStringMethods()) yield return sig;
        yield return Varf("Enum", "equal", new[] { new MethodParameter(string.Empty, "Enum", EnumType: MethodEnumType.FollowVarfOwner, Alias: "value") }, "Bool", "是否相等");
        foreach (MethodSignature sig in GetListMethods()) yield return sig;
        foreach (MethodSignature sig in GetDictMethods()) yield return sig;
        foreach (MethodSignature sig in GetStructMethods()) yield return sig;
        foreach (MethodSignature sig in GetBytesMethods()) yield return sig;
        foreach (MethodSignature sig in GetVector3Methods()) yield return sig;
        foreach (MethodSignature sig in GetColorMethods()) yield return sig;
        foreach (MethodSignature sig in GetDateTimeMethods()) yield return sig;
    }

    private static MethodSignature Varf(
        string ownerType,
        string name,
        MethodParameter[] parameters,
        string? returnType = null,
        string? summary = null,
        RecommendLevel recommend = RecommendLevel.Default,
        CompressionHint? compressionHint = null,
        string? notes = null)
    {
        string? notRecommendedReason = BuiltinRecommendationRules.GetObsoleteReason(
            MethodCategory.VarFunc, ownerType, name);
        bool isObsolete = notRecommendedReason is not null;
        return new MethodSignature(
            MethodCategory.VarFunc,
            ownerType,
            name,
            parameters,
            returnType,
            summary,
            null,
            recommend,
            MethodScope.Universal,
            compressionHint,
            IsObsolete: isObsolete,
            NotRecommendedReason: notRecommendedReason,
            Notes: notes);
    }

    private static IEnumerable<MethodSignature> GetFloatMethods()
    {
        yield return Varf("Float", "set", new[] { new MethodParameter(string.Empty, "Float", Alias: "value") }, null, "设置值");
        yield return Varf("Float", "addEqual", new[] { new MethodParameter(string.Empty, "Float", Alias: "value") }, null, "自加");
        yield return Varf("Float", "minusEqual", new[] { new MethodParameter(string.Empty, "Float", Alias: "value") }, null, "自减");
        yield return Varf("Float", "abs", Array.Empty<MethodParameter>(), "Float", "绝对值");
        yield return Varf("Float", "floor", Array.Empty<MethodParameter>(), "Float", "向下取整", notes: "返回值仍是 Float；需要 Long 时再 .toLong()。");
        yield return Varf("Float", "ceiling", Array.Empty<MethodParameter>(), "Float", "向上取整", notes: "返回值仍是 Float；需要 Long 时再 .toLong()。");
        yield return Varf("Float", "round", Array.Empty<MethodParameter>(), "Float", "四舍五入", notes: "银行家舍入：.5 时取最近的偶数，如 0.5→0, 1.5→2；返回值仍是 Float，需要 Long 时再 .toLong()。");
        yield return Varf("Float", "toLong", Array.Empty<MethodParameter>(), "Long", "转为整数", notes: "截断小数部分，非四舍五入；搜索 转整数/取整数部分 时用这个。");
        yield return Varf("Float", "inside", new[] { new MethodParameter("A", "Float"), new MethodParameter("B", "Float") }, "Bool", "是否在区间内", notes: "闭区间 [A,B]，包含两端边界；等价搜索 inRange/区间。");
    }

    private static IEnumerable<MethodSignature> GetLongMethods()
    {
        yield return Varf("Long", "set", new[] { new MethodParameter(string.Empty, "Long", Alias: "value") }, null, "设置值");
        yield return Varf("Long", "addEqual", new[] { new MethodParameter(string.Empty, "Long", Alias: "value") }, null, "自加");
        yield return Varf("Long", "minusEqual", new[] { new MethodParameter(string.Empty, "Long", Alias: "value") }, null, "自减");
        yield return Varf("Long", "abs", Array.Empty<MethodParameter>(), "Long", "绝对值");
        yield return Varf("Long", "toFloat", Array.Empty<MethodParameter>(), "Float", "转为浮点数", notes: "超过约 1677 万的整数会丢失精度");
        yield return Varf("Long", "toBinary", Array.Empty<MethodParameter>(), "String", "转为二进制文本", notes: "负数返回 64 位补码字符串，正数无前导零，无 0b 前缀");
        yield return Varf("Long", "bitSwitch", new[] { new MethodParameter("序号", "Float"), new MethodParameter("开关", "Bool") }, "Long", "操作单个 bit", notes: "序号范围 0~63，0=最低位；返回新值，不修改自身");
        yield return Varf("Long", "inside", new[] { new MethodParameter("A", "Long"), new MethodParameter("B", "Long") }, "Bool", "是否在区间内", notes: "闭区间 [A,B]，包含两端边界；等价搜索 inRange/区间。");
    }

    private static IEnumerable<MethodSignature> GetStringMethods()
    {
        yield return Varf("String", "set", new[] { new MethodParameter(string.Empty, "String", Alias: "value") }, null, "设置值");
        yield return Varf("String", "length", Array.Empty<MethodParameter>(), "Float", "获取长度");
        yield return Varf("String", "toFloat", Array.Empty<MethodParameter>(), "Float", "转为浮点数", notes: "解析失败返回 0");
        yield return Varf("String", "toLong", Array.Empty<MethodParameter>(), "Long", "转为整数", notes: "解析失败返回 0");
        yield return Varf("String", "split", new[] { new MethodParameter("分隔符", "String") }, "List", "分割字符串", notes: "分隔符必须是单个字符");
        yield return Varf("String", "getChar", new[] { new MethodParameter("序号", "Float") }, "String", "获取字符");
        yield return Varf("String", "substring", new[] { new MethodParameter("起始序号", "Float"), new MethodParameter("字符个数", "Float") }, "String", "截取子串", notes: "字符个数必须 >=1");
        yield return Varf("String", "find", new[] { new MethodParameter(string.Empty, "String", Alias: "text"), new MethodParameter("从后往前找", "Bool") }, "Float", "按大小写查找", notes: "找不到返回 -1");
        yield return Varf("String", "contain", new[] { new MethodParameter(string.Empty, "String", Alias: "text"), new MethodParameter("忽略大小写", "Bool") }, "Bool", "是否包含");
        yield return Varf("String", "replace", new[] { new MethodParameter("被替换", "String"), new MethodParameter("替换为", "String") }, "String", "替换", notes: "替换全部匹配项");
        yield return Varf("String", "ToUpper", Array.Empty<MethodParameter>(), "String", "转大写");
        yield return Varf("String", "ToLower", Array.Empty<MethodParameter>(), "String", "转小写");
        yield return Varf("String", "toAsc", Array.Empty<MethodParameter>(), "Long", "获取字符 ASCII 码", notes: "返回首字符的 Unicode 码点，空字符串时返回值不确定");
        yield return Varf("String", "equal", new[] { new MethodParameter("A", "String") }, "Bool", "是否相等", notes: "区分大小写");
    }

    private static IEnumerable<MethodSignature> GetListMethods()
    {
        // 基础信息
        yield return Varf("List", "count", Array.Empty<MethodParameter>(), "Float", "获取长度");
        yield return Varf("List", "clear", Array.Empty<MethodParameter>(), null, "清空列表");
        yield return Varf("List", "ref", Array.Empty<MethodParameter>(), "List", "复制引用");
        yield return Varf("List", "clone", Array.Empty<MethodParameter>(), "List", "递归(递归仅针对List/Dict/Struct元素)克隆");
        yield return Varf("List", "detail", Array.Empty<MethodParameter>(), "String", "输出详情");
        yield return Varf("List", "types", new[] { new MethodParameter("序号", "Float") }, "String", "获取元素类型");

        // 增删改操作
        CompressionHint addHint = new(CompressionKind.NumericSuffix, "add", Array.Empty<string>());
        yield return Varf("List", "add", new[] { new MethodParameter("数据", "Any") }, null, "添加元素", compressionHint: addHint);
        for (int i = 1; i <= 7; i++)
        {
            int paramCount = i + 1;
            MethodParameter[] parameters = new MethodParameter[paramCount];
            parameters[0] = new MethodParameter("数据", "Any");
            for (int j = 1; j < paramCount; j++)
            {
                parameters[j] = new MethodParameter("数据", "Any");
            }
            yield return Varf("List", $"add{i}", parameters, null, $"添加 {paramCount} 个元素", compressionHint: addHint);
        }
        yield return Varf("List", "insert", new[] { new MethodParameter("序号", "Float"), new MethodParameter("数据", "Any") }, null, "插入元素");
        yield return Varf("List", "insertList", new[] { new MethodParameter("序号", "Float"), new MethodParameter("数据", "List") }, null, "插入列表");
        yield return Varf("List", "insertMulti", new[] { new MethodParameter("数据", "Any"), new MethodParameter("个数", "Float") }, null, "批量插入");
        yield return Varf("List", "remove", new[] { new MethodParameter("序号", "Float") }, null, "删除元素");
        yield return Varf("List", "multipleRemove", new[] { new MethodParameter("序号", "Float"), new MethodParameter("个数", "Float") }, null, "批量删除元素");
        yield return Varf("List", "replace", new[] { new MethodParameter("序号", "Float"), new MethodParameter("数据", "Any") }, null, "替换元素");
        yield return Varf("List", "swap", new[] { new MethodParameter("序号1", "Float"), new MethodParameter("序号2", "Float") }, null, "交换元素");

        // 查找/排序/统计
        yield return Varf("List", "find", new[] { new MethodParameter("数据", "Any") }, "Float", "查找元素");
        yield return Varf("List", "find2", new[] { new MethodParameter("查找用名称", "String"), new MethodParameter("查找用数据", "Any") }, "Float", "按键名查找");
        yield return Varf("List", "sort", new[] { new MethodParameter("是否从小到大", "Bool") }, null, "排序");
        yield return Varf("List", "sort2", new[] { new MethodParameter("是否从小到大", "Bool"), new MethodParameter("排序用名称", "String") }, null, "按键名排序");
        yield return Varf("List", "sortCustom", new[] { new MethodParameter("排序用方法名", "String", IsScriptMethodName: true) }, null, "自定义排序", notes: "每个元素都是字典，排序方法需要两个字典参数表示需要比较的两个数据，一个整数返回值表示哪个数据大，一样大返回0");
        yield return Varf("List", "reverse", Array.Empty<MethodParameter>(), null, "反转");
        yield return Varf("List", "random", Array.Empty<MethodParameter>(), null, "随机顺序");
        yield return Varf("List", "max", Array.Empty<MethodParameter>(), "Float", "获取最大值");
        yield return Varf("List", "min", Array.Empty<MethodParameter>(), "Float", "获取最小值");

        // 文本导入/导出
        yield return Varf("List", "insertVecs", new[] { new MethodParameter("序号", "Float"), new MethodParameter("数据", "String") }, null, "从文本插入向量列表");
        yield return Varf("List", "inserFloats", new[] { new MethodParameter("序号", "Float"), new MethodParameter("数据", "String") }, null, "从文本插入浮点数列表");
        yield return Varf("List", "print", Array.Empty<MethodParameter>(), "String", "输出为文本");

        // 读取不同类型元素
        CompressionHint readHint = new(CompressionKind.TypeSuffix, "read", ReadValueTypes);
        yield return Varf("List", "readFloat", new[] { new MethodParameter("序号", "Float") }, "Float", "读取浮点数", compressionHint: readHint);
        yield return Varf("List", "readLong", new[] { new MethodParameter("序号", "Float") }, "Long", "读取整数");
        yield return Varf("List", "readString", new[] { new MethodParameter("序号", "Float") }, "String", "读取字符串");
        yield return Varf("List", "readBool", new[] { new MethodParameter("序号", "Float") }, "Bool", "读取布尔值");
        yield return Varf("List", "readVector3", new[] { new MethodParameter("序号", "Float") }, "Vector3", "读取向量");
        yield return Varf("List", "readColor", new[] { new MethodParameter("序号", "Float") }, "Color", "读取颜色");
        yield return Varf("List", "readList", new[] { new MethodParameter("序号", "Float") }, "List", "读取列表");
        yield return Varf("List", "readDict", new[] { new MethodParameter("序号", "Float") }, "Dict", "读取字典");
        yield return Varf("List", "readStruct", new[] { new MethodParameter("序号", "Float") }, "Struct", "读取结构体");
        yield return Varf("List", "readDevice", new[] { new MethodParameter("序号", "Float") }, "Device", "读取零件");
        yield return Varf("List", "readMech", new[] { new MethodParameter("序号", "Float") }, "Mech", "读取机械");
        yield return Varf("List", "readPlayer", new[] { new MethodParameter("序号", "Float") }, "Player", "读取玩家");
        yield return Varf("List", "readDatetime", new[] { new MethodParameter("序号", "Float") }, "Datetime", "读取日期时间");
        yield return Varf("List", "readBytes", new[] { new MethodParameter("序号", "Float") }, "Bytes", "读取二进制流");
        yield return Varf("List", "readCanvas", new[] { new MethodParameter("序号", "Float") }, "Canvas", "读取画布");
        yield return Varf("List", "readIcon", new[] { new MethodParameter("序号", "Float") }, "Icon", "读取图标");
        yield return Varf("List", "readUIRect", new[] { new MethodParameter("序号", "Float") }, "UIRect", "读取 UIRect");
        yield return Varf("List", "readUILabel", new[] { new MethodParameter("序号", "Float") }, "UILabel", "读取 UILabel");
        yield return Varf("List", "readUIButton", new[] { new MethodParameter("序号", "Float") }, "UIButton", "读取 UIButton");
        yield return Varf("List", "readUIInput", new[] { new MethodParameter("序号", "Float") }, "UIInput", "读取 UIInput");
        yield return Varf("List", "readUIRawImage", new[] { new MethodParameter("序号", "Float") }, "UIRawImage", "读取 UIRawImage");
        yield return Varf("List", "readUISpine", new[] { new MethodParameter("序号", "Float") }, "UISpine", "读取 UISpine");
        yield return Varf("List", "readUILine", new[] { new MethodParameter("序号", "Float") }, "UILine", "读取 UILine");
        yield return Varf("List", "readUIAvatar", new[] { new MethodParameter("序号", "Float") }, "UIAvatar", "读取 UIAvatar");

        // 导出/导入
        yield return Varf("List", "export", new[] { new MethodParameter("是否压缩", "Bool") }, "String", "导出为 Base64");
        yield return Varf("List", "import", new[] { new MethodParameter("内容", "String"), new MethodParameter("是否压缩", "Bool") }, null, "从 Base64 导入");
        yield return Varf("List", "fromJson", new[] { new MethodParameter("内容@content", "String", Alias: "content") }, null, "从 JSON 导入");
    }

    private static IEnumerable<MethodSignature> GetDictMethods()
    {
        // 基础增删改
        yield return Varf("Dict", "count", Array.Empty<MethodParameter>(), "Float", "获取键数量");
        yield return Varf("Dict", "clear", Array.Empty<MethodParameter>(), null, "清空字典");
        yield return Varf("Dict", "insert", new[] { new MethodParameter("名称", "String"), new MethodParameter("数据", "Any") }, null, "插入或更新键", notes: "同名键已存在时类型必须一致才能覆盖，否则抛异常");
        yield return Varf("Dict", "contains", new[] { new MethodParameter("名字", "String") }, "Bool", "是否包含键");
        yield return Varf("Dict", "remove", new[] { new MethodParameter("名称", "String") }, null, "删除键", notes: "键不存在时抛异常");

        // 引用/克隆
        yield return Varf("Dict", "ref", Array.Empty<MethodParameter>(), "Dict", "复制引用");
        yield return Varf("Dict", "clone", Array.Empty<MethodParameter>(), "Dict", "递归(递归仅针对List/Dict/Struct元素)克隆");

        // 键名与类型
        yield return Varf("Dict", "names", Array.Empty<MethodParameter>(), "List", "获取所有键名");
        yield return Varf("Dict", "types", new[] { new MethodParameter("名称", "String") }, "String", "获取值类型");

        // 读取不同类型值
        CompressionHint readHint = new(CompressionKind.TypeSuffix, "read", ReadValueTypes);
        yield return Varf("Dict", "readFloat", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Float", "读取浮点数", compressionHint: readHint);
        yield return Varf("Dict", "readLong", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Long", "读取整数");
        yield return Varf("Dict", "readString", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "String", "读取字符串");
        yield return Varf("Dict", "readBool", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Bool", "读取布尔值");
        yield return Varf("Dict", "readVector3", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Vector3", "读取向量");
        yield return Varf("Dict", "readColor", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Color", "读取颜色");
        yield return Varf("Dict", "readList", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "List", "读取列表");
        yield return Varf("Dict", "readDict", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Dict", "读取字典");
        yield return Varf("Dict", "readStruct", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Struct", "读取结构体");
        yield return Varf("Dict", "readDevice", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Device", "读取零件");
        yield return Varf("Dict", "readMech", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Mech", "读取机械");
        yield return Varf("Dict", "readPlayer", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Player", "读取玩家");
        yield return Varf("Dict", "readDatetime", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Datetime", "读取日期时间");
        yield return Varf("Dict", "readBytes", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Bytes", "读取二进制流");
        yield return Varf("Dict", "readCanvas", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Canvas", "读取画布");
        yield return Varf("Dict", "readIcon", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "Icon", "读取图标");
        yield return Varf("Dict", "readUIRect", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UIRect", "读取 UIRect");
        yield return Varf("Dict", "readUIInput", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UIInput", "读取 UIInput");
        yield return Varf("Dict", "readUILabel", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UILabel", "读取 UILabel");
        yield return Varf("Dict", "readUIButton", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UIButton", "读取 UIButton");
        yield return Varf("Dict", "readUIRawImage", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UIRawImage", "读取 UIRawImage");
        yield return Varf("Dict", "readUISpine", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UISpine", "读取 UISpine");
        yield return Varf("Dict", "readUILine", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UILine", "读取 UILine");
        yield return Varf("Dict", "readUIAvatar", new[] { new MethodParameter("名称", "String"), new MethodParameter("找不到报错", "Bool") }, "UIAvatar", "读取 UIAvatar");

        // 导出/导入/文件
        yield return Varf("Dict", "export", new[] { new MethodParameter("是否压缩", "Bool") }, "String", "导出为 Base64");
        yield return Varf("Dict", "import", new[] { new MethodParameter("内容", "String"), new MethodParameter("是否压缩", "Bool") }, null, "从 Base64 导入");
        yield return Varf("Dict", "toFile", new[] { new MethodParameter("文件名", "String") }, null, "保存到文件", notes: "文件位于运行时持久数据目录（非工程目录），文件名不含路径分隔符");
        yield return Varf("Dict", "fromFile", new[] { new MethodParameter("文件名", "String"), new MethodParameter("工程目录", "Bool") }, null, "从文件加载", notes: "工程目录=true 时从项目资源目录读（按文件名查找），false 时从运行时持久数据目录读");
        yield return Varf("Dict", "fromHJson", new[] { new MethodParameter("内容@content", "String", Alias: "content") }, null, "从 HJson 导入");
        yield return Varf("Dict", "toHJson", Array.Empty<MethodParameter>(), "String", "导出为 HJson");
        yield return Varf("Dict", "fromJson", new[] { new MethodParameter("内容@content", "String", Alias: "content") }, null, "从 JSON 导入");

        // 调试
        yield return Varf("Dict", "detail", Array.Empty<MethodParameter>(), "String", "输出详情");
        yield return Varf("Dict", "compressBytes", Array.Empty<MethodParameter>(), "Float", "获取压缩后字节数");
    }

    private static IEnumerable<MethodSignature> GetStructMethods()
    {
        // 写入成员
        yield return Varf("Struct", "set", new[] { new MethodParameter("名称", "String"), new MethodParameter("数据", "Any") }, null, "写入成员(复制引用)", notes: "同名成员已存在时类型必须一致才能覆盖，否则抛异常；不存在则新建");

        // 查询/统计
        yield return Varf("Struct", "contains", new[] { new MethodParameter("名字", "String") }, "Bool", "是否包含成员");
        yield return Varf("Struct", "exist", Array.Empty<MethodParameter>(), "Bool", "是否非空");
        yield return Varf("Struct", "count", Array.Empty<MethodParameter>(), "Float", "获取成员数量");

        // 引用
        yield return Varf("Struct", "ref", Array.Empty<MethodParameter>(), "Struct", "复制引用");
        yield return Varf("Struct", "detail", Array.Empty<MethodParameter>(), "String", "输出详情");
        yield return Varf("Struct", "members", Array.Empty<MethodParameter>(), "String", "输出成员列表");

        // 按类型读取成员
        // 不复制时返回变量与内部成员共享同一存储槽位，对返回变量的写操作直接反映到 Struct 内部。
        // 复制时：值类型复制值（独立），引用类型复制引用指针（仍共享底层数据，非深拷贝）。
        const string getNote = "不复制时返回变量即为内部成员的绑定而非独立副本，任何写操作直接改变 Struct 内部状态；复制时对引用类型仍共享底层数据";
        CompressionHint getHint = new(CompressionKind.TypeSuffix, "get", ReadValueTypes);
        yield return Varf("Struct", "getFloat", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Float", "读取成员(值类型,不复制则值同步更新)", compressionHint: getHint, notes: getNote);
        yield return Varf("Struct", "getLong", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Long", "读取成员(值类型,不复制则值同步更新)", notes: getNote);
        yield return Varf("Struct", "getString", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "String", "读取成员(值类型,不复制则值同步更新)", notes: getNote);
        yield return Varf("Struct", "getBool", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Bool", "读取成员(值类型,不复制则值同步更新)", notes: getNote);
        yield return Varf("Struct", "getVector3", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Vector3", "读取成员(值类型,不复制则值同步更新)", notes: getNote);
        yield return Varf("Struct", "getColor", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Color", "读取成员(值类型,不复制则值同步更新)", notes: getNote);
        yield return Varf("Struct", "getList", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "List", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getDict", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Dict", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getStruct", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Struct", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getDevice", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Device", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getMech", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Mech", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getPlayer", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Player", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getDatetime", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Datetime", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getBytes", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Bytes", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getCanvas", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Canvas", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getIcon", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "Icon", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUIRect", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UIRect", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUILabel", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UILabel", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUIButton", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UIButton", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUIInput", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UIInput", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUIRawImage", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UIRawImage", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUISpine", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UISpine", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUILine", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UILine", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
        yield return Varf("Struct", "getUIAvatar", new[] { new MethodParameter("名称", "String"), new MethodParameter("是否复制", "Bool") }, "UIAvatar", "读取成员(引用类型,不复制则引用本身和Struct里面那条引用同步更新,复制也是复制一份引用而不是复制实例)", notes: getNote);
    }

    private static IEnumerable<MethodSignature> GetBytesMethods()
    {
        string[] readTypes = new[] { "Bool", "Bytes", "Color", "Datetime", "Float", "Long", "String", "Vector3" };
        string[] writeTypes = new[] { "Bool", "Bytes", "Color", "Datetime", "Float", "Long", "String", "Vector3" };
        CompressionHint readHint = new(CompressionKind.TypeSuffix, "read", readTypes);
        CompressionHint writeHint = new(CompressionKind.TypeSuffixParam, "write", writeTypes);

        // 基本信息
        yield return Varf("Bytes", "len", Array.Empty<MethodParameter>(), "Long", "获取长度");
        yield return Varf("Bytes", "readOffset", Array.Empty<MethodParameter>(), "Long", "获取读指针位置");
        yield return Varf("Bytes", "setReadOffset", new[] { new MethodParameter("跳过的字节数", "Long") }, null, "设置读指针位置", notes: "参数为绝对位置而非跳过量，范围 [0, len-1]");
        yield return Varf("Bytes", "skipRead", new[] { new MethodParameter("跳过的字节数", "Long") }, null, "跳过字节", notes: "固定跳过 4 字节，参数范围应合法");

        // 顺序写入
        yield return Varf("Bytes", "writeByte", new[] { new MethodParameter(string.Empty, "Long", Alias: "value") }, null, "写入单字节", notes: "值范围 [0,255]，超出抛异常");
        yield return Varf("Bytes", "writeFloat", new[] { new MethodParameter(string.Empty, "Float", Alias: "value") }, null, "写入浮点数", compressionHint: writeHint);
        yield return Varf("Bytes", "writeLong", new[] { new MethodParameter(string.Empty, "Long", Alias: "value") }, null, "写入整数", compressionHint: writeHint);
        yield return Varf("Bytes", "writeBool", new[] { new MethodParameter(string.Empty, "Bool", Alias: "value") }, null, "写入布尔值", compressionHint: writeHint);
        yield return Varf("Bytes", "writeString", new[] { new MethodParameter(string.Empty, "String", Alias: "value") }, null, "写入字符串", compressionHint: writeHint);
        yield return Varf("Bytes", "writeVector", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "value") }, null, "写入向量", compressionHint: writeHint);
        yield return Varf("Bytes", "writeColor", new[] { new MethodParameter(string.Empty, "Color", Alias: "value") }, null, "写入颜色", compressionHint: writeHint);
        yield return Varf("Bytes", "writeDatetime", new[] { new MethodParameter(string.Empty, "Datetime", Alias: "value") }, null, "写入日期时间", compressionHint: writeHint);
        yield return Varf("Bytes", "writeBytes", new[] { new MethodParameter(string.Empty, "Bytes", Alias: "value") }, null, "写入二进制流", compressionHint: writeHint);

        // 顺序读取
        yield return Varf("Bytes", "readByte", Array.Empty<MethodParameter>(), "Long", "读取单字节");
        yield return Varf("Bytes", "readFloat", Array.Empty<MethodParameter>(), "Float", "读取浮点数", compressionHint: readHint);
        yield return Varf("Bytes", "readLong", Array.Empty<MethodParameter>(), "Long", "读取整数", compressionHint: readHint);
        yield return Varf("Bytes", "readBool", Array.Empty<MethodParameter>(), "Bool", "读取布尔值", compressionHint: readHint);
        yield return Varf("Bytes", "readString", Array.Empty<MethodParameter>(), "String", "读取字符串", compressionHint: readHint);
        yield return Varf("Bytes", "readVector", Array.Empty<MethodParameter>(), "Vector3", "读取向量", compressionHint: readHint);
        yield return Varf("Bytes", "readColor", Array.Empty<MethodParameter>(), "Color", "读取颜色", compressionHint: readHint);
        yield return Varf("Bytes", "readDatetime", Array.Empty<MethodParameter>(), "Datetime", "读取日期时间", compressionHint: readHint);
        yield return Varf("Bytes", "readBytes", Array.Empty<MethodParameter>(), "Bytes", "读取二进制流", compressionHint: readHint);

        // 文件导入/导出
        yield return Varf("Bytes", "toFile", new[] { new MethodParameter("文件名", "String"), new MethodParameter("是否压缩", "Bool") }, null, "保存到文件", notes: "文件位于运行时持久数据目录（非工程目录），文件名不含路径分隔符");
        yield return Varf("Bytes", "fromFile", new[] { new MethodParameter("文件名", "String") }, null, "从文件加载", notes: "文件位于运行时持久数据目录（非工程目录），文件名不含路径分隔符");
    }

    private static IEnumerable<MethodSignature> GetVector3Methods()
    {
        // 分量访问与修改
        yield return Varf("Vector3", "x", Array.Empty<MethodParameter>(), "Float", "获取 X 分量");
        yield return Varf("Vector3", "y", Array.Empty<MethodParameter>(), "Float", "获取 Y 分量");
        yield return Varf("Vector3", "z", Array.Empty<MethodParameter>(), "Float", "获取 Z 分量");
        yield return Varf("Vector3", "setX", new[] { new MethodParameter(string.Empty, "Float", Alias: "x") }, null, "设置 X 分量");
        yield return Varf("Vector3", "setY", new[] { new MethodParameter(string.Empty, "Float", Alias: "y") }, null, "设置 Y 分量");
        yield return Varf("Vector3", "setZ", new[] { new MethodParameter(string.Empty, "Float", Alias: "z") }, null, "设置 Z 分量");
        yield return Varf("Vector3", "set", new[] { new MethodParameter(string.Empty, "Vector3", Alias: "value") }, null, "设置向量");

        // 长度
        yield return Varf("Vector3", "length", Array.Empty<MethodParameter>(), "Float", "获取长度", notes: "向量长度/模长/magnitude；两个坐标之间距离用 vector3.distance。");
        yield return Varf("Vector3", "setLength", new[] { new MethodParameter(string.Empty, "Float", Alias: "length") }, "Vector3", "设置长度", notes: "按当前方向设置长度；原版计算 normalized * length，零向量仍没有方向。");
        yield return Varf("Vector3", "limitLength", new[] { new MethodParameter(string.Empty, "Float", Alias: "maxLength") }, "Vector3", "限制长度", notes: "仅当前长度超过 maxLength 时缩短；未超过时返回原向量值。");

        // 旋转
        yield return Varf("Vector3", "rotAxis", new[] { new MethodParameter("轴", "Vector3"), new MethodParameter("角度", "Float") }, "Vector3", "绕轴旋转", notes: "角度单位为度（非弧度），允许负值");
        yield return Varf("Vector3", "rotPos", new[] { new MethodParameter("点", "Vector3"), new MethodParameter("轴", "Vector3"), new MethodParameter("角度", "Float") }, "Vector3", "绕点旋转", notes: "角度单位为度（非弧度），允许负值");

        // 转换
        yield return Varf("Vector3", "toColor", Array.Empty<MethodParameter>(), "Color", "转为颜色", notes: "将向量各分量直接映射为 RGB（范围 0~1，非 0~255），alpha 默认为 1，分量不能小于 0");

        // 其他
        yield return Varf("Vector3", "copy", Array.Empty<MethodParameter>(), "Vector3", "复制");
        yield return Varf("Vector3", "equal", new[] { new MethodParameter("A", "Vector3") }, "Bool", "是否相等", notes: "Unity Vector3 == 的约等于；搜索 equal/equals/等于/相等/比较 时用这个。");
    }

    private static IEnumerable<MethodSignature> GetColorMethods()
    {
        // 分量访问（返回 0~1 浮点数，非 0~255 整数）
        yield return Varf("Color", "r", Array.Empty<MethodParameter>(), "Float", "获取 R 分量");
        yield return Varf("Color", "g", Array.Empty<MethodParameter>(), "Float", "获取 G 分量");
        yield return Varf("Color", "b", Array.Empty<MethodParameter>(), "Float", "获取 B 分量");
        yield return Varf("Color", "a", Array.Empty<MethodParameter>(), "Float", "获取 A 分量");
        yield return Varf("Color", "setR", new[] { new MethodParameter(string.Empty, "Float", Alias: "r") }, null, "设置 R 分量", notes: "取值范围 0~1（非 0~255），超出报错");
        yield return Varf("Color", "setG", new[] { new MethodParameter(string.Empty, "Float", Alias: "g") }, null, "设置 G 分量", notes: "取值范围 0~1（非 0~255），超出报错");
        yield return Varf("Color", "setB", new[] { new MethodParameter(string.Empty, "Float", Alias: "b") }, null, "设置 B 分量", notes: "取值范围 0~1（非 0~255），超出报错");
        yield return Varf("Color", "setA", new[] { new MethodParameter(string.Empty, "Float", Alias: "a") }, null, "设置 A 分量", notes: "取值范围 0~1（非 0~255），超出报错");

        // 转换
        yield return Varf("Color", "toVec", Array.Empty<MethodParameter>(), "Vector3", "转为向量", notes: "丢弃 alpha 通道，仅映射 RGB 到 XYZ");
        yield return Varf("Color", "toHex", Array.Empty<MethodParameter>(), "String", "转为十六进制");
        yield return Varf("Color", "toHSV", Array.Empty<MethodParameter>(), "Vector3", "转为 HSV", notes: "返回 Vector3 中 x=H, y=S, z=V");
        yield return Varf("Color", "fromHSV", new[] { new MethodParameter("hsv", "Vector3") }, null, "从 HSV 设置", notes: "输入 Vector3 中 x=H, y=S, z=V");

        // 其他
        yield return Varf("Color", "copy", Array.Empty<MethodParameter>(), "Color", "复制");
        yield return Varf("Color", "equal", new[] { new MethodParameter("A", "Color") }, "Bool", "是否相等");
        yield return Varf("Color", "lerp", new[] { new MethodParameter("颜色B", "Color"), new MethodParameter("系数", "Float") }, "Color", "颜色插值");
    }

    private static IEnumerable<MethodSignature> GetDateTimeMethods()
    {
        // 组件访问
        yield return Varf("Datetime", "year", Array.Empty<MethodParameter>(), "Float", "获取年");
        yield return Varf("Datetime", "month", Array.Empty<MethodParameter>(), "Float", "获取月");
        yield return Varf("Datetime", "day", Array.Empty<MethodParameter>(), "Float", "获取日");
        yield return Varf("Datetime", "hour", Array.Empty<MethodParameter>(), "Float", "获取时");
        yield return Varf("Datetime", "min", Array.Empty<MethodParameter>(), "Float", "获取分");
        yield return Varf("Datetime", "sec", Array.Empty<MethodParameter>(), "Float", "获取秒");
        yield return Varf("Datetime", "Millisecond", Array.Empty<MethodParameter>(), "Float", "获取毫秒");

        // 设置
        yield return Varf("Datetime", "set", new[] { new MethodParameter("年", "Float"), new MethodParameter("月", "Float"), new MethodParameter("日", "Float"), new MethodParameter("时", "Float"), new MethodParameter("分", "Float"), new MethodParameter("秒", "Float") }, null, "设置时间", notes: "浮点参数被截断为整数（非四舍五入）；非法年月日时分秒会导致运行时异常");

        // 时间运算
        yield return Varf("Datetime", "addSecInt", new[] { new MethodParameter("秒", "Long"), new MethodParameter("毫秒", "Long") }, "Datetime", "增加秒数（整数）", notes: "可传负数实现减法");
        yield return Varf("Datetime", "addSec", new[] { new MethodParameter("秒", "Float") }, "Datetime", "增加秒数");
        yield return Varf("Datetime", "addTime", new[] { new MethodParameter("秒", "Long"), new MethodParameter("分钟", "Long"), new MethodParameter("小时", "Long"), new MethodParameter("天", "Long") }, "Datetime", "增加时间", notes: "可传负数实现减法");
        yield return Varf("Datetime", "delta", new[] { new MethodParameter(string.Empty, "Datetime", Alias: "other") }, "Float", "计算秒数差", notes: "计算方向为 参数时间 - 自身时间；返回 float，大跨度有精度损失");
        yield return Varf("Datetime", "dayDelta", new[] { new MethodParameter(string.Empty, "Datetime", Alias: "other") }, "Float", "计算天数差", notes: "返回整数天数（截断），不含小数天数");
        yield return Varf("Datetime", "unixTime", Array.Empty<MethodParameter>(), "Long", "转 UNIX 时间", notes: "仅支持 1970~2106 年范围，超出会溢出");
        yield return Varf("Datetime", "fromUnixTime", new[] { new MethodParameter("时间戳", "Long") }, null, "设置 UNIX 时间", notes: "时间戳范围为无符号 uint（0~约42亿），不支持负数");
        yield return Varf("Datetime", "daysInMonth", Array.Empty<MethodParameter>(), "Long", "得到本月天数");

        // 其他
        yield return Varf("Datetime", "copy", Array.Empty<MethodParameter>(), "Datetime", "复制");
    }
}
