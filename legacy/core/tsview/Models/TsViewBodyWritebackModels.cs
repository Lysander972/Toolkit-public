using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Core.TsView;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BodyVariableDeclaration), "variableDeclaration")]
[JsonDerivedType(typeof(BodyVariableDeclarationList), "variableDeclarationList")]
[JsonDerivedType(typeof(BodyBlock), "block")]
[JsonDerivedType(typeof(BodyVariableStatement), "variableStatement")]
[JsonDerivedType(typeof(BodyExpressionStatement), "expressionStatement")]
[JsonDerivedType(typeof(BodyIfStatement), "ifStatement")]
[JsonDerivedType(typeof(BodyWhileStatement), "whileStatement")]
[JsonDerivedType(typeof(BodyReturnStatement), "returnStatement")]
[JsonDerivedType(typeof(BodyBreakStatement), "breakStatement")]
[JsonDerivedType(typeof(BodyContinueStatement), "continueStatement")]
[JsonDerivedType(typeof(BodyEmptyStatement), "emptyStatement")]
[JsonDerivedType(typeof(BodyIdentifier), "identifier")]
[JsonDerivedType(typeof(BodyStringLiteral), "stringLiteral")]
[JsonDerivedType(typeof(BodyNumericLiteral), "numericLiteral")]
[JsonDerivedType(typeof(BodyTrueLiteral), "trueLiteral")]
[JsonDerivedType(typeof(BodyFalseLiteral), "falseLiteral")]
[JsonDerivedType(typeof(BodyThisExpression), "thisExpression")]
[JsonDerivedType(typeof(BodyPropertyAccess), "propertyAccess")]
[JsonDerivedType(typeof(BodyCall), "call")]
[JsonDerivedType(typeof(BodyNewExpression), "newExpression")]
[JsonDerivedType(typeof(BodyNotExpression), "notExpression")]
[JsonDerivedType(typeof(BodyBinaryExpression), "binaryExpression")]
[JsonDerivedType(typeof(BodyParenthesizedExpression), "parenthesizedExpression")]
[JsonDerivedType(typeof(BodyAsExpression), "asExpression")]
[JsonDerivedType(typeof(BodyTemplateLiteral), "templateLiteral")]
[JsonDerivedType(typeof(BodyObjectLiteral), "objectLiteral")]
[JsonDerivedType(typeof(BodyObjectProperty), "objectProperty")]
[JsonDerivedType(typeof(BodyArrayLiteral), "arrayLiteral")]
[JsonDerivedType(typeof(BodyAwaitExpression), "awaitExpression")]
[JsonDerivedType(typeof(BodyVoidExpression), "voidExpression")]
[JsonDerivedType(typeof(BodyTypeReference), "typeReference")]
[JsonDerivedType(typeof(BodyArrayType), "arrayType")]
[JsonDerivedType(typeof(BodyTupleType), "tupleType")]
[JsonDerivedType(typeof(BodyUnionType), "unionType")]
[JsonDerivedType(typeof(BodyVoidType), "voidType")]
[JsonDerivedType(typeof(BodySingleLineComment), "singleLineComment")]
[JsonDerivedType(typeof(BodyMultiLineComment), "multiLineComment")]
internal abstract record BodyNode
{
    public int? SourceLine { get; init; }
}

internal sealed record BodyVariableDeclaration(
    BodyIdentifier Name,
    BodyNode? Type,
    BodyNode? Initializer
) : BodyNode;

internal sealed record BodyVariableDeclarationList(
    BodyVariableKind VariableKind,
    IReadOnlyList<BodyVariableDeclaration> Declarations
) : BodyNode;

internal enum BodyVariableKind
{
    Var,
    Let,
    Const
}

internal sealed record BodyBlock(
    IReadOnlyList<BodyNode> Statements
) : BodyNode;

internal sealed record BodyVariableStatement(
    BodyVariableDeclarationList DeclarationList
) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyExpressionStatement(
    BodyNode Expression
) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyIfStatement(
    BodyNode Condition,
    BodyNode ThenStatement,
    BodyNode? ElseStatement = null
) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyWhileStatement(
    BodyNode Condition,
    BodyNode Body
) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyReturnStatement(
    BodyNode? Expression = null
) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyBreakStatement : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyContinueStatement : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyEmptyStatement : BodyNode;

internal sealed record BodyIdentifier(string Text) : BodyNode;

internal sealed record BodyStringLiteral(string Text) : BodyNode;

internal sealed record BodyNumericLiteral(string Text) : BodyNode;

internal sealed record BodyTrueLiteral : BodyNode;

internal sealed record BodyFalseLiteral : BodyNode;

internal sealed record BodyThisExpression : BodyNode;

internal sealed record BodyPropertyAccess(
    BodyNode Expression,
    BodyIdentifier Name
) : BodyNode;

internal sealed record BodyCall(
    BodyNode Expression,
    IReadOnlyList<BodyNode> Arguments,
    IReadOnlyList<BodyNode>? TypeArguments = null
) : BodyNode
{
    public bool IsSyntheticBooleanLowering { get; init; }
}

internal sealed record BodyNewExpression(
    BodyNode Expression,
    IReadOnlyList<BodyNode> Arguments
) : BodyNode;

internal sealed record BodyNotExpression(BodyNode Operand) : BodyNode;

internal sealed record BodyBinaryExpression(
    BodyNode Left,
    BodyBinaryOperator Operator,
    BodyNode Right
) : BodyNode;

internal enum BodyBinaryOperator
{
    Equals,
    PlusEquals,
    MinusEquals,
    EqualsEquals,
    EqualsEqualsEquals,
    ExclamationEquals,
    ExclamationEqualsEquals,
    LessThan,
    LessThanEquals,
    GreaterThan,
    GreaterThanEquals,
    Plus,
    Minus,
    Asterisk,
    Slash,
    Percent,
    AmpersandAmpersand,
    BarBar
}

internal sealed record BodyParenthesizedExpression(BodyNode Expression) : BodyNode;

internal sealed record BodyAsExpression(
    BodyNode Expression,
    BodyNode Type
) : BodyNode;

internal sealed record BodyTemplateLiteral(string Text) : BodyNode;

internal sealed record BodyObjectLiteral(
    IReadOnlyList<BodyObjectProperty> Properties,
    bool IsMultiLine = false
) : BodyNode;

internal sealed record BodyObjectProperty(
    string Name,
    BodyNode Value
) : BodyNode;

internal sealed record BodyArrayLiteral(
    IReadOnlyList<BodyNode> Elements
) : BodyNode;

internal sealed record BodyAwaitExpression(BodyNode Argument) : BodyNode;

internal sealed record BodyVoidExpression(BodyNode Argument) : BodyNode;

internal sealed record BodyTypeReference(
    BodyNode TypeName,
    IReadOnlyList<BodyNode>? TypeArguments = null
) : BodyNode;

internal sealed record BodyArrayType(BodyNode ElementType) : BodyNode;

internal sealed record BodyVoidType : BodyNode;

internal sealed record BodyTupleType(
    IReadOnlyList<BodyNode> Elements
) : BodyNode;

internal sealed record BodyUnionType(
    IReadOnlyList<BodyNode> Types
) : BodyNode;

internal sealed record BodySingleLineComment(string Text) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal sealed record BodyMultiLineComment(string Text) : BodyNode
{
    public bool IsDisabled { get; init; }
}

internal static class BodyWritebackJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
