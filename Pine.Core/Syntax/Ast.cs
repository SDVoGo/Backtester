namespace Pine.Core.Syntax;

/// <summary>1-based position of the first token of a node.</summary>
public readonly record struct SourcePos(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

public abstract record Node(SourcePos Pos);

public sealed record Script(string? Version, IReadOnlyList<Statement> Statements, SourcePos Pos) : Node(Pos);

public sealed record Block(IReadOnlyList<Statement> Statements, SourcePos Pos) : Node(Pos);

/// <summary>
/// Type annotation: <c>float</c>, <c>series float</c>, <c>float[]</c>, <c>array&lt;float&gt;</c>, <c>chart.point</c>.
/// </summary>
public sealed record TypeRef(string? Qualifier, string Name, IReadOnlyList<TypeRef> TypeArguments, bool IsArray, SourcePos Pos) : Node(Pos);

// ── Expressions ─────────────────────────────────────────────

public abstract record Expr(SourcePos Pos) : Node(Pos);

public sealed record IntLiteral(long Value, SourcePos Pos) : Expr(Pos);

public sealed record FloatLiteral(double Value, SourcePos Pos) : Expr(Pos);

public sealed record BoolLiteral(bool Value, SourcePos Pos) : Expr(Pos);

public sealed record StringLiteral(string Value, SourcePos Pos) : Expr(Pos);

/// <summary>Hex color as written in source, e.g. <c>#26c477</c>.</summary>
public sealed record ColorLiteral(string Hex, SourcePos Pos) : Expr(Pos);

public sealed record IdentifierExpr(string Name, SourcePos Pos) : Expr(Pos);

/// <summary><c>Target.Name</c>, e.g. <c>ta.sma</c> or <c>barstate.islast</c>.</summary>
public sealed record MemberExpr(Expr Target, string Name, SourcePos Pos) : Expr(Pos);

public sealed record Argument(string? Name, Expr Value, SourcePos Pos) : Node(Pos);

public sealed record CallExpr(Expr Callee, IReadOnlyList<Argument> Arguments, SourcePos Pos) : Expr(Pos);

/// <summary>History reference <c>Target[Offset]</c>.</summary>
public sealed record HistoryExpr(Expr Target, Expr Offset, SourcePos Pos) : Expr(Pos);

/// <summary><c>[a, b, ...]</c>: tuple returned by functions, or array literal in arguments.</summary>
public sealed record TupleExpr(IReadOnlyList<Expr> Elements, SourcePos Pos) : Expr(Pos);

public enum UnaryOp { Negate, Plus, Not }

public sealed record UnaryExpr(UnaryOp Operator, Expr Operand, SourcePos Pos) : Expr(Pos);

public enum BinaryOp
{
    Add, Subtract, Multiply, Divide, Modulo,
    Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual,
    And, Or,
}

public sealed record BinaryExpr(Expr Left, BinaryOp Operator, Expr Right, SourcePos Pos) : Expr(Pos);

public sealed record TernaryExpr(Expr Condition, Expr Then, Expr Else, SourcePos Pos) : Expr(Pos);

// Structures are expressions too: their value is the last expression of the executed block.

/// <summary><c>else if</c> is an <see cref="Else"/> block holding a single <see cref="IfExpr"/>.</summary>
public sealed record IfExpr(Expr Condition, Block Then, Block? Else, SourcePos Pos) : Expr(Pos);

/// <summary>A null <see cref="Condition"/> marks the default case.</summary>
public sealed record SwitchCase(Expr? Condition, Block Body, SourcePos Pos) : Node(Pos);

/// <summary>Without <see cref="Subject"/> each case condition is a bool expression.</summary>
public sealed record SwitchExpr(Expr? Subject, IReadOnlyList<SwitchCase> Cases, SourcePos Pos) : Expr(Pos);

public sealed record ForExpr(string Variable, Expr From, Expr To, Expr? Step, Block Body, SourcePos Pos) : Expr(Pos);

/// <summary><c>for item in xs</c> or <c>for [index, item] in xs</c>.</summary>
public sealed record ForInExpr(string? IndexVariable, string ItemVariable, Expr Collection, Block Body, SourcePos Pos) : Expr(Pos);

public sealed record WhileExpr(Expr Condition, Block Body, SourcePos Pos) : Expr(Pos);

// ── Statements ──────────────────────────────────────────────

public abstract record Statement(SourcePos Pos) : Node(Pos);

public enum DeclarationMode { Default, Var, Varip }

public sealed record VariableDeclaration(DeclarationMode Mode, TypeRef? Type, string Name, Expr Value, SourcePos Pos) : Statement(Pos);

/// <summary><c>[a, b] = f()</c></summary>
public sealed record TupleDeclaration(IReadOnlyList<string> Names, Expr Value, SourcePos Pos) : Statement(Pos);

public enum AssignOp { Reassign, Add, Subtract, Multiply, Divide, Modulo }

public sealed record Assignment(Expr Target, AssignOp Operator, Expr Value, SourcePos Pos) : Statement(Pos);

public sealed record Parameter(TypeRef? Type, string Name, Expr? Default, SourcePos Pos) : Node(Pos);

public sealed record FunctionDeclaration(string Name, IReadOnlyList<Parameter> Parameters, Block Body, SourcePos Pos) : Statement(Pos);

public sealed record ExpressionStatement(Expr Expression, SourcePos Pos) : Statement(Pos);

public sealed record BreakStatement(SourcePos Pos) : Statement(Pos);

public sealed record ContinueStatement(SourcePos Pos) : Statement(Pos);
