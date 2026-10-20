using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// **Feature: builtin-method-registry, Property 10: 签名格式化输出**
/// **Validates: Requirements 6.1, 6.2, 6.3**
/// </summary>
public class MethodSignatureExtensionsPropertyTests
{
    private static Gen<string> ValidNameGen()
    {
        Gen<char> firstChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c)
        );

        Gen<char[]> restChars = Gen.ArrayOf(Gen.Frequency(
            Tuple.Create(10, Gen.Choose('a', 'z').Select(c => (char)c)),
            Tuple.Create(10, Gen.Choose('A', 'Z').Select(c => (char)c)),
            Tuple.Create(5, Gen.Choose('0', '9').Select(c => (char)c))
        ));

        return from first in firstChar
               from rest in restChars
               select first + new string(rest);
    }

#pragma warning disable TK006 // 属性测试：类型名生成器
    private static Gen<string> TypeNameGen()
    {
        return Gen.Elements("Float", "Long", "String", "Bool", "List", "Dict", "Any", "Vector3");
    }
#pragma warning restore TK006

    private static Gen<MethodParameter> ParameterGen()
    {
        return from name in ValidNameGen()
               from typeName in TypeNameGen()
               select new MethodParameter(name, typeName);
    }

    private static Gen<MethodSignature> GlobalFuncGen()
    {
        return from name in ValidNameGen()
               from paramCount in Gen.Choose(0, 4)
               from parameters in Gen.ListOf(paramCount, ParameterGen())
               from returnType in Gen.OneOf(Gen.Constant<string?>(null), TypeNameGen().Select(static t => (string?)t))
               select new MethodSignature(
                   MethodCategory.GlobalFunc,
                   null,
                   name,
                   parameters.ToArray(),
                   returnType);
    }

    private static Gen<MethodSignature> VarFuncGen()
    {
        return from name in ValidNameGen()
               from ownerType in Gen.Elements("Float", "Long", "String", "List", "Dict", "Device", "Mech")
               from paramCount in Gen.Choose(0, 3)
               from parameters in Gen.ListOf(paramCount, ParameterGen())
               from returnType in Gen.OneOf(Gen.Constant<string?>(null), TypeNameGen().Select(static t => (string?)t))
               select new MethodSignature(
                   MethodCategory.VarFunc,
                   ownerType,
                   name,
                   parameters.ToArray(),
                   returnType);
    }

    private static Gen<MethodSignature> DeviceActionGen()
    {
        return from name in ValidNameGen()
               from ownerType in Gen.Elements("Device", "CoreDevice", "CubeDevice")
               from paramCount in Gen.Choose(0, 3)
               from parameters in Gen.ListOf(paramCount, ParameterGen())
               select new MethodSignature(
                   MethodCategory.DeviceAction,
                   ownerType,
                   name,
                   parameters.ToArray());
    }

    [Property(MaxTest = 100)]
    public Property GetFullPath_GlobalFunc_ShouldReturnMethodName()
    {
        return Prop.ForAll(GlobalFuncGen().ToArbitrary(), static sig =>
        {
            string path = sig.GetFullPath();
            return path == sig.MethodName;
        });
    }

    [Property(MaxTest = 100)]
    public Property GetFullPath_VarFunc_ShouldIncludeOwnerType()
    {
        return Prop.ForAll(VarFuncGen().ToArbitrary(), static sig =>
        {
            string path = sig.GetFullPath();
            return path == $"{sig.OwnerType}.{sig.MethodName}";
        });
    }

    [Property(MaxTest = 100)]
    public Property ToDisplayString_ShouldContainMethodName()
    {
        return Prop.ForAll(
            Gen.OneOf(GlobalFuncGen(), VarFuncGen(), DeviceActionGen()).ToArbitrary(),
            static sig =>
            {
                string display = sig.ToDisplayString();
                return display.Contains(sig.MethodName);
            });
    }

    [Property(MaxTest = 100)]
    public Property ToDisplayString_ShouldContainAllParameters()
    {
        return Prop.ForAll(
            Gen.OneOf(GlobalFuncGen(), VarFuncGen()).ToArbitrary(),
            sig =>
            {
                string display = sig.ToDisplayString();
                return sig.Parameters.All(p => display.Contains(p.EffectiveName) && display.Contains(p.TypeName));
            });
    }

    [Property(MaxTest = 100)]
    public Property ToDisplayString_WithReturnType_ShouldContainArrow()
    {
        return Prop.ForAll(
            GlobalFuncGen().Where(static s => s.ReturnType != null).ToArbitrary(),
            static sig =>
            {
                string display = sig.ToDisplayString();
                if (sig.ReturnType is null)
                    throw new InvalidOperationException("ReturnType 为空");
                return display.Contains(" -> ") && display.Contains(sig.ReturnType);
            });
    }

    [Fact]
    public void EnumDisplay_ShouldUseEnumGroup()
    {
        MethodSignature sig = new MethodSignature(
            MethodCategory.GlobalFunc,
            null,
            "mouse.lock",
            new[]
            {
                new MethodParameter("type", "Enum", EnumType: BuiltinEnumTypes.MouseLockMode)
            });

        string display = sig.ToDisplayString();
        string doc = sig.GetDocumentation();

        Assert.Contains("Enum<MouseLockMode>", display);
        Assert.Contains("鼠标锁定模式", doc);
        Assert.Contains("可选值: None, lock, Confined", doc);
    }
}
