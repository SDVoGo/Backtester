namespace Pine.Core.Lexing;

/// <summary>
/// A lexical token. <see cref="Value"/> holds the source text, except for
/// strings (unescaped content, without quotes) and VERSION (the version number).
/// <see cref="Line"/> and <see cref="Column"/> are 1-based.
/// </summary>
public record Token(TokenKind Kind, string Value, int Line, int Column);

public class LexerException(string message, int line, int column)
    : Exception($"{message} (line {line}, column {column})")
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}
