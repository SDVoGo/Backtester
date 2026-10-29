namespace Pine.Core.Lexing;

public enum TokenKind
{
    EOF,

    // ── Layout ──────────────────────────────────────────────
    NEWLINE,
    INDENT,
    DEDENT,

    // ── Literals ────────────────────────────────────────────
    INT,
    FLOAT,
    STRING,
    COLOR_HEX,
    IDENTIFIER,

    // ── Directives ──────────────────────────────────────────
    VERSION,

    // ── Keywords ────────────────────────────────────────────
    AND,
    OR,
    NOT,
    IF,
    ELSE,
    FOR,
    IN,
    TO,
    BY,
    WHILE,
    SWITCH,
    BREAK,
    CONTINUE,
    VAR,
    VARIP,
    TRUE,
    FALSE,
    IMPORT,
    EXPORT,
    AS,
    TYPE,
    METHOD,
    ENUM,

    // ── Brackets / Parens ───────────────────────────────────
    OPEN_BRACKET,
    CLOSE_BRACKET,
    OPEN_PAREN,
    CLOSE_PAREN,

    // ── Comparison / Assignment ─────────────────────────────
    ASSIGNMENT,
    REASSIGNMENT,
    EQUALS,
    NOT_EQUALS,

    LESS,
    LESS_EQUALS,
    GREATER,
    GREATER_EQUALS,

    PLUS_EQUALS,
    MINUS_EQUALS,
    STAR_EQUALS,
    SLASH_EQUALS,
    PERCENT_EQUALS,

    // ── Punctuation ─────────────────────────────────────────
    ARROW,
    DOT,
    COLON,
    QUESTION,
    COMMA,

    // ── Operators ───────────────────────────────────────────
    PLUS,
    MINUS,
    STAR,
    SLASH,
    PERCENT,
}
