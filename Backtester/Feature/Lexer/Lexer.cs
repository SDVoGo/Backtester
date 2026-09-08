using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

public enum TokenKind
{
    EOF,
    SPACE,
    FLOAT,
    INT,
    STRING,
    IDENTIFIER,

    OPEN_BRACKET,
    CLOSE_BRACKET,
    OPEN_CURLY,
    CLOSE_CURLY,
    OPEN_PAREN,
    CLOSE_PAREN,

    ASSIGNMENT,
    EQUALS,
    NOT_EQUALS,

    LESS,
    LESS_EQUALS,
    GREATER,
    GREATER_EQUALS,

    OR,
    AND,

    DOT,
    SEMICOLON,
    QUESTION,
    COMMA,

    PLUS_PLUS,
    MINUS_MINUS,
    PLUS_EQUALS,
    MINUS_EQUALS,

    PLUS,
    MINUS,
    STAR,
    SLASH,
    PERCENT,
}

public static class TokenKindExtensions
{
    public static string? ToString(this TokenKind kind)
        => Enum.GetName(kind);
}

public static partial class TokenRecognition
{
    public static (TokenKind Kind, Regex Pattern)[] Patterns =
    {
        (TokenKind.SPACE,        WhiteSpace()),
        (TokenKind.STRING,       QuotedString()),
        (TokenKind.FLOAT,        Float()),
        (TokenKind.INT,          Integer()),
        (TokenKind.IDENTIFIER,   Identifier()),

        (TokenKind.OPEN_BRACKET,  OpenBracket()),
        (TokenKind.CLOSE_BRACKET, CloseBracket()),
        (TokenKind.OPEN_CURLY,    OpenCurly()),
        (TokenKind.CLOSE_CURLY,   CloseCurly()),
        (TokenKind.OPEN_PAREN,    OpenParen()),
        (TokenKind.CLOSE_PAREN,   CloseParen()),

        (TokenKind.ASSIGNMENT,   Assignment()),
        (TokenKind.EQUALS,       Equals()),
        (TokenKind.NOT_EQUALS,   NotEquals()),

        (TokenKind.LESS,         Less()),
        (TokenKind.LESS_EQUALS,  LessEquals()),
        (TokenKind.GREATER,      Greater()),
        (TokenKind.GREATER_EQUALS, GreaterEquals()),

        (TokenKind.OR,           Or()),
        (TokenKind.AND,          And()),

        (TokenKind.DOT,        Dot()),
        (TokenKind.SEMICOLON,  Semicolon()),
        (TokenKind.QUESTION,   Question()),
        (TokenKind.COMMA,      Comma()),

        (TokenKind.PLUS_PLUS,   PlusPlus()),
        (TokenKind.MINUS_MINUS, MinusMinus()),
        (TokenKind.PLUS_EQUALS, PlusEquals()),
        (TokenKind.MINUS_EQUALS, MinusEquals()),

        (TokenKind.PLUS,    Plus()),
        (TokenKind.MINUS,   Minus()),
        (TokenKind.STAR,    Star()),
        (TokenKind.SLASH,   Slash()),
        (TokenKind.PERCENT, Percent()),
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    // ── String ──────────────────────────────────────────────
    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedString();

    // ── Number ──────────────────────────────────────────────
    [GeneratedRegex(@"\d+")]
    private static partial Regex Integer();

    [GeneratedRegex(@"\d+\.\d+")]
    private static partial Regex Float();

    // ── Identifier ──────────────────────────────────────────
    [GeneratedRegex(@"[a-zA-Z_]\w*")]
    private static partial Regex Identifier();

    // ── Brackets / Parens ───────────────────────────────────
    [GeneratedRegex(@"\[")]
    private static partial Regex OpenBracket();

    [GeneratedRegex(@"]")]
    private static partial Regex CloseBracket();

    [GeneratedRegex(@"\{")]
    private static partial Regex OpenCurly();

    [GeneratedRegex(@"\}")]
    private static partial Regex CloseCurly();

    [GeneratedRegex(@"\(")]
    private static partial Regex OpenParen();

    [GeneratedRegex(@"\)")]
    private static partial Regex CloseParen();

    // ── Comparison / Assignment ─────────────────────────────
    [GeneratedRegex(@"=")]
    private static partial Regex Assignment();

    [GeneratedRegex(@"==")]
    private static partial Regex Equals();

    [GeneratedRegex(@"!=")]
    private static partial Regex NotEquals();

    [GeneratedRegex(@"<")]
    private static partial Regex Less();

    [GeneratedRegex(@"<=")]
    private static partial Regex LessEquals();

    [GeneratedRegex(@">")]
    private static partial Regex Greater();

    [GeneratedRegex(@">=")]
    private static partial Regex GreaterEquals();

    // ── Logical ─────────────────────────────────────────────
    [GeneratedRegex(@"\|\|")]
    private static partial Regex Or();

    [GeneratedRegex(@"&&")]
    private static partial Regex And();

    // ── Punctuation ─────────────────────────────────────────
    [GeneratedRegex(@"\.")]
    private static partial Regex Dot();

    [GeneratedRegex(@";")]
    private static partial Regex Semicolon();

    [GeneratedRegex(@"\?")]
    private static partial Regex Question();

    [GeneratedRegex(@",")]
    private static partial Regex Comma();

    // ── Operators ───────────────────────────────────────────
    [GeneratedRegex(@"\+\+")]
    private static partial Regex PlusPlus();

    [GeneratedRegex(@"--")]
    private static partial Regex MinusMinus();

    [GeneratedRegex(@"\+=")]
    private static partial Regex PlusEquals();

    [GeneratedRegex(@"-=")]
    private static partial Regex MinusEquals();

    [GeneratedRegex(@"\+")]
    private static partial Regex Plus();

    [GeneratedRegex(@"-")]
    private static partial Regex Minus();

    [GeneratedRegex(@"\*")]
    private static partial Regex Star();

    [GeneratedRegex(@"/")]
    private static partial Regex Slash();

    [GeneratedRegex(@"%")]
    private static partial Regex Percent();
}

public record Token(TokenKind Kind, string Value);


public class Lexer
{
    public int Position { get; private set; } = 0;
    public List<Token> Tokens { get; } = [];

    private void AdvancePosition(int n)
        => Position += n;

    private void AddToken(Token token)
        => Tokens.Add(token);

    private bool TryMatch(string source,
                            out Token? token,
                            out int length)
    {
        var match = TokenRecognition.Patterns.Select(p => (p, match: p.Pattern.Match(source)))
                                                .Where(x => x.match.Success && x.match.Index == 0)
                                                .MaxBy(x => x.match.Length);

        if (match == default)
        {
            token = default;
            length = 0;
            return false;
        }

        var (pattern, tokenMatch) = match;

        var value = pattern.Kind == TokenKind.STRING
                        ? tokenMatch.Value[1..^1]
                        : tokenMatch.Value;

        token = new(pattern.Kind, value);
        length = tokenMatch.Value.Length;

        return true;
    }

    /// <summary>
    /// Collect tokens
    /// </summary>
    /// <param name="source"></param>
    public void Tokenize(string source)
    {
        while (source.Length > 0)
        {
            if (!TryMatch(source, out var token, out var length))
                throw new Exception(
                    $"Unrecognised Token at Position {Position}");

            if (token!.Kind != TokenKind.SPACE)
                AddToken(token);

            AdvancePosition(length);
            source = source[length..];
        }
    }
}
[ApiController]
[Route("api/lexer")]
public class LexerController : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> Scan(string source)
    {
        var lexer = new Lexer();
        lexer.Tokenize(source);
        return Ok(lexer.Tokens);
    }

    [HttpPost("scan001")]
    public async Task<IActionResult> Scan001()
        => await Scan("   1 +    1  * 9.98  - 12 % (\"alfa\")");
}