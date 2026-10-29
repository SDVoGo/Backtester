using System.Globalization;

namespace Pine.Core.Syntax;

/// <summary>
/// Compact S-expression rendering of the AST, used by tests and for debugging.
/// <code>
/// x = a + b * 2      →  (decl x (+ a (* b 2)))
/// if c ... else ...  →  (if c {...} else {...})
/// </code>
/// </summary>
public static class AstPrinter
{
    public static string Print(Script script)
        => string.Join("\n", script.Statements.Select(Print));

    public static string Print(Statement statement)
        => statement switch
        {
            VariableDeclaration d => $"(decl {Join(Mode(d.Mode), d.Type is null ? null : Print(d.Type), d.Name, Print(d.Value))})",
            TupleDeclaration d => $"(decl [{string.Join(", ", d.Names)}] {Print(d.Value)})",
            Assignment a => $"({AssignSymbol(a.Operator)} {Print(a.Target)} {Print(a.Value)})",
            FunctionDeclaration f => $"(def {f.Name} ({string.Join(", ", f.Parameters.Select(Print))}) {Print(f.Body)})",
            ExpressionStatement e => Print(e.Expression),
            BreakStatement => "break",
            ContinueStatement => "continue",
            _ => throw new ArgumentOutOfRangeException(nameof(statement), statement.GetType().Name),
        };

    public static string Print(Expr expr)
        => expr switch
        {
            IntLiteral i => i.Value.ToString(CultureInfo.InvariantCulture),
            FloatLiteral f => f.Value.ToString("0.0###############", CultureInfo.InvariantCulture),
            BoolLiteral b => b.Value ? "true" : "false",
            StringLiteral s => $"\"{s.Value}\"",
            ColorLiteral c => c.Hex,
            IdentifierExpr id => id.Name,
            MemberExpr m => $"{Print(m.Target)}.{m.Name}",
            CallExpr c => $"(call {Join([Print(c.Callee), .. c.Arguments.Select(Print)])})",
            HistoryExpr h => $"{Print(h.Target)}[{Print(h.Offset)}]",
            TupleExpr t => $"[{string.Join(", ", t.Elements.Select(Print))}]",
            UnaryExpr u => $"({UnarySymbol(u.Operator)} {Print(u.Operand)})",
            BinaryExpr b => $"({BinarySymbol(b.Operator)} {Print(b.Left)} {Print(b.Right)})",
            TernaryExpr t => $"(? {Print(t.Condition)} {Print(t.Then)} {Print(t.Else)})",
            IfExpr i => i.Else is null
                ? $"(if {Print(i.Condition)} {Print(i.Then)})"
                : $"(if {Print(i.Condition)} {Print(i.Then)} else {Print(i.Else)})",
            SwitchExpr s => $"(switch {Join([s.Subject is null ? null : Print(s.Subject), .. s.Cases.Select(Print)])})",
            ForExpr f => $"(for {Join(f.Variable, Print(f.From), Print(f.To), f.Step is null ? null : Print(f.Step), Print(f.Body))})",
            ForInExpr f => f.IndexVariable is null
                ? $"(for-in {f.ItemVariable} {Print(f.Collection)} {Print(f.Body)})"
                : $"(for-in [{f.IndexVariable}, {f.ItemVariable}] {Print(f.Collection)} {Print(f.Body)})",
            WhileExpr w => $"(while {Print(w.Condition)} {Print(w.Body)})",
            _ => throw new ArgumentOutOfRangeException(nameof(expr), expr.GetType().Name),
        };

    public static string Print(Block block)
        => $"{{{string.Join("; ", block.Statements.Select(Print))}}}";

    private static string Print(TypeRef type)
    {
        var name = type.TypeArguments.Count == 0
            ? type.Name
            : $"{type.Name}<{string.Join(", ", type.TypeArguments.Select(Print))}>";
        return Join(type.Qualifier, name + (type.IsArray ? "[]" : ""));
    }

    private static string Print(Argument argument)
        => argument.Name is null ? Print(argument.Value) : $"{argument.Name}={Print(argument.Value)}";

    private static string Print(Parameter parameter)
    {
        var text = Join(parameter.Type is null ? null : Print(parameter.Type), parameter.Name);
        return parameter.Default is null ? text : $"{text}={Print(parameter.Default)}";
    }

    private static string Print(SwitchCase @case)
        => @case.Condition is null
            ? $"(default {Print(@case.Body)})"
            : $"(case {Print(@case.Condition)} {Print(@case.Body)})";

    private static string Join(params string?[] parts)
        => string.Join(" ", parts.Where(p => p is not null));

    private static string? Mode(DeclarationMode mode)
        => mode switch
        {
            DeclarationMode.Var => "var",
            DeclarationMode.Varip => "varip",
            _ => null,
        };

    private static string AssignSymbol(AssignOp op)
        => op switch
        {
            AssignOp.Reassign => ":=",
            AssignOp.Add => "+=",
            AssignOp.Subtract => "-=",
            AssignOp.Multiply => "*=",
            AssignOp.Divide => "/=",
            AssignOp.Modulo => "%=",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    private static string UnarySymbol(UnaryOp op)
        => op switch
        {
            UnaryOp.Negate => "-",
            UnaryOp.Plus => "+",
            UnaryOp.Not => "not",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };

    private static string BinarySymbol(BinaryOp op)
        => op switch
        {
            BinaryOp.Add => "+",
            BinaryOp.Subtract => "-",
            BinaryOp.Multiply => "*",
            BinaryOp.Divide => "/",
            BinaryOp.Modulo => "%",
            BinaryOp.Equal => "==",
            BinaryOp.NotEqual => "!=",
            BinaryOp.Less => "<",
            BinaryOp.LessEqual => "<=",
            BinaryOp.Greater => ">",
            BinaryOp.GreaterEqual => ">=",
            BinaryOp.And => "and",
            BinaryOp.Or => "or",
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
}
