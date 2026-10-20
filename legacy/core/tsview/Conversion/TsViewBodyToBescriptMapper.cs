using Toolkit.Core.Bescript;
using Toolkit.Core.Infra;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

/// <summary>
/// TS 视图方法体写回结构 → BescriptAst 映射器。
/// 仅处理 TS 视图允许的语句形态，只处理官方解析后的写回数据，不做 TS 语法解析。
/// </summary>
internal static class TsViewBodyToBescriptMapper
{
    private static readonly HashSet<BescriptVarType> AllowedLocalVarTypes = new(new[]
    {
        BescriptVarType.Float,
        BescriptVarType.Datetime,
        BescriptVarType.UI,
        BescriptVarType.Vector3,
        BescriptVarType.Bool,
        BescriptVarType.String,
        BescriptVarType.UIRect,
        BescriptVarType.UIButton,
        BescriptVarType.UILabel,
        BescriptVarType.Color,
        BescriptVarType.Device,
        BescriptVarType.UIInput,
        BescriptVarType.UILine,
        BescriptVarType.List,
        BescriptVarType.Dict,
        BescriptVarType.UIAvatar,
        BescriptVarType.UISpine,
        BescriptVarType.UIRawImage,
        BescriptVarType.Mech,
        BescriptVarType.Player,
        BescriptVarType.Icon,
        BescriptVarType.Long,
        BescriptVarType.Struct,
        BescriptVarType.Canvas,
        BescriptVarType.Bytes,
        BescriptVarType.Mesh
    });

    private static readonly string AllowedLocalVarTypesText = string.Join(", ", AllowedLocalVarTypes.Select(t => "Be" + Enum.GetName(typeof(BescriptVarType), t)));
    private static readonly string AllowedVarTypesHint = $"BeScript 不允许自定义类型，TS 视图中的变量类型必须使用 d.ts 中声明的内置类型，仅限: {AllowedLocalVarTypesText}";

    internal abstract record MapResult<T>
    {
        public sealed record OkResult(T ResultValue) : MapResult<T>;
        public sealed record FailResult(string ErrorText, int? ErrorLineValue, TsViewDiagnosticTag DiagnosticTagValue) : MapResult<T>
        {
            public FailResult(string errorText, int? errorLineValue) : this(errorText, errorLineValue, TsViewDiagnosticTag.None)
            {
            }
        }

        public bool Success => this is OkResult;
        public T Value => ((OkResult)this).ResultValue;
        public string Error => this is FailResult fail ? fail.ErrorText : string.Empty;
        public int? ErrorLine => this switch
        {
            FailResult fail => fail.ErrorLineValue,
            _ => null
        };
        public TsViewDiagnosticTag DiagnosticTag => this is FailResult fail ? fail.DiagnosticTagValue : TsViewDiagnosticTag.None;
        public static MapResult<T> Ok(T value) => new OkResult(value);
        public static MapResult<T> Fail(string error, int? errorLine = null) =>
            new FailResult(error, errorLine, TsViewDiagnosticTag.None);
        public static MapResult<T> FailTagged(string error, TsViewDiagnosticTag diagnosticTag) =>
            new FailResult(error, null, diagnosticTag);
        public static MapResult<T> FailTagged(string error, int errorLine, TsViewDiagnosticTag diagnosticTag) =>
            new FailResult(error, errorLine, diagnosticTag);
    }

    private readonly record struct BooleanLowerResult(BodyNode Node, bool Rewritten);

    internal static MapResult<(BescriptAst Ast, List<MethodCallInfo> MethodCalls)> MapBody(
        BodyBlock body,
        ScriptConversionContext context,
        int baseLineNumber,
        IReadOnlyList<(int LineNumber, string LineText)>? rawLines,
        List<ToolError> warnings,
        string? targetPath = null,
        List<ToolError>? collectedErrors = null)
    {
        List<BescriptAstNode> nodes = new();
        List<MethodCallInfo> methodCalls = new List<MethodCallInfo>();

        IReadOnlyList<BodyNode> statements = body.Statements;

        // 固定剥离末尾的隐式 return（正向映射固定生成，反向映射固定剥离）
        if (statements.Count >= 1 && statements[^1] is BodyReturnStatement)
        {
            statements = statements.Take(statements.Count - 1).ToList();
        }
        else
        {
            return MapResult<(BescriptAst, List<MethodCallInfo>)>.Fail("方法体末尾缺少隐式 return 语句。", baseLineNumber);
        }

        bool hasReturnDecl = context.ExpectedReturnVarName is not null &&
            context.ParameterNames.Contains(context.ExpectedReturnVarName);
        int localDeclLineNumber = baseLineNumber <= 0 ? 1 : baseLineNumber;
        MapResult<(int LineNumber, bool HasReturnDecl)> localDeclResult = TryBindLocalDeclarations(statements, context, targetPath, warnings, localDeclLineNumber, hasReturnDecl);
        if (!localDeclResult.Success)
        {
            return localDeclResult.ErrorLine is int errorLine
                ? MapResult<(BescriptAst, List<MethodCallInfo>)>.FailTagged(localDeclResult.Error, errorLine, localDeclResult.DiagnosticTag)
                : MapResult<(BescriptAst, List<MethodCallInfo>)>.FailTagged(localDeclResult.Error, localDeclResult.DiagnosticTag);
        }
        hasReturnDecl = localDeclResult.Value.HasReturnDecl;
        localDeclLineNumber = localDeclResult.Value.LineNumber;

        if (context.ExpectedReturnVarName is not null && !hasReturnDecl)
        {
            return MapResult<(BescriptAst, List<MethodCallInfo>)>.Fail($"返回值变量 {context.ExpectedReturnVarName} 必须至少有一处 var 声明或形参来源。", baseLineNumber);
        }

        int lineNumber = baseLineNumber <= 0 ? 1 : baseLineNumber;
        context.ExpansionWarnings = warnings;
        context.ExpansionTargetPath = targetPath;
        if (collectedErrors is not null && targetPath is not null)
        {
            lineNumber = CollectMissingActTargetMarkers(statements, context, lineNumber, targetPath, collectedErrors);
        }

        MapResult<int> bodyResult = TryMapBodyStatements(statements, context, depth: 0, loopDepth: 0, lineNumber, methodCalls, nodes);
        if (!bodyResult.Success)
        {
            return bodyResult.ErrorLine is int errorLine
                ? MapResult<(BescriptAst, List<MethodCallInfo>)>.FailTagged(bodyResult.Error, errorLine, bodyResult.DiagnosticTag)
                : MapResult<(BescriptAst, List<MethodCallInfo>)>.FailTagged(bodyResult.Error, bodyResult.DiagnosticTag);
        }
        lineNumber = bodyResult.Value;

        // 所有语句映射完毕，压缩 if/while 条件中 create.bool(单次使用 Bool 变量) 的冗余
        HashSet<string> inlinedBesNames = InlineSingleUseBoolConditionVars(nodes, warnings, context.ExpansionTargetPath);

        // 原版 BeScript 不支持完全空的方法体（用户代码区段为空会触发原版的自动删除机制）
        if (nodes.Count == 0)
        {
            return MapResult<(BescriptAst, List<MethodCallInfo>)>.Fail("用户代码区段不得为空：原版 BeScript 会自动删除空方法体的脚本。应先删除所有调用处，再将整个方法定义（装饰器+签名+花括号）一次性删掉，而不是只清空方法体。", baseLineNumber);
        }

        IReadOnlyDictionary<string, BescriptVarType> declaredForAst;
        if (context.DeclaredVariables.Count == 0)
        {
            declaredForAst = BescriptAst.EmptyDeclaredVariables;
        }
        else
        {
            Dictionary<string, BescriptVarType> declared = new(StringComparer.Ordinal);
            foreach ((string? tsName, BescriptVarType varType) in context.DeclaredVariables)
            {
                string besName = TsViewIdentifierMap.FromTsIdentifier(tsName);
                if (!inlinedBesNames.Contains(besName))
                    declared[besName] = varType;
            }
            declaredForAst = declared;
        }

        BescriptAst ast = new BescriptAst(null, BescriptHeader.Empty, nodes, declaredForAst);
        return MapResult<(BescriptAst, List<MethodCallInfo>)>.Ok((ast, methodCalls));
    }

    /// <summary>
    /// 压缩 if/while 条件中 create.bool(单次使用 Bool 变量) 的冗余：
    /// Bool x=fun f(); if{fun create.bool(var x)} → if{fun f()}
    /// 每处压缩产出一条警告。
    /// </summary>
    private static HashSet<string> InlineSingleUseBoolConditionVars(
        List<BescriptAstNode> nodes, List<ToolError> warnings, string? targetPath)
    {
        HashSet<string> inlinedNames = new(StringComparer.Ordinal);

        // Pass 1: 全量收集变量读写计数
        Dictionary<string, int> writeCounts = new(StringComparer.Ordinal);
        Dictionary<string, int> readCounts = new(StringComparer.Ordinal);
        Dictionary<string, (int Index, FunCallSignature Call)> rootFunDecls = new(StringComparer.Ordinal);

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is CallAstNode { Call: FunCallSignature fc } && fc.ReturnVar is { Name: var rn })
                rootFunDecls[rn] = (i, fc);
        }

        foreach (BescriptAstNode node in nodes.FlattenNodes())
        {
            if (node is not IHasCall { Call: var call })
                continue;

            if (call.ReturnVar is { Name: var retName })
            {
                writeCounts.TryGetValue(retName, out int wc);
                writeCounts[retName] = wc + 1;
            }

            foreach (BescriptCallArgument arg in call.Arguments)
            {
                if (arg.IsVariable)
                {
                    readCounts.TryGetValue(arg.DecodedText, out int rc);
                    readCounts[arg.DecodedText] = rc + 1;
                }
            }

            string? ownerName = call switch
            {
                VarfCallSignature v => v.OwnerNameIsAlias ? null : v.OwnerName,
                ActCallSignature a => a.OwnerNameIsAlias ? null : a.OwnerName,
                DvfCallSignature d => d.OwnerName,
                _ => null
            };
            if (ownerName != null)
            {
                readCounts.TryGetValue(ownerName, out int rc);
                readCounts[ownerName] = rc + 1;
            }
        }

        // Pass 2: 查找根级 if/while 中 create.bool(var) 条件
        List<(int ControlIndex, int DeclIndex, FunCallSignature DeclCall)> pending = [];

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is not (IfAstNode or WhileAstNode))
                continue;

            BescriptCallSignature condSig = ((IHasCall)nodes[i]).Call;
            if (condSig is not FunCallSignature { FunctionName: "create.bool", Arguments.Count: 1 } funSig)
                continue;
            BescriptCallArgument arg = funSig.Arguments[0];
            if (!arg.IsVariable)
                continue;

            string varName = arg.DecodedText;
            writeCounts.TryGetValue(varName, out int wc);
            readCounts.TryGetValue(varName, out int rc);

            if (wc != 1 || rc != 1)
                continue;

            if (!rootFunDecls.TryGetValue(varName, out (int Index, FunCallSignature Call) decl))
                continue;

            // 声明必须在读取之前：BeScript 局部变量跨调用保持，读取在声明之前说明使用的是上一次调用的值
            if (decl.Index >= i)
                continue;

            pending.Add((i, decl.Index, decl.Call));
        }

        if (pending.Count == 0)
            return inlinedNames;

        // Pass 3: 替换条件、移除声明节点、产出警告
        HashSet<int> removeIndices = [];
        foreach ((int ctrlIdx, int declIdx, FunCallSignature? declCall) in pending)
        {
            if (declCall is not { ReturnVar: { } retVar } call) continue;
            BescriptCallSignature inlinedCond = call with { ReturnVar = null, DiscardsReturn = false };
            nodes[ctrlIdx] = nodes[ctrlIdx] switch
            {
                IfAstNode n => n with { Call = inlinedCond },
                WhileAstNode n => n with { Call = inlinedCond },
                _ => nodes[ctrlIdx]
            };
            removeIndices.Add(declIdx);
            inlinedNames.Add(retVar.Name);

            warnings.Add(new ToolError(targetPath, null, null,
                $"if/while 条件中 create.bool(var {retVar.Name}) 的变量仅使用一次，已内联为 {call.FunctionName}(...)。",
                ToolDiagnosticSeverity.Warning));
        }

        foreach (int idx in removeIndices.OrderDescending())
            nodes.RemoveAt(idx);

        return inlinedNames;
    }

    private static int CollectMissingActTargetMarkers(
        IReadOnlyList<BodyNode> statements,
        ScriptConversionContext context,
        int lineNumber,
        string targetPath,
        List<ToolError> errors)
    {
        int currentLine = lineNumber;
        foreach (BodyNode stmt in statements)
        {
            bool isDisabled = GetStmtIsDisabled(stmt);

            int statementLine = currentLine;
            switch (stmt)
            {
                case BodyVariableStatement varStmt:
                    foreach (BodyVariableDeclaration decl in varStmt.DeclarationList.Declarations)
                    {
                        if (decl.Initializer is not null)
                        {
                            CollectMissingActTargetMarkerFromExpression(decl.Initializer, context, targetPath, statementLine, errors);
                        }
                    }
                    currentLine++;
                    break;

                case BodyExpressionStatement exprStmt:
                    CollectMissingActTargetMarkerFromExpression(exprStmt.Expression, context, targetPath, statementLine, errors);
                    currentLine++;
                    break;

                case BodyIfStatement ifStmt:
                    if (ifStmt.Condition is BodyCall ifCall)
                    {
                        CollectMissingActTargetMarkerFromCall(ifCall, context, targetPath, statementLine, errors);
                    }
                    currentLine++;
                    if (ifStmt.ThenStatement is BodyBlock thenBlock)
                    {
                        currentLine = CollectMissingActTargetMarkers(thenBlock.Statements, context, currentLine, targetPath, errors);
                    }
                    if (isDisabled)
                    {
                        currentLine++;
                    }
                    break;

                case BodyWhileStatement whileStmt:
                    if (whileStmt.Condition is BodyCall whileCall)
                    {
                        CollectMissingActTargetMarkerFromCall(whileCall, context, targetPath, statementLine, errors);
                    }
                    currentLine++;
                    if (whileStmt.Body is BodyBlock whileBlock)
                    {
                        currentLine = CollectMissingActTargetMarkers(whileBlock.Statements, context, currentLine, targetPath, errors);
                    }
                    if (isDisabled)
                    {
                        currentLine++;
                    }
                    break;

                default:
                    currentLine++;
                    break;
            }
        }
        return currentLine;
    }

    private static void CollectMissingActTargetMarkerFromExpression(
        BodyNode expr,
        ScriptConversionContext context,
        string targetPath,
        int lineNumber,
        List<ToolError> errors)
    {
        if (expr is BodyBinaryExpression { Operator: BodyBinaryOperator.Equals, Right: var rhs })
        {
            CollectMissingActTargetMarkerFromExpression(rhs, context, targetPath, lineNumber, errors);
            return;
        }

        if (expr is not BodyCall call)
        {
            // await 表达式：穿透到内部调用
            if (expr is BodyAwaitExpression awaitExpr && awaitExpr.Argument is BodyCall awaitCall)
            {
                CollectMissingActTargetMarkerFromCall(awaitCall, context, targetPath, lineNumber, errors);
                return;
            }
            return;
        }

        CollectMissingActTargetMarkerFromCall(call, context, targetPath, lineNumber, errors);
    }

    private static void CollectMissingActTargetMarkerFromCall(
        BodyCall call,
        ScriptConversionContext context,
        string targetPath,
        int lineNumber,
        List<ToolError> errors)
    {
        if (!TryParseCallExpression(call, context).Success)
        {
            return;
        }

        TsViewCallExpr viewCall = TryParseCallExpression(call, context).Value;
        if (TsViewCallMapper.IsMissingActTargetPlaceholder(viewCall))
        {
            errors.Add(new ToolError(targetPath, lineNumber, null, TsViewCallMapper.MissingActTargetErrorMessage, ToolDiagnosticSeverity.Error));
        }
    }

    private static MapResult<(int LineNumber, bool HasReturnDecl)> TryBindLocalDeclarations(
        IReadOnlyList<BodyNode> statements,
        ScriptConversionContext context,
        string? targetPath,
        List<ToolError> warnings,
        int lineNumber,
        bool hasReturnDecl)
    {
        int currentLine = lineNumber;
        bool currentHasReturnDecl = hasReturnDecl;
        foreach (BodyNode stmt in statements)
        {
            int statementLine = currentLine;
            switch (stmt)
            {
                case BodyVariableStatement varStmt:
                    {
                        BodyVariableDeclarationList declList = varStmt.DeclarationList;
                        if (declList.VariableKind == BodyVariableKind.Var && declList.Declarations.Count == 1)
                        {
                            BodyVariableDeclaration decl = declList.Declarations[0];
                            // 缺类型注解但有初值：警告并跳过 TryBindLocalDeclaration，类型在映射阶段从 SDK/方法签名返回值推断后做全路径一致性校验
                            if (decl.Type is null && decl.Initializer is not null)
                            {
                                string tsName = decl.Name.Text;
                                string rawName = TsViewIdentifierMap.FromTsIdentifier(tsName);
                                string normalizedTsName = TsViewIdentifierMap.ToTsIdentifier(rawName);
                                if (!string.Equals(tsName, normalizedTsName, StringComparison.Ordinal))
                                {
                                    return MapResult<(int, bool)>.Fail($"局部变量声明必须使用唯一映射的变量转义名：当前 {tsName}，应为 {normalizedTsName}。{TsViewNaming.EscapedNameAbuseHint}", statementLine);
                                }
                                if (context.GlobalVariables.ContainsKey(tsName))
                                {
                                    return MapResult<(int, bool)>.Fail($"局部变量声明 '{tsName}' 与全局变量重复。", statementLine);
                                }
                                if (context.ParameterNames.Contains(tsName))
                                {
                                    return MapResult<(int, bool)>.Fail($"局部变量声明 '{tsName}' 与方法形参重复。", statementLine);
                                }
                                if (string.Equals(decl.Name.Text, context.ExpectedReturnVarName, StringComparison.Ordinal))
                                {
                                    currentHasReturnDecl = true;
                                }
                                warnings.Add(new ToolError(targetPath, statementLine, null,
                                    $"var 声明缺少类型注解是错误写法: {tsName}。已自动从初始化调用推断类型。正确写法: var {tsName}: BeType = ...。", ToolDiagnosticSeverity.Warning));
                                currentLine++;
                                break;
                            }
                        }

                        MapResult<bool> bindResult = TryBindLocalDeclaration(varStmt, context);
                        if (!bindResult.Success)
                        {
                            return MapResult<(int, bool)>.Fail(bindResult.Error, statementLine);
                        }
                        if (bindResult.Value)
                        {
                            currentHasReturnDecl = true;
                        }
                        currentLine++;
                        break;
                    }

                case BodyIfStatement ifStmt:
                    currentLine++;
                    if (ifStmt.ThenStatement is BodyBlock thenBlock)
                    {
                        MapResult<(int LineNumber, bool HasReturnDecl)> thenResult = TryBindLocalDeclarations(thenBlock.Statements, context, targetPath, warnings, currentLine, currentHasReturnDecl);
                        if (!thenResult.Success)
                        {
                            return thenResult;
                        }
                        currentLine = thenResult.Value.LineNumber;
                        currentHasReturnDecl = thenResult.Value.HasReturnDecl;
                    }
                    if (ifStmt.IsDisabled)
                    {
                        currentLine++;
                    }
                    break;

                case BodyWhileStatement whileStmt:
                    currentLine++;
                    if (whileStmt.Body is BodyBlock whileBlock)
                    {
                        MapResult<(int LineNumber, bool HasReturnDecl)> whileResult = TryBindLocalDeclarations(whileBlock.Statements, context, targetPath, warnings, currentLine, currentHasReturnDecl);
                        if (!whileResult.Success)
                        {
                            return whileResult;
                        }
                        currentLine = whileResult.Value.LineNumber;
                        currentHasReturnDecl = whileResult.Value.HasReturnDecl;
                    }
                    if (whileStmt.IsDisabled)
                    {
                        currentLine++;
                    }
                    break;

                default:
                    currentLine++;
                    break;
            }
        }
        return MapResult<(int, bool)>.Ok((currentLine, currentHasReturnDecl));
    }

    private static MapResult<bool> TryBindLocalDeclaration(
        BodyVariableStatement varStmt,
        ScriptConversionContext context)
    {
        BodyVariableDeclarationList declList = varStmt.DeclarationList;
        if (declList.VariableKind != BodyVariableKind.Var)
        {
            return MapResult<bool>.Fail("局部变量仅允许 var 声明。");
        }

        if (declList.Declarations.Count != 1)
        {
            return MapResult<bool>.Fail("每条 var 语句仅允许一个变量声明。");
        }

        BodyVariableDeclaration decl = declList.Declarations[0];
        if (decl.Type switch { BodyTypeReference { TypeName: BodyIdentifier id } => id.Text, BodyIdentifier id => id.Text, _ => null } is not string typeName ||
            TsViewTypeUtils.TryParseBescriptVarType(typeName) is not BescriptVarType varType)
        {
            return MapResult<bool>.Fail($"var 声明缺少有效类型；仅允许: {AllowedLocalVarTypesText}。");
        }

        if (varType == BescriptVarType.Enum)
        {
            return MapResult<bool>.Fail("枚举值不允许声明为局部变量，必须以常量形式内联使用（如 BeEnum<ForceMode>.fromBeConst(ForceMode.Acceleration)）。");
        }

        if (!AllowedLocalVarTypes.Contains(varType))
        {
            return MapResult<bool>.Fail($"局部变量仅允许类型: {AllowedLocalVarTypesText}。");
        }

        string tsName = decl.Name.Text;
        string rawName = TsViewIdentifierMap.FromTsIdentifier(tsName);
        string normalizedTsName = TsViewIdentifierMap.ToTsIdentifier(rawName);

        // 命名规则：Agent 可能误用非规范化的转义名（如 X_abc 而非从原始名映射得到的规范化名称）
        if (!string.Equals(tsName, normalizedTsName, StringComparison.Ordinal))
        {
            return MapResult<bool>.Fail($"局部变量声明必须使用唯一映射的变量转义名：当前 {tsName}，应为 {normalizedTsName}。{TsViewNaming.EscapedNameAbuseHint}");
        }

        bool declaresReturnVar = string.Equals(tsName, context.ExpectedReturnVarName, StringComparison.Ordinal);
        if (declaresReturnVar &&
            context.ExpectedReturnType is not null &&
            TsViewTypeUtils.TryParseBescriptVarType(context.ExpectedReturnType) is BescriptVarType expectedReturnType &&
            expectedReturnType != varType)
        {
            return MapResult<bool>.Fail($"返回值声明类型必须为 {context.ExpectedReturnType}");
        }

        if (context.GlobalVariables.ContainsKey(tsName))
        {
            return MapResult<bool>.Fail($"局部变量声明 '{tsName}' 与全局变量重复。");
        }

        if (context.ParameterNames.Contains(tsName))
        {
            return MapResult<bool>.Fail($"局部变量声明 '{tsName}' 与方法形参重复。");
        }

        if (context.DeclaredVariables.TryGetValue(tsName, out BescriptVarType existing))
        {
            if (existing != varType)
            {
                return MapResult<bool>.Fail($"局部变量重复 var 声明且类型不一致: {tsName}");
            }
            return MapResult<bool>.Ok(declaresReturnVar);
        }

        context.DeclaredVariables[tsName] = varType;
        return MapResult<bool>.Ok(declaresReturnVar);
    }

    private static MapResult<int> TryMapBodyStatements(
        IReadOnlyList<BodyNode> statements,
        ScriptConversionContext context,
        int depth,
        int loopDepth,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        int currentLine = lineNumber;
        foreach (BodyNode stmt in statements)
        {
            bool isDisabled = GetStmtIsDisabled(stmt);

            MapResult<int> stmtResult = TryMapStatement(stmt, context, depth, loopDepth, isDisabled, currentLine, methodCalls, output);
            if (!stmtResult.Success)
            {
                return stmtResult;
            }
            currentLine = stmtResult.Value;
        }

        return MapResult<int>.Ok(currentLine);
    }

    private static MapResult<int> TryMapStatement(
        BodyNode stmt,
        ScriptConversionContext context,
        int depth,
        int loopDepth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        int statementLine = lineNumber;
        switch (stmt)
        {
            case BodyVariableStatement varStmt:
                {
                    MapResult<int> vr = TryMapVariableStatement(varStmt, context, depth, isDisabled, lineNumber, methodCalls, output);
                    if (!vr.Success)
                    {
                        return MapResult<int>.Fail(vr.Error, statementLine);
                    }
                    return MapResult<int>.Ok(vr.Value);
                }

            case BodyExpressionStatement exprStmt:
                {
                    MapResult<int> er = TryMapExpressionStatement(exprStmt.Expression, context, depth, loopDepth, isDisabled, lineNumber, methodCalls, output);
                    if (!er.Success)
                    {
                        return MapResult<int>.Fail(er.Error, statementLine);
                    }
                    return MapResult<int>.Ok(er.Value);
                }

            case BodyIfStatement ifStmt:
                {
                    MapResult<int> ifr = TryMapIfStatement(ifStmt, context, depth, loopDepth, isDisabled, lineNumber, methodCalls, output);
                    if (!ifr.Success)
                    {
                        return ifr;
                    }
                    return MapResult<int>.Ok(ifr.Value);
                }

            case BodyWhileStatement whileStmt:
                {
                    MapResult<int> wr = TryMapWhileStatement(whileStmt, context, depth, loopDepth, isDisabled, lineNumber, methodCalls, output);
                    if (!wr.Success)
                    {
                        return wr;
                    }
                    return MapResult<int>.Ok(wr.Value);
                }

            // ═══════════════════════════════════════════════════════════════════════════════
            // return / break 映射规范：
            //
            // BeScript 中只有 break()，其语义取决于上下文：
            //   - 在有效 while 内：跳出当前循环
            //   - 不在有效 while 内：结束脚本执行并返回值
            //
            // 正向映射（BeScript → TS）：
            //   - break() 在有效 while 内 → break
            //   - break() 不在有效 while 内 → return <retVarName>
            //
            // 反向映射（TS → BeScript）必须严格对称：
            //   - break 必须在有效 while 内，否则 fatal
            //   - return <retVarName> 必须不在有效 while 内，且标识符必须匹配声明的返回变量名，否则 fatal
            //   - 有效 while 内出现 return → fatal
            //   - 非有效 while 内出现 break → fatal
            //
            // "有效 while" 指至少一层未被禁用的 while（loopDepth > 0）。
            // ═══════════════════════════════════════════════════════════════════════════════
            case BodyReturnStatement retStmt:
                {
                    // return 只能出现在非有效 while 内（脚本顶层或全部 while 均被禁用）
                    if (loopDepth > 0)
                    {
                        return MapResult<int>.Fail("return 不能出现在有效 while 内部（至少一层未禁用的 while）。若要跳出循环请使用 break。", statementLine);
                    }

                    // 校验返回值标识符必须与声明的返回变量名一致
                    string? expectedRetVar = context.ExpectedReturnVarName;
                    if (retStmt.Expression is BodyIdentifier retId)
                    {
                        if (!string.Equals(retId.Text, expectedRetVar, StringComparison.Ordinal))
                        {
                            return MapResult<int>.Fail(
                                expectedRetVar is null
                                    ? $"此脚本无返回值，不应写 return {retId.Text}。"
                                    : $"return 的标识符必须是 {expectedRetVar}，而非 {retId.Text}。",
                                statementLine);
                        }
                    }
                    else if (retStmt.Expression is not null)
                    {
                        return MapResult<int>.Fail("return 后只能跟返回变量标识符，不能是表达式。", statementLine);
                    }
                    else
                    {
                        // return; 无返回值的情况
                        if (expectedRetVar is not null)
                        {
                            return MapResult<int>.Fail($"此脚本声明了返回值，应写 return {expectedRetVar}; 而非 return;。", statementLine);
                        }
                    }

                    int returnLine = lineNumber++;
                    FunCallSignature sig = new("break", "break", Array.Empty<BescriptCallArgument>(), SegmentSpans: [], null);
                    output.Add(new BreakAstNode(returnLine, depth, output.Count, "fun break()", sig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
                    return MapResult<int>.Ok(lineNumber);
                }

            case BodyBreakStatement:
                {
                    // break 只能出现在有效 while 内（至少一层未禁用的 while）
                    if (loopDepth <= 0)
                    {
                        string hint = context.ExpectedReturnVarName is not null
                            ? $"若要结束脚本请使用 return {context.ExpectedReturnVarName};。"
                            : "若要结束脚本请使用 return;。";
                        return MapResult<int>.Fail($"break 只能出现在有效 while 内部（至少一层未禁用的 while）。{hint}", statementLine);
                    }

                    FunCallSignature sig = new("break", "break", Array.Empty<BescriptCallArgument>(), SegmentSpans: [], null);
                    output.Add(new BreakAstNode(lineNumber++, depth, output.Count, "fun break()", sig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
                    return MapResult<int>.Ok(lineNumber);
                }

            case BodyContinueStatement:
                {
                    if (loopDepth <= 0)
                    {
                        return MapResult<int>.Fail("continue 只能出现在 while 内部。", statementLine);
                    }
                    FunCallSignature sig = new("continue", "continue", Array.Empty<BescriptCallArgument>(), SegmentSpans: [], null);
                    output.Add(new ContinueAstNode(lineNumber++, depth, output.Count, "fun continue()", sig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
                    return MapResult<int>.Ok(lineNumber);
                }

            case BodyEmptyStatement:
                lineNumber++;
                return MapResult<int>.Ok(lineNumber);

            case BodySingleLineComment singleLineComment:
                {
                    MapResult<int> cr = TryMapComment(singleLineComment.Text, isMultiLine: false, singleLineComment.IsDisabled, depth, lineNumber, output);
                    if (!cr.Success)
                    {
                        return MapResult<int>.Fail(cr.Error, statementLine);
                    }
                    return MapResult<int>.Ok(cr.Value);
                }

            case BodyMultiLineComment multiLineComment:
                {
                    MapResult<int> cr = TryMapComment(multiLineComment.Text, isMultiLine: true, multiLineComment.IsDisabled, depth, lineNumber, output);
                    if (!cr.Success)
                    {
                        return MapResult<int>.Fail(cr.Error, statementLine);
                    }
                    return MapResult<int>.Ok(cr.Value);
                }

            default:
                return MapResult<int>.Fail($"不支持的语句类型: {stmt.GetType().Name}", statementLine);
        }
    }

    /// <summary>
    /// 映射 TS var 声明到 BES 命令调用。
    ///
    /// BES 格式说明（重要，不可混淆）：
    /// - 左侧（变量声明）：类型 变量名=表达式（例如：Float a=GetFloat 0）
    ///   必须使用类型名（Float、Int等），不使用 var 关键字。
    /// - 右侧（实参位置）：var 变量名（例如：SetFloat var a）
    ///   var 关键字表示引用变量 a 作为实参。
    ///
    /// TS 视图格式：
    /// - 返回值变量可以保留裸声明：var ret: BeType。
    /// - 普通局部变量建议在首次赋值处内联声明：var a: BeType = call(...)。
    /// - 后续赋值：a = call(...)。
    ///
    /// 本方法处理的情况：
    /// - 裸 var 声明只进入方法级变量表。
    /// - 带初始化的 var 声明按同一行赋值写回 BeScript。
    /// - TS 视图只使用 var 关键字声明变量，禁止 const 或 let。
    /// </summary>
    private static MapResult<int> TryMapVariableStatement(
        BodyVariableStatement varStmt,
        ScriptConversionContext context,
        int depth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        BodyVariableDeclaration decl = varStmt.DeclarationList.Declarations[0];
        if (decl.Initializer is null)
        {
            lineNumber++;
            return MapResult<int>.Ok(lineNumber);
        }

        BodyBinaryExpression assignment = new(
            new BodyIdentifier(decl.Name.Text),
            BodyBinaryOperator.Equals,
            decl.Initializer);
        return TryMapAssignment(assignment, context, depth, isDisabled, lineNumber, methodCalls, output, decl.Type is null);
    }

    /// <summary>
    /// 递归降级布尔表达式为 SDK 调用：!a → G.bool.not(a)，a&amp;&amp;b → G.bool.and(a,b)，
    /// a||b → G.bool.or(a,b)，a==b → G.bool.equal(a,b)，a!=b → G.bool.xor(a,b)。
    /// 生成的 SDK 调用会带上布尔降级标记，后续展开时抑制重复 Warning。
    /// 调用表达式只降级其参数（不改变调用本身），确保嵌套布尔表达式在参数位置也能正确处理。
    /// </summary>
    private static BooleanLowerResult LowerBooleanExpression(BodyNode node, ScriptConversionContext context)
    {
        switch (node)
        {
            case BodyParenthesizedExpression parenthesized:
                return LowerBooleanExpression(parenthesized.Expression, context);

            case BodyNotExpression { Operand: var operand }:
                {
                    BooleanLowerResult loweredOperand = LowerBooleanExpression(operand, context);
                    BodyCall call = BuildBooleanSdkCall(context, "not", loweredOperand.Node);
                    return new BooleanLowerResult(call, true);
                }

            case BodyBinaryExpression { Operator: BodyBinaryOperator.AmpersandAmpersand } binary:
                return LowerBooleanBinaryExpression(binary, context, "and");

            case BodyBinaryExpression { Operator: BodyBinaryOperator.BarBar } binary:
                return LowerBooleanBinaryExpression(binary, context, "or");

            case BodyBinaryExpression { Operator: BodyBinaryOperator.EqualsEquals or BodyBinaryOperator.EqualsEqualsEquals } binary:
                return LowerBooleanBinaryExpression(binary, context, "equal");

            case BodyBinaryExpression { Operator: BodyBinaryOperator.ExclamationEquals or BodyBinaryOperator.ExclamationEqualsEquals } binary:
                return LowerBooleanBinaryExpression(binary, context, "xor");

            case BodyBinaryExpression binary:
                return new BooleanLowerResult(binary, false);

            case BodyCall call:
                {
                    bool rewritten = false;
                    List<BodyNode> args = new(call.Arguments.Count);
                    foreach (BodyNode arg in call.Arguments)
                    {
                        BooleanLowerResult loweredArg = LowerBooleanExpression(arg, context);
                        rewritten |= loweredArg.Rewritten;
                        args.Add(loweredArg.Node);
                    }
                    if (!rewritten)
                    {
                        return new BooleanLowerResult(call, false);
                    }
                    return new BooleanLowerResult(call with { Arguments = args }, true);
                }

            default:
                return new BooleanLowerResult(node, false);
        }
    }

    private static BooleanLowerResult LowerBooleanBinaryExpression(
        BodyBinaryExpression binary,
        ScriptConversionContext context,
        string methodName)
    {
        BooleanLowerResult left = LowerBooleanExpression(binary.Left, context);
        BooleanLowerResult right = LowerBooleanExpression(binary.Right, context);
        BodyCall call = BuildBooleanSdkCall(context, methodName, left.Node, right.Node);
        return new BooleanLowerResult(call, true);
    }

    private static BodyCall BuildBooleanSdkCall(
        ScriptConversionContext context,
        string methodName,
        params BodyNode[] args)
    {
        BodyCall call = new(
            new BodyPropertyAccess(
                new BodyPropertyAccess(new BodyIdentifier("G"), new BodyIdentifier("bool")),
                new BodyIdentifier(methodName)),
            args)
        {
            IsSyntheticBooleanLowering = true
        };
        return call;
    }

    private static void AddBooleanRewriteWarning(ScriptConversionContext context, int lineNumber, string message)
    {
        context.ExpansionWarnings.Add(new ToolError(
            context.ExpansionTargetPath,
            lineNumber,
            null,
            message,
            ToolDiagnosticSeverity.Warning));
    }

    private static BodyCall LowerCallBooleanExpressions(
        BodyCall call,
        ScriptConversionContext context,
        int lineNumber)
    {
        BooleanLowerResult lowered = LowerBooleanExpression(call, context);
        if (lowered.Rewritten)
        {
            AddBooleanRewriteWarning(context, lineNumber, "TS 布尔表达式是错误写法，禁止直接使用 !/&&/||/==/!= 等运算符。已自动改写为 SDK 调用形式。正确写法应使用 G.bool.not/G.bool.and/G.bool.or/G.bool.equal/G.bool.xor 等方法。");
        }

        return (BodyCall)lowered.Node;
    }

    /// <summary>
    /// 控制流条件降级：unwrap await → 拒绝delay → 布尔表达式降级 → 裸变量/常量包装为 G.create.bool 调用。
    /// if/while 共用，controlName 用于错误消息区分。
    /// </summary>
    private static MapResult<BodyCall> LowerControlCondition(
        BodyNode condition,
        ScriptConversionContext context,
        int lineNumber,
        string controlName)
    {
        if (condition is BodyAwaitExpression { Argument: not BodyCall })
        {
            return MapResult<BodyCall>.Fail("条件必须是调用表达式。", lineNumber);
        }

        BodyNode actualCondition = condition is BodyAwaitExpression { Argument: var awaitedCondition }
            ? awaitedCondition
            : condition;

        if (actualCondition is BodyCall awaitWrappedCall &&
            awaitWrappedCall.Expression is BodyIdentifier awaitWrappedIdent &&
            string.Equals(awaitWrappedIdent.Text, "delay", StringComparison.Ordinal))
        {
            return MapResult<BodyCall>.Fail($"{controlName} 条件不允许使用 delay。原版条件位只接受调用本体。", lineNumber);
        }

        BooleanLowerResult lowered = LowerBooleanExpression(actualCondition, context);
        if (lowered.Rewritten)
        {
            AddBooleanRewriteWarning(context, lineNumber, "if/while 条件中直接写布尔表达式是错误写法。已自动改写为 SDK 调用形式。正确写法应将布尔表达式拆为独立 var 声明或使用 G.create.bool 包装。");
        }

        BodyNode loweredNode = lowered.Node;
        if (loweredNode is BodyIdentifier or BodyPropertyAccess { Expression: BodyThisExpression })
        {
            AddBooleanRewriteWarning(context, lineNumber, "if/while 条件中直接写裸 Bool 变量是错误写法。已自动改写为 G.create.bool 调用形式。");
            return MapResult<BodyCall>.Ok(new BodyCall(
                new BodyPropertyAccess(
                    new BodyPropertyAccess(new BodyIdentifier("G"), new BodyIdentifier("create")),
                    new BodyIdentifier("bool")),
                new BodyNode[] { loweredNode }));
        }

        if (loweredNode is BodyTrueLiteral or BodyFalseLiteral)
        {
            BodyCall constArg = new(
                new BodyPropertyAccess(new BodyIdentifier("BeBool"), new BodyIdentifier("fromBeConst")),
                new BodyNode[] { new BodyStringLiteral(loweredNode is BodyTrueLiteral ? "1" : "0") });
            return MapResult<BodyCall>.Ok(new BodyCall(
                new BodyPropertyAccess(
                    new BodyPropertyAccess(new BodyIdentifier("G"), new BodyIdentifier("create")),
                    new BodyIdentifier("bool")),
                new BodyNode[] { constArg }));
        }

        if (loweredNode is BodyCall call)
        {
            return MapResult<BodyCall>.Ok(call);
        }

        return MapResult<BodyCall>.Fail("条件必须是调用表达式。", lineNumber);
    }

    private static MapResult<int> TryMapExpressionStatement(
        BodyNode expr,
        ScriptConversionContext context,
        int depth,
        int loopDepth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        // 赋值：this.<global> = call
        if (expr is BodyBinaryExpression binary && binary.Operator == BodyBinaryOperator.Equals)
        {
            return TryMapAssignment(binary, context, depth, isDisabled, lineNumber, methodCalls, output, inferLocalDeclarationType: false);
        }

        // void await 表达式 → 丢弃返回值的 Delay 或 Async
        if (expr is BodyVoidExpression voidExpr && voidExpr.Argument is BodyAwaitExpression voidAwaitExpr)
        {
            BodyCall? voidActualCall;
            BescriptStatementKind voidStmtKind;

            if (voidAwaitExpr.Argument is BodyCall voidOuterCall &&
                voidOuterCall.Expression is BodyIdentifier voidDelayIdent &&
                string.Equals(voidDelayIdent.Text, "delay", StringComparison.Ordinal) &&
                voidOuterCall.Arguments.Count == 1)
            {
                // TS 视图 await 纯按 SDK 判定，delay 参数中的 await 是合法的，剥离取内部调用
                BodyNode voidDelayArg = voidOuterCall.Arguments[0] is BodyAwaitExpression { Argument: var voidAwaitedDelayArg }
                    ? voidAwaitedDelayArg
                    : voidOuterCall.Arguments[0];
                if (voidDelayArg is not BodyCall voidInnerDelayCall)
                {
                    return MapResult<int>.Fail("delay 仅接受调用表达式作为参数。");
                }
                voidStmtKind = BescriptStatementKind.Delay;
                voidActualCall = voidInnerDelayCall;
            }
            else if (voidAwaitExpr.Argument is BodyCall voidAsyncCall)
            {
                voidStmtKind = BescriptStatementKind.Async;
                voidActualCall = voidAsyncCall;
            }
            else
            {
                return MapResult<int>.Fail("void await 仅接受调用表达式作为参数。");
            }

            voidActualCall = LowerCallBooleanExpressions(voidActualCall, context, lineNumber);

            MapResult<BodyCall> expandResult = TryExpandNestedCalls(voidActualCall, context, depth, isDisabled, lineNumber, methodCalls, output);
            if (!expandResult.Success)
            {
                return MapResult<int>.FailTagged(expandResult.Error, expandResult.DiagnosticTag);
            }
            voidActualCall = expandResult.Value;
            MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(voidActualCall, context);
            if (!parseResult.Success)
            {
                return MapResult<int>.FailTagged(parseResult.Error, parseResult.DiagnosticTag);
            }
            TsViewCallExpr voidInner = parseResult.Value;
            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> voidMapResult = TsViewCallMapper.MapToBescript(voidInner, context);
            if (!voidMapResult.Success)
            {
                return MapResult<int>.FailTagged(voidMapResult.Error, voidMapResult.DiagnosticTag);
            }
            BescriptCallSignature voidSig = voidMapResult.Value.Sig;
            ParseResult<BescriptCallSignature> voidValidation = ValidateDynamicMethodNameArgs(context, voidInner, voidSig, lineNumber, depth, output);
            if (!voidValidation.Success)
            {
                return MapResult<int>.Fail(voidValidation.Error);
            }
            voidSig = voidValidation.Value;
            if (voidInner is { Kind: TsViewCallKind.Act, MethodName: { } actName })
            {
                methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber, voidInner.UseThisTarget));
            }

            // Delay 不允许携带返回值（与 if/while 同理）
            // void await 的 Async 需保留裸 '=' 前缀
            if (voidStmtKind != BescriptStatementKind.Delay)
            {
                voidSig = voidSig with { DiscardsReturn = true };
            }
            output.Add(voidStmtKind == BescriptStatementKind.Delay ? new DelayAstNode(lineNumber++, depth, output.Count, string.Empty, voidSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled) : new AsyncAstNode(lineNumber++, depth, output.Count, string.Empty, voidSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
            return MapResult<int>.Ok(lineNumber);
        }

        // await 表达式 → Delay 或 Async
        if (expr is BodyAwaitExpression awaitExpr)
        {
            // 判断 Delay vs Async：await delay(innerCall) → Delay，其他 → Async
            BodyCall? actualCall;
            BescriptStatementKind awaitStmtKind;

            if (awaitExpr.Argument is BodyCall outerCall &&
                outerCall.Expression is BodyIdentifier delayIdent &&
                string.Equals(delayIdent.Text, "delay", StringComparison.Ordinal) &&
                outerCall.Arguments.Count == 1)
            {
                // TS 视图 await 纯按 SDK 判定，delay 参数中的 await 是合法的，剥离取内部调用
                BodyNode delayArg = outerCall.Arguments[0] is BodyAwaitExpression { Argument: var awaitedDelayArg }
                    ? awaitedDelayArg
                    : outerCall.Arguments[0];
                if (delayArg is not BodyCall innerDelayCall)
                {
                    return MapResult<int>.Fail("delay 仅接受调用表达式作为参数。");
                }
                awaitStmtKind = BescriptStatementKind.Delay;
                actualCall = innerDelayCall;
            }
            else if (awaitExpr.Argument is BodyCall asyncCall)
            {
                // await call → Async
                awaitStmtKind = BescriptStatementKind.Async;
                actualCall = asyncCall;
            }
            else
            {
                return MapResult<int>.Fail("await 仅接受调用表达式作为参数。");
            }

            actualCall = LowerCallBooleanExpressions(actualCall, context, lineNumber);

            MapResult<BodyCall> expandResult = TryExpandNestedCalls(actualCall, context, depth, isDisabled, lineNumber, methodCalls, output);
            if (!expandResult.Success)
            {
                return MapResult<int>.FailTagged(expandResult.Error, expandResult.DiagnosticTag);
            }
            actualCall = expandResult.Value;
            MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(actualCall, context);
            if (!parseResult.Success)
            {
                return MapResult<int>.FailTagged(parseResult.Error, parseResult.DiagnosticTag);
            }
            TsViewCallExpr awaitInner = parseResult.Value;
            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> awaitMapResult = TsViewCallMapper.MapToBescript(awaitInner, context);
            if (!awaitMapResult.Success)
            {
                return MapResult<int>.FailTagged(awaitMapResult.Error, awaitMapResult.DiagnosticTag);
            }
            BescriptCallSignature awaitSig = awaitMapResult.Value.Sig;
            ActTargetInfo? awaitActTarget = awaitMapResult.Value.ResolvedActTarget;
            ParseResult<BescriptCallSignature> awaitValidation = ValidateDynamicMethodNameArgs(context, awaitInner, awaitSig, lineNumber, depth, output);
            if (!awaitValidation.Success)
            {
                return MapResult<int>.Fail(awaitValidation.Error);
            }
            awaitSig = awaitValidation.Value;

            if (awaitInner is { Kind: TsViewCallKind.Act, MethodName: { } actName })
            {
                methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber, awaitInner.UseThisTarget));
            }

            // Delay/Async 不允许携带返回值（与 if/while 同理）
            // Async 按目标签名重算 = 前缀；Delay 固定无返回值
            BescriptCallSignature finalAwaitSig = awaitStmtKind != BescriptStatementKind.Delay &&
                TsViewCallMapper.ShouldPrefixReturn(awaitInner, awaitSig, context, awaitActTarget)
                ? awaitSig with { DiscardsReturn = true }
                : awaitSig;

            output.Add(awaitStmtKind == BescriptStatementKind.Delay ? new DelayAstNode(lineNumber++, depth, output.Count, string.Empty, finalAwaitSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled) : new AsyncAstNode(lineNumber++, depth, output.Count, string.Empty, finalAwaitSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
            return MapResult<int>.Ok(lineNumber);
        }

        // 普通调用
        if (expr is BodyCall call)
        {
            call = LowerCallBooleanExpressions(call, context, lineNumber);

            MapResult<BodyCall> expandResult = TryExpandNestedCalls(call, context, depth, isDisabled, lineNumber, methodCalls, output);
            if (!expandResult.Success)
            {
                return MapResult<int>.FailTagged(expandResult.Error, expandResult.DiagnosticTag);
            }
            call = expandResult.Value;
            MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(call, context);
            if (!parseResult.Success)
            {
                return MapResult<int>.FailTagged(parseResult.Error, parseResult.DiagnosticTag);
            }
            TsViewCallExpr tsCall = parseResult.Value;

            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> mapResult = TsViewCallMapper.MapToBescript(tsCall, context);
            if (!mapResult.Success)
            {
                return MapResult<int>.FailTagged(mapResult.Error, mapResult.DiagnosticTag);
            }
            BescriptCallSignature sig = mapResult.Value.Sig;
            ActTargetInfo? actTargetInfo = mapResult.Value.ResolvedActTarget;
            ParseResult<BescriptCallSignature> sigValidation = ValidateDynamicMethodNameArgs(context, tsCall, sig, lineNumber, depth, output);
            if (!sigValidation.Success)
            {
                return MapResult<int>.Fail(sigValidation.Error);
            }
            sig = sigValidation.Value;

            if (tsCall is { Kind: TsViewCallKind.Act, MethodName: { } actName })
            {
                methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber, tsCall.UseThisTarget));
            }

            BescriptCallSignature finalSig = TsViewCallMapper.ShouldPrefixReturn(tsCall, sig, context, actTargetInfo)
                ? sig with { DiscardsReturn = true }
                : sig;

            output.Add(new CallAstNode(lineNumber++, depth, output.Count, string.Empty, finalSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
            return MapResult<int>.Ok(lineNumber);
        }

        return MapResult<int>.Fail("不支持的表达式语句。TS 视图仅支持：控制流 if/while/break/continue/return（不支持 else）；表达式语句仅允许赋值（变量 = 调用）或调用（G.xxx/Act.xxx 等）。检验参照 temp/toolkit/bescript_reference/sdk.ts 获取底层SDK声明");
    }

    private static MapResult<int> TryMapComment(
        string rawText,
        bool isMultiLine,
        bool isDisabled,
        int depth,
        int lineNumber,
        List<BescriptAstNode> output)
    {
        string content = rawText;

        if (isMultiLine)
        {
            content = BescriptSyntax.TsDisabledStart + content + BescriptSyntax.TsDisabledEnd;
        }
        else
        {
            // 普通注释：统一输出为 "//<原文>"（无空格）
            // 规范: TS视图普通 "//<text>"注释 ↔ BeScript "//<text>"
            if (!content.StartsWith("//", StringComparison.Ordinal))
            {
                content = "//" + content;
            }
        }

        output.Add(new SkipAstNode(
            lineNumber++,
            depth,
            output.Count,
            content,
            Array.Empty<BescriptAstNode>(),
            LineStartColumns: [0],
            isDisabled));
        return MapResult<int>.Ok(lineNumber);
    }

    /// <summary>
    /// 映射赋值语句。
    /// </summary>
    /// <remarks>
    /// <para><b>返回类型优先级规则</b></para>
    /// <para>
    /// 当赋值右侧存在 <c>as BeXxx</c> 强转时，<c>as</c> 中的类型具有最高优先级，
    /// 直接作为 BES 声明的返回类型，不再查询方法签名。
    /// </para>
    /// <para>
    /// 这是正向转换中"类型不一致时添加 as 强转"的逆操作，确保 roundtrip 一致性。
    /// </para>
    /// </remarks>
    private static MapResult<int> TryMapAssignment(
        BodyBinaryExpression binary,
        ScriptConversionContext context,
        int depth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output,
        bool inferLocalDeclarationType)
    {
        string targetName;
        BescriptVarType varType;
        bool isGlobal = false;

        if (binary.Left is BodyPropertyAccess { Expression: BodyThisExpression, Name: BodyIdentifier prop })
        {
            targetName = prop.Text;
            if (!context.GlobalVariables.TryGetValue(targetName, out varType))
            {
                return MapResult<int>.FailTagged(
                    $"未知全局变量: {targetName}。全局变量必须在 TS 视图的全局变量声明区声明，且{AllowedVarTypesHint}",
                    TsViewDiagnosticTag.ForSubject(TsViewDiagnosticCodes.MissingGlobalVariable, targetName));
            }
            isGlobal = true;
        }
        else if (binary.Left is BodyIdentifier ident)
        {
            targetName = ident.Text;
            if (!context.DeclaredVariables.TryGetValue(targetName, out varType))
            {
                if (!inferLocalDeclarationType)
                {
                    return MapResult<int>.Fail($"未知变量: {targetName}。局部变量必须使用 var 声明；全局变量必须使用 this.<变量名>。");
                }
            }
        }
        else
        {
            return MapResult<int>.Fail("仅允许 this.<全局变量> 或已声明变量赋值。");
        }

        // 穿透 as 强转：x = call() as Type → x = call()，并提取类型信息
        string? asReturnType = null;
        BodyNode rhsExpr = binary.Right;
        while (rhsExpr is BodyAsExpression asExpr)
        {
            // 提取 as 断言中的类型（最外层的类型优先）
            if (asReturnType == null && asExpr.Type is BodyTypeReference { TypeName: BodyIdentifier typeId })
            {
                asReturnType = typeId.Text;
            }
            rhsExpr = asExpr.Expression;
        }

        // await 赋值：x = await call() 或 x = await G.delay(call())
        if (rhsExpr is BodyAwaitExpression awaitAssignExpr)
        {
            BodyCall? awaitAssignActualCall;
            BescriptStatementKind awaitAssignStmtKind;

            if (awaitAssignExpr.Argument is BodyCall outerAwaitCall &&
                outerAwaitCall.Expression is BodyIdentifier awaitDelayIdent &&
                string.Equals(awaitDelayIdent.Text, "delay", StringComparison.Ordinal) &&
                outerAwaitCall.Arguments.Count == 1)
            {
                // TS 视图 await 纯按 SDK 判定，delay 参数中的 await 是合法的，剥离取内部调用
                BodyNode assignDelayArg = outerAwaitCall.Arguments[0] is BodyAwaitExpression { Argument: var awaitedAssignDelayArg }
                    ? awaitedAssignDelayArg
                    : outerAwaitCall.Arguments[0];
                if (assignDelayArg is not BodyCall innerAwaitDelayCall)
                {
                    return MapResult<int>.Fail("delay 仅接受调用表达式作为参数。");
                }
                awaitAssignStmtKind = BescriptStatementKind.Delay;
                awaitAssignActualCall = innerAwaitDelayCall;
            }
            else if (awaitAssignExpr.Argument is BodyCall awaitAssignInnerCall)
            {
                awaitAssignStmtKind = BescriptStatementKind.Async;
                awaitAssignActualCall = awaitAssignInnerCall;
            }
            else
            {
                return MapResult<int>.Fail("await 仅接受调用表达式作为参数。");
            }

            awaitAssignActualCall = LowerCallBooleanExpressions(awaitAssignActualCall, context, lineNumber);

            MapResult<BodyCall> expandResult = TryExpandNestedCalls(awaitAssignActualCall, context, depth, isDisabled, lineNumber, methodCalls, output);
            if (!expandResult.Success)
            {
                return MapResult<int>.FailTagged(expandResult.Error, expandResult.DiagnosticTag);
            }
            awaitAssignActualCall = expandResult.Value;
            MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(awaitAssignActualCall, context);
            if (!parseResult.Success)
            {
                return MapResult<int>.FailTagged(parseResult.Error, parseResult.DiagnosticTag);
            }
            TsViewCallExpr awaitAssignInner = parseResult.Value;
            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> awaitAssignMapResult = TsViewCallMapper.MapToBescript(awaitAssignInner, context);
            if (!awaitAssignMapResult.Success)
            {
                return MapResult<int>.FailTagged(awaitAssignMapResult.Error, awaitAssignMapResult.DiagnosticTag);
            }
            BescriptCallSignature awaitAssignSig = awaitAssignMapResult.Value.Sig;
            ActTargetInfo? awaitAssignActTarget = awaitAssignMapResult.Value.ResolvedActTarget;
            ParseResult<BescriptCallSignature> awaitAssignValidation = ValidateDynamicMethodNameArgs(context, awaitAssignInner, awaitAssignSig, lineNumber, depth, output);
            if (!awaitAssignValidation.Success)
            {
                return MapResult<int>.Fail(awaitAssignValidation.Error);
            }
            awaitAssignSig = awaitAssignValidation.Value;
            if (awaitAssignInner is { Kind: TsViewCallKind.Act, MethodName: { } actAssignName })
            {
                methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actAssignName, lineNumber, awaitAssignInner.UseThisTarget));
            }

            string awaitBesName = TsViewIdentifierMap.FromTsIdentifier(targetName);
            if (isGlobal && !context.DeclaredVariables.ContainsKey(targetName))
            {
                context.DeclaredVariables[targetName] = varType;
            }

            string awaitResolvedType;
            if (inferLocalDeclarationType)
            {
                if (asReturnType is not null)
                {
                    awaitResolvedType = TsViewTypeUtils.ConvertToTsTypeName(asReturnType);
                }
                else
                {
                    string? awaitReturnType = ResolveCallReturnType(awaitAssignInner, awaitAssignSig, context, awaitAssignActTarget);
                    if (awaitReturnType is null)
                        return MapResult<int>.Fail($"var 声明缺少有效类型；仅允许: {AllowedLocalVarTypesText}。");
                    awaitResolvedType = TsViewTypeUtils.ConvertToTsTypeName(awaitReturnType);
                }
            }
            else
            {
                awaitResolvedType = ResolveAssignmentReturnTypeName(
                    asReturnType, awaitAssignInner, awaitAssignSig, context, awaitAssignActTarget, varType);
            }
            MapResult<string> awaitTypeNameResult = BindInferredVarType(targetName, awaitResolvedType, context, lineNumber, inferLocalDeclarationType);
            if (!awaitTypeNameResult.Success)
                return MapResult<int>.Fail(awaitTypeNameResult.Error);
            string awaitTypeName = awaitTypeNameResult.Value;
            awaitAssignSig = awaitAssignSig with { ReturnVar = new ReturnVarDecl(awaitTypeName, awaitBesName) };
            output.Add(awaitAssignStmtKind == BescriptStatementKind.Delay ? new DelayAstNode(lineNumber++, depth, output.Count, string.Empty, awaitAssignSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled) : new AsyncAstNode(lineNumber++, depth, output.Count, string.Empty, awaitAssignSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
            return MapResult<int>.Ok(lineNumber);
        }

        BooleanLowerResult loweredRhs = LowerBooleanExpression(rhsExpr, context);
        if (loweredRhs.Rewritten)
        {
            AddBooleanRewriteWarning(context, lineNumber, "TS 布尔表达式是错误写法，禁止直接使用 !/&&/||/==/!= 等运算符。已自动改写为 SDK 调用形式。正确写法应使用 G.bool.not/G.bool.and/G.bool.or/G.bool.equal/G.bool.xor 等方法。");
            rhsExpr = loweredRhs.Node;
        }

        // ── 场景 2：变量复制 auto-fix（RHS 是 BodyIdentifier）──
        if (rhsExpr is BodyIdentifier sourceIdent)
        {
            MapResult<BescriptCallSignature?> varCopyResult = TryAutoFixVariableCopy(sourceIdent, context);
            if (!varCopyResult.Success)
            {
                return MapResult<int>.Fail(varCopyResult.Error);
            }
            BescriptCallSignature? varCopySig = varCopyResult.Value;
            if (varCopySig is not null)
            {
                string besNameCopy = TsViewIdentifierMap.FromTsIdentifier(targetName);
                if (isGlobal && !context.DeclaredVariables.ContainsKey(targetName))
                    context.DeclaredVariables[targetName] = varType;
                string copyResolvedType;
                if (inferLocalDeclarationType)
                {
                    if (asReturnType is not null)
                        copyResolvedType = TsViewTypeUtils.ConvertToTsTypeName(asReturnType);
                    else if (context.DeclaredVariables.TryGetValue(sourceIdent.Text, out BescriptVarType sourceLocalType))
                        copyResolvedType = sourceLocalType.ToString();
                    else
                        copyResolvedType = context.GlobalVariables[sourceIdent.Text].ToString();
                }
                else
                {
                    copyResolvedType = asReturnType is not null
                        ? TsViewTypeUtils.ConvertToTsTypeName(asReturnType)
                        : varType.ToString();
                }
                MapResult<string> copyTypeNameResult = BindInferredVarType(targetName, copyResolvedType, context, lineNumber, inferLocalDeclarationType);
                if (!copyTypeNameResult.Success)
                    return MapResult<int>.Fail(copyTypeNameResult.Error);
                string copyTypeName = copyTypeNameResult.Value;
                varCopySig = varCopySig with { ReturnVar = new ReturnVarDecl(copyTypeName, besNameCopy) };
                output.Add(new CallAstNode(lineNumber++, depth, output.Count, string.Empty, varCopySig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
                context.ExpansionWarnings.Add(new ToolError(context.ExpansionTargetPath, lineNumber - 1, null, $"`{sourceIdent.Text} = {targetName}` 变量直接复制是错误写法。已自动改写为 create/copy/ref 调用。正确写法：赋值右侧必须是调用表达式，如 ret = G.create.float(BeFloat.fromBeConst(\"1\"))。", ToolDiagnosticSeverity.Warning));
                return MapResult<int>.Ok(lineNumber);
            }
        }

        if (rhsExpr is not BodyCall rhs)
        {
            return MapResult<int>.Fail("赋值右侧必须为调用表达式，禁止使用 fromBeConst 直接赋值或变量直接复制。若需要赋值常量,应使用`G.create.*(支持创建:bool/bytes/canvas/color/dict/float/icon/listN/long/string/struct)`或`G.creatVariable.*(Vector3/device/mech/player)`");
        }

        // ── 场景 1：fromBeConst 直接赋值规范化 ──
        // fromBeConst 只能作为调用参数位常量；赋值位出现时必须写回为对应 create 调用。
        // AI幻觉总是导致滥用这个写法,这是高频触发路径,必须考虑到!必须改写! 但是G.create.string()内部嵌套需与别的改写规则配合: 对于6类允许字面量的 直接压成String字面量;其他的照常拆出去
        // 这是 TS 视图写回规范autofix改写流程，不是兼容兜底，禁止删除成硬失败。
        BescriptCallSignature? autoFixResult = TryAutoFixBeConstAssignment(rhs, context);
        if (autoFixResult is not null)
        {
            string besNameAuto = TsViewIdentifierMap.FromTsIdentifier(targetName);
            if (isGlobal && !context.DeclaredVariables.ContainsKey(targetName))
                context.DeclaredVariables[targetName] = varType;
            string autoResolvedType;
            if (inferLocalDeclarationType)
            {
                if (asReturnType is not null)
                {
                    autoResolvedType = TsViewTypeUtils.ConvertToTsTypeName(asReturnType);
                }
                else
                {
                    string? autoReturnType = context.MethodRegistry.GetByPath(autoFixResult.FunctionName, MethodCategory.GlobalFunc)?.ReturnType;
                    if (autoReturnType is null)
                        return MapResult<int>.Fail($"var 声明缺少有效类型；仅允许: {AllowedLocalVarTypesText}。");
                    autoResolvedType = TsViewTypeUtils.ConvertToTsTypeName(autoReturnType);
                }
            }
            else
            {
                autoResolvedType = asReturnType is not null
                    ? TsViewTypeUtils.ConvertToTsTypeName(asReturnType)
                    : varType.ToString();
            }
            MapResult<string> autoTypeNameResult = BindInferredVarType(targetName, autoResolvedType, context, lineNumber, inferLocalDeclarationType);
            if (!autoTypeNameResult.Success)
                return MapResult<int>.Fail(autoTypeNameResult.Error);
            string autoTypeName = autoTypeNameResult.Value;
            autoFixResult = autoFixResult with { ReturnVar = new ReturnVarDecl(autoTypeName, besNameAuto) };
            output.Add(new CallAstNode(lineNumber++, depth, output.Count, string.Empty, autoFixResult, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
            context.ExpansionWarnings.Add(new ToolError(context.ExpansionTargetPath, lineNumber - 1, null, "`fromBeConst` 直接赋值是错误写法。已自动改写为 create 调用。正确写法：赋值右侧必须是调用表达式，如 ret = G.create.float(BeFloat.fromBeConst(\"1\"))。", ToolDiagnosticSeverity.Warning));
            return MapResult<int>.Ok(lineNumber);
        }

        MapResult<BodyCall> expandResult2 = TryExpandNestedCalls(rhs, context, depth, isDisabled, lineNumber, methodCalls, output);
        if (!expandResult2.Success)
        {
            return MapResult<int>.FailTagged(expandResult2.Error, expandResult2.DiagnosticTag);
        }
        rhs = expandResult2.Value;
        MapResult<TsViewCallExpr> parseResult2 = TryParseCallExpression(rhs, context);
        if (!parseResult2.Success)
        {
            return MapResult<int>.FailTagged(parseResult2.Error, parseResult2.DiagnosticTag);
        }
        TsViewCallExpr tsCall = parseResult2.Value;

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> normalMapResult = TsViewCallMapper.MapToBescript(tsCall, context);
        if (!normalMapResult.Success)
        {
            return MapResult<int>.FailTagged(normalMapResult.Error, normalMapResult.DiagnosticTag);
        }
        BescriptCallSignature sigNormal = normalMapResult.Value.Sig;
        ActTargetInfo? actTargetNormal = normalMapResult.Value.ResolvedActTarget;

        ParseResult<BescriptCallSignature> sigNormalValidation = ValidateDynamicMethodNameArgs(context, tsCall, sigNormal, lineNumber, depth, output);
        if (!sigNormalValidation.Success)
        {
            return MapResult<int>.Fail(sigNormalValidation.Error);
        }
        sigNormal = sigNormalValidation.Value;

        if (tsCall is { Kind: TsViewCallKind.Act, MethodName: { } actName })
        {
            methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber));
        }

        string besNameNormal = TsViewIdentifierMap.FromTsIdentifier(targetName);
        if (isGlobal && !context.DeclaredVariables.ContainsKey(targetName))
        {
            context.DeclaredVariables[targetName] = varType;
        }

        // as 强转类型具有最高优先级，直接使用
        string normalResolvedType;
        if (inferLocalDeclarationType)
        {
            if (asReturnType is not null)
            {
                normalResolvedType = TsViewTypeUtils.ConvertToTsTypeName(asReturnType);
            }
            else
            {
                string? callReturnType = ResolveCallReturnType(tsCall, sigNormal, context, actTargetNormal);
                if (callReturnType is null)
                    return MapResult<int>.Fail($"var 声明缺少有效类型；仅允许: {AllowedLocalVarTypesText}。");
                normalResolvedType = TsViewTypeUtils.ConvertToTsTypeName(callReturnType);
            }
        }
        else
        {
            normalResolvedType = ResolveAssignmentReturnTypeName(
                asReturnType, tsCall, sigNormal, context, actTargetNormal, varType);
        }
        MapResult<string> typeNameNormalResult = BindInferredVarType(targetName, normalResolvedType, context, lineNumber, inferLocalDeclarationType);
        if (!typeNameNormalResult.Success)
            return MapResult<int>.Fail(typeNameNormalResult.Error);
        string typeNameNormal = typeNameNormalResult.Value;
        sigNormal = sigNormal with { ReturnVar = new ReturnVarDecl(typeNameNormal, besNameNormal) };
        output.Add(new CallAstNode(lineNumber++, depth, output.Count, string.Empty, sigNormal, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));
        return MapResult<int>.Ok(lineNumber);
    }

    /// <summary>
    /// 标识符全路径类型一致性校验。
    /// 首次赋值时从 SDK/方法签名返回值推断类型并注册，后续赋值时校验类型自洽。
    /// 当 inferLocalDeclarationType 为 false 时原样返回 typeName。
    /// </summary>
    private static MapResult<string> BindInferredVarType(
        string targetName, string inferredTypeName, ScriptConversionContext context, int lineNumber,
        bool inferLocalDeclarationType)
    {
        if (!inferLocalDeclarationType)
            return MapResult<string>.Ok(inferredTypeName);

        if (TsViewTypeUtils.TryParseBescriptVarType(inferredTypeName) is not BescriptVarType inferredType ||
            !AllowedLocalVarTypes.Contains(inferredType))
            return MapResult<string>.Fail($"var 声明缺少有效类型；仅允许: {AllowedLocalVarTypesText}。");

        if (string.Equals(targetName, context.ExpectedReturnVarName, StringComparison.Ordinal) &&
            context.ExpectedReturnType is not null &&
            TsViewTypeUtils.TryParseBescriptVarType(context.ExpectedReturnType) is BescriptVarType expectedReturnType &&
            expectedReturnType != inferredType)
            return MapResult<string>.Fail($"返回值声明类型必须为 {context.ExpectedReturnType}");

        if (context.DeclaredVariables.TryGetValue(targetName, out BescriptVarType existing))
        {
            // 全路径类型一致性：同一个标识符在全方法范围内类型必须自洽，不允许中途转变
            if (existing != inferredType)
                return MapResult<string>.Fail($"变量 '{targetName}' 全路径类型不一致：已声明/推断为 {existing}，此处赋值为 {inferredType}");
        }
        else
        {
            context.DeclaredVariables[targetName] = inferredType;
        }

        return MapResult<string>.Ok(inferredTypeName);
    }

    private static MapResult<int> TryMapIfStatement(
        BodyIfStatement ifStmt,
        ScriptConversionContext context,
        int depth,
        int loopDepth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        int startLine = lineNumber;
        if (ifStmt.ElseStatement is not null)
        {
            return MapResult<int>.Fail("不支持 else/else if。TS 视图控制流仅支持 if/while/break/continue/return，需用多个独立 if 语句替代 else 分支。", startLine);
        }

        MapResult<BodyCall> conditionResult = LowerControlCondition(ifStmt.Condition, context, lineNumber, "if");
        if (!conditionResult.Success)
        {
            return MapResult<int>.Fail(conditionResult.Error, startLine);
        }
        BodyCall ifCondExpr = conditionResult.Value;

        MapResult<BodyCall> expandResult = TryExpandNestedCalls(ifCondExpr, context, depth, isDisabled, lineNumber, methodCalls, output);
        if (!expandResult.Success)
        {
            return MapResult<int>.FailTagged(expandResult.Error, startLine, expandResult.DiagnosticTag);
        }
        ifCondExpr = expandResult.Value;
        MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(ifCondExpr, context);
        if (!parseResult.Success)
        {
            return MapResult<int>.FailTagged(parseResult.Error, startLine, parseResult.DiagnosticTag);
        }
        TsViewCallExpr ifCondCall = parseResult.Value;

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> condMapResult = TsViewCallMapper.MapToBescript(ifCondCall, context);
        if (!condMapResult.Success)
        {
            return MapResult<int>.FailTagged(condMapResult.Error, startLine, condMapResult.DiagnosticTag);
        }
        BescriptCallSignature condSig = condMapResult.Value.Sig;

        if (ifCondCall is { Kind: TsViewCallKind.Act, MethodName: { } actName })
        {
            methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber, ifCondCall.UseThisTarget));
        }

        if (ifStmt.ThenStatement is not BodyBlock thenBlock)
        {
            return MapResult<int>.Fail("if 体必须是代码块。", startLine);
        }

        int ifLine = lineNumber++;
        List<BescriptAstNode> children = new();
        MapResult<int> bodyResult = TryMapBodyStatements(thenBlock.Statements, context, depth + 1, loopDepth, lineNumber, methodCalls, children);
        if (!bodyResult.Success)
        {
            return bodyResult;
        }
        lineNumber = bodyResult.Value;

        output.Add(new IfAstNode(ifLine, depth, output.Count, string.Empty, condSig, children, LineStartColumns: [0], IsDisabled: isDisabled));
        if (isDisabled)
        {
            // 末尾壳行计入行号，保持与 TS 视图行数一致
            lineNumber++;
        }

        return MapResult<int>.Ok(lineNumber);
    }

    private static MapResult<int> TryMapWhileStatement(
        BodyWhileStatement whileStmt,
        ScriptConversionContext context,
        int depth,
        int loopDepth,
        bool isDisabled,
        int lineNumber,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        int startLine = lineNumber;

        MapResult<BodyCall> conditionResult = LowerControlCondition(whileStmt.Condition, context, lineNumber, "while");
        if (!conditionResult.Success)
        {
            return MapResult<int>.Fail(conditionResult.Error, startLine);
        }
        BodyCall whileCondExpr = conditionResult.Value;

        // 冗余 create.bool 包装会被 TryExpandNestedCalls 剥离，此处仅检查剥离后的实际嵌套
        BodyCall preCheckExpr = whileCondExpr;
        if (GetPropertyPathText(preCheckExpr.Expression) is "G.create.bool" &&
            preCheckExpr.Arguments.Count == 1 &&
            preCheckExpr.Arguments[0] is BodyCall innerOfBool)
        {
            preCheckExpr = innerOfBool;
        }

        foreach (BodyNode arg in preCheckExpr.Arguments)
        {
            if (arg is BodyCall innerCall && !TryParseBeConstArg(innerCall).Success)
            {
                return MapResult<int>.Fail("while 条件不支持需要嵌套展开的布尔表达式。请先把复杂条件拆成循环内更新的 Bool 变量，再在 while 条件中直接使用该变量。", startLine);
            }
        }

        MapResult<BodyCall> expandResult = TryExpandNestedCalls(whileCondExpr, context, depth, isDisabled, lineNumber, methodCalls, output);
        if (!expandResult.Success)
        {
            return MapResult<int>.FailTagged(expandResult.Error, startLine, expandResult.DiagnosticTag);
        }
        whileCondExpr = expandResult.Value;
        MapResult<TsViewCallExpr> parseResult = TryParseCallExpression(whileCondExpr, context);
        if (!parseResult.Success)
        {
            return MapResult<int>.FailTagged(parseResult.Error, startLine, parseResult.DiagnosticTag);
        }
        TsViewCallExpr whileCondCall = parseResult.Value;

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> whileCondMapResult = TsViewCallMapper.MapToBescript(whileCondCall, context);
        if (!whileCondMapResult.Success)
        {
            return MapResult<int>.FailTagged(whileCondMapResult.Error, startLine, whileCondMapResult.DiagnosticTag);
        }
        BescriptCallSignature whileCondSig = whileCondMapResult.Value.Sig;

        if (whileCondCall is { Kind: TsViewCallKind.Act, MethodName: { } actName })
        {
            methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, lineNumber, whileCondCall.UseThisTarget));
        }

        if (whileStmt.Body is not BodyBlock bodyBlock)
        {
            return MapResult<int>.Fail("while 体必须是代码块。", startLine);
        }

        int whileLine = lineNumber++;
        List<BescriptAstNode> children = new();
        int bodyLoopDepth = isDisabled ? loopDepth : loopDepth + 1;
        MapResult<int> bodyResult = TryMapBodyStatements(bodyBlock.Statements, context, depth + 1, bodyLoopDepth, lineNumber, methodCalls, children);
        if (!bodyResult.Success)
        {
            return bodyResult;
        }
        lineNumber = bodyResult.Value;

        output.Add(new WhileAstNode(whileLine, depth, output.Count, string.Empty, whileCondSig, children, LineStartColumns: [0], IsDisabled: isDisabled));
        if (isDisabled)
        {
            lineNumber++;
        }

        return MapResult<int>.Ok(lineNumber);
    }

    private static MapResult<TsViewCallExpr> TryParseCallExpression(
        BodyCall call,
        ScriptConversionContext context)
    {
        // Act.* 调用：Act.xxx(...).method(...)
        MapResult<TsViewCallExpr> actResult = TryParseActCall(call, context);
        if (actResult.Success)
        {
            return MapResult<TsViewCallExpr>.Ok(actResult.Value);
        }
        if (actResult.Error.Length > 0)
        {
            return MapResult<TsViewCallExpr>.FailTagged(actResult.Error, actResult.DiagnosticTag);
        }

        // 普通调用（G.* 或 varf）
        MapResult<TsViewCallExpr> simpleResult = TryParseSimpleCall(call, context);
        if (simpleResult.Success)
        {
            return MapResult<TsViewCallExpr>.Ok(simpleResult.Value);
        }

        return MapResult<TsViewCallExpr>.FailTagged(simpleResult.Error, simpleResult.DiagnosticTag);
    }

    private static MapResult<TsViewCallExpr> TryParseActCall(
        BodyCall call,
        ScriptConversionContext context)
    {
        if (call.Expression is not BodyPropertyAccess { Name: BodyIdentifier methodIdent, Expression: BodyCall actTargetCall })
        {
            return MapResult<TsViewCallExpr>.Fail("");
        }

        if (actTargetCall.Expression is not BodyPropertyAccess { Expression: BodyIdentifier actIdent, Name: BodyIdentifier actFuncIdent })
        {
            return MapResult<TsViewCallExpr>.Fail("");
        }

        if (!string.Equals(actIdent.Text, "Act", StringComparison.Ordinal))
        {
            return MapResult<TsViewCallExpr>.Fail("");
        }

        if (actTargetCall.Arguments.Count != 1)
        {
            return MapResult<TsViewCallExpr>.Fail("Act 目标调用需要且仅需要 1 个参数。");
        }

        MapResult<TsViewArg> actTargetResult = TryParseArg(actTargetCall.Arguments[0], context, allowBeConst: true);
        if (!actTargetResult.Success)
        {
            return MapResult<TsViewCallExpr>.FailTagged(actTargetResult.Error, actTargetResult.DiagnosticTag);
        }
        TsViewArg actTargetArg = actTargetResult.Value;

        string? actTargetType = TryParseGenericType(actTargetCall.TypeArguments);
        List<TsViewArg> methodArgs = new();
        foreach (BodyNode argNode in call.Arguments)
        {
            MapResult<TsViewArg> argResult = TryParseArg(argNode, context, allowBeConst: true);
            if (!argResult.Success)
            {
                return MapResult<TsViewCallExpr>.FailTagged(argResult.Error, argResult.DiagnosticTag);
            }
            methodArgs.Add(argResult.Value);
        }

        string? genericType = TryParseGenericType(call.TypeArguments);
        TsViewCallExpr viewCall = new TsViewCallExpr(
            TsViewCallKind.Act,
            null,
            null,
            null,
            methodIdent.Text,
            genericType,
            actTargetArg,
            methodArgs,
            false,
            actTargetType);
        return MapResult<TsViewCallExpr>.Ok(viewCall);
    }

    /// <summary>
    /// 解析简单调用表达式（G.* 或 varf）。
    /// </summary>
    /// <remarks>
    /// <para><b>悬空变量规则</b></para>
    /// <list type="bullet">
    ///   <item>原版 BES 允许悬空变量：从未在赋值左侧出现的标识符，在 varf 右侧使用</item>
    ///   <item>原版能正常解析和显示，但会有警告</item>
    ///   <item>已声明变量：TS 视图输出 xxx.method()，类型从上下文推断</item>
    ///   <item>悬空变量：TS 视图输出 (xxx as BeType).method()，类型通过 as 断言保留</item>
    ///   <item>反向映射时：检测 as 断言，从中提取类型信息</item>
    /// </list>
    /// </remarks>
    private static MapResult<TsViewCallExpr> TryParseSimpleCall(
        BodyCall call,
        ScriptConversionContext context)
    {
        // 检测 as 断言：(xxx as BeType).method() 形式
        string? asTargetType = null;
        BodyNode rootExpr = call.Expression;
        if (rootExpr is BodyPropertyAccess { Expression: BodyParenthesizedExpression paren } &&
            paren.Expression is BodyAsExpression asExpr)
        {
            // 提取 as 断言的类型
            if (asExpr.Type is BodyTypeReference { TypeName: BodyIdentifier typeId })
            {
                string typeName = typeId.Text;
                if (typeName.StartsWith("Be", StringComparison.Ordinal))
                {
                    asTargetType = typeName.Substring(2);
                }
            }
        }

        string[] segments = GetPropertyPathSegments(call.Expression);
        if (segments.Length == 0)
        {
            return MapResult<TsViewCallExpr>.Fail("不支持的调用表达式。");
        }

        string dottedPath = string.Join(".", segments);
        string root = segments[0];
        string methodName = segments[^1];

        // ═══════════════════════════════════════════════════════════════════
        // 规则：禁止绕过命名空间直接调用
        // ═══════════════════════════════════════════════════════════════════
        if (segments.Length < 2)
        {
            return MapResult<TsViewCallExpr>.Fail($"禁止直接调用方法 '{dottedPath}'。全局方法使用 G.{dottedPath}(...)，自定义脚本使用 Act.self<目标类型>(this).{dottedPath}(...)。");
        }

        string? genericType = TryParseGenericType(call.TypeArguments);
        List<TsViewArg> args = new();
        foreach (BodyNode argNode in call.Arguments)
        {
            MapResult<TsViewArg> argResult = TryParseArg(argNode, context, allowBeConst: true);
            if (!argResult.Success)
            {
                return MapResult<TsViewCallExpr>.FailTagged(argResult.Error, argResult.DiagnosticTag);
            }
            args.Add(argResult.Value);
        }

        if (string.Equals(root, "G", StringComparison.Ordinal))
        {
            string methodPath = string.Join(".", segments[1..]);

            // G.create.canvas 特殊校验：参数必须是 Canvas 常量（空常量和非空常量均可）
            if (string.Equals(methodPath, "create.canvas", StringComparison.Ordinal))
            {
                if (args.Count == 0)
                {
                    return MapResult<TsViewCallExpr>.Fail("G.create.canvas 必须提供 canvas 常量参数。");
                }
                TsViewArg canvasArg = args[0];
                if (canvasArg.Kind != TsViewArgKind.BeConst)
                {
                    return MapResult<TsViewCallExpr>.Fail("G.create.canvas 的参数必须是 canvas 常量（BeCanvas.fromBeConst() / fromBase64Const / fromNodeConst / fromGuidConst），不支持变量。");
                }
            }

            TsViewCallExpr funViewCall = new TsViewCallExpr(
                TsViewCallKind.Fun,
                methodPath,
                null,
                null,
                methodName,
                genericType,
                null,
                args);
            return MapResult<TsViewCallExpr>.Ok(funViewCall);
        }

        bool useThis = string.Equals(root, "this", StringComparison.Ordinal);
        string targetName;
        if (useThis)
        {
            if (segments.Length < 3)
            {
                return MapResult<TsViewCallExpr>.Fail("变量调用缺少目标或方法名。");
            }
            targetName = segments[1];
        }
        else
        {
            targetName = root;
        }
        string? normalizedGenericType = genericType is null ? null : TsViewTypeUtils.ConvertToTsTypeName(genericType);

        // 1. 类型推断：有 as 直接用 as，没有就用上下文推断
        string? ownerType = null;
        if (asTargetType is not null)
        {
            ownerType = asTargetType;
        }
        else if (context.DeclaredVariables.TryGetValue(targetName, out BescriptVarType declaredType))
        {
            ownerType = Enum.GetName(typeof(BescriptVarType), declaredType);
        }
        else if (context.GlobalVariables.TryGetValue(targetName, out BescriptVarType globalType))
        {
            ownerType = Enum.GetName(typeof(BescriptVarType), globalType);
        }

        if (ownerType is null)
        {
            if (string.Equals(methodName, "GlobalVarInitialize", StringComparison.Ordinal) &&
                targetName.StartsWith("Be", StringComparison.Ordinal))
            {
                // ═══════════════════════════════════════════════════════════════════════════════
                // 规则：GlobalVarInitialize 滥用检测
                // ───────────────────────────────────────────────────────────────────────────────
                // 检查：BeCanvas.GlobalVarInitialize() / BeUIRect.GlobalVarInitialize() 等
                // 原因：GlobalVarInitialize 仅用于全局变量声明初始化，绝对禁止在方法体中使用
                // 这是 Agent 常见的错误用法，需要明确拒绝
                // ═══════════════════════════════════════════════════════════════════════════════
                return MapResult<TsViewCallExpr>.Fail($"禁止在方法体中使用 {targetName}.GlobalVarInitialize()。GlobalVarInitialize 仅用于全局变量声明初始化，绝对禁止在脚本方法体内调用。局部变量无需初始化，直接在使用时赋值即可。");
            }
            else if (string.Equals(methodName, "fromBeConst", StringComparison.Ordinal) &&
                targetName.StartsWith("Be", StringComparison.Ordinal))
            {
                return MapResult<TsViewCallExpr>.Fail($"禁止直接使用 {targetName}.fromBeConst 作为调用参数或赋值右侧。BeScript 语法规则：单行只允许唯一调用（delay 按照形如if/while这种关键字处理）；fromBeConst 仅允许作为该调用的参数位常量。合规写法：await delay(G.create.float({targetName}.fromBeConst(\"1\")))、ret = G.create.float({targetName}.fromBeConst(\"1\")) 等。");
            }
            else
            {
                TsViewDiagnosticTag diagnosticTag = useThis
                    ? TsViewDiagnosticTag.ForSubject(TsViewDiagnosticCodes.InvalidThisGlobalReference, targetName)
                    : TsViewDiagnosticTag.None;
                return MapResult<TsViewCallExpr>.FailTagged(
                    $"无法推断变量类型: {targetName}。局部变量必须使用 var 声明；全局变量必须使用 this.<变量名>，且{AllowedVarTypesHint}",
                    diagnosticTag);
            }
        }

        // 2. 方法定位
        TsViewCallKind callKind;
        if (TsViewCallMapper.ResolveVarFuncWithCorrection(ownerType, methodName, normalizedGenericType, args.Count).Result.Found)
        {
            callKind = TsViewCallKind.Varf;
        }
        else
        {
            return MapResult<TsViewCallExpr>.Fail($"未知变量方法: {ownerType}.{methodName}。BeScript 的 fun/varf 关键字方法完全不可自定义，禁止伪造方法，全部的底层方法都可以到 sdk.ts 上查找。");
        }
        TsViewCallExpr viewCall = new TsViewCallExpr(
            callKind,
            null,
            ownerType,
            targetName,
            methodName,
            genericType,
            null,
            args,
            useThis,
            null);
        return MapResult<TsViewCallExpr>.Ok(viewCall);
    }

    /// <summary>
    /// 统一属性访问路径解析（禁止分裂缝合，集中到此处）。
    /// 取代 CollectPropertyPath + CollectPropertyAccessText 的碎片化处理。
    /// 示例：(xxx as BeType).foo.bar() → "foo.bar"
    /// </summary>
    internal static string GetPropertyPathText(BodyNode expr)
    {
        return string.Join(".", GetPropertyPathSegments(expr));
    }

    /// <summary>
    /// 从 AST 嵌套结构直接提取属性访问路径段（不经过文本拼接再拆分）。
    /// 示例：(xxx as BeType).foo.bar() → ["foo", "bar"]
    /// </summary>
    internal static string[] GetPropertyPathSegments(BodyNode expr)
    {
        List<string> parts = new();
        while (true)
        {
            if (expr is BodyPropertyAccess pae)
            {
                if (pae.Name is BodyIdentifier name)
                {
                    parts.Insert(0, name.Text);
                }
                expr = pae.Expression;
                continue;
            }

            if (expr is BodyParenthesizedExpression paren)
            {
                expr = paren.Expression;
                continue;
            }

            if (expr is BodyAsExpression asExpr)
            {
                expr = asExpr.Expression;
                continue;
            }

            break;
        }

        if (expr is BodyIdentifier id)
        {
            parts.Insert(0, id.Text);
        }
        else if (expr is BodyThisExpression)
        {
            parts.Insert(0, "this");
        }

        return parts.ToArray();
    }



    private static MapResult<TsViewArg> TryParseArg(
        BodyNode node,
        ScriptConversionContext context,
        bool allowBeConst)
    {
        switch (node)
        {
            case BodyCall callExpr:
                if (callExpr.Expression is BodyPropertyAccess { Name: BodyIdentifier methodIdent } &&
                    string.Equals(methodIdent.Text, "GlobalVarInitialize", StringComparison.Ordinal))
                {
                    return MapResult<TsViewArg>.Fail("GlobalVarInitialize 仅允许用于全局变量声明初始化，禁止在用户代码中调用。");
                }
                MapResult<TsViewArg> beConstResult = TryParseBeConstArg(callExpr);
                if (beConstResult.Success)
                {
                    if (!allowBeConst)
                    {
                        return MapResult<TsViewArg>.Fail("按 BeScript 语法规则，单行只允许唯一调用。TS 视图中，fromBeConst 仅允许作为参数位常量映射，禁止直接作为赋值右侧。合规写法：ret = G.create.xxx(BeType.fromBeConst(...))。");
                    }
                    return MapResult<TsViewArg>.Ok(beConstResult.Value);
                }
                if (beConstResult.Error.Length > 0)
                    return MapResult<TsViewArg>.Fail(beConstResult.Error);
                return MapResult<TsViewArg>.Fail("按 BeScript 语法规则，单行只允许唯一调用。映射到 TS 视图时，除 BeType.fromBeConst(...) 作为参数位常量映射外，禁止一切嵌套调用。");

            case BodyIdentifier id:
                if (string.Equals(id.Text, "undefined", StringComparison.Ordinal))
                {
                    // undefined 占位符：原始脚本缺失参数，反向映射时保留，输出到 BeScript 时剔除
                    return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.Undefined, "undefined"));
                }
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.Identifier, id.Text));

            case BodyAsExpression asExpr:
                // as 类型断言：递归处理内部表达式，忽略类型断言部分
                return TryParseArg(asExpr.Expression, context, allowBeConst);

            case BodyThisExpression:
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.Identifier, "this"));

            case BodyPropertyAccess propertyAccess:
                if (propertyAccess is { Expression: BodyThisExpression, Name: BodyIdentifier member })
                {
                    return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.ThisMember, member.Text));
                }

                // 裸枚举成员在签名校验阶段继续核对目标枚举组。
                if (propertyAccess.Expression is BodyIdentifier enumTypeIdent &&
                    BuiltinEnumTypes.ByTsTypeName.TryGetValue(enumTypeIdent.Text, out MethodEnumType? enumType))
                {
                    if (BuiltinEnumTypes.GetMemberByTsName(enumType, propertyAccess.Name.Text) is EnumMemberInfo enumMember)
                    {
                        return MapResult<TsViewArg>.Ok(new TsViewArg(
                            TsViewArgKind.BeConst,
                            enumMember.RawValue,
                            enumMember.RawValue,
                            nameof(BescriptVarType.Enum),
                            enumType.Name));
                    }

                    return MapResult<TsViewArg>.Fail($"枚举 {enumType.Name} 不存在成员 {propertyAccess.Name.Text}。");
                }

                return MapResult<TsViewArg>.Fail($"不支持的属性访问表达式: {GetPropertyPathText(propertyAccess)}。枚举常量必须写成 BeEnum.fromBeConst(具体枚举成员)。");

            // 字面量在此保留为对应 Kind，由 BuildBescriptArgs 根据 SDK 签名期望类型转为标准 BeConst 形式。不匹配期望类型的字面量将被拒绝。
            case BodyStringLiteral str:
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.StringLiteral, str.Text, str.Text));

            case BodyNumericLiteral num:
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.NumberLiteral, num.Text, num.Text));

            case BodyTrueLiteral:
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.BooleanLiteral, "true", "true"));

            case BodyFalseLiteral:
                return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.BooleanLiteral, "false", "false"));

            case BodyArrayLiteral array:
                {
                    string[] values = new string[array.Elements.Count];
                    for (int i = 0; i < array.Elements.Count; i++)
                    {
                        BodyNode element = array.Elements[i];
                        switch (element)
                        {
                            case BodyStringLiteral str:
                                values[i] = str.Text;
                                break;
                            case BodyNumericLiteral num:
                                values[i] = num.Text;
                                break;
                            default:
                                return MapResult<TsViewArg>.Fail($"数组常量只能由字符串或数字字面量组成，当前元素类型: {element.GetType().Name}。");
                        }
                    }

                    string raw = string.Join(",", values);
                    // 数组元素逗号拼接为 StringLiteral，后续由 BuildBescriptArgs 根据签名期望类型转为对应 BeConst
                    return MapResult<TsViewArg>.Ok(new TsViewArg(TsViewArgKind.StringLiteral, raw, raw));
                }

            default:
                return MapResult<TsViewArg>.Fail($"不支持的参数类型: {node.GetType().Name}。");
        }
    }

    private static MapResult<TsViewArg> TryParseBeConstArg(BodyCall call)
    {
        ConstParseResult constResult = ConstFillMode.TryParseConstValue(call);
        if (!constResult.Success)
        {
            if (constResult.Error is not null)
            {
                return MapResult<TsViewArg>.Fail(constResult.Error);
            }
            return MapResult<TsViewArg>.Fail("");
        }

        string? enumTypeName = null;
        if (string.Equals(constResult.TypeName, nameof(BescriptVarType.Enum), StringComparison.Ordinal) &&
            ConstFillMode.TryGetEnumTypeArgumentName(call) is string parsedEnumTypeName)
        {
            enumTypeName = parsedEnumTypeName;
        }

        TsViewArg arg = new TsViewArg(TsViewArgKind.BeConst, constResult.Value, constResult.Value, constResult.TypeName, enumTypeName);
        return MapResult<TsViewArg>.Ok(arg);
    }

    private static string? TryParseGenericType(IReadOnlyList<BodyNode>? typeArgs)
    {
        if (typeArgs is null || typeArgs.Count == 0)
        {
            return null;
        }

        BodyNode first = typeArgs[0];

        // 规则 3.1：禁止字符串字面量作为泛型约束，统一使用类型引用

        // 单一标识符：ClassName
        if (first is BodyIdentifier id2)
        {
            return id2.Text;
        }

        // 类型引用：TypeName<...>
        if (first is BodyTypeReference { TypeName: BodyIdentifier id })
        {
            return id.Text;
        }

        // 带命名空间的类型：Namespace.ClassName
        if (first is BodyTypeReference { TypeName: BodyPropertyAccess pae })
        {
            return GetPropertyPathText(pae);
        }

        if (first is BodyPropertyAccess pae2)
        {
            return GetPropertyPathText(pae2);
        }

        return null;
    }


    private static ParseResult<BescriptCallSignature> ValidateDynamicMethodNameArgs(
        ScriptConversionContext context,
        TsViewCallExpr tsCall,
        BescriptCallSignature sig,
        int lineNumber,
        int depth,
        List<BescriptAstNode> output)
    {
        MethodSignature? methodSig = ResolveMethodSignature(context, sig);
        if (methodSig is null)
        {
            return new ParseResult<BescriptCallSignature>.OkResult(sig);
        }

        IReadOnlyList<TsViewArg> args = tsCall.Args;
        BescriptCallArgument[]? fixedArgs = null;

        bool ApplyConstArgFix(TsViewArg fixArg, int fixIdx, string fixWarning, IReadOnlyList<BescriptCallArgument> currentArgs)
        {
            string? tempVar = TryAutoFixConstArgToTempVar(fixArg, context, lineNumber, depth, output, fixWarning);
            if (tempVar is null) return false;
            fixedArgs ??= currentArgs.ToArray();
            string besTempName = TsViewIdentifierMap.FromTsIdentifier(tempVar);
            fixedArgs[fixIdx] = new BescriptCallArgument(true, "var", besTempName, besTempName, null);
            return true;
        }

        static string FormatArgActualText(TsViewArg a) =>
            a.Kind == TsViewArgKind.BeConst
                ? string.IsNullOrEmpty(a.StringValue)
                    ? $"Be{a.BeTypeName}.fromBeConst()"
                    : $"Be{a.BeTypeName}.fromBeConst(\"{a.StringValue}\")"
                : a.Raw;

        for (int i = 0; i < methodSig.Parameters.Count && i < args.Count; i++)
        {
            MethodParameter param = methodSig.Parameters[i];
            TsViewArg arg = args[i];

            // Any 参数位检查：只允许变量、BeAny.fromBeConst() 或 undefined 补位
            string paramTypeName = param.TypeName;
            if (paramTypeName.Length != 0 &&
                BescriptVarTypeHelper.TryParse(paramTypeName) is BescriptVarType paramType &&
                paramType == BescriptVarType.Any)
            {
                bool isVariable = arg.Kind is TsViewArgKind.Identifier or TsViewArgKind.ThisMember;
                bool isArgUndefined = arg.Kind == TsViewArgKind.Undefined;
                if (!isVariable && !isArgUndefined)
                {
                    bool isAnyEmptyConst = arg.Kind == TsViewArgKind.BeConst &&
                                          string.Equals(arg.BeTypeName, "Any", StringComparison.Ordinal) &&
                                          string.IsNullOrEmpty(arg.StringValue);
                    if (!isAnyEmptyConst)
                    {
                        if (ApplyConstArgFix(arg, i, $"参数 {i + 1}（{param.EffectiveName}）是 Any 类型，直接写类型常量是错误写法。已自动展开为临时变量。正确写法应使用独立的 var 声明。", sig.Arguments))
                            continue;
                        return new ParseResult<BescriptCallSignature>.FailResult($"参数 {i + 1}（{param.EffectiveName}）是 Any 类型，只允许变量或空值常量 BeAny.fromBeConst()，当前={FormatArgActualText(arg)}。");
                    }
                }
            }

            // BeStringConvertible 参数位检查：只允许变量、String 类型常量或 undefined 补位
            if (param.AllowImplicitToString)
            {
                bool isVariable = arg.Kind is TsViewArgKind.Identifier or TsViewArgKind.ThisMember;
                bool isArgUndefined = arg.Kind == TsViewArgKind.Undefined;
                if (!isVariable && !isArgUndefined)
                {
                    bool isStringConst = arg.Kind == TsViewArgKind.BeConst &&
                                        string.Equals(arg.BeTypeName, "String", StringComparison.Ordinal);
                    if (!isStringConst)
                    {
                        // AnyPositionNonEmptyConstTypes 6类型若唯一消费处是String且嵌套则需自动展开改用String字面量(包括多重嵌套的create.string本身)
                        if (sig.Kind == BescriptCallKind.Fun &&
                            string.Equals(sig.FunctionName, "create.string", StringComparison.Ordinal) &&
                            arg.Kind == TsViewArgKind.BeConst &&
                            arg.BeTypeName is string sourceBeType &&
                            ConstFillMode.AnyPositionNonEmptyConstTypes.Contains(sourceBeType) &&
                            arg.StringValue is string stringValue)
                        {
                            fixedArgs ??= sig.Arguments.ToArray();
                            fixedArgs[i] = new BescriptCallArgument(false, "String", stringValue, stringValue, BescriptVarType.String);
                            context.ExpansionWarnings.Add(new ToolError(
                                context.ExpansionTargetPath, lineNumber, null,
                                $"参数 {i + 1}（{param.EffectiveName}）需要 String，非 String 常量已自动改写为 BeString.fromBeConst 形式。",
                                ToolDiagnosticSeverity.Warning));
                            continue;
                        }
                        if (ApplyConstArgFix(arg, i, $"参数 {i + 1}（{param.EffectiveName}）需要 String/变量，直接写非 String 类型常量是错误写法。已自动展开为临时变量。正确写法应使用独立的 var 声明。", sig.Arguments))
                            continue;
                        return new ParseResult<BescriptCallSignature>.FailResult($"参数 {i + 1}（{param.EffectiveName}）是 BeStringConvertible 类型，只允许变量或 BeString.fromBeConst(...)，当前={FormatArgActualText(arg)}。");
                    }
                }
            }

            // 自定义脚本名参数位专项检查：
            // 这些参数位接收 String 形式的自定义脚本方法名，TS 视图上必须写原始 BeScript 名称，
            // 不写 TS 转义名。转义后的方法名只属于 Act 关键字后的静态引用，两者不能混淆。
            // 触发前提只能来自 SDK 注册的 IsScriptMethodName，禁止用参数名碎片猜语义；
            // 参数名包含 method 不等于脚本方法名，例如 UIRect.scrollView 的 method 是
            // ScrollViewMovement 枚举，不能被这里拦截。
            // 检查内容：
            //   1. 实参形态只允许字符串字面量、BeString.fromBeConst、标识符、this 成员或 undefined 占位；
            //   2. 对可静态读出的名称值做反向转义检查，拦截 X_... 这类 Agent 额外转义后的脚本名；
            //   3. 拦截不依赖实际自定义脚本是否存在，只要值本身能从 TS 标识符反转义出不同原名就失败；
            //   4. 变量形态只校验形态，不猜运行时字符串内容。
            if (!param.IsScriptMethodName)
            {
                continue;
            }

            bool isBeStringConst = arg.Kind == TsViewArgKind.BeConst &&
                                   string.Equals(arg.BeTypeName, "String", StringComparison.Ordinal);
            // undefined 作为占位符允许通过，由 CollectNonCompliantMarkers 检测并参与 diff 计算
            bool isAllowedMethodNameArg = arg.Kind is TsViewArgKind.StringLiteral or TsViewArgKind.Identifier or TsViewArgKind.ThisMember or TsViewArgKind.Undefined
                                         || isBeStringConst;

            if (!isAllowedMethodNameArg)
            {
                return new ParseResult<BescriptCallSignature>.FailResult($"参数 {i + 1}（{param.EffectiveName}）是方法名位置，必须使用原始 BeScript 名称的字符串/标识符，不需要转义。");
            }

            if (arg.Kind == TsViewArgKind.Undefined)
            {
                continue;
            }

            string nameValue = arg.Raw;
            if (TsViewNaming.IsValidIdentifier(nameValue))
            {
                string decoded = TsViewIdentifierMap.FromTsIdentifier(nameValue);
                if (!string.Equals(decoded, nameValue, StringComparison.Ordinal))
                {
                    return new ParseResult<BescriptCallSignature>.FailResult($"参数 {i + 1}（{param.EffectiveName}）是方法名位置，必须使用原始 BeScript 名称，不要使用转义形式；这里不需要转义。{TsViewNaming.EscapedNameAbuseHint}");
                }
            }
        }

        if (fixedArgs is not null)
        {
            sig = sig with { Arguments = fixedArgs };
        }

        return new ParseResult<BescriptCallSignature>.OkResult(sig);
    }

    private static MethodSignature? ResolveMethodSignature(
        ScriptConversionContext context,
        BescriptCallSignature sig)
    {
        BuiltinMethodRegistry registry = context.MethodRegistry;

        switch (sig.Kind)
        {
            case BescriptCallKind.Fun:
                return registry.GetByPath(sig.FunctionName, MethodCategory.GlobalFunc);

            case BescriptCallKind.Varf:
                {
                    string owner = ((VarfCallSignature)sig).OwnerType;
                    return registry.GetByPathWithInheritance($"{owner}.{sig.FunctionName}", MethodCategory.VarFunc);
                }

            case BescriptCallKind.Dvf:
                {
                    string owner = ((DvfCallSignature)sig).OwnerType;
                    return registry.GetByPath($"{owner}.{sig.FunctionName}", MethodCategory.EditorFunc);
                }

            case BescriptCallKind.Act:
                {
                    string func = sig.FunctionName;
                    string? ownerType = ((ActCallSignature)sig).OwnerType;
                    if (ownerType is not null && int.TryParse(ownerType, out int ownerId))
                        ownerType = DeviceTypeIds.GetActionOwnerTypeName(ownerId);
                    if (ownerType is not null)
                    {
                        MethodSignature? methodSig = registry.GetByPathWithInheritance($"{ownerType}.{func}", MethodCategory.DeviceAction);
                        if (methodSig is not null)
                        {
                            return methodSig;
                        }
                    }

                    return registry.GetByPath($"Device.{func}", MethodCategory.DeviceAction);
                }

            case BescriptCallKind.Toolf:
            case BescriptCallKind.None:
                return null;

            default:
                return null;
        }
    }

    private static bool GetStmtIsDisabled(BodyNode stmt) => stmt switch
    {
        BodyVariableStatement v => v.IsDisabled,
        BodyExpressionStatement e => e.IsDisabled,
        BodyIfStatement ifs => ifs.IsDisabled,
        BodyWhileStatement ws => ws.IsDisabled,
        BodyReturnStatement r => r.IsDisabled,
        BodyBreakStatement b => b.IsDisabled,
        BodyContinueStatement c => c.IsDisabled,
        _ => false
    };

    /// <summary>
    /// 【原版行为】BES 赋值语句的返回值类型由右侧方法签名的返回值决定，
    /// 不是由左侧变量的声明类型决定。
    /// 例如：UIRect.b.copy() 方法签名返回 UI，则 "UI b=varf UIRect.b.copy()" 中类型就是 UI。
    /// </summary>
    private static string? ResolveCallReturnType(
        TsViewCallExpr call,
        BescriptCallSignature sig,
        ScriptConversionContext context,
        ActTargetInfo? actTarget)
    {
        switch (call.Kind)
        {
            case TsViewCallKind.Fun:
                return context.MethodRegistry.GetByPath(sig.FunctionName, MethodCategory.GlobalFunc)?.ReturnType;

            case TsViewCallKind.Varf:
                return context.MethodRegistry.GetByPathWithInheritance(
                    ((VarfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc)?.ReturnType;
            case TsViewCallKind.Dvf:
                return context.MethodRegistry.GetByPathWithInheritance(
                    ((DvfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc)?.ReturnType;

            case TsViewCallKind.Act:
                if (actTarget is null)
                {
                    return null;
                }
                if (actTarget.ScriptRawNameToReturnType.TryGetValue(sig.FunctionName, out string? customRet) &&
                    customRet is not null)
                {
                    return customRet;
                }
                return context.MethodRegistry.GetByPathWithInheritance(
                    actTarget.ActionOwnerTypeName + "." + sig.FunctionName,
                    MethodCategory.DeviceAction)?.ReturnType;

            default:
                return null;
        }
    }

    private static string ResolveAssignmentReturnTypeName(
        string? asReturnType,
        TsViewCallExpr call,
        BescriptCallSignature sig,
        ScriptConversionContext context,
        ActTargetInfo? actTarget,
        BescriptVarType varType)
    {
        if (asReturnType is not null)
            return TsViewTypeUtils.ConvertToTsTypeName(asReturnType);
        string? methodType = ResolveCallReturnType(call, sig, context, actTarget);
        if (methodType is not null)
            return TsViewTypeUtils.ConvertToTsTypeName(methodType);
        return varType.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════
    // 嵌套调用自动展开：将参数位嵌套的 BodyCall 替换为临时变量
    // ═══════════════════════════════════════════════════════════════════

    private static MapResult<BodyCall> TryExpandNestedCalls(
        BodyCall outerCall,
        ScriptConversionContext context,
        int depth,
        bool isDisabled,
        int line,
        List<MethodCallInfo> methodCalls,
        List<BescriptAstNode> output)
    {
        bool hasNested = false;
        foreach (BodyNode arg in outerCall.Arguments)
        {
            if (arg is BodyCall innerCall)
            {
                MapResult<TsViewArg> beConstResult = TryParseBeConstArg(innerCall);
                if (beConstResult.Error.Length > 0)
                {
                    return MapResult<BodyCall>.Fail(beConstResult.Error);
                }
                if (beConstResult.Success)
                {
                    continue;
                }
                hasNested = true;
                break;
            }
        }

        if (!hasNested)
        {
            return MapResult<BodyCall>.Ok(outerCall);
        }

        List<BodyNode> newArgs = new(outerCall.Arguments.Count);
        foreach (BodyNode arg in outerCall.Arguments)
        {
            if (arg is not BodyCall innerCallExpr)
            {
                newArgs.Add(arg);
                continue;
            }

            MapResult<TsViewArg> beConstResult = TryParseBeConstArg(innerCallExpr);
            if (beConstResult.Error.Length > 0)
            {
                return MapResult<BodyCall>.Fail(beConstResult.Error);
            }
            if (beConstResult.Success)
            {
                newArgs.Add(arg);
                continue;
            }

            bool suppressExpandWarning = innerCallExpr.IsSyntheticBooleanLowering;

            // 递归展开内层调用的参数
            MapResult<BodyCall> innerExpandResult = TryExpandNestedCalls(innerCallExpr, context, depth, isDisabled, line, methodCalls, output);
            if (!innerExpandResult.Success)
            {
                return MapResult<BodyCall>.FailTagged(innerExpandResult.Error, innerExpandResult.DiagnosticTag);
            }
            innerCallExpr = innerExpandResult.Value;

            MapResult<TsViewCallExpr> innerParseResult = TryParseCallExpression(innerCallExpr, context);
            if (!innerParseResult.Success)
            {
                return MapResult<BodyCall>.FailTagged(innerParseResult.Error, innerParseResult.DiagnosticTag);
            }
            TsViewCallExpr innerViewCall = innerParseResult.Value;

            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> innerMapResult = TsViewCallMapper.MapToBescript(innerViewCall, context);
            if (!innerMapResult.Success)
            {
                return MapResult<BodyCall>.FailTagged(innerMapResult.Error, innerMapResult.DiagnosticTag);
            }
            BescriptCallSignature innerSig = innerMapResult.Value.Sig;
            ActTargetInfo? innerActTarget = innerMapResult.Value.ResolvedActTarget;

            ParseResult<BescriptCallSignature> innerValidation = ValidateDynamicMethodNameArgs(context, innerViewCall, innerSig, line, depth, output);
            if (!innerValidation.Success)
            {
                return MapResult<BodyCall>.Fail(innerValidation.Error);
            }
            innerSig = innerValidation.Value;

            string? rawRetType = ResolveCallReturnType(innerViewCall, innerSig, context, innerActTarget);
            if (rawRetType is null || rawRetType.Equals("void", StringComparison.Ordinal))
            {
                return MapResult<BodyCall>.Fail("嵌套调用返回 void，不能作为参数使用。");
            }

            if (TsViewTypeUtils.TryParseBescriptVarType(rawRetType) is not BescriptVarType retVarType)
            {
                return MapResult<BodyCall>.Fail($"无法解析嵌套调用返回类型: {rawRetType}");
            }

            string tsTypeName = TsViewTypeUtils.ConvertToTsTypeName(rawRetType);
            string expandWarnMsg = BuildExpandWarningForCall(innerCallExpr);
            string tempVarName = EmitCallAsTempVar(innerSig, retVarType, tsTypeName, context, line, depth, isDisabled, output,
                expandWarnMsg, suppressExpandWarning);

            if (innerViewCall is { Kind: TsViewCallKind.Act, MethodName: { } actName })
            {
                methodCalls.Add(new MethodCallInfo(MethodCallKind.Act, actName, line, innerViewCall.UseThisTarget));
            }

            newArgs.Add(new BodyIdentifier(tempVarName));
        }

        BodyCall rewrittenCall = outerCall with { Arguments = newArgs };
        return MapResult<BodyCall>.Ok(rewrittenCall);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 通用：将调用签名作为临时变量发出
    // ═══════════════════════════════════════════════════════════════════

    private static string EmitCallAsTempVar(
        BescriptCallSignature callSig,
        BescriptVarType varType,
        string tsTypeName,
        ScriptConversionContext context,
        int lineNumber,
        int depth,
        bool isDisabled,
        List<BescriptAstNode> output,
        string warningMessage,
        bool suppressWarning)
    {
        string tempVarName = context.GenerateExpansionTempVarName();
        context.DeclaredVariables[tempVarName] = varType;

        string besTempName = TsViewIdentifierMap.FromTsIdentifier(tempVarName);
        BescriptCallSignature tempSig = callSig with { ReturnVar = new ReturnVarDecl(tsTypeName, besTempName) };

        output.Add(new CallAstNode(lineNumber, depth, output.Count, string.Empty, tempSig, Array.Empty<BescriptAstNode>(), LineStartColumns: [0], IsDisabled: isDisabled));

        if (!suppressWarning)
        {
            context.ExpansionWarnings.Add(new ToolError(
                context.ExpansionTargetPath, lineNumber, null,
                warningMessage,
                ToolDiagnosticSeverity.Warning));
        }

        return tempVarName;
    }

    /// <summary>
    /// 将 BeConst 常量参数自动修复为临时变量：构造 create.xxx(...) 调用并发射为临时变量节点。
    /// 成功时返回临时变量名，失败返回 null。
    /// </summary>
    private static string? TryAutoFixConstArgToTempVar(
        TsViewArg arg,
        ScriptConversionContext context,
        int lineNumber,
        int depth,
        List<BescriptAstNode> output,
        string warningMessage)
    {
        if (arg.Kind != TsViewArgKind.BeConst || string.IsNullOrEmpty(arg.BeTypeName))
            return null;

        BescriptCallSignature? createSig = TryBuildCreateCallFromBeConst(arg.BeTypeName, arg, context);
        if (createSig is null)
            return null;

        if (BescriptVarTypeHelper.TryParse(arg.BeTypeName) is not BescriptVarType varType)
            return null;

        string tsTypeName = TsViewTypeUtils.ConvertToTsTypeName(arg.BeTypeName);
        return EmitCallAsTempVar(createSig, varType, tsTypeName, context, lineNumber, depth, false, output, warningMessage, false);
    }

    // ═══════════════════════════════════════════════════════════════════
    // 赋值位自动修复：fromBeConst 直接赋值 / 变量复制
    // fromBeConst 直接赋值的规范结果是 create 调用(对抗最频繁触发的滥用场景)；这条路径不可删除。
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string> BeConstAutoFixCreateMethod = new(StringComparer.Ordinal)
    {
        ["Bool"] = "create.bool",
        ["Float"] = "create.float",
        ["Long"] = "create.long",
        ["String"] = "create.string",
        ["Color"] = "create.color",
        ["Icon"] = "create.icon",
        ["Canvas"] = "create.canvas",
        ["Bytes"] = "create.bytes",
        ["Device"] = "creatVariable.device",
        ["Mech"] = "creatVariable.mech",
        ["Player"] = "creatVariable.player",
        ["List"] = "create.list",
        ["Dict"] = "create.dict",
        ["Struct"] = "create.struct",
        ["Vector3"] = "creatVariable.Vector3",
    };

    /// <summary>
    /// 可降级的 create 调用路径 → 对应 BeType 名。
    /// 嵌套作为实参时，降级为 BeType.fromBeConst(...) 而非拆分为临时变量。
    /// </summary>
    internal static readonly Dictionary<string, string> DowngradableCreateBeTypeName = new(StringComparer.Ordinal)
    {
        ["create.float"] = "Float",
        ["create.long"] = "Long",
        ["create.bool"] = "Bool",
        ["create.string"] = "String",
        ["create.color"] = "Color",
        ["creatVariable.Vector3"] = "Vector3",
    };

    /// <summary>
    /// 为嵌套展开场景构造警告消息：若调用属可降级 create 系列（6 类型），提示直接使用
    /// fromBeConst 常量形式；否则返回通用展开警告。
    /// </summary>
    private static string BuildExpandWarningForCall(BodyCall call)
    {
        string pathText = GetPropertyPathText(call.Expression);
        int pDot = pathText.IndexOf('.');
        if (pDot >= 0 && pathText.IndexOf('.', pDot + 1) >= 0 &&
            string.Equals(pathText[..pDot], "G", StringComparison.Ordinal) &&
            DowngradableCreateBeTypeName.ContainsKey(pathText[(pDot + 1)..]))
        {
            string methodPath = pathText[(pDot + 1)..];
            return $"G.{methodPath} 嵌套调用是错误写法。已自动展开为临时变量。正确写法应直接使用 Be{DowngradableCreateBeTypeName[methodPath]}.fromBeConst(...) 常量形式。";
        }
        return "嵌套调用是错误写法。已自动展开为临时变量。正确写法应使用独立的 var 声明。";
    }

    private static BescriptCallSignature? TryBuildCreateCallFromBeConst(
        string beTypeName,
        TsViewArg beConstArg,
        ScriptConversionContext context)
    {
        if (!BeConstAutoFixCreateMethod.ContainsKey(beTypeName))
            return null;

        string createMethodPath = BeConstAutoFixCreateMethod[beTypeName];

        List<TsViewArg> createArgs;
        if (beTypeName is "List" or "Dict" or "Struct")
        {
            createArgs = [];
        }
        else if (string.Equals(beTypeName, "Vector3", StringComparison.Ordinal))
        {
            if (beConstArg.StringValue is null) return null;
            string[] parts = beConstArg.StringValue.Split(',');
            if (parts.Length != 3) return null;
            createArgs = new(3);
            foreach (string part in parts)
            {
                createArgs.Add(new TsViewArg(TsViewArgKind.BeConst, part, part, "Float"));
            }
        }
        else
        {
            createArgs = [beConstArg];
        }

        TsViewCallExpr syntheticCall = new TsViewCallExpr(
            TsViewCallKind.Fun,
            createMethodPath,
            null, null, null, null, null,
            createArgs,
            false, null);

        ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> createResult = TsViewCallMapper.MapToBescript(syntheticCall, context);
        if (!createResult.Success)
            return null;

        return createResult.Value.Sig;
    }

    private static BescriptCallSignature? TryAutoFixBeConstAssignment(
        BodyCall rhs,
        ScriptConversionContext context)
    {
        if (rhs.Expression is not BodyPropertyAccess { Expression: BodyIdentifier typeIdent, Name: BodyIdentifier methodIdent })
            return null;

        if (!string.Equals(methodIdent.Text, "fromBeConst", StringComparison.Ordinal))
            return null;

        string tsTypeName = typeIdent.Text;
        if (tsTypeName.Length <= 2 || !tsTypeName.StartsWith("Be", StringComparison.Ordinal))
            return null;

        MapResult<TsViewArg> beConstResult = TryParseBeConstArg(rhs);
        if (!beConstResult.Success)
            return null;
        TsViewArg beConstArg = beConstResult.Value;
        return beConstArg.BeTypeName switch
        {
            { } beTypeName => TryBuildCreateCallFromBeConst(beTypeName, beConstArg, context),
            _ => null
        };
    }

    private static MapResult<BescriptCallSignature?> TryAutoFixVariableCopy(
        BodyIdentifier sourceIdent,
        ScriptConversionContext context)
    {
        string sourceName = sourceIdent.Text;

        BescriptVarType sourceVarType;
        bool isSourceGlobal;
        if (context.DeclaredVariables.TryGetValue(sourceName, out sourceVarType))
        {
            isSourceGlobal = false;
        }
        else if (context.GlobalVariables.TryGetValue(sourceName, out sourceVarType))
        {
            isSourceGlobal = true;
        }
        else
        {
            return MapResult<BescriptCallSignature?>.Ok(null);
        }

        string? ownerTypeName = Enum.GetName(typeof(BescriptVarType), sourceVarType);
        if (ownerTypeName is null)
            return MapResult<BescriptCallSignature?>.Ok(null);

        // Fun (create) path: create method takes 1 same-type parameter
        if (BeConstAutoFixCreateMethod.ContainsKey(ownerTypeName))
        {
            string createPath = BeConstAutoFixCreateMethod[ownerTypeName];
            MethodSignature? methodSig = context.MethodRegistry.GetByPath(createPath, MethodCategory.GlobalFunc);
            if (methodSig is not null && methodSig.Parameters.Count == 1 &&
                string.Equals(methodSig.Parameters[0].TypeName, ownerTypeName, StringComparison.Ordinal))
            {
                TsViewArg sourceArg = isSourceGlobal
                    ? new TsViewArg(TsViewArgKind.ThisMember, sourceName)
                    : new TsViewArg(TsViewArgKind.Identifier, sourceName);

                TsViewCallExpr syntheticCall = new TsViewCallExpr(
                    TsViewCallKind.Fun,
                    createPath,
                    null, null, null, null, null,
                    new[] { sourceArg },
                    false, null);

                ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> synthResult1 = TsViewCallMapper.MapToBescript(syntheticCall, context);
                if (!synthResult1.Success)
                    return MapResult<BescriptCallSignature?>.Ok(null);
                return MapResult<BescriptCallSignature?>.Ok(synthResult1.Value.Sig);
            }
        }

        // Varf (copy) path
        if (context.MethodRegistry.GetByPathWithInheritance(ownerTypeName + ".copy", MethodCategory.VarFunc) is not null)
        {
            TsViewCallExpr syntheticCall = new TsViewCallExpr(
                TsViewCallKind.Varf,
                null, null, sourceName, "copy", null, null,
                Array.Empty<TsViewArg>(),
                false, null);

            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> synthResult2 = TsViewCallMapper.MapToBescript(syntheticCall, context);
            if (!synthResult2.Success)
                return MapResult<BescriptCallSignature?>.Ok(null);
            return MapResult<BescriptCallSignature?>.Ok(synthResult2.Value.Sig);
        }

        // Varf (ref) path
        if (context.MethodRegistry.GetByPathWithInheritance(ownerTypeName + ".ref", MethodCategory.VarFunc) is not null)
        {
            TsViewCallExpr syntheticCall = new TsViewCallExpr(
                TsViewCallKind.Varf,
                null, null, sourceName, "ref", null, null,
                Array.Empty<TsViewArg>(),
                false, null);

            ParseResult<(BescriptCallSignature Sig, ActTargetInfo? ResolvedActTarget)> synthResult3 = TsViewCallMapper.MapToBescript(syntheticCall, context);
            if (!synthResult3.Success)
                return MapResult<BescriptCallSignature?>.Ok(null);
            return MapResult<BescriptCallSignature?>.Ok(synthResult3.Value.Sig);
        }

        return MapResult<BescriptCallSignature?>.Ok(null);
    }
#pragma warning restore TK004
#pragma warning restore TK005
}
