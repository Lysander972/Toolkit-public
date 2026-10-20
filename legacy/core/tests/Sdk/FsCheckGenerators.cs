using FsCheck;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// 共享的 FsCheck 生成器，用于 SDK 属性测试
/// </summary>
internal static class FsCheckGenerators
{
    /// <summary>
    /// 生成有效的标识符名称（字母开头，字母数字组合）
    /// </summary>
    public static Gen<string> ValidNameGen(int maxLength = 15)
    {
        Gen<char> firstChar = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('A', 'Z').Select(c => (char)c)
        );

        Gen<char[]> restChars = Gen.ArrayOf(Gen.Frequency(
            Tuple.Create(10, Gen.Choose('a', 'z').Select(c => (char)c)),
            Tuple.Create(10, Gen.Choose('A', 'Z').Select(c => (char)c)),
            Tuple.Create(3, Gen.Choose('0', '9').Select(c => (char)c))
        ));

        return from first in firstChar
               from rest in restChars
               let name = first + new string(rest)
               where name.Length > 0 && name.Length < maxLength
               select name;
    }

    /// <summary>
    /// 生成基本类型名称
    /// </summary>
    public static Gen<string> TypeNameGen()
    {
        return Gen.Elements("Float", "Long", "String", "Bool", "List", "Dict", "Any");
    }

    /// <summary>
    /// 生成变量所有者类型名称
    /// </summary>
    public static Gen<string> OwnerTypeGen()
    {
        return Gen.Elements(
            "Float", "Long", "String", "Bool", "List", "Dict", "Struct", "Bytes",
            "Vector3", "Color", "Datetime", "Device", "Mech", "Player", "Canvas",
            "UILabel", "UIButton", "UIRect", "UIInput", "UIRawImage", "UILine", "UICircle", "UIAvatar");
    }

    /// <summary>
    /// 生成方法参数
    /// </summary>
    public static Gen<MethodParameter> ParameterGen()
    {
        return from name in ValidNameGen()
               from typeName in TypeNameGen()
               select new MethodParameter(name, typeName);
    }

    /// <summary>
    /// 生成全局函数签名
    /// </summary>
    public static Gen<MethodSignature> GlobalFuncGen()
    {
        return from name in ValidNameGen()
               from paramCount in Gen.Choose(0, 3)
               from parameters in Gen.ListOf(paramCount, ParameterGen())
               select new MethodSignature(
                   MethodCategory.GlobalFunc,
                   null,
                   name,
                   parameters.ToArray());
    }

    /// <summary>
    /// 生成变量方法签名
    /// </summary>
    public static Gen<MethodSignature> VarFuncGen()
    {
        return from name in ValidNameGen()
               from ownerType in Gen.Elements("Float", "Long", "String", "List", "Dict")
               from paramCount in Gen.Choose(0, 2)
               from parameters in Gen.ListOf(paramCount, ParameterGen())
               select new MethodSignature(
                   MethodCategory.VarFunc,
                   ownerType,
                   name,
                   parameters.ToArray());
    }
}
