using FsCheck;
using FsCheck.Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// **Feature: builtin-method-registry, Property 1: 数据模型完整性**
/// **Validates: Requirements 1.1, 1.2, 1.4**
/// </summary>
public class BuiltinMethodModelsPropertyTests
{
    /// <summary>
    /// 生成有效的方法名（字母或下划线开头，仅包含字母、数字、下划线）
    /// </summary>
    private static Arbitrary<string> ValidMethodNameArb()
    {
        Gen<char> firstChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Constant('_')
        );

        Gen<char[]> restChars = Gen.ArrayOf(Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Choose('0', '9').Select(c => (char)c),
            Gen.Constant('_')
        ));

        return Arb.From(
            from first in firstChar
            from rest in restChars
            select first + new string(rest)
        );
    }

    /// <summary>
    /// 生成无效的方法名（数字开头或包含非法字符）
    /// </summary>
    private static Arbitrary<string> InvalidMethodNameArb()
    {
        return Arb.From(Gen.OneOf(
            // 数字开头
            from digit in Gen.Choose('0', '9').Select(static c => (char)c)
            from rest in Gen.ArrayOf(Gen.Choose('a', 'z').Select(static c => (char)c))
            select digit + new string(rest),
            // 包含非法字符
            from valid in Gen.Elements("test", "method", "func")
            from invalid in Gen.Elements("!", "@", "#", "$", "%", " ", "-")
            select valid + invalid + "name",
            // 空字符串
            Gen.Constant(string.Empty),
            // 纯空白
            Gen.Constant("   ")
        ));
    }

    /// <summary>
    /// 生成有效的"全名"（允许点号/斜杠分隔）
    /// 例如: map.maxPlayer / map.team.autoBalance / ui.2dmode / plane/raycast
    /// </summary>
    private static Arbitrary<string> ValidFullMethodNameArb()
    {
        Gen<char> firstChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Constant('_')
        );

        Gen<char[]> restChars = Gen.ArrayOf(Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Choose('0', '9').Select(c => (char)c),
            Gen.Constant('_')
        ));

        Gen<string> firstSeg = from first in firstChar
                               from rest in restChars
                               select first + new string(rest);

        // 后续段允许数字开头（如 ui.2dmode）
        Gen<char> segChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c),
            Gen.Choose('0', '9').Select(c => (char)c),
            Gen.Constant('_')
        );
        Gen<string> nextSeg = Gen.NonEmptyListOf(segChar).Select(list => new string(list.ToArray()));

        Gen<char> sep = Gen.Elements('.', '/');

        Gen<string> twoSeg = from a in firstSeg
                             from s in sep
                             from b in nextSeg
                             select $"{a}{s}{b}";

        Gen<string> threeSeg = from a in firstSeg
                               from s1 in sep
                               from b in nextSeg
                               from s2 in sep
                               from c in nextSeg
                               select $"{a}{s1}{b}{s2}{c}";

        return Arb.From(Gen.OneOf(twoSeg, threeSeg));
    }

    [Property(MaxTest = 100)]
    public Property ValidMethodName_ShouldPassValidation()
    {
        return Prop.ForAll(ValidMethodNameArb(), static name =>
            MethodSignature.IsValidMethodName(name));
    }

    [Property(MaxTest = 100)]
    public Property ValidFullMethodName_ShouldPassValidation()
    {
        return Prop.ForAll(ValidFullMethodNameArb(), static name =>
            MethodSignature.IsValidMethodName(name));
    }


    [Property(MaxTest = 100)]
    public Property InvalidMethodName_ShouldFailValidation()
    {
        return Prop.ForAll(InvalidMethodNameArb(), static name =>
            !MethodSignature.IsValidMethodName(name));
    }

    [Property(MaxTest = 100)]
    public Property MethodParameter_WithValidFields_ShouldBeValid()
    {
        return Prop.ForAll(
            ValidMethodNameArb(),
            Gen.Elements("Float", "Long", "String", "Bool", "List", "Dict", "Any").ToArbitrary(),
            static (name, typeName) =>
            {
                MethodParameter param = new MethodParameter(name, typeName);
                return param.IsValid();
            });
    }

    [Property(MaxTest = 100)]
    public Property MethodParameter_WithEmptyTypeName_ShouldBeValid()
    {
        // IsValid 不校验 TypeName 是否为空
        return Prop.ForAll(
            ValidMethodNameArb(),
            Gen.Elements(string.Empty, "   ").ToArbitrary(),
            static (name, typeName) =>
            {
                MethodParameter param = new MethodParameter(name, typeName);
                return param.IsValid();
            });
    }

    [Property(MaxTest = 100)]
    public Property MethodSignature_GlobalFunc_WithValidName_ShouldBeValid()
    {
        return Prop.ForAll(ValidMethodNameArb(), static name =>
        {
            MethodSignature sig = new MethodSignature(
                MethodCategory.GlobalFunc,
                null,
                name,
                Array.Empty<MethodParameter>());
            return sig.IsValid();
        });
    }

    [Property(MaxTest = 100)]
    public Property MethodSignature_VarFunc_WithoutOwnerType_ShouldBeInvalid()
    {
        return Prop.ForAll(ValidMethodNameArb(), static name =>
        {
            MethodSignature sig = new MethodSignature(
                MethodCategory.VarFunc,
                null,
                name,
                Array.Empty<MethodParameter>());
            return !sig.IsValid();
        });
    }

    [Property(MaxTest = 100)]
    public Property MethodSignature_VarFunc_WithOwnerType_ShouldBeValid()
    {
        return Prop.ForAll(
            ValidMethodNameArb(),
            Gen.Elements("Float", "Long", "String", "List", "Dict", "Device", "Mech").ToArbitrary(),
            static (name, ownerType) =>
            {
                MethodSignature sig = new MethodSignature(
                    MethodCategory.VarFunc,
                    ownerType,
                    name,
                    Array.Empty<MethodParameter>());
                return sig.IsValid();
            });
    }

    [Property(MaxTest = 100)]
    public Property MethodSignature_DeviceAction_WithoutOwnerType_ShouldBeInvalid()
    {
        return Prop.ForAll(ValidMethodNameArb(), static name =>
        {
            MethodSignature sig = new MethodSignature(
                MethodCategory.DeviceAction,
                null,
                name,
                Array.Empty<MethodParameter>());
            return !sig.IsValid();
        });
    }

    [Property(MaxTest = 100)]
    public Property MethodSignature_DeviceAction_WithOwnerType_ShouldBeValid()
    {
        return Prop.ForAll(
            ValidMethodNameArb(),
            Gen.Elements("Device", "CoreDevice", "CubeDevice").ToArbitrary(),
            static (name, ownerType) =>
            {
                MethodSignature sig = new MethodSignature(
                    MethodCategory.DeviceAction,
                    ownerType,
                    name,
                    Array.Empty<MethodParameter>());
                return sig.IsValid();
            });
    }
}
