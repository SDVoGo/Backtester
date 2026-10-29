using System.Text;

namespace Pine.Core.Lexing;

/// <summary>
/// Hand-written PineScript scanner.
/// Blocks are delimited by indentation (multiples of 4 spaces, a tab counts as 4)
/// and reported as INDENT / DEDENT tokens; statements end with NEWLINE.
/// A line indented by a non-multiple of 4, or any line inside ( ) or [ ],
/// continues the previous statement.
/// </summary>
public sealed class Lexer
{
    private const int TabWidth = 4;
    private const int IndentWidth = 4;

    private static readonly Dictionary<string, TokenKind> Keywords = new()
    {
        ["and"] = TokenKind.AND,
        ["or"] = TokenKind.OR,
        ["not"] = TokenKind.NOT,
        ["if"] = TokenKind.IF,
        ["else"] = TokenKind.ELSE,
        ["for"] = TokenKind.FOR,
        ["in"] = TokenKind.IN,
        ["to"] = TokenKind.TO,
        ["by"] = TokenKind.BY,
        ["while"] = TokenKind.WHILE,
        ["switch"] = TokenKind.SWITCH,
        ["break"] = TokenKind.BREAK,
        ["continue"] = TokenKind.CONTINUE,
        ["var"] = TokenKind.VAR,
        ["varip"] = TokenKind.VARIP,
        ["true"] = TokenKind.TRUE,
        ["false"] = TokenKind.FALSE,
        ["import"] = TokenKind.IMPORT,
        ["export"] = TokenKind.EXPORT,
        ["as"] = TokenKind.AS,
        ["type"] = TokenKind.TYPE,
        ["method"] = TokenKind.METHOD,
        ["enum"] = TokenKind.ENUM,
    };

    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private readonly Stack<int> _indents = new([0]);

    private int _pos;
    private int _line = 1;
    private int _lineStart;
    private int _groupDepth;
    private bool _atLineStart = true;

    private Lexer(string source)
        => _source = source;

    public static IReadOnlyList<Token> Tokenize(string source)
        => new Lexer(source).Run();

    private char Current => Peek(0);

    private char Peek(int offset)
        => _pos + offset < _source.Length ? _source[_pos + offset] : '\0';

    private int Column => _pos - _lineStart + 1;

    private List<Token> Run()
    {
        while (_pos < _source.Length)
        {
            if (_atLineStart)
            {
                _atLineStart = false;
                if (_groupDepth == 0)
                    HandleIndentation();
                continue;
            }

            switch (Current)
            {
                case ' ' or '\t' or '\r':
                    _pos++;
                    break;
                case '\n':
                    NewLine();
                    break;
                case '/' when Peek(1) == '/':
                    Comment();
                    break;
                default:
                    ScanToken();
                    break;
            }
        }

        EmitNewLine();
        while (_indents.Peek() > 0)
        {
            _indents.Pop();
            Add(TokenKind.DEDENT, "", Column);
        }
        Add(TokenKind.EOF, "", Column);

        return _tokens;
    }

    private void Add(TokenKind kind, string value, int column)
        => _tokens.Add(new Token(kind, value, _line, column));

    private LexerException Error(string message, int column)
        => new(message, _line, column);

    private void NewLine()
    {
        _pos++;
        _line++;
        _lineStart = _pos;
        _atLineStart = true;
    }

    /// <summary>
    /// Ends the current statement, unless nothing has been emitted since the last one.
    /// </summary>
    private void EmitNewLine()
    {
        if (_tokens.Count == 0)
            return;

        if (_tokens[^1].Kind is TokenKind.NEWLINE or TokenKind.INDENT or TokenKind.DEDENT)
            return;

        Add(TokenKind.NEWLINE, "", Column);
    }

    private void HandleIndentation()
    {
        var width = 0;
        while (Current is ' ' or '\t')
        {
            width += Current == '\t' ? TabWidth : 1;
            _pos++;
        }

        // Blank and comment-only lines don't affect the layout
        if (Current is '\0' or '\n' or '\r' || (Current == '/' && Peek(1) == '/'))
            return;

        // Continuation of the previous line
        if (width % IndentWidth != 0)
            return;

        EmitNewLine();

        if (width > _indents.Peek())
        {
            _indents.Push(width);
            Add(TokenKind.INDENT, "", Column);
            return;
        }

        while (width < _indents.Peek())
        {
            _indents.Pop();
            Add(TokenKind.DEDENT, "", Column);
        }

        if (width != _indents.Peek())
            throw Error("Inconsistent indentation", Column);
    }

    private void Comment()
    {
        var start = _pos;
        var column = Column;
        var wholeLine = _source.AsSpan(_lineStart, start - _lineStart).IsWhiteSpace();

        while (Current is not ('\0' or '\n'))
            _pos++;

        const string versionPrefix = "//@version=";
        var text = _source.AsSpan(start, _pos - start).TrimEnd();

        if (!wholeLine || !text.StartsWith(versionPrefix))
            return;

        var version = text[versionPrefix.Length..];
        if (version.IsEmpty || version.ContainsAnyExceptInRange('0', '9'))
            throw Error("Invalid version directive", column);

        EmitNewLine();
        Add(TokenKind.VERSION, version.ToString(), column);
    }

    private void ScanToken()
    {
        var c = Current;

        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek(1))))
            Number();
        else if (IsIdentifierStart(c))
            IdentifierOrKeyword();
        else if (c is '"' or '\'')
            QuotedString(c);
        else if (c == '#')
            HexColor();
        else
            Operator();
    }

    private static bool IsIdentifierStart(char c)
        => char.IsAsciiLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_';

    private void Number()
    {
        var start = _pos;
        var column = Column;
        var isFloat = false;

        while (char.IsAsciiDigit(Current))
            _pos++;

        // "1.5", ".5" and "1." are floats; "1.foo" is not
        if (Current == '.' && !IsIdentifierStart(Peek(1)))
        {
            isFloat = true;
            _pos++;
            while (char.IsAsciiDigit(Current))
                _pos++;
        }

        if (Current is 'e' or 'E')
        {
            var sign = Peek(1) is '+' or '-' ? 1 : 0;
            if (char.IsAsciiDigit(Peek(1 + sign)))
            {
                isFloat = true;
                _pos += 1 + sign;
                while (char.IsAsciiDigit(Current))
                    _pos++;
            }
        }

        Add(isFloat ? TokenKind.FLOAT : TokenKind.INT,
            _source[start.._pos],
            column);
    }

    private void IdentifierOrKeyword()
    {
        var start = _pos;
        var column = Column;

        while (IsIdentifierPart(Current))
            _pos++;

        var text = _source[start.._pos];
        Add(Keywords.GetValueOrDefault(text, TokenKind.IDENTIFIER), text, column);
    }

    private void QuotedString(char quote)
    {
        var column = Column;
        var value = new StringBuilder();
        _pos++;

        while (Current != quote)
        {
            if (Current is '\0' or '\n')
                throw Error("Unterminated string", column);

            if (Current == '\\')
            {
                _pos++;
                value.Append(Current switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\0' or '\n' => throw Error("Unterminated string", column),
                    var other => other,
                });
            }
            else
            {
                value.Append(Current);
            }

            _pos++;
        }

        _pos++;
        Add(TokenKind.STRING, value.ToString(), column);
    }

    private void HexColor()
    {
        var start = _pos;
        var column = Column;
        _pos++;

        while (char.IsAsciiHexDigit(Current))
            _pos++;

        var digits = _pos - start - 1;
        if ((digits != 6 && digits != 8) || IsIdentifierPart(Current))
            throw Error("Invalid color literal, expected #RRGGBB or #RRGGBBAA", column);

        Add(TokenKind.COLOR_HEX, _source[start.._pos], column);
    }

    private void Operator()
    {
        var column = Column;

        var (kind, length) = (Current, Peek(1)) switch
        {
            (':', '=') => (TokenKind.REASSIGNMENT, 2),
            ('=', '=') => (TokenKind.EQUALS, 2),
            ('=', '>') => (TokenKind.ARROW, 2),
            ('!', '=') => (TokenKind.NOT_EQUALS, 2),
            ('<', '=') => (TokenKind.LESS_EQUALS, 2),
            ('>', '=') => (TokenKind.GREATER_EQUALS, 2),
            ('+', '=') => (TokenKind.PLUS_EQUALS, 2),
            ('-', '=') => (TokenKind.MINUS_EQUALS, 2),
            ('*', '=') => (TokenKind.STAR_EQUALS, 2),
            ('/', '=') => (TokenKind.SLASH_EQUALS, 2),
            ('%', '=') => (TokenKind.PERCENT_EQUALS, 2),

            ('=', _) => (TokenKind.ASSIGNMENT, 1),
            ('<', _) => (TokenKind.LESS, 1),
            ('>', _) => (TokenKind.GREATER, 1),
            ('+', _) => (TokenKind.PLUS, 1),
            ('-', _) => (TokenKind.MINUS, 1),
            ('*', _) => (TokenKind.STAR, 1),
            ('/', _) => (TokenKind.SLASH, 1),
            ('%', _) => (TokenKind.PERCENT, 1),
            ('?', _) => (TokenKind.QUESTION, 1),
            (':', _) => (TokenKind.COLON, 1),
            ('.', _) => (TokenKind.DOT, 1),
            (',', _) => (TokenKind.COMMA, 1),
            ('(', _) => (TokenKind.OPEN_PAREN, 1),
            (')', _) => (TokenKind.CLOSE_PAREN, 1),
            ('[', _) => (TokenKind.OPEN_BRACKET, 1),
            (']', _) => (TokenKind.CLOSE_BRACKET, 1),

            _ => throw Error($"Unexpected character '{Current}'", column),
        };

        if (kind is TokenKind.OPEN_PAREN or TokenKind.OPEN_BRACKET)
            _groupDepth++;
        else if (kind is TokenKind.CLOSE_PAREN or TokenKind.CLOSE_BRACKET && _groupDepth > 0)
            _groupDepth--;

        Add(kind, _source.Substring(_pos, length), column);
        _pos += length;
    }
}
