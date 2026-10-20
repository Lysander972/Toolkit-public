using Xunit;
using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Bescript.Base;

/// <summary>
/// BescriptAst 序列化往返测试。
/// 验证 BeScript文本 → BescriptAst → BeScript文本 的无损转换。
/// </summary>
public sealed class BescriptAstSerializerTests
{
    [Fact]
    public void Roundtrip_SimpleCall_ShouldMatch()
    {
        string[] original = new[]
        {
            "fun test()"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_CallWithArgs_ShouldMatch()
    {
        string[] original = new[]
        {
            "fun create.float(Float 123)"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_CallWithMultipleArgs_ShouldMatch()
    {
        string[] original = new[]
        {
            "fun add(Float 1:Float 2)"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_CallWithReturn_ShouldMatch()
    {
        string[] original = new[]
        {
            "Float x=fun create.float(Float 1)"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_VarfCall_ShouldMatch()
    {
        string[] original = new[]
        {
            "varf Float.x.add(Float 1)"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_ActCall_ShouldMatch()
    {
        string[] original = new[]
        {
            "act 1101.this.test()"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_ActCallWithAlias_ShouldMatch()
    {
        string[] original = new[]
        {
            "act 1101./myDevice.test()"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_IfBlock_ShouldMatch()
    {
        string[] original = new[]
        {
            "if{fun bool.not(Bool 1)",
            "  fun break()",
            "}"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_WhileBlock_ShouldMatch()
    {
        string[] original = new[]
        {
            "while{fun bool.not(Bool 0)",
            "  fun continue()",
            "}"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_NestedBlocks_ShouldMatch()
    {
        string[] original = new[]
        {
            "if{fun bool.not(Bool 0)",
            "  while{fun bool.not(Bool 1)",
            "    fun break()",
            "  }",
            "}"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_DelayStatement_ShouldMatch()
    {
        string[] original = new[]
        {
            "delay fun create.float(Float 1)"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_AsyncStatement_ShouldMatch()
    {
        string[] original = new[]
        {
            "async fun test()"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_BreakContinue_ShouldMatch()
    {
        string[] original = new[]
        {
            "fun break()",
            "fun continue()"
        };

        AssertRoundtrip(original);
    }

    [Fact]
    public void Roundtrip_ComplexScript_ShouldMatch()
    {
        string[] original = new[]
        {
            "Float x=fun create.float(Float 0)",
            "if{fun bool.not(Bool 0)",
            "  Float y=fun create.float(Float 1)",
            "  varf Float.y.add(Float 2)",
            "  while{fun bool.not(Bool 1)",
            "    delay fun create.float(Float 3)",
            "    async fun test()",
            "    fun break()",
            "  }",
            "}",
            "act 1101.this.test()"
        };

        AssertRoundtrip(original);
    }

    private static void AssertRoundtrip(string[] originalLines)
    {
        string scriptText = string.Join('\n', new[]
        {
            "begincodeV2",
            "name:test",
            "lines:"
        }) + "\n" + string.Join('\n', originalLines) + "\nendcode";
        (BescriptScriptInfo info, List<ToolError> errors, List<ToolError> _) = BescriptTextParser.Parse("test.code", scriptText);
        Assert.Empty(errors);

        BescriptAst ast = info.Ast;
        List<string> serialized = BescriptAstSerializer.Serialize(ast);

        Assert.Equal(originalLines.Length, serialized.Count);
        for (int i = 0; i < originalLines.Length; i++)
        {
            Assert.Equal(originalLines[i], serialized[i]);
        }
    }

}
