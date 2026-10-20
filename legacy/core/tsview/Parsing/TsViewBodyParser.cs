using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;

namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图方法体解析器。
/// TS 语法事实来自内嵌官方 TypeScript API，C# 只执行 BeScript 业务映射。
/// </summary>
internal static class TsViewBodyParser
{
    internal const string BodyStructureConventionPrefix = "方法体结构约定：";
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> EmptyThisMemberUsages =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);

    internal readonly record struct TsBodyParseResult(
        IReadOnlyList<(int LineNumber, string LineText)> NormalizedLines,
        BescriptAst Ast,
        List<MethodCallInfo> MethodCalls,
        IReadOnlyList<DeviceTsView.InlineCanvasLiteral> InlineCanvases,
        IReadOnlyDictionary<string, IReadOnlyList<int>> ThisMemberUsages,
        IReadOnlyList<int> AsAssertionLines,
        bool IsSuccess,
        string Error,
        int? ErrorLine,
        TsViewDiagnosticTag DiagnosticTag);

    private readonly record struct BodyFacts(
        IReadOnlyList<DeviceTsView.InlineCanvasLiteral> InlineCanvases,
        IReadOnlyDictionary<string, IReadOnlyList<int>> ThisMemberUsages);

    internal static TsBodyParseResult ParseTsBodyToAst(
        IReadOnlyList<(int LineNumber, string LineText)> bodyLines,
        ScriptConversionContext ctx,
        string? expectedRetVar,
        List<ToolError> warnings,
        string? targetPath = null,
        List<ToolError>? collectedErrors = null)
    {
        List<(int LineNumber, string LineText)> effectiveLines = [];
        bool hasContentLine = bodyLines.Any(static line => !line.LineText.All(char.IsWhiteSpace));
        foreach ((int lineNumber, string lineText) in bodyLines)
        {
            if (lineText.All(char.IsWhiteSpace))
                effectiveLines.Add((lineNumber, hasContentLine ? ";" : "//"));
            else
                effectiveLines.Add((lineNumber, lineText));
        }

        IReadOnlyList<(int LineNumber, string LineText)> normalizedLines = effectiveLines;
        BescriptAst besAst = new BescriptAst(null, BescriptHeader.Empty, Array.Empty<BescriptAstNode>(), BescriptAst.EmptyDeclaredVariables);
        List<MethodCallInfo> methodCalls = [];
        IReadOnlyList<DeviceTsView.InlineCanvasLiteral> inlineCanvases = Array.Empty<DeviceTsView.InlineCanvasLiteral>();
        string error = string.Empty;
        int? errorLine = null;

        int baseLine = effectiveLines[0].LineNumber;
        string text = string.Join("\n", effectiveLines.Select(l => l.LineText));
        string? retVarForReturn = null;
        if (expectedRetVar != null)
            retVarForReturn = TsViewIdentifierMap.ToTsIdentifier(expectedRetVar);
        string contextJson = System.Text.Json.JsonSerializer.Serialize(new { expectedRetVar = retVarForReturn, normalizeImplicitReturn = true });
        string json = TsCompilerHost.ParseMethodBody(text, contextJson);
        OfficialBodyParseResult? parseResult = System.Text.Json.JsonSerializer.Deserialize<OfficialBodyParseResult>(json, BodyWritebackJson.Options);
        if (parseResult is null)
            throw new InvalidOperationException("TS 官方方法体解析返回空结果。");
        if (parseResult.Error is not null)
        {
            error = PrefixBodyStructureError(parseResult.Error);
            errorLine = baseLine + parseResult.ErrorLine.GetValueOrDefault(1) - 1;
            return new TsBodyParseResult(normalizedLines, besAst, methodCalls, inlineCanvases, EmptyThisMemberUsages, [], false, error, errorLine, TsViewDiagnosticTag.None);
        }

        List<BodyNode> statements = parseResult.Statements;
        normalizedLines = parseResult.NormalizedLines
            .Select(x => (
                x.LineNumber > 0 && x.LineNumber <= effectiveLines.Count
                    ? effectiveLines[x.LineNumber - 1].LineNumber
                    : effectiveLines[^1].LineNumber + x.LineNumber - effectiveLines.Count,
                x.LineText))
            .ToArray();
        IReadOnlyList<int> asAssertionLines = parseResult.AsAssertionLines
            .Select(line => effectiveLines[line - 1].LineNumber)
            .ToArray();
        if (parseResult.InsertedLeadingEmptyComment)
        {
            warnings.Add(new ToolError(targetPath, effectiveLines[0].LineNumber, null,
                "方法体仅含 return 语句是错误写法。已自动补充空注释行。正确做法应添加实际逻辑。",
                ToolDiagnosticSeverity.Warning));
        }
        foreach (OfficialRewriteWarning rewriteWarning in parseResult.RewriteWarnings)
        {
            warnings.Add(new ToolError(targetPath, null, null, rewriteWarning.Message, ToolDiagnosticSeverity.Warning));
        }
        foreach (int inlineCommentLine in parseResult.InlineCommentLines)
        {
            warnings.Add(new ToolError(targetPath, effectiveLines[inlineCommentLine - 1].LineNumber, null,
                "行内注释已自动拆分到独立行。注释应独占一行。",
                ToolDiagnosticSeverity.Warning));
        }
        BodyBlock body = new(statements);

        TsViewBodyToBescriptMapper.MapResult<(BescriptAst Ast, List<MethodCallInfo> MethodCalls)> mapResult =
            TsViewBodyToBescriptMapper.MapBody(body, ctx, baseLine, normalizedLines, warnings, targetPath, collectedErrors);
        if (!mapResult.Success)
        {
            error = PrefixBodyStructureError(mapResult.Error);
            errorLine = mapResult.ErrorLine;
            BodyFacts facts = CollectBodyFacts();
            return new TsBodyParseResult(normalizedLines, besAst, methodCalls, facts.InlineCanvases, facts.ThisMemberUsages, asAssertionLines, false, error, errorLine, mapResult.DiagnosticTag);
        }

        besAst = mapResult.Value.Ast;
        methodCalls = mapResult.Value.MethodCalls;
        BodyFacts successFacts = CollectBodyFacts();
        return new TsBodyParseResult(normalizedLines, besAst, methodCalls, successFacts.InlineCanvases, successFacts.ThisMemberUsages, asAssertionLines, true, string.Empty, null, TsViewDiagnosticTag.None);

        BodyFacts CollectBodyFacts()
        {
            List<DeviceTsView.InlineCanvasLiteral> canvasLiterals = [];
            Dictionary<string, SortedSet<int>> usageSets = new(StringComparer.Ordinal);

            void AddUsage(string name, int line)
            {
                if (!usageSets.TryGetValue(name, out SortedSet<int>? lines))
                {
                    lines = [];
                    usageSets[name] = lines;
                }
                lines.Add(line);
            }

            void Visit(BodyNode node)
            {
                if (node is BodyCall bodyCall && TryGetCanvasHtmlLiteral(bodyCall) is string html)
                {
                    int lineNumber = ResolveSourceLine(bodyCall.SourceLine);
                    canvasLiterals.Add(new DeviceTsView.InlineCanvasLiteral(lineNumber, html));
                }

                if (node is BodyPropertyAccess
                    {
                        Expression: BodyThisExpression,
                        Name: BodyIdentifier { Text: string name }
                    } thisAccess)
                {
                    AddUsage(name, ResolveSourceLine(thisAccess.SourceLine));
                }

                switch (node)
                {
                    case BodyBlock block:
                        foreach (BodyNode stmt in block.Statements)
                            Visit(stmt);
                        break;
                    case BodyVariableStatement varStmt:
                        Visit(varStmt.DeclarationList);
                        break;
                    case BodyVariableDeclarationList declList:
                        foreach (BodyVariableDeclaration decl in declList.Declarations)
                            Visit(decl);
                        break;
                    case BodyVariableDeclaration decl:
                        if (decl.Initializer is not null)
                            Visit(decl.Initializer);
                        break;
                    case BodyExpressionStatement exprStmt:
                        Visit(exprStmt.Expression);
                        break;
                    case BodyIfStatement ifStmt:
                        Visit(ifStmt.Condition);
                        Visit(ifStmt.ThenStatement);
                        if (ifStmt.ElseStatement is not null)
                            Visit(ifStmt.ElseStatement);
                        break;
                    case BodyWhileStatement whileStmt:
                        Visit(whileStmt.Condition);
                        Visit(whileStmt.Body);
                        break;
                    case BodyReturnStatement returnStmt:
                        if (returnStmt.Expression is not null)
                            Visit(returnStmt.Expression);
                        break;
                    case BodyPropertyAccess propertyAccess:
                        Visit(propertyAccess.Expression);
                        break;
                    case BodyCall call:
                        Visit(call.Expression);
                        foreach (BodyNode arg in call.Arguments)
                            Visit(arg);
                        break;
                    case BodyNewExpression newExpression:
                        Visit(newExpression.Expression);
                        foreach (BodyNode arg in newExpression.Arguments)
                            Visit(arg);
                        break;
                    case BodyNotExpression notExpression:
                        Visit(notExpression.Operand);
                        break;
                    case BodyBinaryExpression binaryExpression:
                        Visit(binaryExpression.Left);
                        Visit(binaryExpression.Right);
                        break;
                    case BodyParenthesizedExpression parenthesizedExpression:
                        Visit(parenthesizedExpression.Expression);
                        break;
                    case BodyAsExpression asExpression:
                        Visit(asExpression.Expression);
                        break;
                    case BodyObjectLiteral objectLiteral:
                        foreach (BodyObjectProperty property in objectLiteral.Properties)
                            Visit(property.Value);
                        break;
                    case BodyArrayLiteral arrayLiteral:
                        foreach (BodyNode element in arrayLiteral.Elements)
                            Visit(element);
                        break;
                    case BodyAwaitExpression awaitExpression:
                        Visit(awaitExpression.Argument);
                        break;
                    case BodyVoidExpression voidExpression:
                        Visit(voidExpression.Argument);
                        break;
                    default:
                        return;
                }
            }

            Visit(body);
            Dictionary<string, IReadOnlyList<int>> thisMemberUsages = usageSets.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<int>)kvp.Value.ToArray(),
                StringComparer.Ordinal);
            return new BodyFacts(canvasLiterals, thisMemberUsages);
        }

        string? TryGetCanvasHtmlLiteral(BodyCall call)
        {
            if (call.Expression is not BodyPropertyAccess
                {
                    Expression: BodyIdentifier { Text: "BeCanvas" },
                    Name: BodyIdentifier { Text: var methodName }
                })
            {
                return null;
            }

            if (methodName == "fromBase64Const")
            {
                return call.Arguments.Count > 0 && call.Arguments[0] is BodyTemplateLiteral html
                    ? html.Text
                    : null;
            }

            if (methodName == "fromNodeConst")
            {
                return call.Arguments.Count > 1 && call.Arguments[1] is BodyTemplateLiteral html
                    ? html.Text
                    : null;
            }

            return null;
        }

        int ResolveSourceLine(int? sourceLine)
        {
            if (sourceLine is not { } line)
                return baseLine;
            return line > 0 && line <= normalizedLines.Count
                ? normalizedLines[line - 1].LineNumber
                : line;
        }
    }

    private sealed record OfficialBodyParseResult(
        List<BodyNode> Statements,
        List<OfficialBodyLine> NormalizedLines,
        List<OfficialRewriteWarning> RewriteWarnings,
        List<int> AsAssertionLines,
        List<int> InlineCommentLines,
        bool InsertedLeadingEmptyComment,
        string? Error,
        int? ErrorLine);

    private sealed record OfficialBodyLine(int LineNumber, string LineText);

    private sealed record OfficialRewriteWarning(string Message);

    private static string PrefixBodyStructureError(string message)
    {
        if (message.StartsWith(BodyStructureConventionPrefix, StringComparison.Ordinal))
        {
            return message;
        }

        return BodyStructureConventionPrefix + message;
    }

}

internal enum MethodCallKind
{
    Act,
    G
}

internal sealed record MethodCallInfo(MethodCallKind Kind, string MethodName, int Line, bool IsSelfTarget = false);

internal abstract record ParseResult<T>
{
    public sealed record OkResult(T ResultValue) : ParseResult<T>;
    public sealed record FailResult(string ErrorText, TsViewDiagnosticTag DiagnosticTagValue) : ParseResult<T>
    {
        public FailResult(string errorText) : this(errorText, TsViewDiagnosticTag.None)
        {
        }
    }

    public bool Success => this is OkResult;
    public T Value => ((OkResult)this).ResultValue;
    public string Error
    {
        get
        {
            if (this is FailResult fail)
                return fail.ErrorText;

            return string.Empty;
        }
    }

    public TsViewDiagnosticTag DiagnosticTag => this is FailResult fail ? fail.DiagnosticTagValue : TsViewDiagnosticTag.None;

    public static ParseResult<T> Ok(T value) => new OkResult(value);
    public static ParseResult<T> Fail(string error) => new FailResult(error, TsViewDiagnosticTag.None);
    public static ParseResult<T> Fail(string error, TsViewDiagnosticTag diagnosticTag) => new FailResult(error, diagnosticTag);
}
