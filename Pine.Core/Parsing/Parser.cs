using System.Globalization;
using Pine.Core.Lexing;
using Pine.Core.Syntax;

namespace Pine.Core.Parsing;

public class ParseException(string message, int line, int column)
    : Exception($"{message} (line {line}, column {column})")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

/// <summary>
/// Recursive descent parser for statements, Pratt parser for expressions.
/// Operator precedence follows the Pine manual (low to high):
/// <c>?:</c>, <c>or</c>, <c>and</c>, <c>== !=</c>, <c>&lt; &lt;= &gt; &gt;=</c>,
/// <c>+ -</c>, <c>* / %</c>, unary <c>+ - not</c>, postfix <c>[] () .</c>
/// </summary>
public sealed class Parser
{
    private static readonly HashSet<string> TypeQualifiers = ["const", "simple", "series", "input"];

    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    private Parser(IReadOnlyList<Token> tokens)
        => _tokens = tokens;

    public static Script Parse(string source)
        => Parse(Lexer.Tokenize(source));

    public static Script Parse(IReadOnlyList<Token> tokens)
        => new Parser(tokens).ParseScript();

    // ── Token helpers ───────────────────────────────────────

    private Token Current => _tokens[_pos];

    private Token Peek(int offset)
        => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

    private Token Previous => _tokens[_pos - 1];

    private bool Check(TokenKind kind)
        => Current.Kind == kind;

    private Token Advance()
    {
        var token = Current;
        if (token.Kind != TokenKind.EOF)
            _pos++;
        return token;
    }

    private bool Match(TokenKind kind)
    {
        if (!Check(kind))
            return false;
        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string what)
        => Check(kind) ? Advance() : throw Error($"Expected {what}");

    private ParseException Error(string message)
    {
        var found = Current.Kind switch
        {
            TokenKind.EOF => "end of file",
            TokenKind.NEWLINE => "end of line",
            TokenKind.INDENT => "indentation",
            TokenKind.DEDENT => "end of block",
            _ => $"'{Current.Value}'",
        };
        return new ParseException($"{message}, found {found}", Current.Line, Current.Column);
    }

    private static SourcePos PosOf(Token token)
        => new(token.Line, token.Column);

    /// <summary>Index of the bracket closing the one at <paramref name="openIndex"/>, or -1.</summary>
    private int FindClosing(int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < _tokens.Count; i++)
        {
            switch (_tokens[i].Kind)
            {
                case TokenKind.OPEN_PAREN or TokenKind.OPEN_BRACKET:
                    depth++;
                    break;
                case TokenKind.CLOSE_PAREN or TokenKind.CLOSE_BRACKET:
                    if (--depth == 0)
                        return i;
                    break;
                case TokenKind.NEWLINE or TokenKind.EOF:
                    return -1;
            }
        }
        return -1;
    }

    // ── Script / blocks ─────────────────────────────────────

    private Script ParseScript()
    {
        var pos = PosOf(Current);
        string? version = null;

        if (Check(TokenKind.VERSION))
        {
            version = Advance().Value;
            Match(TokenKind.NEWLINE);
        }

        var statements = new List<Statement>();
        while (!Check(TokenKind.EOF))
            statements.AddRange(ParseStatementLine());

        return new Script(version, statements, pos);
    }

    /// <summary>NEWLINE INDENT statement+ DEDENT</summary>
    private Block ParseBlock()
    {
        Expect(TokenKind.NEWLINE, "end of line");
        var pos = PosOf(Expect(TokenKind.INDENT, "an indented block"));

        var statements = new List<Statement>();
        while (!Check(TokenKind.DEDENT) && !Check(TokenKind.EOF))
            statements.AddRange(ParseStatementLine());

        Expect(TokenKind.DEDENT, "end of block");
        return new Block(statements, pos);
    }

    /// <summary>Body after <c>=&gt;</c>: an indented block or statements on the same line.</summary>
    private Block ParseArrowBody()
    {
        if (Check(TokenKind.NEWLINE))
            return ParseBlock();

        var pos = PosOf(Current);
        return new Block(ParseSimpleStatements(), pos);
    }

    /// <summary>
    /// One logical line: comma separated statements, terminated by NEWLINE.
    /// A line ending with an indented block has already consumed its terminator.
    /// </summary>
    private List<Statement> ParseStatementLine()
    {
        var statements = ParseSimpleStatements();

        if (Previous.Kind != TokenKind.DEDENT)
            Expect(TokenKind.NEWLINE, "end of line");

        return statements;
    }

    private List<Statement> ParseSimpleStatements()
    {
        var statements = new List<Statement> { ParseStatement() };
        while (Previous.Kind != TokenKind.DEDENT && Match(TokenKind.COMMA))
            statements.Add(ParseStatement());
        return statements;
    }

    // ── Statements ──────────────────────────────────────────

    private Statement ParseStatement()
    {
        var start = Current;
        var pos = PosOf(start);

        switch (start.Kind)
        {
            case TokenKind.BREAK:
                Advance();
                return new BreakStatement(pos);
            case TokenKind.CONTINUE:
                Advance();
                return new ContinueStatement(pos);
            case TokenKind.VAR or TokenKind.VARIP:
                return ParseVariableDeclaration();
            case TokenKind.IMPORT or TokenKind.EXPORT or TokenKind.TYPE or TokenKind.METHOD or TokenKind.ENUM:
                throw Error($"'{start.Value}' is not supported yet");
            case TokenKind.OPEN_BRACKET when IsTupleDeclaration():
                return ParseTupleDeclaration();
            case TokenKind.IDENTIFIER when Peek(1).Kind == TokenKind.OPEN_PAREN && IsFunctionDeclaration():
                return ParseFunctionDeclaration();
            case TokenKind.IDENTIFIER when Peek(1).Kind == TokenKind.ASSIGNMENT:
                return ParseVariableDeclaration();
            case TokenKind.IDENTIFIER when TryParseTypeBeforeName() is { } type:
                return ParseVariableDeclaration(DeclarationMode.Default, type, pos);
        }

        var expr = ParseValue();

        AssignOp? op = Current.Kind switch
        {
            TokenKind.REASSIGNMENT => AssignOp.Reassign,
            TokenKind.PLUS_EQUALS => AssignOp.Add,
            TokenKind.MINUS_EQUALS => AssignOp.Subtract,
            TokenKind.STAR_EQUALS => AssignOp.Multiply,
            TokenKind.SLASH_EQUALS => AssignOp.Divide,
            TokenKind.PERCENT_EQUALS => AssignOp.Modulo,
            _ => null,
        };

        if (op is null)
            return new ExpressionStatement(expr, pos);

        if (expr is not (IdentifierExpr or MemberExpr))
            throw new ParseException("Invalid assignment target", start.Line, start.Column);

        Advance();
        return new Assignment(expr, op.Value, ParseValue(), pos);
    }

    private VariableDeclaration ParseVariableDeclaration()
    {
        var pos = PosOf(Current);

        var mode = Current.Kind switch
        {
            TokenKind.VAR => DeclarationMode.Var,
            TokenKind.VARIP => DeclarationMode.Varip,
            _ => DeclarationMode.Default,
        };
        if (mode != DeclarationMode.Default)
            Advance();

        return ParseVariableDeclaration(mode, TryParseTypeBeforeName(), pos);
    }

    private VariableDeclaration ParseVariableDeclaration(DeclarationMode mode, TypeRef? type, SourcePos pos)
    {
        var name = Expect(TokenKind.IDENTIFIER, "variable name").Value;
        Expect(TokenKind.ASSIGNMENT, "'='");
        return new VariableDeclaration(mode, type, name, ParseValue(), pos);
    }

    private bool IsTupleDeclaration()
    {
        var close = FindClosing(_pos);
        return close >= 0 && _tokens[close + 1].Kind == TokenKind.ASSIGNMENT;
    }

    private TupleDeclaration ParseTupleDeclaration()
    {
        var pos = PosOf(Expect(TokenKind.OPEN_BRACKET, "'['"));

        var names = new List<string>();
        do
            names.Add(Expect(TokenKind.IDENTIFIER, "variable name").Value);
        while (Match(TokenKind.COMMA));

        Expect(TokenKind.CLOSE_BRACKET, "']'");
        Expect(TokenKind.ASSIGNMENT, "'='");
        return new TupleDeclaration(names, ParseValue(), pos);
    }

    private bool IsFunctionDeclaration()
    {
        var close = FindClosing(_pos + 1);
        return close >= 0 && _tokens[close + 1].Kind == TokenKind.ARROW;
    }

    private FunctionDeclaration ParseFunctionDeclaration()
    {
        var nameToken = Advance();
        Expect(TokenKind.OPEN_PAREN, "'('");

        var parameters = new List<Parameter>();
        if (!Check(TokenKind.CLOSE_PAREN))
        {
            do
            {
                var pos = PosOf(Current);
                var type = TryParseTypeBeforeName();
                var name = Expect(TokenKind.IDENTIFIER, "parameter name").Value;
                var defaultValue = Match(TokenKind.ASSIGNMENT) ? ParseExpression() : null;
                parameters.Add(new Parameter(type, name, defaultValue, pos));
            }
            while (Match(TokenKind.COMMA));
        }

        Expect(TokenKind.CLOSE_PAREN, "')'");
        Expect(TokenKind.ARROW, "'=>'");

        return new FunctionDeclaration(nameToken.Value, parameters, ParseArrowBody(), PosOf(nameToken));
    }

    // ── Types ───────────────────────────────────────────────

    /// <summary>
    /// Parses a type annotation only if it is followed by a name
    /// (<c>float x</c>); otherwise restores the position and returns null.
    /// </summary>
    private TypeRef? TryParseTypeBeforeName()
    {
        var start = _pos;

        var type = TryParseType();
        if (type is not null && Check(TokenKind.IDENTIFIER))
            return type;

        _pos = start;
        return null;
    }

    private TypeRef? TryParseType()
    {
        if (!Check(TokenKind.IDENTIFIER))
            return null;

        var pos = PosOf(Current);

        string? qualifier = null;
        if (TypeQualifiers.Contains(Current.Value) && Peek(1).Kind == TokenKind.IDENTIFIER)
            qualifier = Advance().Value;

        var name = Advance().Value;
        while (Check(TokenKind.DOT) && Peek(1).Kind == TokenKind.IDENTIFIER)
        {
            Advance();
            name += "." + Advance().Value;
        }

        var typeArguments = new List<TypeRef>();
        if (Match(TokenKind.LESS))
        {
            do
            {
                if (TryParseType() is not { } argument)
                    return null;
                typeArguments.Add(argument);
            }
            while (Match(TokenKind.COMMA));

            if (!Match(TokenKind.GREATER))
                return null;
        }

        var isArray = false;
        if (Check(TokenKind.OPEN_BRACKET) && Peek(1).Kind == TokenKind.CLOSE_BRACKET)
        {
            _pos += 2;
            isArray = true;
        }

        return new TypeRef(qualifier, name, typeArguments, isArray, pos);
    }

    // ── Structures ──────────────────────────────────────────

    /// <summary>Right-hand side of a declaration: an expression or a structure (if, switch, for, while).</summary>
    private Expr ParseValue()
        => Current.Kind switch
        {
            TokenKind.IF => ParseIf(),
            TokenKind.SWITCH => ParseSwitch(),
            TokenKind.FOR => ParseFor(),
            TokenKind.WHILE => ParseWhile(),
            _ => ParseExpression(),
        };

    private IfExpr ParseIf()
    {
        var pos = PosOf(Expect(TokenKind.IF, "'if'"));
        var condition = ParseExpression();
        var then = ParseBlock();

        Block? @else = null;
        if (Check(TokenKind.ELSE))
        {
            var elsePos = PosOf(Advance());
            @else = Check(TokenKind.IF)
                ? new Block([new ExpressionStatement(ParseIf(), elsePos)], elsePos)
                : ParseBlock();
        }

        return new IfExpr(condition, then, @else, pos);
    }

    private SwitchExpr ParseSwitch()
    {
        var pos = PosOf(Expect(TokenKind.SWITCH, "'switch'"));
        var subject = Check(TokenKind.NEWLINE) ? null : ParseExpression();

        Expect(TokenKind.NEWLINE, "end of line");
        Expect(TokenKind.INDENT, "switch cases");

        var cases = new List<SwitchCase>();
        while (!Check(TokenKind.DEDENT) && !Check(TokenKind.EOF))
        {
            var casePos = PosOf(Current);
            var condition = Check(TokenKind.ARROW) ? null : ParseExpression();
            Expect(TokenKind.ARROW, "'=>'");
            cases.Add(new SwitchCase(condition, ParseArrowBody(), casePos));

            if (Previous.Kind != TokenKind.DEDENT)
                Expect(TokenKind.NEWLINE, "end of line");
        }

        Expect(TokenKind.DEDENT, "end of block");
        return new SwitchExpr(subject, cases, pos);
    }

    private Expr ParseFor()
    {
        var pos = PosOf(Expect(TokenKind.FOR, "'for'"));

        if (Match(TokenKind.OPEN_BRACKET))
        {
            var index = Expect(TokenKind.IDENTIFIER, "index variable").Value;
            Expect(TokenKind.COMMA, "','");
            var item = Expect(TokenKind.IDENTIFIER, "item variable").Value;
            Expect(TokenKind.CLOSE_BRACKET, "']'");
            Expect(TokenKind.IN, "'in'");
            var collection = ParseExpression();
            return new ForInExpr(index, item, collection, ParseBlock(), pos);
        }

        var variable = Expect(TokenKind.IDENTIFIER, "loop variable").Value;

        if (Match(TokenKind.IN))
        {
            var collection = ParseExpression();
            return new ForInExpr(null, variable, collection, ParseBlock(), pos);
        }

        Expect(TokenKind.ASSIGNMENT, "'=' or 'in'");
        var from = ParseExpression();
        Expect(TokenKind.TO, "'to'");
        var to = ParseExpression();
        var step = Match(TokenKind.BY) ? ParseExpression() : null;

        return new ForExpr(variable, from, to, step, ParseBlock(), pos);
    }

    private WhileExpr ParseWhile()
    {
        var pos = PosOf(Expect(TokenKind.WHILE, "'while'"));
        var condition = ParseExpression();
        return new WhileExpr(condition, ParseBlock(), pos);
    }

    // ── Expressions (Pratt) ─────────────────────────────────

    private static int BinaryPrecedence(TokenKind kind)
        => kind switch
        {
            TokenKind.OR => 1,
            TokenKind.AND => 2,
            TokenKind.EQUALS or TokenKind.NOT_EQUALS => 3,
            TokenKind.LESS or TokenKind.LESS_EQUALS or TokenKind.GREATER or TokenKind.GREATER_EQUALS => 4,
            TokenKind.PLUS or TokenKind.MINUS => 5,
            TokenKind.STAR or TokenKind.SLASH or TokenKind.PERCENT => 6,
            _ => 0,
        };

    private static BinaryOp ToBinaryOp(TokenKind kind)
        => kind switch
        {
            TokenKind.OR => BinaryOp.Or,
            TokenKind.AND => BinaryOp.And,
            TokenKind.EQUALS => BinaryOp.Equal,
            TokenKind.NOT_EQUALS => BinaryOp.NotEqual,
            TokenKind.LESS => BinaryOp.Less,
            TokenKind.LESS_EQUALS => BinaryOp.LessEqual,
            TokenKind.GREATER => BinaryOp.Greater,
            TokenKind.GREATER_EQUALS => BinaryOp.GreaterEqual,
            TokenKind.PLUS => BinaryOp.Add,
            TokenKind.MINUS => BinaryOp.Subtract,
            TokenKind.STAR => BinaryOp.Multiply,
            TokenKind.SLASH => BinaryOp.Divide,
            TokenKind.PERCENT => BinaryOp.Modulo,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private Expr ParseExpression()
        => ParseTernary();

    private Expr ParseTernary()
    {
        var condition = ParseBinary(0);
        if (!Check(TokenKind.QUESTION))
            return condition;

        Advance();
        var then = ParseTernary();
        Expect(TokenKind.COLON, "':'");
        var @else = ParseTernary();
        return new TernaryExpr(condition, then, @else, condition.Pos);
    }

    private Expr ParseBinary(int minPrecedence)
    {
        var left = ParseUnary();

        while (BinaryPrecedence(Current.Kind) is var precedence && precedence > minPrecedence)
        {
            var op = ToBinaryOp(Advance().Kind);
            var right = ParseBinary(precedence);
            left = new BinaryExpr(left, op, right, left.Pos);
        }

        return left;
    }

    private Expr ParseUnary()
    {
        UnaryOp? op = Current.Kind switch
        {
            TokenKind.MINUS => UnaryOp.Negate,
            TokenKind.PLUS => UnaryOp.Plus,
            TokenKind.NOT => UnaryOp.Not,
            _ => null,
        };

        if (op is null)
            return ParsePostfix(ParsePrimary());

        var pos = PosOf(Advance());
        return new UnaryExpr(op.Value, ParseUnary(), pos);
    }

    private Expr ParsePostfix(Expr expr)
    {
        while (true)
        {
            switch (Current.Kind)
            {
                case TokenKind.DOT:
                    Advance();
                    expr = new MemberExpr(expr, Expect(TokenKind.IDENTIFIER, "member name").Value, expr.Pos);
                    break;
                case TokenKind.OPEN_PAREN:
                    expr = new CallExpr(expr, ParseArguments(), expr.Pos);
                    break;
                case TokenKind.OPEN_BRACKET:
                    Advance();
                    var offset = ParseExpression();
                    Expect(TokenKind.CLOSE_BRACKET, "']'");
                    expr = new HistoryExpr(expr, offset, expr.Pos);
                    break;
                default:
                    return expr;
            }
        }
    }

    private List<Argument> ParseArguments()
    {
        Expect(TokenKind.OPEN_PAREN, "'('");

        var arguments = new List<Argument>();
        if (!Check(TokenKind.CLOSE_PAREN))
        {
            do
            {
                var pos = PosOf(Current);
                string? name = null;
                if (Check(TokenKind.IDENTIFIER) && Peek(1).Kind == TokenKind.ASSIGNMENT)
                {
                    name = Advance().Value;
                    Advance();
                }
                else if (arguments.Count > 0 && arguments[^1].Name is not null)
                {
                    throw Error("Positional argument after named argument");
                }

                arguments.Add(new Argument(name, ParseExpression(), pos));
            }
            while (Match(TokenKind.COMMA));
        }

        Expect(TokenKind.CLOSE_PAREN, "')'");
        return arguments;
    }

    private Expr ParsePrimary()
    {
        var token = Current;
        var pos = PosOf(token);

        switch (token.Kind)
        {
            case TokenKind.INT:
                Advance();
                return long.TryParse(token.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? new IntLiteral(value, pos)
                    : throw new ParseException("Integer literal out of range", token.Line, token.Column);
            case TokenKind.FLOAT:
                Advance();
                return new FloatLiteral(double.Parse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture), pos);
            case TokenKind.STRING:
                Advance();
                return new StringLiteral(token.Value, pos);
            case TokenKind.COLOR_HEX:
                Advance();
                return new ColorLiteral(token.Value, pos);
            case TokenKind.TRUE or TokenKind.FALSE:
                Advance();
                return new BoolLiteral(token.Kind == TokenKind.TRUE, pos);
            case TokenKind.IDENTIFIER:
                Advance();
                return new IdentifierExpr(token.Value, pos);
            case TokenKind.OPEN_PAREN:
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.CLOSE_PAREN, "')'");
                return inner;
            case TokenKind.OPEN_BRACKET:
                Advance();
                var elements = new List<Expr>();
                if (!Check(TokenKind.CLOSE_BRACKET))
                {
                    do
                        elements.Add(ParseExpression());
                    while (Match(TokenKind.COMMA));
                }
                Expect(TokenKind.CLOSE_BRACKET, "']'");
                return new TupleExpr(elements, pos);
            default:
                throw Error("Expected an expression");
        }
    }
}
