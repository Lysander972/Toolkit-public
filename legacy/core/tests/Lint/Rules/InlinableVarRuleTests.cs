using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Lint;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Lint.Rules;

public sealed class InlinableVarRuleTests
{
    private static ScriptAnalysisService CreateAnalysisService(DeviceEntity device)
    {
        using ProjectModel model = ProjectModel.CreateForTesting();
        ProjectEntity? current = device;
        while (current is ChildProjectEntity child)
            current = child.Parent;

        if (current is ProjectRootEntity root)
            model.SetRootForTesting(root);

        return new ScriptAnalysisService(model);
    }

    private static DeviceEntity CreateDeviceWithScript(string bodyLines)
    {
        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");

        device.SetVarsInternal(new VarsConfig
        {
            Variables =
            [
                new("HP", "Float", null),
                new("Name", "String", null)
            ]
        });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        scriptGroup.SetScriptEntriesInternal([new("A", string.Empty, false)]);

        string sourceText = "begincodeV2\nname:A\nlines:\n" + bodyLines + "\nendcode";
        TestEntityFactory.CreateInlineScript(scriptGroup, "A", sourceText);

        return device;
    }

    [Fact]
    public void Should_Warn_When_CreateFloat_ConstArg_UsedInFloatAbs()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)\n" +
            "fun float.abs(var x)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
        Assert.Equal(LintSeverity.Warning, diagnostics[0].Severity);
        Assert.Contains("x", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("Float 5", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Warn_When_CreateLong_ConstArg_UsedInLongAbs()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long l=fun create.long(Long 42)\n" +
            "fun long.abs(var l)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
        Assert.Contains("l", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Warn_When_CreateBool_ConstArg_UsedInBoolNot()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Bool b=fun create.bool(Bool 1)\n" +
            "fun bool.not(var b)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
    }

    [Fact]
    public void Should_Warn_When_CreateColor_ConstArg_UsedInDebugSetColor()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Color c=fun create.color(Color FF0000)\n" +
            "fun debug.setColor(var c)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
    }

    [Fact]
    public void Should_Warn_When_CreateString_ConstArg_UsedInGetCustomFloat()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "String s=fun create.string(String abc)\n" +
            "fun map.getCustomFloat(var s)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
    }

    [Fact]
    public void Should_NotWarn_When_UsedInAnyParam()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)\n" +
            "fun create.list1(var x)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_NotWarn_When_MultiAssignment()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)\n" +
            "Float x=fun create.float(Float 10)\n" +
            "fun float.abs(var x)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_NotWarn_When_CreateArgIsVariable()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float y=fun create.float(Float 5)\n" +
            "Float x=fun create.float(var y)\n" +
            "fun float.abs(var x)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        // y can be inlined, but x cannot (its create arg is variable)
        // Only y should be flagged
        Assert.Single(diagnostics);
        Assert.Contains("y", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotWarn_When_ZeroUsages()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarUnused, diagnostics[0].RuleId);
    }

    [Fact]
    public void Should_NotWarn_When_ConditionConstMustVar_Context()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Bool b=fun create.bool(Bool 1)\n" +
            "if{fun bool.not(var b)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        // b is used in bool.not inside if condition, must be var
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_Warn_When_CreatVariableVector3_ConstArgs_UsedInGravity()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Vector3 v=fun creatVariable.Vector3(Float 1:Float 2:Float 3)\n" +
            "fun gravity(var v)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
        Assert.Contains("v", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("Vector3", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Warn_StringConst_When_CreatVariableVector3_ConstArgs_UsedInCreateString()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Vector3 v=fun creatVariable.Vector3(Float 1:Float 2:Float 3)\n" +
            "String s=fun create.string(var v)\n" +
            "fun debug.print(var s:Color 255,255,255,255:Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        LintDiagnostic diag = Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diag.RuleId);
        Assert.Contains("v", diag.Message, StringComparison.Ordinal);
        Assert.Contains("String 1,2,3", diag.Message, StringComparison.Ordinal);
        Assert.Contains("G.create.string", diag.Message, StringComparison.Ordinal);
        Assert.Contains("BeString.fromBeConst(\"1,2,3\")", diag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Warn_StringConst_When_CreateString_ConstArg_UsedInCreateString()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "String v=fun create.string(String abc)\n" +
            "String s=fun create.string(var v)\n" +
            "fun debug.print(var s:Color 255,255,255,255:Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        LintDiagnostic diag = Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diag.RuleId);
        Assert.Contains("String abc", diag.Message, StringComparison.Ordinal);
        Assert.Contains("BeString.fromBeConst(\"abc\")", diag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotWarn_StringConst_When_CreateString_IsNotOnlyConsumer()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Vector3 v=fun creatVariable.Vector3(Float 1:Float 2:Float 3)\n" +
            "String s=fun create.string(var v)\n" +
            "fun gravity(var v)\n" +
            "fun debug.print(var s:Color 255,255,255,255:Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_NotWarn_StringConst_When_ImplicitStringParamIsNotCreateString()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Vector3 v=fun creatVariable.Vector3(Float 1:Float 2:Float 3)\n" +
            "fun debug.print(var v:Color 255,255,255,255:Bool 0)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_NotWarn_When_CreatVariableVector3_HasVariableArg()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float a=fun create.float(Float 1)\n" +
            "Vector3 v=fun creatVariable.Vector3(var a:Float 2:Float 3)\n" +
            "fun gravity(var v)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        // a is inlinable (Float), v is not (has variable arg)
        Assert.Single(diagnostics);
        Assert.Contains("a", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotWarn_When_UsedAsVarfOwner()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)\n" +
            "varf Float.x.abs()");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        // x is used as varf owner, cannot inline
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Should_NotWarn_When_UsedAsVarfOwner_AndArgument()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float x=fun create.float(Float 5)\n" +
            "varf Float.x.abs()\n" +
            "fun float.abs(var x)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        // x has both varf owner usage and argument usage → owner blocks inlining
        Assert.Empty(diagnostics);
    }

    // ── 表达式内联：Bool 变量 + create.bool 消费 + if/while 条件 ──

    [Fact]
    public void Should_Warn_When_BoolFromLongEqual_UsedInCreateBoolInIf()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long dir=fun create.long(Long 1)\n" +
            "Bool flag=fun long.equal(var dir:Long 2)\n" +
            "if{fun create.bool(var flag)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        LintDiagnostic diag = Assert.Single(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
        Assert.Contains("flag", diag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_Warn_When_BoolFromLongLt_UsedInCreateBoolInIf()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float idx=fun create.float(Float 3)\n" +
            "Float cnt=fun create.float(Float 5)\n" +
            "Bool cond=fun float.lt(var idx:var cnt)\n" +
            "if{fun create.bool(var cond)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Contains(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr && d.Message.Contains("cond", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_NotWarn_When_BoolConsumedOutsideIf()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long dir=fun create.long(Long 1)\n" +
            "Bool flag=fun long.equal(var dir:Long 2)\n" +
            "fun bool.not(var flag)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
    }

    [Fact]
    public void Should_NotWarn_When_NonBoolExprVar()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Float a=fun create.float(Float 3)\n" +
            "Float sum=fun float.add(var a:Float 1)\n" +
            "fun float.abs(var sum)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
    }

    [Fact]
    public void Should_NotWarn_When_BoolConsumedByNonCreateBoolInIf()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long n=fun create.long(Long 1)\n" +
            "Bool flag=fun long.equal(var n:Long 1)\n" +
            "if{fun bool.not(var flag)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
    }

    [Fact]
    public void Should_NotWarn_When_BoolInWhile_OneInitOneUpdate()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long n=fun create.long(Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 0)\n" +
            "while{fun create.bool(var flag)\n" +
            "  Long n=fun long.add(var n:Long 1)\n" +
            "  Bool flag=fun long.equal(var n:Long 5)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr && d.Message.Contains("flag", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_NotWarn_When_BoolInWhile_OneInitOnly()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long n=fun create.long(Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 0)\n" +
            "while{fun create.bool(var flag)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr && d.Message.Contains("flag", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_NotWarn_When_BoolInWhile_TwoWritesOutside()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long n=fun create.long(Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 1)\n" +
            "while{fun create.bool(var flag)\n" +
            "  fun debug.enable(Bool 1)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
    }

    [Fact]
    public void Should_NotWarn_When_BoolInWhile_TwoWritesInside()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Bool flag=fun create.bool(Bool 1)\n" +
            "while{fun create.bool(var flag)\n" +
            "  Bool flag=fun long.equal(Long 1:Long 1)\n" +
            "  Bool flag=fun long.equal(Long 2:Long 2)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr && d.Message.Contains("flag", StringComparison.Ordinal));
    }

    [Fact]
    public void Should_NotWarn_When_BoolInWhile_ThreeWrites()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Long n=fun create.long(Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 0)\n" +
            "Bool flag=fun long.equal(var n:Long 1)\n" +
            "while{fun create.bool(var flag)\n" +
            "  Bool flag=fun long.equal(var n:Long 2)\n" +
            "}");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableExpr);
    }

    [Fact]
    public void Should_NotWarn_When_CreateBoolWithVarArg()
    {
        DeviceEntity device = CreateDeviceWithScript(
            "Bool src=fun create.bool(Bool 1)\n" +
            "Bool wrapped=fun create.bool(var src)\n" +
            "fun bool.not(var wrapped)");
        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.Single(diagnostics);
        Assert.Equal(LintRuleIds.VarInlinableConst, diagnostics[0].RuleId);
        Assert.Contains("src", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotWarn_When_LocalVarHasSameNameAsParameter()
    {
        // 局部变量与参数同名时，参数侧有外部调用者写入，不应报可内联
        string sourceText = "begincodeV2\nname:A\nparams:Float lerpSpeed\nlines:\n" +
            "Float lerpSpeed=fun create.float(Float 0)\n" +
            "fun camera.setpos(var lerpSpeed)\n" +
            "endcode";

        ProjectRootEntity root = TestEntityFactory.CreateRoot();
        MapEntity map = TestEntityFactory.CreateMap(root, "lint");
        DeviceEntity device = TestEntityFactory.CreateDevice(map, deviceId: 1, deviceType: 1001, logicName: "test");
        device.SetVarsInternal(new VarsConfig { Variables = [] });

        ScriptGroupEntity scriptGroup = TestEntityFactory.CreateScriptGroup(device);
        scriptGroup.SetScriptEntriesInternal([new("A", string.Empty, false)]);
        TestEntityFactory.CreateInlineScript(scriptGroup, "A", sourceText);

        ScriptAnalysisService analysisService = CreateAnalysisService(device);

        IReadOnlyList<LintDiagnostic> diagnostics = LintEngine.ValidateUnusedLocalVars(device, analysisService);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == LintRuleIds.VarInlinableConst);
    }
}
