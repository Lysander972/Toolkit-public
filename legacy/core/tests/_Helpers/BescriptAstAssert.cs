using Xunit;
using Toolkit.Core.Bescript;

namespace Toolkit.Core.Tests;

internal static class BescriptAstAssert
{
    internal static void AssertAstEqual(BescriptAst expected, BescriptAst actual, string path)
    {
        // 比较 Header
        AssertHeaderEqual(expected.Header, actual.Header, path);

        // 比较 RootNodes
        Assert.True(expected.RootNodes.Count == actual.RootNodes.Count,
            $"[{path}] RootNodes.Count mismatch: expected={expected.RootNodes.Count}, actual={actual.RootNodes.Count}");
        for (int i = 0; i < expected.RootNodes.Count; i++)
        {
            AssertNodeEqual(expected.RootNodes[i], actual.RootNodes[i], path);
        }

        Assert.Equal(expected.DeclaredVariables.Count, actual.DeclaredVariables.Count);
        foreach (KeyValuePair<string, BescriptVarType> kv in expected.DeclaredVariables)
        {
            Assert.True(actual.DeclaredVariables.TryGetValue(kv.Key, out BescriptVarType v), $"[{path}] 缺少变量 {kv.Key}");
            Assert.True(kv.Value == v, $"[{path}] 变量 {kv.Key} 类型不匹配: expected={kv.Value}, actual={v}");
        }
    }

    private static void AssertHeaderEqual(BescriptHeader expected, BescriptHeader actual, string path)
    {
        Assert.True(expected.Name == actual.Name, $"[{path}] Header.Name mismatch: expected='{expected.Name}', actual='{actual.Name}'");
        Assert.True(expected.Comment == actual.Comment, $"[{path}] Header.Comment mismatch: expected='{expected.Comment}', actual='{actual.Comment}'");
        Assert.True(expected.Debug == actual.Debug, $"[{path}] Header.Debug mismatch: expected={expected.Debug}, actual={actual.Debug}");
        Assert.True(expected.Skip == actual.Skip, $"[{path}] Header.Skip mismatch: expected={expected.Skip}, actual={actual.Skip}");
        Assert.True(expected.Private == actual.Private, $"[{path}] Header.Private mismatch: expected={expected.Private}, actual={actual.Private}");
        Assert.True(expected.Guid == actual.Guid, $"[{path}] Header.Guid mismatch: expected='{expected.Guid}', actual='{actual.Guid}'");

        // Color
        if (expected.Color is null || actual.Color is null)
        {
            Assert.True(expected.Color is null == actual.Color is null, $"[{path}] Header.Color null mismatch");
        }
        else
        {
            Assert.True(expected.Color.R == actual.Color.R && expected.Color.G == actual.Color.G && expected.Color.B == actual.Color.B,
                $"[{path}] Header.Color mismatch: expected=({expected.Color.R},{expected.Color.G},{expected.Color.B}), actual=({actual.Color.R},{actual.Color.G},{actual.Color.B})");
        }

        // Parameters
        Assert.True(expected.Parameters.Count == actual.Parameters.Count, $"[{path}] Header.Parameters.Count mismatch: expected={expected.Parameters.Count}, actual={actual.Parameters.Count}");
        for (int i = 0; i < expected.Parameters.Count; i++)
        {
            BescriptParameter ep = expected.Parameters[i];
            BescriptParameter ap = actual.Parameters[i];
            Assert.True(ep.TypeName == ap.TypeName && ep.Name == ap.Name,
                $"[{path}] Header.Parameters[{i}] mismatch: expected=({ep.TypeName} {ep.Name}), actual=({ap.TypeName} {ap.Name})");
        }

        // ReturnParameter
        if (expected.ReturnParameter is null || actual.ReturnParameter is null)
        {
            Assert.True(expected.ReturnParameter is null == actual.ReturnParameter is null, $"[{path}] Header.ReturnParameter null mismatch");
        }
        else
        {
            Assert.True(expected.ReturnParameter.TypeName == actual.ReturnParameter.TypeName && expected.ReturnParameter.Name == actual.ReturnParameter.Name,
                $"[{path}] Header.ReturnParameter mismatch: expected=({expected.ReturnParameter.TypeName} {expected.ReturnParameter.Name}), actual=({actual.ReturnParameter.TypeName} {actual.ReturnParameter.Name})");
        }
    }

    private static void AssertNodeEqual(BescriptAstNode expected, BescriptAstNode actual, string path)
    {
        Assert.True(expected.Line == actual.Line, $"[{path}] Line mismatch: expected={expected.Line}, actual={actual.Line}");
        Assert.True(expected.Depth == actual.Depth, $"[{path}] Depth mismatch at line {expected.Line}: expected={expected.Depth}, actual={actual.Depth}");
        // Index 是解析时的全局命令序号，不是 AST 语义内容，不比较
        Assert.True(expected.Kind == actual.Kind, $"[{path}] Kind mismatch at line {expected.Line}: expected={expected.Kind}, actual={actual.Kind}");
        Assert.True(expected.IsDisabled == actual.IsDisabled, $"[{path}] IsDisabled mismatch at line {expected.Line}: expected={expected.IsDisabled}, actual={actual.IsDisabled}");

        bool expectedHasCall = expected is IHasCall;
        bool actualHasCall = actual is IHasCall;
        if (!expectedHasCall || !actualHasCall)
        {
            Assert.Equal(expectedHasCall, actualHasCall);
        }
        else
        {
            AssertCallEqual(((IHasCall)expected).Call, ((IHasCall)actual).Call, path, expected.Line);
        }

        Assert.Equal(expected.Children.Count, actual.Children.Count);
        for (int i = 0; i < expected.Children.Count; i++)
        {
            AssertNodeEqual(expected.Children[i], actual.Children[i], path);
        }
    }

    private static void AssertCallEqual(BescriptCallSignature expected, BescriptCallSignature actual, string path, int line)
    {
        Assert.True(expected.Kind == actual.Kind, $"[{path}] Call.Kind mismatch at line {line}: expected={expected.Kind}, actual={actual.Kind}");

        string? expectedOwnerType = expected switch { VarfCallSignature v => v.OwnerType, ActCallSignature a => a.OwnerType, DvfCallSignature d => d.OwnerType, _ => null };
        string? actualOwnerType = actual switch { VarfCallSignature v => v.OwnerType, ActCallSignature a => a.OwnerType, DvfCallSignature d => d.OwnerType, _ => null };
        Assert.True(expectedOwnerType == actualOwnerType, $"[{path}] Call.OwnerType mismatch at line {line}: expected={expectedOwnerType}, actual={actualOwnerType}");

        string? expectedOwnerName = expected switch { VarfCallSignature v => v.OwnerName, ActCallSignature a => a.OwnerName, DvfCallSignature d => d.OwnerName, _ => null };
        string? actualOwnerName = actual switch { VarfCallSignature v => v.OwnerName, ActCallSignature a => a.OwnerName, DvfCallSignature d => d.OwnerName, _ => null };
        Assert.True(expectedOwnerName == actualOwnerName, $"[{path}] Call.OwnerName mismatch at line {line}: expected='{expectedOwnerName}', actual='{actualOwnerName}'");

        bool expectedAlias = expected switch { VarfCallSignature v => v.OwnerNameIsAlias, ActCallSignature a => a.OwnerNameIsAlias, DvfCallSignature d => d.OwnerNameIsAlias, _ => false };
        bool actualAlias = actual switch { VarfCallSignature v => v.OwnerNameIsAlias, ActCallSignature a => a.OwnerNameIsAlias, DvfCallSignature d => d.OwnerNameIsAlias, _ => false };
        Assert.True(expectedAlias == actualAlias, $"[{path}] Call.OwnerNameIsAlias mismatch at line {line}: expected={expectedAlias}, actual={actualAlias}");

        Assert.True(expected.FunctionName == actual.FunctionName, $"[{path}] Call.FunctionName mismatch at line {line}: expected='{expected.FunctionName}', actual='{actual.FunctionName}'");
        // SaveName 是解析产物，不是 AST 语义内容，不比较
        // ReturnVar 整体可空，其中字段绝对不可空
        Assert.True(
            (expected.ReturnVar == null) == (actual.ReturnVar == null),
            $"[{path}] Call.ReturnVar null mismatch at line {line}: expected null={expected.ReturnVar == null}, actual null={actual.ReturnVar == null}");
        if (expected.ReturnVar is not null && actual.ReturnVar is not null)
        {
            Assert.True(
                expected.ReturnVar.Name == actual.ReturnVar.Name,
                $"[{path}] Call.ReturnVar.Name mismatch at line {line}: expected='{expected.ReturnVar.Name}', actual='{actual.ReturnVar.Name}'");
            Assert.True(
                expected.ReturnVar.TypeName == actual.ReturnVar.TypeName,
                $"[{path}] Call.ReturnVar.TypeName mismatch at line {line}: expected='{expected.ReturnVar.TypeName}', actual='{actual.ReturnVar.TypeName}'");
        }

        Assert.True(expected.Arguments.Count == actual.Arguments.Count, $"[{path}] Call.Arguments.Count mismatch at line {line}: expected={expected.Arguments.Count}, actual={actual.Arguments.Count}");
        for (int i = 0; i < expected.Arguments.Count; i++)
        {
            BescriptCallArgument ea = expected.Arguments[i];
            BescriptCallArgument aa = actual.Arguments[i];
            Assert.True(ea.IsVariable == aa.IsVariable, $"[{path}] Call.Arguments[{i}].IsVariable mismatch at line {line}: expected={ea.IsVariable}, actual={aa.IsVariable}");
            Assert.True(ea.TypeName == aa.TypeName, $"[{path}] Call.Arguments[{i}].TypeName mismatch at line {line}: expected='{ea.TypeName}', actual='{aa.TypeName}'");
            Assert.True(ea.RawText == aa.RawText, $"[{path}] Call.Arguments[{i}].RawText mismatch at line {line}: expected='{ea.RawText}', actual='{aa.RawText}'");
            Assert.True(ea.DecodedText == aa.DecodedText, $"[{path}] Call.Arguments[{i}].DecodedText mismatch at line {line}: expected='{ea.DecodedText}', actual='{aa.DecodedText}'");
            Assert.True(ea.VarType == aa.VarType, $"[{path}] Call.Arguments[{i}].VarType mismatch at line {line}: expected='{ea.VarType}', actual='{aa.VarType}'");
        }
    }
}
