using Toolkit.Core.Bescript;
using Toolkit.Core.Sdk;

namespace Toolkit.Core.TsView;

internal static class BescriptToDeviceTsViewMapper
{
    private abstract class ReturnMapping
    {
        public sealed class None : ReturnMapping;
        public sealed class Value(string variableName) : ReturnMapping
        {
            public string VariableName { get; } = variableName;
        }
    }

    private abstract class SignatureReturnType
    {
        public sealed class Missing : SignatureReturnType;
        public sealed class Found(string typeName) : SignatureReturnType
        {
            public string TypeName { get; } = typeName;
        }
    }

    internal static DeviceTsViewScriptBody Map(BescriptAst besAst, ScriptConversionContext context, int loopDepth, IReadOnlyList<DeviceTsViewScriptBodyLine> lines)
    {
        RegisterDeclaredVariables(besAst, context);
        List<DeviceTsViewStatement> statements = [];
        HashSet<string> emittedLocalDeclarations = new(context.ParameterNames, StringComparer.Ordinal);
        Dictionary<string, BescriptVarType> pendingDisabledDeclarations = new(StringComparer.Ordinal);
        MapNodes(besAst.RootNodes, context, loopDepth, new ReturnMapping.None(), emittedLocalDeclarations, pendingDisabledDeclarations, statements);
        statements.Add(new DeviceTsViewStatement("return", false, null));
        return new DeviceTsViewScriptBody(statements, lines);
    }

    internal static DeviceTsViewScriptBody Map(BescriptAst besAst, ScriptConversionContext context, int loopDepth, string returnVarName, IReadOnlyList<DeviceTsViewScriptBodyLine> lines)
    {
        RegisterDeclaredVariables(besAst, context);
        List<DeviceTsViewStatement> statements = [];
        HashSet<string> emittedLocalDeclarations = new(context.ParameterNames, StringComparer.Ordinal);

        if (!context.ParameterNames.Contains(returnVarName) &&
            context.DeclaredVariables.TryGetValue(returnVarName, out BescriptVarType returnVarType) &&
            returnVarType != BescriptVarType.Void)
        {
            statements.Add(VarStatement(false, null, "var", returnVarName, "Be" + returnVarType, null));
            emittedLocalDeclarations.Add(returnVarName);
        }

        Dictionary<string, BescriptVarType> pendingDisabledDeclarations = new(StringComparer.Ordinal);
        MapNodes(besAst.RootNodes, context, loopDepth, new ReturnMapping.Value(returnVarName), emittedLocalDeclarations, pendingDisabledDeclarations, statements);
        statements.Add(new DeviceTsViewStatement("return", false, null, Expression: Identifier(returnVarName)));
        return new DeviceTsViewScriptBody(statements, lines);
    }

    private static void RegisterDeclaredVariables(BescriptAst besAst, ScriptConversionContext context)
    {
        foreach ((string varName, BescriptVarType varType) in besAst.DeclaredVariables)
        {
            string tsName = TsViewIdentifierMap.ToTsIdentifier(varName);
            if (!context.DeclaredVariables.ContainsKey(tsName) &&
                !context.GlobalVariables.ContainsKey(tsName))
            {
                context.DeclaredVariables[tsName] = varType;
            }
        }
    }

    private static void MapNodes(
        IReadOnlyList<BescriptAstNode> nodes,
        ScriptConversionContext context,
        int loopDepth,
        ReturnMapping returnMapping,
        HashSet<string> emittedLocalDeclarations,
        Dictionary<string, BescriptVarType> pendingDisabledDeclarations,
        List<DeviceTsViewStatement> output)
    {
        foreach (BescriptAstNode node in nodes)
            MapNode(node, context, loopDepth, returnMapping, emittedLocalDeclarations, pendingDisabledDeclarations, output);
    }

    private static void MapNode(
        BescriptAstNode node,
        ScriptConversionContext context,
        int loopDepth,
        ReturnMapping returnMapping,
        HashSet<string> emittedLocalDeclarations,
        Dictionary<string, BescriptVarType> pendingDisabledDeclarations,
        List<DeviceTsViewStatement> output)
    {
        if (node.IsDisabled)
        {
            Stack<BescriptAstNode> stack = new();
            stack.Push(node);
            while (stack.Count > 0)
            {
                BescriptAstNode current = stack.Pop();
                if (current is IHasCall { Call.ReturnVar: { } disabledRet } &&
                    BescriptVarTypeHelper.TryParse(disabledRet.TypeName) is { } varType &&
                    varType != BescriptVarType.UI)
                {
                    string tsName = TsViewIdentifierMap.ToTsIdentifier(disabledRet.Name);
                    if (!context.GlobalVariables.ContainsKey(tsName) &&
                        !context.ParameterNames.Contains(tsName) &&
                        !pendingDisabledDeclarations.ContainsKey(tsName))
                    {
                        pendingDisabledDeclarations[tsName] = varType;
                    }
                }

                for (int i = current.Children.Count - 1; i >= 0; i--)
                    stack.Push(current.Children[i]);
            }

        }

        if (pendingDisabledDeclarations.Count > 0 && node is IHasCall { Call: var pendingReadCall })
        {
            foreach (BescriptCallArgument arg in pendingReadCall.Arguments)
            {
                if (!arg.IsVariable)
                    continue;

                string tsName = TsViewIdentifierMap.ToTsIdentifier(arg.DecodedText);
                if (pendingDisabledDeclarations.Remove(tsName, out BescriptVarType varType) &&
                    emittedLocalDeclarations.Add(tsName))
                {
                    output.Add(VarStatement(false, null, "var", tsName, "Be" + varType, null));
                }
            }

            string? ownerReadName = pendingReadCall switch
            {
                VarfCallSignature { OwnerNameIsAlias: false } varf => varf.OwnerName,
                DvfCallSignature dvf => dvf.OwnerName,
                _ => null
            };
            if (ownerReadName is not null)
            {
                string tsName = TsViewIdentifierMap.ToTsIdentifier(ownerReadName);
                if (pendingDisabledDeclarations.Remove(tsName, out BescriptVarType varType) &&
                    emittedLocalDeclarations.Add(tsName))
                {
                    output.Add(VarStatement(false, null, "var", tsName, "Be" + varType, null));
                }
            }
        }

        switch (node.Kind)
        {
            case BescriptStatementKind.If:
                MapIf(node, context, loopDepth, returnMapping, emittedLocalDeclarations, pendingDisabledDeclarations, output);
                break;
            case BescriptStatementKind.While:
                MapWhile(node, context, loopDepth, returnMapping, emittedLocalDeclarations, pendingDisabledDeclarations, output);
                break;
            case BescriptStatementKind.Delay:
                MapDelay(node, context, emittedLocalDeclarations, output);
                break;
            case BescriptStatementKind.Async:
                MapAsync(node, context, emittedLocalDeclarations, output);
                break;
            case BescriptStatementKind.Break:
                MapBreak(node, loopDepth, returnMapping, output);
                break;
            case BescriptStatementKind.Continue:
                output.Add(new DeviceTsViewStatement("continue", node.IsDisabled, node.Line));
                break;
            case BescriptStatementKind.Call:
                MapCall(node, context, emittedLocalDeclarations, output);
                break;
            case BescriptStatementKind.Skip:
            case BescriptStatementKind.Comment:
                string content = node.Text;
#pragma warning disable TK010 // TsDisabledStart/End 带空格，识别完整禁用壳时需使用无外侧空格的边界
                string trimmedDisabledStart = BescriptSyntax.TsDisabledStart.TrimEnd();
                string trimmedDisabledEnd = BescriptSyntax.TsDisabledEnd.TrimStart();
#pragma warning restore TK010
                if (node.IsDisabled)
                {
                    if (content.StartsWith("//", StringComparison.Ordinal))
                    {
                        output.Add(new DeviceTsViewStatement("singleLineComment", true, node.Line, Text: content["//".Length..]));
                    }
                    else if (content.StartsWith(trimmedDisabledStart, StringComparison.Ordinal))
                    {
                        string inner = content;
                        if (content.EndsWith(trimmedDisabledEnd, StringComparison.Ordinal) && content.Length >= 4)
                            inner = content[trimmedDisabledStart.Length..^trimmedDisabledEnd.Length];
                        output.Add(new DeviceTsViewStatement("multiLineComment", true, node.Line, Text: inner));
                    }
                    else
                    {
                        output.Add(new DeviceTsViewStatement("multiLineComment", true, node.Line, Text: content));
                    }
                    break;
                }

                if (content.StartsWith(trimmedDisabledStart, StringComparison.Ordinal))
                {
                    string inner = content;
                    if (content.EndsWith(trimmedDisabledEnd, StringComparison.Ordinal) && content.Length >= 4)
                        inner = content[trimmedDisabledStart.Length..^trimmedDisabledEnd.Length];
                    output.Add(new DeviceTsViewStatement("multiLineComment", false, node.Line, Text: inner));
                    break;
                }

                if (content.StartsWith("//", StringComparison.Ordinal))
                    content = content["//".Length..];
                output.Add(new DeviceTsViewStatement("singleLineComment", false, node.Line, Text: content));
                break;
            default:
                throw new InvalidOperationException($"第 {node.Line} 行: 未知语句类型 {node.Kind}");
        }

        if (!node.IsDisabled && node is IHasCall { Call.ReturnVar: { } ret })
            pendingDisabledDeclarations.Remove(TsViewIdentifierMap.ToTsIdentifier(ret.Name));
    }

    private static void MapIf(
        BescriptAstNode node,
        ScriptConversionContext context,
        int loopDepth,
        ReturnMapping returnMapping,
        HashSet<string> emittedLocalDeclarations,
        Dictionary<string, BescriptVarType> pendingDisabledDeclarations,
        List<DeviceTsViewStatement> output)
    {
        DeviceTsViewExpression condition = BuildNodeCallExpression(((IHasCall)node).Call, node.Kind, context);
        List<DeviceTsViewStatement> bodyStatements = [];
        MapNodes(node.Children, context, loopDepth, returnMapping, emittedLocalDeclarations, pendingDisabledDeclarations, bodyStatements);
        output.Add(new DeviceTsViewStatement(
            "if",
            node.IsDisabled,
            node.Line,
            Expression: condition,
            ThenStatement: Block(bodyStatements)));
    }

    private static void MapWhile(
        BescriptAstNode node,
        ScriptConversionContext context,
        int loopDepth,
        ReturnMapping returnMapping,
        HashSet<string> emittedLocalDeclarations,
        Dictionary<string, BescriptVarType> pendingDisabledDeclarations,
        List<DeviceTsViewStatement> output)
    {
        DeviceTsViewExpression condition = BuildNodeCallExpression(((IHasCall)node).Call, node.Kind, context);
        List<DeviceTsViewStatement> bodyStatements = [];
        int innerLoopDepth = node.IsDisabled ? loopDepth : loopDepth + 1;
        MapNodes(node.Children, context, innerLoopDepth, returnMapping, emittedLocalDeclarations, pendingDisabledDeclarations, bodyStatements);
        output.Add(new DeviceTsViewStatement(
            "while",
            node.IsDisabled,
            node.Line,
            Expression: condition,
            Body: Block(bodyStatements)));
    }

    private static void MapDelay(
        BescriptAstNode node,
        ScriptConversionContext context,
        HashSet<string> emittedLocalDeclarations,
        List<DeviceTsViewStatement> output)
    {
        DeviceTsViewExpression call = BuildNodeCallExpression(((IHasCall)node).Call, node.Kind, context);
        DeviceTsViewExpression awaitExpr = Await(Call(Identifier("delay"), [call]));
        ReturnVarDecl? delayRet = ((IHasCall)node).Call.ReturnVar;
        if (delayRet is null)
        {
            output.Add(ExpressionStatement(awaitExpr, node.IsDisabled, node.Line));
            return;
        }

#pragma warning disable TK011
        if (delayRet.Name == string.Empty)
#pragma warning restore TK011
        {
            output.Add(ExpressionStatement(new DeviceTsViewExpression("void", Argument: awaitExpr), node.IsDisabled, node.Line));
            return;
        }

        string tsName = TsViewIdentifierMap.ToTsIdentifier(delayRet.Name);
        BescriptVarType varType = ResolveVarType(((IHasCall)node).Call, tsName, context);
        SignatureReturnType sigRetType = ResolveSignatureReturnType(((IHasCall)node).Call, context);
        AppendReturnValueAssignment(output, context, emittedLocalDeclarations, tsName, varType, sigRetType, awaitExpr, node.IsDisabled, node.Line);
    }

    private static void MapAsync(
        BescriptAstNode node,
        ScriptConversionContext context,
        HashSet<string> emittedLocalDeclarations,
        List<DeviceTsViewStatement> output)
    {
        DeviceTsViewExpression call = BuildNodeCallExpression(((IHasCall)node).Call, node.Kind, context);
        ReturnVarDecl? asyncRet = ((IHasCall)node).Call.ReturnVar;
        if (asyncRet is null)
        {
            output.Add(ExpressionStatement(call, node.IsDisabled, node.Line));
            return;
        }

#pragma warning disable TK011
        if (asyncRet.Name == string.Empty)
#pragma warning restore TK011
        {
            output.Add(ExpressionStatement(new DeviceTsViewExpression("void", Argument: call), node.IsDisabled, node.Line));
            return;
        }

        string tsName = TsViewIdentifierMap.ToTsIdentifier(asyncRet.Name);
        BescriptVarType varType = ResolveVarType(((IHasCall)node).Call, tsName, context);
        SignatureReturnType sigRetType = ResolveSignatureReturnType(((IHasCall)node).Call, context);
        AppendReturnValueAssignment(output, context, emittedLocalDeclarations, tsName, varType, sigRetType, call, node.IsDisabled, node.Line);
    }

    private static void AppendReturnValueAssignment(
        List<DeviceTsViewStatement> output,
        ScriptConversionContext context,
        HashSet<string> emittedLocalDeclarations,
        string tsName,
        BescriptVarType besRetType,
        SignatureReturnType sigRetType,
        DeviceTsViewExpression rhs,
        bool isDisabled,
        int? sourceLine)
    {
        string besTypeName = besRetType.ToString();
        DeviceTsViewExpression finalRhs = rhs;
        if (sigRetType is SignatureReturnType.Found foundReturnType)
        {
            string normalizedReturnType = TsViewTypeUtils.ConvertToTsTypeName(foundReturnType.TypeName);
            if (!string.Equals(besTypeName, normalizedReturnType, StringComparison.Ordinal))
                finalRhs = new DeviceTsViewExpression("as", Expression: rhs, TypeName: "Be" + besTypeName);
        }

        if (context.GlobalVariables.ContainsKey(tsName) && !context.DeclaredVariables.ContainsKey(tsName))
        {
            output.Add(ExpressionStatement(Binary(Property(This(), tsName), "equals", finalRhs), isDisabled, sourceLine));
            return;
        }

        if (!context.DeclaredVariables.ContainsKey(tsName))
            context.DeclaredVariables[tsName] = besRetType;

        if (!emittedLocalDeclarations.Contains(tsName))
        {
            output.Add(VarStatement(isDisabled, sourceLine, "var", tsName, "Be" + besRetType, finalRhs));
            if (!isDisabled)
                emittedLocalDeclarations.Add(tsName);
            return;
        }

        output.Add(ExpressionStatement(Binary(Identifier(tsName), "equals", finalRhs), isDisabled, sourceLine));
    }

    private static void MapBreak(
        BescriptAstNode node,
        int loopDepth,
        ReturnMapping returnMapping,
        List<DeviceTsViewStatement> output)
    {
        if (loopDepth > 0)
        {
            output.Add(new DeviceTsViewStatement("break", node.IsDisabled, node.Line));
            return;
        }

        DeviceTsViewExpression? expression = null;
        if (returnMapping is ReturnMapping.Value valueReturn)
            expression = Identifier(valueReturn.VariableName);
        output.Add(new DeviceTsViewStatement("return", node.IsDisabled, node.Line, Expression: expression));
    }

    private static void MapCall(
        BescriptAstNode node,
        ScriptConversionContext context,
        HashSet<string> emittedLocalDeclarations,
        List<DeviceTsViewStatement> output)
    {
        DeviceTsViewExpression call = BuildNodeCallExpression(((IHasCall)node).Call, node.Kind, context);
        ReturnVarDecl? callRet = ((IHasCall)node).Call.ReturnVar;
#pragma warning disable TK011
        if (callRet is null || callRet.Name == string.Empty)
#pragma warning restore TK011
        {
            output.Add(ExpressionStatement(call, node.IsDisabled, node.Line));
            return;
        }

        string tsVarName = TsViewIdentifierMap.ToTsIdentifier(callRet.Name);
        BescriptVarType varType = ResolveVarType(((IHasCall)node).Call, tsVarName, context);
        SignatureReturnType sigRetType = ResolveSignatureReturnType(((IHasCall)node).Call, context);
        AppendReturnValueAssignment(output, context, emittedLocalDeclarations, tsVarName, varType, sigRetType, call, node.IsDisabled, node.Line);
    }

    private static DeviceTsViewExpression BuildNodeCallExpression(
        BescriptCallSignature sig,
        BescriptStatementKind kind,
        ScriptConversionContext context)
    {
        DeviceTsViewExpression result = BuildCallExpression(sig, context);
        if (result.Kind == "await")
            return result;

        if (kind == BescriptStatementKind.Async)
            return Await(result);

        if (kind is BescriptStatementKind.If or BescriptStatementKind.While or BescriptStatementKind.Delay &&
            TsViewAsyncValidation.ResolveSdkSignature(sig) is { AsyncMode: not SdkAsyncMode.Sync })
        {
            return Await(result);
        }

        return result;
    }

    private static DeviceTsViewExpression BuildCallExpression(BescriptCallSignature sig, ScriptConversionContext context)
    {
        MethodSignature? methodSig = ResolveMethodSignature(sig, context);
        List<DeviceTsViewExpression> args = [];
        for (int i = 0; i < sig.Arguments.Count; i++)
        {
            BescriptCallArgument arg = sig.Arguments[i];
            MethodParameter? expectedParam = methodSig is not null && i < methodSig.Parameters.Count ? methodSig.Parameters[i] : null;
            MethodEnumType? expectedEnumType = ResolveExpectedEnumType(sig, arg, expectedParam, context);
            args.Add(BuildArgument(arg, context, expectedEnumType, sig.FunctionName));
        }

        if (methodSig is not null)
        {
            while (args.Count < methodSig.Parameters.Count)
                args.Add(Identifier("undefined"));
        }

        if (sig.Kind == BescriptCallKind.Act)
            return BuildActCall((ActCallSignature)sig, args, context);
        if (sig.Kind == BescriptCallKind.Fun)
        {
            // 未注册的全局函数：用 BES 原函数名降级占位，让转换层不再因未注册函数
            // 直接抛错（典型场景：SDK 已删除但 BeScript 残留的历史函数）。
            // TS 视图上表现为 G.<转义后函数名>(...)，TS DTS 不会声明此调用，
            // 作为问题标记让 Agent/用户看到并定位；LintEngine 会单独输出
            // "内置方法签名未注册"诊断。
            // 未注册函数名必须走 TsViewIdentifierMap.ToTsIdentifier 统一转义，
            // 否则 BES 残留的关键字函数名（如 public/class/return）会撞 TS 语法
            // 直接卡死文本化层；与已注册函数的命名规则保持一致。
            BuiltinMethodMap.ForwardEntry? forward = BuiltinMethodMap.Instance.GetGlobalForward(sig.FunctionName);
            string tsMethodName = forward is { } registered
                ? registered.TsMethodName
                : TsViewIdentifierMap.ToTsIdentifier(sig.FunctionName);
            DeviceTsViewExpression gAccess = Identifier("G");
            foreach (string part in tsMethodName.Split('.', StringSplitOptions.RemoveEmptyEntries))
                gAccess = Property(gAccess, part);
            return Call(gAccess, args, forward switch
            {
                null => null,
                _ => BuildGenericTypeArgs(forward.GenericBeTypeName)
            });
        }
        if (sig.Kind == BescriptCallKind.Varf)
            return BuildVarfCall((VarfCallSignature)sig, args, context);
        if (sig.Kind == BescriptCallKind.Dvf)
            return BuildDvfCall((DvfCallSignature)sig, args);
        throw new InvalidOperationException($"未知调用类型: {sig.Kind}");
    }

    private static MethodSignature? ResolveMethodSignature(BescriptCallSignature sig, ScriptConversionContext context)
    {
        return sig.Kind switch
        {
            BescriptCallKind.Fun => context.MethodRegistry.GetByPath(sig.FunctionName, MethodCategory.GlobalFunc),
            BescriptCallKind.Varf or BescriptCallKind.Dvf => context.MethodRegistry.GetByPathWithInheritance(
                TsViewTypeUtils.ConvertToTsTypeName(((VarfCallSignature)sig).OwnerType) + "." + sig.FunctionName,
                MethodCategory.VarFunc),
            BescriptCallKind.Act => ResolveActMethodSignature((ActCallSignature)sig, context),
            _ => null
        };
    }

    private static MethodSignature? ResolveActMethodSignature(ActCallSignature act, ScriptConversionContext context)
    {
        ActTargetInfo? targetInfo = ResolveActTarget(act, context);
        if (targetInfo?.ScriptRawNameToMethodName.ContainsKey(act.FunctionName) != true &&
            targetInfo is not null)
        {
            return context.MethodRegistry.GetByPathWithInheritance(
                targetInfo.ActionOwnerTypeName + "." + act.FunctionName,
                MethodCategory.DeviceAction);
        }
        return null;
    }

    private static MethodEnumType? ResolveExpectedEnumType(
        BescriptCallSignature sig,
        BescriptCallArgument arg,
        MethodParameter? expectedParam,
        ScriptConversionContext context)
    {
        if (!string.Equals(TsViewTypeUtils.ConvertToTsTypeName(arg.TypeName), nameof(BescriptVarType.Enum), StringComparison.Ordinal) ||
            expectedParam is null)
        {
            return null;
        }

        if (ReferenceEquals(expectedParam.EnumType, MethodEnumType.FollowVarfOwner) && sig is VarfCallSignature varfSig)
        {
            context.VariableEnumTypes.TryGetValue(TsViewIdentifierMap.ToTsIdentifier(varfSig.OwnerName), out MethodEnumType? ownerEnumType);
            return ownerEnumType;
        }

        return expectedParam.EnumType;
    }

    private static DeviceTsViewExpression BuildVarfCall(
        VarfCallSignature sig,
        IReadOnlyList<DeviceTsViewExpression> args,
        ScriptConversionContext context)
    {
        string ownerType = TsViewTypeUtils.ConvertToTsTypeName(sig.OwnerType);
        BuiltinMethodMap.ForwardEntry? forward = BuiltinMethodMap.Instance.GetVarFuncForward(ownerType, sig.FunctionName);
        if (forward is null)
            throw new InvalidOperationException($"未知 VarFunc 方法: {ownerType}.{sig.FunctionName}");

        string tsTargetName = TsViewIdentifierMap.ToTsIdentifier(sig.OwnerName);
        bool isGlobal = context.GlobalVariables.ContainsKey(tsTargetName);
        bool isDeclared = context.DeclaredVariables.ContainsKey(tsTargetName);
        string? contextTypeName = null;
        if (isDeclared && context.DeclaredVariables.TryGetValue(tsTargetName, out BescriptVarType declaredType))
            contextTypeName = Enum.GetName(typeof(BescriptVarType), declaredType);
        else if (isGlobal && context.GlobalVariables.TryGetValue(tsTargetName, out BescriptVarType globalType))
            contextTypeName = Enum.GetName(typeof(BescriptVarType), globalType);

        bool needsAssertion = contextTypeName is null ||
            !string.Equals(contextTypeName, ownerType, StringComparison.Ordinal);
        DeviceTsViewExpression targetExpr;
        if (needsAssertion)
        {
            DeviceTsViewExpression baseExpr = Identifier(tsTargetName);
            if (isGlobal && !isDeclared)
                baseExpr = Property(This(), tsTargetName);
            targetExpr = new DeviceTsViewExpression("parenthesized", Expression: new DeviceTsViewExpression("as", Expression: baseExpr, TypeName: "Be" + ownerType));
        }
        else if (isGlobal && !isDeclared)
        {
            targetExpr = Property(This(), tsTargetName);
        }
        else
        {
            targetExpr = Identifier(tsTargetName);
        }

        return Call(Property(targetExpr, forward.TsMethodName), args, BuildGenericTypeArgs(forward.GenericBeTypeName));
    }

    private static DeviceTsViewExpression BuildDvfCall(DvfCallSignature sig, IReadOnlyList<DeviceTsViewExpression> args)
    {
        string ownerType = TsViewTypeUtils.ConvertToTsTypeName(sig.OwnerType);
        BuiltinMethodMap.ForwardEntry? forward = BuiltinMethodMap.Instance.GetVarFuncForward(ownerType, sig.FunctionName);
        if (forward is null)
            throw new InvalidOperationException($"未知 VarFunc 方法: {ownerType}.{sig.FunctionName}");

        string tsTargetName = TsViewIdentifierMap.ToTsIdentifier(sig.OwnerName);
        DeviceTsViewExpression targetExpr = new("parenthesized", Expression: new DeviceTsViewExpression("as", Expression: Identifier(tsTargetName), TypeName: "Be" + ownerType));
        return Call(Property(targetExpr, forward.TsMethodName), args, BuildGenericTypeArgs(forward.GenericBeTypeName));
    }

    private static DeviceTsViewExpression BuildActCall(
        ActCallSignature sig,
        IReadOnlyList<DeviceTsViewExpression> args,
        ScriptConversionContext context)
    {
        ActTargetInfo? targetInfo = ResolveActTarget(sig, context);
        if (targetInfo is null)
            return BuildUnresolvedActTargetCall(sig, context);

        string targetTypeName = targetInfo.TsTypeName;
        DeviceTsViewExpression actTargetCall;
        if (sig.OwnerNameIsAlias)
        {
            actTargetCall = Call(Property(Identifier("Act"), "byName"), [String(sig.OwnerName)], [targetTypeName]);
        }
        else if (sig.OwnerName == "this")
        {
            actTargetCall = Call(Property(Identifier("Act"), "self"), [This()], [targetTypeName]);
        }
        else
        {
            actTargetCall = Call(Property(Identifier("Act"), "byId"), [Number(sig.OwnerName)], [targetTypeName]);
        }

        string actionOwnerType = targetInfo.ActionOwnerTypeName;
#pragma warning disable TK011
        if (actionOwnerType == string.Empty)
#pragma warning restore TK011
            throw new InvalidOperationException($"Act 目标缺少类型信息（ownerName={sig.OwnerName}）。");

        string tsMethodName;
        string? methodGeneric;
        BuiltinMethodMap.ForwardEntry? forward = BuiltinMethodMap.Instance.GetDeviceActionForward(actionOwnerType, sig.FunctionName);
        if (forward is not null)
        {
            tsMethodName = forward.TsMethodName;
            methodGeneric = forward.GenericBeTypeName;
        }
        else
        {
            tsMethodName = targetInfo.ScriptRawNameToMethodName.TryGetValue(sig.FunctionName, out string? mapped)
                ? mapped
                : TsViewIdentifierMap.ToTsIdentifier(sig.FunctionName);
            methodGeneric = null;
        }

#pragma warning disable TK011
        if (tsMethodName == string.Empty)
#pragma warning restore TK011
            throw new InvalidOperationException("Act 方法名解析失败: " + sig.FunctionName);

        DeviceTsViewExpression callExpr = Call(Property(actTargetCall, tsMethodName), args, BuildGenericTypeArgs(methodGeneric));
        MethodSignature? sdkSignature = TsViewAsyncValidation.ResolveSdkSignature(sig);
        bool needsAwait = targetInfo.AsyncWithReturnRawNames.Contains(sig.FunctionName);
        if (sdkSignature is not null)
            needsAwait = sdkSignature.AsyncMode is SdkAsyncMode.Async or SdkAsyncMode.TsAsync;
        return needsAwait ? Await(callExpr) : callExpr;
    }

    private static ActTargetInfo? ResolveActTarget(ActCallSignature sig, ScriptConversionContext context)
    {
        string ownerName = sig.OwnerName;
        if (sig.OwnerNameIsAlias)
        {
            context.ActTargetMap.ByName.TryGetValue(ownerName, out ActTargetInfo? byName);
            return byName;
        }

        if (ownerName == "this")
            return context.ActTargetMap.Self;

        if (int.TryParse(ownerName, out int id))
        {
            context.ActTargetMap.ById.TryGetValue(id, out ActTargetInfo? byId);
            return byId;
        }

        return null;
    }

    private static DeviceTsViewExpression BuildUnresolvedActTargetCall(ActCallSignature sig, ScriptConversionContext context)
    {
        if (!sig.OwnerNameIsAlias && sig.OwnerName == "this")
            throw new InvalidOperationException("Self 未配置（这不应该发生，self 永远指向自身）");

        int ownerTypeId = int.Parse(sig.OwnerType);
        string targetTypeName = BescriptDtsGenerator.BuildDeviceTypeTypeName(ownerTypeId);
        DeviceTsViewExpression actTargetCall = int.TryParse(sig.OwnerName, out _)
            ? Call(Property(Identifier("Act"), "byId"), [Number(sig.OwnerName)], [targetTypeName])
            : Call(Property(Identifier("Act"), "byName"), [String(sig.OwnerName)], [targetTypeName]);

        List<DeviceTsViewExpression> args = [];
        foreach (BescriptCallArgument arg in sig.Arguments)
        {
            if (arg.IsVariable)
            {
                string varName = TsViewIdentifierMap.ToTsIdentifier(arg.DecodedText);
                args.Add(BuildVarAccess(varName, context));
            }
            else
            {
                args.Add(Identifier(arg.RawText));
            }
        }

        return Call(Property(actTargetCall, sig.FunctionName), args);
    }

    private static IReadOnlyList<string>? BuildGenericTypeArgs(string? genericBeTypeName)
    {
#pragma warning disable TK011
        if (genericBeTypeName is null || genericBeTypeName == string.Empty)
#pragma warning restore TK011
            return null;

        string typeName = TsViewTypeUtils.ConvertToTsTypeName(genericBeTypeName);
        return ["Be" + typeName];
    }

    private static DeviceTsViewExpression BuildArgument(
        BescriptCallArgument arg,
        ScriptConversionContext context,
        MethodEnumType? expectedEnumType,
        string methodName)
    {
        if (arg.IsVariable)
        {
            string varName = TsViewIdentifierMap.ToTsIdentifier(arg.DecodedText);
            return BuildVarAccess(varName, context);
        }

        string normalizedType = TsViewTypeUtils.ConvertToTsTypeName(arg.TypeName);
        return BuildLiteral(normalizedType, arg.DecodedText, expectedEnumType, methodName);
    }

    private static DeviceTsViewExpression BuildLiteral(
        string typeName,
        string value,
        MethodEnumType? expectedEnumType,
        string methodName)
    {
        ConstFillContext ctx = new()
        {
            TypeName = typeName,
            Value = value,
            MethodName = methodName,
            EnumType = expectedEnumType
        };

        ConstExpressionFillResult fillResult = ConstFillMode.BuildConstExpression(ctx);
        if (fillResult is { Success: true, Expression: { } expression })
            return expression;
        throw new InvalidOperationException(fillResult.Error);
    }

    private static DeviceTsViewExpression BuildVarAccess(string varName, ScriptConversionContext context)
    {
        if (context.GlobalVariables.ContainsKey(varName) && !context.DeclaredVariables.ContainsKey(varName))
            return Property(This(), varName);
        return Identifier(varName);
    }

    private static BescriptVarType ResolveVarType(BescriptCallSignature sig, string tsName, ScriptConversionContext context)
    {
        if (sig.ReturnVar is { } ret &&
            BescriptVarTypeHelper.TryParse(ret.TypeName) is { } parsedType)
        {
            return parsedType;
        }

        if (context.DeclaredVariables.TryGetValue(tsName, out BescriptVarType ctxType))
            return ctxType;

        throw new InvalidOperationException($"无法确定变量类型: {tsName}");
    }

    private static SignatureReturnType ResolveSignatureReturnType(BescriptCallSignature sig, ScriptConversionContext context)
    {
        switch (sig.Kind)
        {
            case BescriptCallKind.Fun:
                if (context.MethodRegistry.GetByPath(sig.FunctionName, MethodCategory.GlobalFunc) is MethodSignature funSig &&
                    funSig.ReturnType is string funReturnType)
                {
                    return new SignatureReturnType.Found(funReturnType);
                }
                return new SignatureReturnType.Missing();
            case BescriptCallKind.Varf:
                if (context.MethodRegistry.GetByPathWithInheritance(
                    ((VarfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc) is MethodSignature varfSig &&
                    varfSig.ReturnType is string varfReturnType)
                {
                    return new SignatureReturnType.Found(varfReturnType);
                }
                return new SignatureReturnType.Missing();
            case BescriptCallKind.Dvf:
                if (context.MethodRegistry.GetByPathWithInheritance(
                    ((DvfCallSignature)sig).OwnerType + "." + sig.FunctionName,
                    MethodCategory.VarFunc) is MethodSignature dvfSig &&
                    dvfSig.ReturnType is string dvfReturnType)
                {
                    return new SignatureReturnType.Found(dvfReturnType);
                }
                return new SignatureReturnType.Missing();
            case BescriptCallKind.Act:
                ActCallSignature act = (ActCallSignature)sig;
                ActTargetInfo? targetInfo = ResolveActTarget(act, context);
                if (targetInfo?.ScriptRawNameToReturnType.TryGetValue(sig.FunctionName, out string? customRet) == true &&
                    customRet.Length > 0)
                {
                    return new SignatureReturnType.Found(customRet);
                }

                if (targetInfo is not null &&
                    context.MethodRegistry.GetByPathWithInheritance(
                        targetInfo.ActionOwnerTypeName + "." + sig.FunctionName,
                        MethodCategory.DeviceAction) is MethodSignature actSig &&
                    actSig.ReturnType is string actReturnType)
                {
                    return new SignatureReturnType.Found(actReturnType);
                }
                return new SignatureReturnType.Missing();
            case BescriptCallKind.Toolf:
            case BescriptCallKind.None:
                return new SignatureReturnType.Missing();
            default:
                return new SignatureReturnType.Missing();
        }
    }

    private static DeviceTsViewStatement Block(IReadOnlyList<DeviceTsViewStatement> statements)
        => new("block", false, null, Statements: statements);

    private static DeviceTsViewStatement ExpressionStatement(DeviceTsViewExpression expression, bool isDisabled, int? sourceLine)
        => new("expression", isDisabled, sourceLine, Expression: expression);

    private static DeviceTsViewStatement VarStatement(bool isDisabled, int? sourceLine, string variableKind, string name, string? typeName, DeviceTsViewExpression? initializer)
        => new("variable", isDisabled, sourceLine, DeclarationList: new DeviceTsViewVariableDeclarationList(
            variableKind,
            [new DeviceTsViewVariableDeclaration(name, typeName, initializer)]));

    private static DeviceTsViewExpression Identifier(string text)
        => new("identifier", Text: text);

    private static DeviceTsViewExpression String(string text)
        => new("string", Text: text);

    private static DeviceTsViewExpression Number(string text)
        => new("number", Text: text);

    private static DeviceTsViewExpression This()
        => new("this");

    private static DeviceTsViewExpression Property(DeviceTsViewExpression expression, string name)
        => new("propertyAccess", Expression: expression, Name: name);

    private static DeviceTsViewExpression Call(DeviceTsViewExpression expression, IReadOnlyList<DeviceTsViewExpression> args, IReadOnlyList<string>? typeArguments = null)
        => new("call", Expression: expression, Arguments: args, TypeArguments: typeArguments);

    private static DeviceTsViewExpression Await(DeviceTsViewExpression expression)
        => new("await", Argument: expression);

    private static DeviceTsViewExpression Binary(DeviceTsViewExpression left, string op, DeviceTsViewExpression right)
        => new("binary", Left: left, Operator: op, Right: right);
}
