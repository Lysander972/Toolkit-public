using System.Text;
using Toolkit.Core.Bescript;
using Toolkit.Core.Canvas;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// 常量填充上下文。
/// </summary>
internal readonly struct ConstFillContext
{
    /// <summary>类型名称（已规范化）。</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>常量值。</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>消费方法名。</summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>具体枚举分组（仅 Enum 类型使用）。</summary>
    public MethodEnumType? EnumType { get; init; }

    public ConstFillContext() { }
}

internal readonly struct ConstExpressionFillResult
{
    public bool Success { get; init; }

    public DeviceTsViewExpression? Expression { get; init; }

    public string Error { get; init; } = string.Empty;

    public ConstExpressionFillResult() { }

    public static ConstExpressionFillResult Ok(DeviceTsViewExpression expression) => new() { Success = true, Expression = expression };
    public static ConstExpressionFillResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// 常量反向解析结果。
/// </summary>
internal readonly struct ConstParseResult
{
    public bool Success { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Error { get; init; }

    public ConstParseResult() { }

    public static ConstParseResult Ok(string typeName, string value) => new() { Success = true, TypeName = typeName, Value = value };
    public static ConstParseResult Fail(string error) => new() { Error = error };
    public static ConstParseResult NotBeConst => new();
}

/// <summary>
/// 常量填充规则与转换统一处理器。
/// </summary>
/// <remarks>
/// <para><b>第一层: 任意位置自由值</b></para>
/// <list type="bullet">
///   <item>Bool/Float/Long/Vector3/Color/String：允许在任意参数位携带非空值</item>
/// </list>
/// <para><b>第二层: 消费位自由值</b></para>
/// <list type="bullet">
///   <item>Enum/Canvas/Icon 只在对应消费位允许携带非空值</item>
/// </list>
/// <para><b>第三层: 固定值</b></para>
/// <list type="bullet">
///   <item>Number 固定为 ?，Datetime 固定为 0</item>
///   <item>其他类型固定为空值，对应无参 fromBeConst()</item>
/// </list>
/// </remarks>
internal static class ConstFillMode
{
    /// <summary>
    /// 普通 fromBeConst 允许在任意参数位携带非空值的类型。
    /// 不在此集合内的类型在普通 fromBeConst 上固定使用无参形式。
    /// 此外也支持在G.create.string()嵌套常量时,降级转String
    /// 唯一权威：DTS 声明层、AST 生成层、校验层均引用此集合，禁止各自维护独立分类。
    /// </summary>
    internal static readonly HashSet<string> AnyPositionNonEmptyConstTypes = new(StringComparer.Ordinal)
    {
        "Bool", "Float", "Long", "Color", "Vector3", "String"
    };

    // 第二层：这些类型允许非空自由值，但只能落在指定消费位，禁止混入 AnyPositionNonEmptyConstTypes。
    private static readonly Dictionary<string, string> ConsumerScopedFreeValueMethods = new(StringComparer.Ordinal)
    {
        [nameof(BescriptVarType.Canvas)] = "create.canvas",
        [nameof(BescriptVarType.Icon)] = "create.icon"
    };

    // 第三层：非自由值类型固定写入的常量值；不在此表中的类型固定为空白值。
    private static readonly Dictionary<string, string> FixedConstValues = new(StringComparer.Ordinal)
    {
        [nameof(BescriptVarType.Number)] = "?",
        [nameof(BescriptVarType.Datetime)] = "0"
    };

    public static ConstExpressionFillResult BuildConstExpression(ConstFillContext ctx)
    {
        string typeName = ctx.TypeName;
        string value = ctx.Value;

        if (string.Equals(typeName, "Bool", StringComparison.Ordinal))
        {
            value = value switch
            {
                "1" => "1",
                "0" => "0",
                "true" => "1",
                "false" => "0",
                _ => throw new InvalidOperationException($"Bool 只允许 0/1/true/false，当前为: {value}")
            };
            ctx = new ConstFillContext
            {
                TypeName = ctx.TypeName,
                Value = value,
                MethodName = ctx.MethodName,
                EnumType = ctx.EnumType
            };
        }

        if (!IsValueAllowed(ctx, value))
        {
            return ConstExpressionFillResult.Fail($"类型 {typeName} 在 {ctx.MethodName} 参数位不允许填充值'{value}'");
        }

        if (string.Equals(typeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal) && value.Length > 0)
        {
            if (CanvasInlineArg.Parse(value) is CanvasInlineArgData inlineArg)
            {
                if (inlineArg.FormatKind == CanvasInlineArgFormatKind.LegacyBase64)
                    return BuildCanvasBase64Expression(inlineArg);

                return BuildCanvasNodeHjsonExpression(value);
            }

            if (IsCanvasGuidMode(value))
            {
                return BuildCanvasGuidExpression(value);
            }
        }

        return BuildGenericConstExpression(ctx);
    }

    /// <summary>
    /// Canvas 常量格式统一提示：四种非空 Canvas 赋值方法的合法格式与禁止事项。
    /// </summary>
    private const string CanvasFormatHint = "Canvas 常量格式规范：BeCanvas.fromBase64Const / fromNodeConst 参数必须是 Canvas HTML 视图模板字符串（根元素为 <BeCanvas>；自闭合标签可读入但生成视图会输出显式闭合标签），禁止直接填入原始 base64 或 hjson 字符串；BeCanvas.fromGuidConst / fromGuidConstLegacy 参数必须为 32 位十六进制 GUID 本身，禁止携带 guid: / ==guid: / == 等前后缀。请先看 SDK 参考文档中的 G.create.canvas / BeCanvas.fromNodeConst，以及 Skills 的 Canvas 内联模板。";

    /// <summary>
    /// 从 TS 视图写回数据解析常量值（反向转换统一入口）。
    /// </summary>
    /// <remarks>
    /// <para><b>SDK 契约（唯一权威来源：DTS 声明 + AnyPositionNonEmptyConstTypes）</b></para>
    /// <list type="bullet">
    ///   <item><b>任意位置非空类型</b>（在 AnyPositionNonEmptyConstTypes 中）：Float, Long, Color, Vector3, String → SDK 声明为 fromBeConst(value: string)</item>
    ///   <item><b>Bool</b>（在 AnyPositionNonEmptyConstTypes 中，特殊）：SDK 声明为 fromBeConst(value: "0" | "1")</item>
    ///   <item><b>无参类型</b>：Datetime, Number, Any, Canvas, Device, Mech, Player, Dict, List, Struct 等 → SDK 声明为 fromBeConst()</item>
    ///   <item><b>BeEnum&lt;T&gt;</b>（唯一泛型 fromBeConst）：SDK 声明为 fromBeConst&lt;T extends EnumType&gt;(value: T)，泛型参数在 BodyCall.TypeArguments 上</item>
    ///   <item><b>BeCanvas</b>（5方法）：除无参 fromBeConst() 外还有 fromBase64Const / fromNodeConst / fromGuidConstLegacy / fromGuidConst</item>
    ///   <item><b>BeIcon</b>：除空值 fromBeConst() 外，非空 fromBeConst("...") 仅属于 G.create.icon 消费位</item>
    /// </list>
    /// <para><b>TS 视图生成形状（BES → TS）</b></para>
    /// <list type="bullet">
    ///   <item>普通类型：BeFloat.fromBeConst("1.0")</item>
    ///   <item>Enum：BeEnum.fromBeConst&lt;SpecificType&gt;(SpecificType.Member)</item>
    ///   <item>无参类型：BeDatetime.fromBeConst()</item>
    /// </list>
    /// <para><b>Enum 处理规则（只看参数能否提取有效枚举数值）</b></para>
    /// <list type="bullet">
    ///   <item>参数必须是已知枚举类型.成员 的属性访问形式 → 提取数值</item>
    ///   <item>泛型具体类型是否与成员匹配 → 不在此处拒绝</item>
    ///   <item>数值是否对应该参数位的合法取值范围 → 由后续 BuildBescriptArgs / ValidateEnumConstArgs 校验</item>
    ///   <item>拒绝：参数不是属性访问形式（是变量/字面量/其他）</item>
    ///   <item>拒绝：参数中的枚举类型或成员未知</item>
    /// </list>
    /// <para><b>解析器接受的写回形态</b></para>
    /// <list type="bullet">
    ///   <item>propAccess.Expression 必须为 BodyIdentifier；泛型（如有）只从 call.TypeArguments 取</item>
    /// </list>
    /// <para><b>值格式约定</b></para>
    /// <list type="bullet">
    ///   <item>Enum：参数必须是已知枚举类型.成员 的属性访问形式，泛型枚举类型必须与成员所属枚举一致</item>
    ///   <item>Bool：只接受 "0"/"1"/"true"/"false"/"True"/"False"，统一归一化为 "1"/"0"</item>
    ///   <item>Datetime 无参：写回固定值 "0"</item>
    ///   <item>Number 无参：写回固定值 "?"</item>
    ///   <item>其他无参类型：写回空串</item>
    ///   <item>带参类型：参数必须是 BodyStringLiteral，值原样写回</item>
    /// </list>
    /// </remarks>
    public static ConstParseResult TryParseConstValue(BodyCall call)
    {
        if (call.Expression is not BodyPropertyAccess propAccess)
            return ConstParseResult.NotBeConst;
        if (propAccess.Name is not BodyIdentifier methodIdent)
            return ConstParseResult.NotBeConst;

        if (propAccess.Expression is not BodyIdentifier plainTypeIdent)
        {
            return ConstParseResult.NotBeConst;
        }

        string fullTypeName = plainTypeIdent.Text;
#pragma warning disable TK022 // TypeArguments 来自官方 TypeScript API，可缺省
        if (call.TypeArguments is { Count: > 0 } typeArgs)
#pragma warning restore TK022
            fullTypeName += "<" + string.Join(", ", typeArgs.Select(GetTypeArgumentText)) + ">";

        // 裸 BeEnum（无泛型或泛型被解析器丢弃）→ 若参数是已知枚举成员或字符串字面量，按 Enum 类型继续处理
        if (TsViewTypeUtils.TryParseBescriptTypeNameFromTsTypeName(fullTypeName) is not string typeName)
        {
            if (fullTypeName.StartsWith("BeEnum", StringComparison.Ordinal) &&
                call.Arguments.Count > 0)
            {
                if (call.Arguments[0] is BodyPropertyAccess or BodyStringLiteral)
                {
                    typeName = nameof(BescriptVarType.Enum);
                }
                else
                {
                    return ConstParseResult.Fail("BeEnum.fromBeConst 参数必须是具体枚举成员，不能是变量或字面量。");
                }
            }
            else
            {
                string detail = fullTypeName.StartsWith("BeEnum", StringComparison.Ordinal)
                    ? $"。BeEnum 必须带已知枚举类型作为泛型参数，可用枚举: {string.Join(", ", BuiltinEnumTypes.ByTsTypeName.Keys.OrderBy(static k => k, StringComparer.Ordinal))}"
                    : string.Empty;
                return ConstParseResult.Fail($"fromBeConst 类型名无效: {fullTypeName}{detail}");
            }
        }

        // Canvas 类型走专用方法；普通 fromBeConst 在 TS 侧固定为无参形式。
        // 视图为完全一次性重新生成的内容，这里只接受当前唯一规则。
        if (string.Equals(typeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal))
        {
            if (string.Equals(methodIdent.Text, "fromBase64Const", StringComparison.Ordinal))
            {
                if (call.Arguments.Count == 0 || call.Arguments[0] is not BodyTemplateLiteral base64Template)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromBase64Const 参数格式错误。{CanvasFormatHint}");
                }

                try
                {
                    return ConstParseResult.Ok(typeName, ParseCanvasHtmlToLegacyInline(base64Template.Text));
                }
                catch (Exception ex)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromBase64Const 参数格式错误: {ex.Message}。{CanvasFormatHint}");
                }
            }

            if (string.Equals(methodIdent.Text, "fromNodeConst", StringComparison.Ordinal))
            {
                if (call.Arguments.Count < 2 ||
                    call.Arguments[0] is not BodyStringLiteral nameStr ||
                    call.Arguments[1] is not BodyTemplateLiteral nodeTemplate)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromNodeConst 参数必须为 (名称字符串, 模板字符串)。{CanvasFormatHint}");
                }

                try
                {
                    return ConstParseResult.Ok(typeName, ParseCanvasNodeTextToInline(nameStr.Text, nodeTemplate.Text));
                }
                catch (Exception ex)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromNodeConst 参数格式错误: {ex.Message}。{CanvasFormatHint}");
                }
            }

            if (string.Equals(methodIdent.Text, "fromGuidConstLegacy", StringComparison.Ordinal))
            {
                if (call.Arguments.Count == 0 || call.Arguments[0] is not BodyStringLiteral guidStr)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromGuidConstLegacy 参数格式错误。{CanvasFormatHint}");
                }

                string guid = guidStr.Text;
                if (!GuidText.Validate(guid))
                {
                    return ConstParseResult.Fail($"BeCanvas.fromGuidConstLegacy 参数格式错误。{CanvasFormatHint}");
                }

                return ConstParseResult.Ok(typeName, BescriptSyntax.FormatLegacyRef(guid));
            }

            if (string.Equals(methodIdent.Text, "fromGuidConst", StringComparison.Ordinal))
            {
                if (call.Arguments.Count == 0 || call.Arguments[0] is not BodyStringLiteral guidStr)
                {
                    return ConstParseResult.Fail($"BeCanvas.fromGuidConst 参数格式错误。{CanvasFormatHint}");
                }

                string guid = guidStr.Text;
                if (!GuidText.Validate(guid))
                {
                    return ConstParseResult.Fail($"BeCanvas.fromGuidConst 参数格式错误。{CanvasFormatHint}");
                }

                return ConstParseResult.Ok(typeName, BescriptSyntax.FormatNewRef(guid));
            }

            // fromBeConst() 在这里固定走无参路径，其他方法名都不属于这条规则
            if (!string.Equals(methodIdent.Text, "fromBeConst", StringComparison.Ordinal))
            {
                return ConstParseResult.NotBeConst;
            }
        }

        if (!string.Equals(methodIdent.Text, "fromBeConst", StringComparison.Ordinal))
        {
            return ConstParseResult.NotBeConst;
        }

        if (string.Equals(typeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
        {
            if (call.Arguments.Count == 0)
                return ConstParseResult.Fail("BeEnum.fromBeConst 缺少参数。");

            // 兜底：仅当泛型参数为未知/undefined 时（正向降级形式 BeEnum.fromBeConst<undefined>("0")）
            // 才接受 BES 原值字符串字面量，让写回路径不卡死。已知枚举类型 + 字符串字面量是不合规写法
            // （应使用 Type.member 属性访问形式），仍按原有约束拒绝。
            if (call.Arguments[0] is BodyStringLiteral enumStrLit)
            {
                string? genericTypeName = TryGetEnumTypeArgumentName(call);
                bool isKnownEnumType = genericTypeName is not null
                    && BuiltinEnumTypes.ByTsTypeName.ContainsKey(genericTypeName);
                if (isKnownEnumType)
                {
                    return ConstParseResult.Fail("BeEnum.fromBeConst 参数必须是具体枚举成员，不能是变量或字面量。");
                }
                return ConstParseResult.Ok(typeName, enumStrLit.Text);
            }

            if (call.Arguments[0] is not BodyPropertyAccess enumAccess)
                return ConstParseResult.Fail("BeEnum.fromBeConst 参数必须是具体枚举成员，不能是变量或字面量。");

            EnumValueParseResult enumResult = TryParseEnumValue(enumAccess);
            if (!enumResult.Success)
                return ConstParseResult.Fail(enumResult.Error);

            // 泛型有无/对错不在此处拒绝——只要参数是已知枚举成员，能提取有效数值就接受。
            // 数值是否对应该参数位要求的枚举类型，由后续 ValidateEnumConstArgs / BuildBescriptArgs 校验。
            return ConstParseResult.Ok(typeName, enumResult.Value);
        }

        // 通用常量形式：
        // TS 侧按类型固定是否带参；
        // 无参形式写回 BES 时按类型映射为固定文本。
        if (call.Arguments.Count == 0)
        {
            string value = typeName switch
            {
                nameof(BescriptVarType.Datetime) => "0",
                nameof(BescriptVarType.Number) => "?",
                _ => string.Empty
            };
            return ConstParseResult.Ok(typeName, value);
        }

        if (call.Arguments[0] is not BodyStringLiteral str)
        {
            return ConstParseResult.Fail("fromBeConst 参数必须是字符串");
        }

        // Icon 的非空值使用 fromBeConst("...") 承载；是否允许非空值由 ConsumerScopedFreeValueMethods 统一按消费位校验。
        if (string.Equals(typeName, nameof(BescriptVarType.Icon), StringComparison.Ordinal))
        {
            return ConstParseResult.Ok(typeName, str.Text);
        }

        if (!AnyPositionNonEmptyConstTypes.Contains(typeName))
        {
            return ConstParseResult.Fail($"Be{typeName}.fromBeConst 不允许携带字符串参数；普通带参 fromBeConst 仅限 Bool/Float/Long/Vector3/Color/String。");
        }

        return ConstParseResult.Ok(typeName, str.Text);
    }

    internal static string? TryGetEnumTypeArgumentName(BodyCall call)
    {
#pragma warning disable TK022 // TypeArguments 来自官方 TypeScript API，可缺省
        if (call.TypeArguments is not { Count: 1 } typeArgs)
#pragma warning restore TK022
        {
            return null;
        }

        return typeArgs[0] switch
        {
            BodyIdentifier id => id.Text,
            BodyTypeReference { TypeName: BodyIdentifier typeId } => typeId.Text,
            _ => null
        };
    }

    private static string GetTypeArgumentText(BodyNode node) => node switch
    {
        BodyIdentifier id => id.Text,
        BodyTypeReference { TypeName: BodyIdentifier typeId } => typeId.Text,
        _ => node.ToString()
    };

    private readonly struct EnumValueParseResult
    {
        public bool Success { get; init; }
        public string Value { get; init; } = string.Empty;
        public string EnumTypeName { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;

        public EnumValueParseResult() { }

        public static EnumValueParseResult Ok(string value, string enumTypeName) => new() { Success = true, Value = value, EnumTypeName = enumTypeName };
        public static EnumValueParseResult Fail(string error) => new() { Error = error };
    }

    private static EnumValueParseResult TryParseEnumValue(BodyPropertyAccess propertyAccess)
    {
        if (propertyAccess.Expression is not BodyIdentifier typeIdent)
        {
            return EnumValueParseResult.Fail($"枚举常量必须写成 已知枚举类型.成员 的形式，当前为: {propertyAccess}");
        }

        if (!BuiltinEnumTypes.ByTsTypeName.TryGetValue(typeIdent.Text, out MethodEnumType? enumType))
        {
            return EnumValueParseResult.Fail($"未知枚举类型: {typeIdent.Text}");
        }

        EnumMemberInfo? member = BuiltinEnumTypes.GetMemberByTsName(enumType, propertyAccess.Name.Text);
        if (member is null)
        {
            return EnumValueParseResult.Fail($"枚举 {enumType.Name} 不存在成员 {propertyAccess.Name.Text}");
        }

        return EnumValueParseResult.Ok(member.Value.RawValue, enumType.Name);
    }

    #region 校验逻辑

    private static bool IsValueAllowed(ConstFillContext ctx, string value)
    {
        string typeName = ctx.TypeName;

        // 第一层：任意位置自由值。
        if (AnyPositionNonEmptyConstTypes.Contains(typeName))
        {
            return true;
        }

        // Enum 类型：未知具体枚举类型时（如未注册函数残留参数、声明未带枚举类型）
        // 一律允许，由 BuildGenericConstExpression 用 undefined 泛型 + BES 原值字符串字面量降级，
        // 让转换层不再因未知枚举而抛错拖垮整个 MCP 视图重建。
        if (string.Equals(typeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
        {
            return true;
        }

        // 第二层：消费位自由值。
        if (IsConsumerScopedFreeValueAllowed(ctx, value))
        {
            return true;
        }

        // 第三层：固定值。Number/Datetime 是固定非空值，其他类型固定为空值。
        if (FixedConstValues.ContainsKey(typeName))
        {
            return string.Equals(value, FixedConstValues[typeName], StringComparison.Ordinal);
        }

        return string.Equals(value, string.Empty, StringComparison.Ordinal);
    }

    private static bool IsConsumerScopedFreeValueAllowed(ConstFillContext ctx, string value)
    {
        if (string.Equals(value, string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        string typeName = ctx.TypeName;
        if (string.Equals(typeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
        {
            return ctx.EnumType is not null;
        }

        if (!ConsumerScopedFreeValueMethods.ContainsKey(typeName) ||
            !string.Equals(ctx.MethodName, ConsumerScopedFreeValueMethods[typeName], StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(typeName, nameof(BescriptVarType.Canvas), StringComparison.Ordinal))
        {
            return IsCanvasInlineMode(value) || IsCanvasGuidMode(value);
        }

        return true;
    }

    internal static string? ValidateConsumerScopedFreeValueArg(string typeName, string value, string consumerMethodName, int argIndex)
    {
        if (string.Equals(value, string.Empty, StringComparison.Ordinal) ||
            !ConsumerScopedFreeValueMethods.ContainsKey(typeName))
        {
            return null;
        }

        string expectedMethodName = ConsumerScopedFreeValueMethods[typeName];
        if (string.Equals(consumerMethodName, expectedMethodName, StringComparison.Ordinal))
        {
            return null;
        }

        return $"参数 {argIndex}: {typeName} 消费位自由值仅允许用于 G.{expectedMethodName} 参数。其他 {typeName} 参数位只能使用变量或 Be{typeName}.fromBeConst() 空常量。";
    }

    #endregion

    #region 类型判断

    private static bool IsCanvasInlineMode(string value)
        => CanvasInlineArg.Parse(value) is not null;

    private static bool IsCanvasGuidMode(string value)
    {
        return BescriptSyntax.IsAnyRef(value);
    }

    #endregion

    #region 正向转换（BES → Device TS DTO）

    private static ConstExpressionFillResult BuildCanvasBase64Expression(CanvasInlineArgData inlineArg)
    {
        byte[] canvasBytes = Convert.FromBase64String(inlineArg.PayloadText);

        CanvasNodeResult result = CanvasConverter.FromBinary(canvasBytes);
        if (!result.Ok || result.Root == null)
        {
            return ConstExpressionFillResult.Fail($"Canvas 解析失败: {result.Diagnostics[0].Message}");
        }
        string html = CanvasConverter.ToHtmlInline(result.Root, inlineArg.CanvasName);

        string wrapped = TsViewTemplateLiteralText.Dedent("\n" + html);
        return ConstExpressionFillResult.Ok(Call(
            Property(Identifier("BeCanvas"), "fromBase64Const"),
            [Template(wrapped)]));
    }

    private static ConstExpressionFillResult BuildCanvasNodeHjsonExpression(string value)
    {
        CanvasNodeResult result = CanvasConverter.FromInline(value);
        if (!result.Ok || result.Root is not { } root)
        {
            return ConstExpressionFillResult.Fail($"Canvas 解析失败: {result.Diagnostics[0].Message}");
        }

        string html = CanvasConverter.ToHtmlInline(root, result.CanvasName);
        string wrapped = TsViewTemplateLiteralText.Dedent("\n" + html);
        return ConstExpressionFillResult.Ok(Call(
            Property(Identifier("BeCanvas"), "fromNodeConst"),
            [String(result.CanvasName), Template(wrapped)]));
    }

    private static ConstExpressionFillResult BuildCanvasGuidExpression(string value)
    {
        bool isLegacy = BescriptSyntax.IsLegacyRef(value);
        string methodName = isLegacy ? "fromGuidConstLegacy" : "fromGuidConst";
        string guidValue = isLegacy
            ? value[BescriptSyntax.GuidPrefix.Length..]
            : value[BescriptSyntax.NewRefPrefix.Length..^BescriptSyntax.NewRefSuffix.Length];

        return ConstExpressionFillResult.Ok(Call(
            Property(Identifier("BeCanvas"), methodName),
            [String(guidValue)]));
    }

    private static ConstExpressionFillResult BuildGenericConstExpression(ConstFillContext ctx)
    {
        string typeName = ctx.TypeName;
        string value = ctx.Value;
        if (string.Equals(typeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal))
        {
            // 已知具体枚举类型：正常映射成员；找不到对应底层值是真实数据错误，必须显式失败。
            if (ctx.EnumType is { } knownEnumType)
            {
                EnumMemberInfo? found = BuiltinEnumTypes.GetMemberByRawValue(knownEnumType, value);
                if (found is null)
                {
                    return ConstExpressionFillResult.Fail($"枚举 {knownEnumType.Name} 不存在底层值 {value}");
                }

                return ConstExpressionFillResult.Ok(Call(
                    Property(Identifier("BeEnum"), "fromBeConst"),
                    [Property(Identifier(knownEnumType.Name), found.Value.TsName)],
                    [knownEnumType.Name]));
            }

            // 未知具体枚举类型：生成 BeEnum.fromBeConst<undefined>("value") 形式。
            // - undefined 不满足 T extends EnumType 约束，IDE 会凸显语义错误引导 Agent 修正
            // - AST 结构合法，转换层和文本化层都不会卡死
            // - 参数位保留 BES 原值字符串字面量，写回路径 ConstFillMode.TryParseConstValue 兜底接受
            // - 不向 SDK 注册表输出 Unknown/undefined 类型定义，避免掩盖上游错误
            // - 函数注册合法性（包括"未知函数"）由 LintEngine 在更早阶段以 Error 形式报告
            //
            // 【触发条件】这条分支只在未注册函数 + 残留 Enum 参数场景生效。
            // SDK 注册约定：所有 Enum 类型参数都必然带 EnumType（参见 *Definitions.cs 中的
            // MethodParameter(... "Enum", EnumType: BuiltinEnumTypes.XXX)）。BescriptToDeviceTsViewMapper
            // .ResolveExpectedEnumType 中只要 methodSig 为 null（未注册函数）就会让 expectedParam
            // 为 null，进而 expectedEnumType 为 null。已注册函数的 Enum 参数位永远不会走到这里。
            // 因此这是真正的兜底路径——LintEngine 已先一步报"内置方法签名未注册" Error，
            // 本兜底只是确保 MCP 视图重建不会因转换层抛 InvalidOperationException 触发 FailFast。
            return ConstExpressionFillResult.Ok(Call(
                Property(Identifier("BeEnum"), "fromBeConst"),
                [String(value)],
                ["undefined"]));
        }

        if (string.Equals(typeName, nameof(BescriptVarType.Icon), StringComparison.Ordinal) && value.Length > 0)
        {
            return ConstExpressionFillResult.Ok(Call(
                Property(Identifier("BeIcon"), "fromBeConst"),
                [String(value)]));
        }

        string tsTypeName = "Be" + typeName;
        bool isSemanticType = AnyPositionNonEmptyConstTypes.Contains(typeName);
        IReadOnlyList<DeviceTsViewExpression> args = isSemanticType
            ? [String(value)]
            : [];
        return ConstExpressionFillResult.Ok(Call(
            Property(Identifier(tsTypeName), "fromBeConst"),
            args));
    }

    private static DeviceTsViewExpression Identifier(string text)
        => new("identifier", Text: text);

    private static DeviceTsViewExpression String(string text)
        => new("string", Text: text);

    private static DeviceTsViewExpression Template(string text)
        => new("template", Text: text);

    private static DeviceTsViewExpression Property(DeviceTsViewExpression expression, string name)
        => new("propertyAccess", Expression: expression, Name: name);

    private static DeviceTsViewExpression Call(DeviceTsViewExpression expression, IReadOnlyList<DeviceTsViewExpression> args, IReadOnlyList<string>? typeArguments = null)
        => new("call", Expression: expression, Arguments: args, TypeArguments: typeArguments);

    #endregion

    #region 反向转换（TS → BES）

#pragma warning disable TK006 // Canvas 格式转换对，各自封装独立的转换管线
    private static string ParseCanvasHtmlToLegacyInline(string htmlContent)
    {
        // 移除缩进和前后空行
        string html = RemoveIndentFromMultilineText(htmlContent);

        // 转换层保持纯净：仅做无条件互转，不做中间校验
        (CanvasNode root, string canvasName) = CanvasConverter.FromHtmlInline(html);
        return CanvasConverter.ToInlineBase64(root, canvasName);
    }

    private static string ParseCanvasNodeTextToInline(string canvasName, string htmlContent)
    {
        // TS 视图中展示的是 HTML，写回时转为 Hjson 内联格式
        string html = RemoveIndentFromMultilineText(htmlContent);
        (CanvasNode root, _) = CanvasConverter.FromHtmlInline(html);
        return CanvasConverter.ToInlineNodeHjson(root, canvasName);
    }
#pragma warning restore TK006

    #endregion

    #region 辅助方法

    /// <summary>
    /// 移除 HTML 每行的公共缩进前缀，去除前后空行。
    /// </summary>
    private static string RemoveIndentFromMultilineText(string html)
    {
        string[] lines = html.Split('\n');

#pragma warning disable TK011 // intentional: Split('\n') produces empty entries for blank lines
        // 找到有内容的行范围
        int start = 0;
        while (start < lines.Length && lines[start] == string.Empty) start++;
        int end = lines.Length - 1;
        while (end >= start && lines[end] == string.Empty) end--;
        if (start > end) return string.Empty;

        // 计算公共缩进（只看非空行）
        int minIndent = int.MaxValue;
        for (int i = start; i <= end; i++)
        {
            string line = lines[i];
            if (line == string.Empty) continue;
            int spaces = 0;
            while (spaces < line.Length && line[spaces] == ' ') spaces++;
            if (spaces < minIndent) minIndent = spaces;
        }
#pragma warning restore TK011
        // 移除公共缩进
        StringBuilder sb = new();
        for (int i = start; i <= end; i++)
        {
            if (i > start) sb.Append('\n');
            string line = lines[i];
            if (line.Length > minIndent)
                sb.Append(line, minIndent, line.Length - minIndent);
        }
        return sb.ToString();
    }

    #endregion
}
