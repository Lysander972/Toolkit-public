using FsCheck;
using FsCheck.Xunit;
using Xunit;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.Tests.Sdk;

/// <summary>
/// BuiltinMethodRegistry 属性测试
/// </summary>
public class BuiltinMethodRegistryPropertyTests
{
    /// <summary>
    /// **Feature: builtin-method-registry, Property 4: 重复注册检测**
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DuplicateRegistration_ShouldOverwrite_AndKeepOnlyOne()
    {
        return Prop.ForAll(FsCheckGenerators.GlobalFuncGen().ToArbitrary(), static sig =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();

            // 注册第一次
            registry.Register(sig);
            int countAfterFirst = registry.Count;

            // 创建同名但不同参数的方法
            MethodSignature sig2 = new MethodSignature(
                sig.Category,
                sig.OwnerType,
                sig.MethodName,
                new[] { new MethodParameter("newParam", "String") });

            // 注册第二次（同路径）
            registry.Register(sig2);
            int countAfterSecond = registry.Count;

            // 应该只有一个方法
            return countAfterFirst == 1 && countAfterSecond == 1;
        });
    }

    [Property(MaxTest = 100)]
    public Property DuplicateRegistration_ShouldReturnLatestVersion()
    {
        return Prop.ForAll(FsCheckGenerators.GlobalFuncGen().ToArbitrary(), static sig =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();

            registry.Register(sig);

            // 创建同名但带有 Summary 的新版本
            MethodSignature sig2 = sig with { Summary = "Updated version" };
            registry.Register(sig2);

            MethodSignature? retrieved = registry.GetByPath(sig.GetFullPath());
            return retrieved?.Summary == "Updated version";
        });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 5: 查询过滤正确性**
    /// **Validates: Requirements 2.5, 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Query_ByCategory_ShouldReturnOnlyMatchingCategory()
    {
        return Prop.ForAll(
            Gen.ListOf(5, Gen.OneOf(FsCheckGenerators.GlobalFuncGen(), FsCheckGenerators.VarFuncGen())).ToArbitrary(),
            static sigs =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                foreach (MethodSignature? sig in sigs)
                {
                    registry.Register(sig);
                }

                MethodQueryResult result = registry.Query(category: MethodCategory.GlobalFunc);
                return result.Items.All(static s => s.Category == MethodCategory.GlobalFunc);
            });
    }

    [Property(MaxTest = 100)]
    public Property Query_ByOwnerType_ShouldReturnOnlyMatchingOwnerType()
    {
        return Prop.ForAll(
            Gen.ListOf(5, FsCheckGenerators.VarFuncGen()).ToArbitrary(),
            static sigs =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                foreach (MethodSignature? sig in sigs)
                {
                    registry.Register(sig);
                }

                MethodQueryResult result = registry.Query(ownerType: "List");
                return result.Items.All(static s =>
                    s.OwnerType is not null &&
                    s.OwnerType.Contains("List", StringComparison.Ordinal));
            });
    }

    [Property(MaxTest = 100)]
    public Property Query_ByNamePattern_ShouldReturnOnlyMatchingNames()
    {
        return Prop.ForAll(FsCheckGenerators.GlobalFuncGen().ToArbitrary(), sig =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
            registry.Register(sig);

            // 使用方法名的前两个字符作为模式，不足两个则用全名
            int patLen = Math.Min(2, sig.MethodName.Length);
            string pattern = sig.MethodName[..patLen];

            MethodQueryResult result = registry.Query(namePattern: pattern);
            return result.Items.All(s =>
                s.MethodName.Contains(pattern, StringComparison.Ordinal) ||
                s.GetFullPath().Contains(pattern, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 6: 查询结果排序**
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Query_Results_ShouldBeSortedByCategoryThenOwnerTypeThenName()
    {
        return Prop.ForAll(
            Gen.ListOf(10, Gen.OneOf(FsCheckGenerators.GlobalFuncGen(), FsCheckGenerators.VarFuncGen())).ToArbitrary(),
            static sigs =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                foreach (MethodSignature? sig in sigs)
                {
                    registry.Register(sig);
                }

                MethodQueryResult result = registry.Query();
                List<MethodSignature> items = result.Items.ToList();

                for (int i = 1; i < items.Count; i++)
                {
                    MethodSignature prev = items[i - 1];
                    MethodSignature curr = items[i];

                    // 先按 Category 排序
                    if (prev.Category > curr.Category)
                        return false;

                    if (prev.Category == curr.Category)
                    {
                        string? prevOwner = prev.OwnerType;
                        string? currOwner = curr.OwnerType;
                        // 使用 Ordinal 比较（与实现一致）
                        int ownerCompare = string.Compare(prevOwner, currOwner, StringComparison.Ordinal);

                        // 再按 OwnerType 排序
                        if (ownerCompare > 0)
                            return false;

                        // 最后按 MethodName 排序
                        if (ownerCompare == 0)
                        {
                            // 使用 Ordinal 比较（与实现一致）
                            int nameCompare = string.Compare(prev.MethodName, curr.MethodName, StringComparison.Ordinal);
                            if (nameCompare > 0)
                                return false;
                        }
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 7: GetByPath 行为一致性**
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetByPath_RegisteredMethod_ShouldReturnMethod()
    {
        return Prop.ForAll(
            Gen.OneOf(FsCheckGenerators.GlobalFuncGen(), FsCheckGenerators.VarFuncGen()).ToArbitrary(),
            static sig =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                registry.Register(sig);

                MethodSignature? retrieved = registry.GetByPath(sig.GetFullPath());
                return retrieved is not null && retrieved.MethodName == sig.MethodName;
            });
    }

    [Property(MaxTest = 100)]
    public Property GetByPath_UnregisteredMethod_ShouldReturnNull()
    {
        return Prop.ForAll(FsCheckGenerators.ValidNameGen().ToArbitrary(), static name =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
            MethodSignature? retrieved = registry.GetByPath(name);
            return retrieved is null;
        });
    }

    [Property(MaxTest = 100)]
    public Property GetByPath_EmptyOrNull_ShouldReturnNull()
    {
        return Prop.ForAll(
            Gen.Elements(string.Empty, "   ").ToArbitrary(),
            static path =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodSignature? retrieved = registry.GetByPath(path);
                return retrieved is null;
            });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 8: 类别统计准确性**
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetCategoryCounts_SumShouldEqualTotalCount()
    {
        return Prop.ForAll(
            Gen.ListOf(10, Gen.OneOf(FsCheckGenerators.GlobalFuncGen(), FsCheckGenerators.VarFuncGen())).ToArbitrary(),
            static sigs =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                foreach (MethodSignature? sig in sigs)
                {
                    registry.Register(sig);
                }

                IReadOnlyDictionary<MethodCategory, int> counts = registry.GetCategoryCounts();
                int sum = counts.Values.Sum();
                return sum == registry.Count;
            });
    }

    [Property(MaxTest = 100)]
    public Property GetCategoryCounts_ShouldMatchActualCounts()
    {
        return Prop.ForAll(
            Gen.ListOf(10, Gen.OneOf(FsCheckGenerators.GlobalFuncGen(), FsCheckGenerators.VarFuncGen())).ToArbitrary(),
            static sigs =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                foreach (MethodSignature? sig in sigs)
                {
                    registry.Register(sig);
                }

                IReadOnlyDictionary<MethodCategory, int> counts = registry.GetCategoryCounts();
                MethodQueryResult queryResult = registry.Query(category: MethodCategory.GlobalFunc);

                return counts[MethodCategory.GlobalFunc] == queryResult.Total;
            });
    }
}


/// <summary>
/// ValidateCall 属性测试
/// **Feature: builtin-method-registry, Property 9: ValidateCall 验证逻辑**
/// **Validates: Requirements 5.2, 5.3, 5.4, 5.5**
/// </summary>
public class ValidateCallPropertyTests
{
    [Property(MaxTest = 100)]
    public Property ValidateCall_UnknownPath_ShouldReturnError()
    {
        return Prop.ForAll(FsCheckGenerators.ValidNameGen().ToArbitrary(), static name =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
            MethodValidationResult result = registry.ValidateCall(name, Array.Empty<string>());
            return !result.IsValid && result.ErrorMessage?.Contains("未知方法") == true;
        });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_CorrectArgCount_ShouldBeValid()
    {
        return Prop.ForAll(
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            Gen.Choose(0, 3).ToArbitrary(),
            static (name, paramCount) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodParameter[] parameters = Enumerable.Range(0, paramCount)
                    .Select(static i => new MethodParameter($"p{i}", "Float"))
                    .ToArray();

                MethodSignature sig = new MethodSignature(
                    MethodCategory.GlobalFunc,
                    null,
                    name,
                    parameters);

                registry.Register(sig);

                string[] argTypes = Enumerable.Range(0, paramCount)
                    .Select(static _ => "Float")
                    .ToArray();

                MethodValidationResult result = registry.ValidateCall(name, argTypes);
                return result.IsValid;
            });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_MissingArgs_ShouldReturnError()
    {
        return Prop.ForAll(
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            Gen.Choose(2, 4).ToArbitrary(),
            static (name, paramCount) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodParameter[] parameters = Enumerable.Range(0, paramCount)
                    .Select(static i => new MethodParameter($"p{i}", "Float"))
                    .ToArray();

                MethodSignature sig = new MethodSignature(
                    MethodCategory.GlobalFunc,
                    null,
                    name,
                    parameters);

                registry.Register(sig);

                // 传入比需要少的参数 - 应该返回错误（参数数量不匹配）
                string[] argTypes = new[] { "Float" };

                MethodValidationResult result = registry.ValidateCall(name, argTypes);
                return !result.IsValid && result.ErrorMessage?.Contains("参数数量不匹配") == true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_TooManyArgs_ShouldReturnError()
    {
        return Prop.ForAll(
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            Gen.Choose(1, 2).ToArbitrary(),
            static (name, paramCount) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodParameter[] parameters = Enumerable.Range(0, paramCount)
                    .Select(static i => new MethodParameter($"p{i}", "Float"))
                    .ToArray();

                MethodSignature sig = new MethodSignature(
                    MethodCategory.GlobalFunc,
                    null,
                    name,
                    parameters);

                registry.Register(sig);

                // 传入比需要多的参数 - 应该返回错误（参数数量不匹配）
                string[] argTypes = Enumerable.Range(0, paramCount + 3)
                    .Select(static _ => "Float")
                    .ToArray();

                MethodValidationResult result = registry.ValidateCall(name, argTypes);
                return !result.IsValid && result.ErrorMessage?.Contains("参数数量不匹配") == true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_FewerArgs_ShouldReturnError()
    {
        return Prop.ForAll(FsCheckGenerators.ValidNameGen().ToArbitrary(), static name =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
            MethodParameter[] parameters = new[]
            {
                new MethodParameter("required", "Float"),
                new MethodParameter("second", "String")
            };

            MethodSignature sig = new MethodSignature(
                MethodCategory.GlobalFunc,
                null,
                name,
                parameters);

            registry.Register(sig);

            // 只传一个参数（而需要两个）- 应该返回错误
            MethodValidationResult result = registry.ValidateCall(name, new[] { "Float" });
            return !result.IsValid && result.ErrorMessage?.Contains("参数数量不匹配") == true;
        });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_TypeMismatch_ShouldReturnWarning()
    {
        return Prop.ForAll(FsCheckGenerators.ValidNameGen().ToArbitrary(), static name =>
        {
            BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
            MethodSignature sig = new MethodSignature(
                MethodCategory.GlobalFunc,
                null,
                name,
                new[] { new MethodParameter("num", "Float") });

            registry.Register(sig);

            // 传入错误类型
            MethodValidationResult result = registry.ValidateCall(name, new[] { "String" });
            return result.IsValid && result.Warnings.Count > 0;
        });
    }

    [Property(MaxTest = 100)]
    public Property ValidateCall_AnyType_ShouldAcceptAllTypes()
    {
        return Prop.ForAll(
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            FsCheckGenerators.TypeNameGen().ToArbitrary(),
            static (name, argType) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodSignature sig = new MethodSignature(
                    MethodCategory.GlobalFunc,
                    null,
                    name,
                    new[] { new MethodParameter("data", "Any") });

                registry.Register(sig);

                MethodValidationResult result = registry.ValidateCall(name, new[] { argType });
                return result.IsValid && result.Warnings.Count == 0;
            });
    }
}


/// <summary>
/// 注册关联一致性属性测试
/// **Feature: builtin-method-registry, Property 3: 注册关联一致性**
/// **Validates: Requirements 2.2, 2.3**
/// </summary>
public class RegistrationConsistencyPropertyTests
{
    [Fact]
    public void Instance_ShouldHaveBuiltinMethods()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        Assert.True(registry.Count > 0, "注册表应包含内置方法");
    }

    [Fact]
    public void Instance_ShouldHaveAllCategories()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        IReadOnlyDictionary<MethodCategory, int> counts = registry.GetCategoryCounts();

        Assert.True(counts[MethodCategory.GlobalFunc] > 0, "应包含全局函数");
        Assert.True(counts[MethodCategory.VarFunc] > 0, "应包含变量方法");
        Assert.True(counts[MethodCategory.DeviceAction] > 0, "应包含零件动作");
        Assert.True(counts[MethodCategory.EditorFunc] > 0, "应包含编辑器函数");
    }

    [Fact]
    public void VarFunc_ShouldHaveOwnerType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult varFuncs = registry.Query(category: MethodCategory.VarFunc, take: 1000);

        foreach (MethodSignature sig in varFuncs.Items)
        {
            Assert.NotNull(sig.OwnerType);
            Assert.NotEmpty(sig.OwnerType);
        }
    }

    [Fact]
    public void DeviceAction_ShouldHaveOwnerType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult actions = registry.Query(category: MethodCategory.DeviceAction, take: 1000);

        foreach (MethodSignature sig in actions.Items)
        {
            Assert.NotNull(sig.OwnerType);
            Assert.NotEmpty(sig.OwnerType);
        }
    }

    [Fact]
    public void GlobalFunc_ShouldNotHaveOwnerType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult funcs = registry.Query(category: MethodCategory.GlobalFunc, take: 1000);

        foreach (MethodSignature sig in funcs.Items)
        {
            Assert.Null(sig.OwnerType);
        }
    }

    [Fact]
    public void Query_ByOwnerType_ShouldReturnConsistentResults()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        // 查询 List 类型的方法
        MethodQueryResult listMethods = registry.Query(ownerType: "List", take: 1000);

        foreach (MethodSignature sig in listMethods.Items)
        {
            Assert.Contains("List", sig.OwnerType, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GetByPath_RegisteredMethods_ShouldBeRetrievable()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        // 测试一些已知的内置方法
        MethodSignature? debugPrint = registry.GetByPath("debug.print");
        Assert.NotNull(debugPrint);
        Assert.Equal("debug.print", debugPrint.MethodName);

        MethodSignature? listAdd = registry.GetByPath("List.add");
        Assert.NotNull(listAdd);
        Assert.Equal("add", listAdd.MethodName);
        Assert.Equal("List", listAdd.OwnerType);
    }

    [Fact]
    public void CreateMechFuncs_PosType_ShouldBindToCreateMechPositionAlignment()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        MethodSignature? createPlayerMech = registry.GetByPath("createPlayerMech", MethodCategory.GlobalFunc);
        Assert.NotNull(createPlayerMech);
        Assert.Equal(BuiltinEnumTypes.CreateMechPositionAlignment, createPlayerMech.Parameters[4].EnumType);

        MethodSignature? createAIMech = registry.GetByPath("createAIMech", MethodCategory.GlobalFunc);
        Assert.NotNull(createAIMech);
        Assert.Equal(BuiltinEnumTypes.CreateMechPositionAlignment, createAIMech.Parameters[4].EnumType);
    }

    [Fact]
    public void UiSetPixelPos_ShouldBindToUiPixelAlignment()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;

        MethodSignature? setPixelPos = registry.GetByPath("UI.setPixelPos", MethodCategory.VarFunc);
        Assert.NotNull(setPixelPos);
        Assert.Equal(BuiltinEnumTypes.UiPixelAlignment, setPixelPos.Parameters[1].EnumType);
    }

    [Fact]
    public void BuiltinEnumParameters_ShouldAlwaysDeclareConcreteEnumType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult allMethods = registry.Query(take: 5000);

        foreach (MethodSignature sig in allMethods.Items)
        {
            foreach (MethodParameter? param in sig.Parameters.Where(static p => p.TypeName == "Enum"))
            {
                Assert.NotNull(param.EnumType);
                Assert.NotEqual(string.Empty, param.EnumType.Name);
            }
        }
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 3: 注册关联一致性**
    /// 对于任意注册的 VarFunc 方法，通过 OwnerType 查询应能找到该方法
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VarFunc_QueryByOwnerType_ShouldFindRegisteredMethod()
    {
        return Prop.ForAll(
            FsCheckGenerators.OwnerTypeGen().ToArbitrary(),
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            (ownerType, methodName) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodSignature sig = new MethodSignature(
                    MethodCategory.VarFunc,
                    ownerType,
                    methodName,
                    Array.Empty<MethodParameter>());

                registry.Register(sig);

                // 通过 OwnerType 查询
                MethodQueryResult result = registry.Query(ownerType: ownerType);

                // 应该能找到注册的方法
                return result.Items.Any(s =>
                    s.MethodName == methodName &&
                    s.OwnerType?.Equals(ownerType, StringComparison.Ordinal) == true);
            });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 3: 注册关联一致性**
    /// 对于任意注册的 DeviceAction 方法，通过 OwnerType 查询应能找到该方法
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DeviceAction_QueryByOwnerType_ShouldFindRegisteredMethod()
    {
        return Prop.ForAll(
            Gen.Elements("Device", "CoreDevice", "CubeDevice", "WheelDevice", "WeaponDevice").ToArbitrary(),
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            (ownerType, methodName) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodSignature sig = new MethodSignature(
                    MethodCategory.DeviceAction,
                    ownerType,
                    methodName,
                    Array.Empty<MethodParameter>());

                registry.Register(sig);

                // 通过 OwnerType 查询
                MethodQueryResult result = registry.Query(ownerType: ownerType);

                // 应该能找到注册的方法
                return result.Items.Any(s =>
                    s.MethodName == methodName &&
                    s.OwnerType?.Equals(ownerType, StringComparison.Ordinal) == true);
            });
    }

    /// <summary>
    /// **Feature: builtin-method-registry, Property 3: 注册关联一致性**
    /// 注册的方法的 OwnerType 字段应与注册时指定的类型一致
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredMethod_OwnerType_ShouldMatchRegistration()
    {
        return Prop.ForAll(
            FsCheckGenerators.OwnerTypeGen().ToArbitrary(),
            FsCheckGenerators.ValidNameGen().ToArbitrary(),
            static (ownerType, methodName) =>
            {
                BuiltinMethodRegistry registry = BuiltinMethodRegistry.CreateEmpty();
                MethodSignature sig = new MethodSignature(
                    MethodCategory.VarFunc,
                    ownerType,
                    methodName,
                    Array.Empty<MethodParameter>());

                registry.Register(sig);

                // 通过完整路径获取
                string path = $"{ownerType}.{methodName}";
                MethodSignature? retrieved = registry.GetByPath(path);

                // OwnerType 应该一致
                return retrieved is not null &&
                       retrieved.OwnerType?.Equals(ownerType, StringComparison.Ordinal) == true;
            });
    }

    /// <summary>
    /// 验证内置方法定义中的 VarFunc 都能通过 OwnerType 查询到
    /// </summary>
    [Fact]
    public void BuiltinVarFuncs_ShouldBeQueryableByOwnerType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult allVarFuncs = registry.Query(category: MethodCategory.VarFunc, take: 2000);

        // 按 OwnerType 分组
        IEnumerable<IGrouping<string?, MethodSignature>> byOwnerType = allVarFuncs.Items.GroupBy(s => s.OwnerType);

        foreach (IGrouping<string?, MethodSignature> group in byOwnerType)
        {
            if (group.Key is null)
                throw new InvalidOperationException("OwnerType 为空");
            string ownerType = group.Key;
            MethodQueryResult queryResult = registry.Query(ownerType: ownerType, take: 1000);

            // 查询结果应该包含该 OwnerType 的所有方法
            foreach (MethodSignature? sig in group)
            {
                Assert.Contains(queryResult.Items, s =>
                    s.MethodName == sig.MethodName &&
                    s.OwnerType == sig.OwnerType);
            }
        }
    }

    /// <summary>
    /// 验证内置方法定义中的 DeviceAction 都能通过 OwnerType 查询到
    /// </summary>
    [Fact]
    public void BuiltinDeviceActions_ShouldBeQueryableByOwnerType()
    {
        BuiltinMethodRegistry registry = BuiltinMethodRegistry.Instance;
        MethodQueryResult allActions = registry.Query(category: MethodCategory.DeviceAction, take: 1000);

        // 按 OwnerType 分组
        IEnumerable<IGrouping<string?, MethodSignature>> byOwnerType = allActions.Items.GroupBy(s => s.OwnerType);

        foreach (IGrouping<string?, MethodSignature> group in byOwnerType)
        {
            if (group.Key is null)
                throw new InvalidOperationException("OwnerType 为空");
            string ownerType = group.Key;
            MethodQueryResult queryResult = registry.Query(category: MethodCategory.DeviceAction, ownerType: ownerType, take: 1000);

            // 查询结果应该包含该 OwnerType 的所有方法
            foreach (MethodSignature? sig in group)
            {
                Assert.Contains(queryResult.Items, s =>
                    s.MethodName == sig.MethodName &&
                    s.OwnerType == sig.OwnerType);
            }
        }
    }
}
