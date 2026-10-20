using Xunit;
using Toolkit.Core.Analysis;
using Toolkit.Core.Infra;

namespace Toolkit.Core.Tests.Search;

public sealed class BescriptScannerTests
{
    private readonly BescriptScanner _scanner = new();

    [Fact]
    public void ScanActCalls_ValidActCall_ReturnsScriptName()
    {
        string text = @"begincodeV2
name:test
lines:
=act 1101.this.MyScript()
endcode";

        IReadOnlyList<BescriptActCallRange> results = _scanner.ScanActCalls(text);

        Assert.Single(results);
        Assert.Equal("MyScript", results[0].ScriptName);
        Assert.True(results[0].Range.Line > 0);
    }

    [Fact]
    public void ScanActCalls_MultipleActCalls_ReturnsAll()
    {
        string text = @"begincodeV2
name:test
lines:
=act 1101.this.Script1()
=act 1101.this.Script2()
endcode";

        IReadOnlyList<BescriptActCallRange> results = _scanner.ScanActCalls(text);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, static r => r.ScriptName == "Script1");
        Assert.Contains(results, static r => r.ScriptName == "Script2");
    }

    [Fact]
    public void ScanBuiltinMethodCalls_ValidFunCall_ReturnsBuiltinMethod()
    {
        string text = @"begincodeV2
name:test
lines:
fun break()
endcode";

        IReadOnlyList<BescriptBuiltinMethodRange> results = _scanner.ScanBuiltinMethodCalls(text);

        Assert.Single(results);
        Assert.Equal("break", results[0].MethodName);
    }

    [Fact]
    public void ScanCanvasArgs_CanvasWithPath_ReturnsNonGuid()
    {
        // canvas@1 = canvas:, @3 = =
        string text = @"begincodeV2
name:test
lines:
fun create.canvas(Canvas canvas@1MyCanvas@3SGVsbG8@3)
endcode";

        IReadOnlyList<BescriptCanvasArgRange> results = _scanner.ScanCanvasArgs(text);

        Assert.Single(results);
        Assert.Equal("canvas:MyCanvas=SGVsbG8=", results[0].RawArg); // 解码后格式
        BescriptInlineCanvasArgRange inlineArg = Assert.IsType<BescriptInlineCanvasArgRange>(results[0]);
        Assert.Equal("MyCanvas", inlineArg.CanvasName);
    }

    [Fact]
    public void ScanCanvasArgs_CanvasWithGuid_ReturnsGuid()
    {
        string text = @"begincodeV2
name:test
lines:
fun create.canvas(Canvas guid@100000000000000000000000000000000)
endcode";

        IReadOnlyList<BescriptCanvasArgRange> results = _scanner.ScanCanvasArgs(text);

        Assert.Single(results);
        Assert.IsType<BescriptGuidCanvasArgRange>(results[0]);
    }

    [Fact]
    public void ScanCanvasArgs_MultiLineNodeHjson_ReturnsCanvasNameAndRange()
    {
        string text = @"begincodeV2
name:test
lines:
fun create.canvas(Canvas ==hjson@1TestCanvas@1
[
 {
  type@1UILabel
  text@1Hello
 }
]==)
endcode";

        IReadOnlyList<BescriptCanvasArgRange> results = _scanner.ScanCanvasArgs(text);

        Assert.Single(results);
        BescriptInlineCanvasArgRange inlineArg = Assert.IsType<BescriptInlineCanvasArgRange>(results[0]);
        Assert.Equal("TestCanvas", inlineArg.CanvasName);
        Assert.StartsWith("==hjson:TestCanvas:", results[0].RawArg, StringComparison.Ordinal);
        Assert.Equal(4, results[0].Range.Line);
        Assert.True(results[0].Range.Length > 0);
    }

    [Fact]
    public void ScanGlobalVarRefs_EmptyNames_ReturnsEmpty()
    {
        Assert.Empty(_scanner.ScanGlobalVarRefs("some text", Array.Empty<string>()));
    }

    [Fact]
    public void ScanGlobalVarRefs_VarfCall_MatchesGlobalVar()
    {
        string text = @"begincodeV2
name:test
lines:
varf Float.MyVar.add(Float 1)
endcode";
        IReadOnlyList<BescriptGlobalVarRange> results = _scanner.ScanGlobalVarRefs(text, new[] { "MyVar" });

        Assert.Single(results);
        Assert.Equal("MyVar", results[0].Name);
    }

    [Fact]
    public void ScanSemanticTokens_WhileConditionVariable_DoesNotHitWhileKeyword()
    {
        string text = @"begincodeV2
name:test
lines:
Bool i=fun create.bool(Bool 1)
while{fun bool.not(var i)}
  fun break()
endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());
        string lineText = "while{fun bool.not(var i)}";
        int expectedColumn = lineText.LastIndexOf("i", StringComparison.Ordinal);
        int wrongColumn = lineText.IndexOf("i", StringComparison.Ordinal);

        Assert.Contains(results, r => r.Type == 1 && r.Line == 5 && r.Column == expectedColumn && r.Length == 1);
        Assert.DoesNotContain(results, r => r.Type == 1 && r.Line == 5 && r.Column == wrongColumn);
    }

    [Fact]
    public void ScanSemanticTokens_Emit_Class_Type_And_DeviceSelectorTokens()
    {
        string text = @"begincodeV2
name:test
params:Float speed,Device target
return:Bool ok
lines:
Bool value=fun create.bool(Bool 1)
=act 1101./Alias.MyScript()
endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());

        Assert.Contains(results, static r => r.Type == 7 && r.Line == 3 && r.Length == "Float".Length);
        Assert.Contains(results, static r => r.Type == 7 && r.Line == 3 && r.Length == "Device".Length);
        Assert.Contains(results, static r => r.Type == 7 && r.Line == 4 && r.Length == "Bool".Length);
        Assert.Contains(results, static r => r.Type == 7 && r.Line == 6 && r.Length == "Bool".Length);
        Assert.Contains(results, static r => r.Type == 6 && r.Line == 6 && r.Length == "create".Length);
        Assert.Contains(results, static r => r.Type == 6 && r.Line == 7 && r.Length == "1101".Length);
        Assert.Contains(results, static r => r.Type == 6 && r.Line == 7 && r.Length == "Alias".Length);
        Assert.Contains(results, static r => r.Type == 0 && r.Line == 7 && r.Length == "MyScript".Length);
    }

    [Fact]
    public void ScanSemanticTokens_Emit_Embedded_CallKeywords_In_ControlStatements()
    {
        string text = @"begincodeV2
name:test
lines:
if{fun float.lte(var 颜色候选数:var f0)
  fun debug.enable(Bool 1)
}
while{varf UIButton.button.isDown()
  fun break()
}
delay fun create.float(Float 0)
async =act 1.this.findPathAsync(Vector3 0,0,0:Vector3 0,0,0)
endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());

        string ifLine = "if{fun float.lte(var 颜色候选数:var f0)";
        string whileLine = "while{varf UIButton.button.isDown()";
        string delayLine = "delay fun create.float(Float 0)";
        string asyncLine = "async =act 1.this.findPathAsync(Vector3 0,0,0:Vector3 0,0,0)";

        Assert.Contains(results, r => r.Type == 4 && r.Line == 4 && r.Column == ifLine.IndexOf("if", StringComparison.Ordinal) && r.Length == "if".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 4 && r.Column == ifLine.IndexOf("fun", StringComparison.Ordinal) && r.Length == "fun".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 7 && r.Column == whileLine.IndexOf("while", StringComparison.Ordinal) && r.Length == "while".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 7 && r.Column == whileLine.IndexOf("varf", StringComparison.Ordinal) && r.Length == "varf".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 10 && r.Column == delayLine.IndexOf("delay", StringComparison.Ordinal) && r.Length == "delay".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 10 && r.Column == delayLine.IndexOf("fun", StringComparison.Ordinal) && r.Length == "fun".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 11 && r.Column == asyncLine.IndexOf("async", StringComparison.Ordinal) && r.Length == "async".Length);
        Assert.Contains(results, r => r.Type == 4 && r.Line == 11 && r.Column == asyncLine.IndexOf("act", StringComparison.Ordinal) && r.Length == "act".Length);
    }

    [Fact]
    public void ScanSemanticTokens_MultiLineCanvasArgument_DoesNotEmitCrossLineToken()
    {
        string text = @"begincodeV2
name:test
lines:
fun create.canvas(Canvas ==hjson@1TestCanvas@1
[
 {
  type@1UILabel
  text@1Hello
 }
]==)
endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());
        string[] lines = TextLines.SplitLines(text);

        foreach (BescriptSemanticToken token in results)
        {
            Assert.InRange(token.Line, 1, lines.Length);
            string line = lines[token.Line - 1];
            Assert.True(token.Column >= 0);
            Assert.True(token.Column + token.Length <= line.Length);
        }
    }

    [Fact]
    public void ScanGlobalVarRefs_WhileConditionVariable_DoesNotHitWhileKeyword()
    {
        string text = @"begincodeV2
name:test
lines:
Bool i=fun create.bool(Bool 1)
while{fun bool.not(var i)}
  fun break()
endcode";

        IReadOnlyList<BescriptGlobalVarRange> results = _scanner.ScanGlobalVarRefs(text, new[] { "i" });
        string lineText = "while{fun bool.not(var i)}";
        int expectedColumn = lineText.LastIndexOf("i", StringComparison.Ordinal);
        int wrongColumn = lineText.IndexOf("i", StringComparison.Ordinal);

        Assert.Contains(results, r => r.Name == "i" && r.Range.Column == expectedColumn);
        Assert.DoesNotContain(results, r => r.Name == "i" && r.Range.Column == wrongColumn);
    }

    [Fact]
    public void ScanSemanticTokens_CommentLine_Emits_TK_COMMENT()
    {
        string text = @"begincodeV2
    name:test
    lines:
    // this is a comment
    endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());
        Assert.Contains(results, static r => r.Type == 8 && r.Line == 4);
    }

    [Fact]
    public void ScanSemanticTokens_DisabledLine_Emits_TK_DISABLED()
    {
        string text = @"begincodeV2
    name:test
    lines:
    /* Bool ok=fun create.bool(Bool 1)
    endcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());
        Assert.Contains(results, static r => r.Type == 9 && r.Line == 4);
    }

    [Fact]
    public void ScanSemanticTokens_DisabledLine_WithLineNumberPrefix_Emits_TK_DISABLED()
    {
        string text = "begincodeV2\nname:test\nlines:\n4:  /* Bool ok=fun create.bool(Bool 1)\nendcode";

        IReadOnlyList<BescriptSemanticToken> results = _scanner.ScanSemanticTokens(text, Array.Empty<string>());
        Assert.Contains(results, static r => r.Type == 9 && r.Line == 4);
    }
}
