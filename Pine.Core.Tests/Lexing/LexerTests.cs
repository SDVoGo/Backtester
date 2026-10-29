using Pine.Core.Lexing;
using static Pine.Core.Lexing.TokenKind;

namespace Pine.Core.Tests.Lexing;

public class LexerTests
{
    private static TokenKind[] Kinds(string source)
        => Lexer.Tokenize(source).Select(t => t.Kind).ToArray();

    private static Token Single(string source)
        => Lexer.Tokenize(source)[0];

    [Fact]
    public void Expression()
    {
        var tokens = Lexer.Tokenize("   1 +    1  * 9.98  - 12 % (\"alfa\")");

        Assert.Equal(
            [INT, PLUS, INT, STAR, FLOAT, MINUS, INT, PERCENT, OPEN_PAREN, STRING, CLOSE_PAREN, NEWLINE, EOF],
            tokens.Select(t => t.Kind));
        Assert.Equal("9.98", tokens[4].Value);
        Assert.Equal("alfa", tokens[9].Value);
    }

    [Fact]
    public void EmptySource_OnlyEof()
        => Assert.Equal([EOF], Kinds(""));

    [Theory]
    [InlineData("and", AND)]
    [InlineData("or", OR)]
    [InlineData("not", NOT)]
    [InlineData("if", IF)]
    [InlineData("else", ELSE)]
    [InlineData("for", FOR)]
    [InlineData("to", TO)]
    [InlineData("by", BY)]
    [InlineData("in", IN)]
    [InlineData("while", WHILE)]
    [InlineData("switch", SWITCH)]
    [InlineData("var", VAR)]
    [InlineData("varip", VARIP)]
    [InlineData("true", TRUE)]
    [InlineData("false", FALSE)]
    [InlineData("na", IDENTIFIER)]
    [InlineData("float", IDENTIFIER)]
    [InlineData("color", IDENTIFIER)]
    [InlineData("android", IDENTIFIER)]
    [InlineData("_x1", IDENTIFIER)]
    public void KeywordsAndIdentifiers(string source, TokenKind expected)
        => Assert.Equal(expected, Single(source).Kind);

    [Theory]
    [InlineData(":=", REASSIGNMENT)]
    [InlineData("==", EQUALS)]
    [InlineData("!=", NOT_EQUALS)]
    [InlineData("<=", LESS_EQUALS)]
    [InlineData(">=", GREATER_EQUALS)]
    [InlineData("=>", ARROW)]
    [InlineData("+=", PLUS_EQUALS)]
    [InlineData("-=", MINUS_EQUALS)]
    [InlineData("*=", STAR_EQUALS)]
    [InlineData("/=", SLASH_EQUALS)]
    [InlineData("%=", PERCENT_EQUALS)]
    [InlineData("=", ASSIGNMENT)]
    [InlineData("<", LESS)]
    [InlineData(">", GREATER)]
    [InlineData("?", QUESTION)]
    [InlineData(":", COLON)]
    [InlineData(".", DOT)]
    [InlineData(",", COMMA)]
    public void Operators(string source, TokenKind expected)
    {
        var token = Single(source);
        Assert.Equal(expected, token.Kind);
        Assert.Equal(source, token.Value);
    }

    [Theory]
    [InlineData("&")]
    [InlineData("!")]
    [InlineData("{")]
    [InlineData(";")]
    [InlineData("@")]
    public void UnknownCharacter_Throws(string source)
        => Assert.Throws<LexerException>(() => Lexer.Tokenize(source));

    [Theory]
    [InlineData("42", INT)]
    [InlineData("1.5", FLOAT)]
    [InlineData(".5", FLOAT)]
    [InlineData("1.", FLOAT)]
    [InlineData("1e3", FLOAT)]
    [InlineData("2.5E-4", FLOAT)]
    [InlineData("3e+2", FLOAT)]
    public void Numbers(string source, TokenKind expected)
    {
        var token = Single(source);
        Assert.Equal(expected, token.Kind);
        Assert.Equal(source, token.Value);
    }

    [Fact]
    public void HistoryReferenceIsNotFloat()
        => Assert.Equal([IDENTIFIER, OPEN_BRACKET, INT, CLOSE_BRACKET, NEWLINE, EOF], Kinds("close[1]"));

    [Theory]
    [InlineData("\"abc\"", "abc")]
    [InlineData("'abc'", "abc")]
    [InlineData("'it\\'s'", "it's")]
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("\"a\\nb\"", "a\nb")]
    [InlineData("\"#.##\"", "#.##")]
    [InlineData("\"// not a comment\"", "// not a comment")]
    public void Strings(string source, string expected)
    {
        var token = Single(source);
        Assert.Equal(STRING, token.Kind);
        Assert.Equal(expected, token.Value);
    }

    [Theory]
    [InlineData("\"abc")]
    [InlineData("\"abc\ndef\"")]
    public void UnterminatedString_Throws(string source)
        => Assert.Throws<LexerException>(() => Lexer.Tokenize(source));

    [Theory]
    [InlineData("#26c477")]
    [InlineData("#FFD600")]
    [InlineData("#FFD60080")]
    public void Colors(string source)
    {
        var token = Single(source);
        Assert.Equal(COLOR_HEX, token.Kind);
        Assert.Equal(source, token.Value);
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#26c477z")]
    public void InvalidColor_Throws(string source)
        => Assert.Throws<LexerException>(() => Lexer.Tokenize(source));

    [Fact]
    public void Comments_AreSkipped()
        => Assert.Equal(
            [IDENTIFIER, ASSIGNMENT, INT, NEWLINE, IDENTIFIER, ASSIGNMENT, INT, NEWLINE, EOF],
            Kinds("// header\nx = 1 // trailing\n// between\ny = 2"));

    [Fact]
    public void VersionDirective()
    {
        var tokens = Lexer.Tokenize("// header\n//@version=6\nindicator(\"x\")");

        Assert.Equal(
            [VERSION, NEWLINE, IDENTIFIER, OPEN_PAREN, STRING, CLOSE_PAREN, NEWLINE, EOF],
            tokens.Select(t => t.Kind));
        Assert.Equal("6", tokens[0].Value);
    }

    [Fact]
    public void VersionDirective_AfterCode_IsComment()
        => Assert.Equal([IDENTIFIER, NEWLINE, EOF], Kinds("x //@version=6"));

    [Fact]
    public void Blocks_ProduceIndentAndDedent()
    {
        const string source = """
            if a
                b := 1
            else
                c := 2
            d = 3
            """;

        Assert.Equal(
            [
                IF, IDENTIFIER, NEWLINE,
                INDENT, IDENTIFIER, REASSIGNMENT, INT, NEWLINE,
                DEDENT, ELSE, NEWLINE,
                INDENT, IDENTIFIER, REASSIGNMENT, INT, NEWLINE,
                DEDENT, IDENTIFIER, ASSIGNMENT, INT, NEWLINE,
                EOF,
            ],
            Kinds(source));
    }

    [Fact]
    public void NestedBlocks_ClosedAtEndOfFile()
    {
        const string source = "if a\n    if b\n        c := 1\n";

        Assert.Equal(
            [
                IF, IDENTIFIER, NEWLINE,
                INDENT, IF, IDENTIFIER, NEWLINE,
                INDENT, IDENTIFIER, REASSIGNMENT, INT, NEWLINE,
                DEDENT, DEDENT, EOF,
            ],
            Kinds(source));
    }

    [Fact]
    public void BlankAndCommentLines_DoNotAffectIndentation()
    {
        const string source = "if a\n    b := 1\n\n  // comment\n    \n    c := 2\nd = 1";

        Assert.Equal(
            [
                IF, IDENTIFIER, NEWLINE,
                INDENT, IDENTIFIER, REASSIGNMENT, INT, NEWLINE,
                IDENTIFIER, REASSIGNMENT, INT, NEWLINE,
                DEDENT, IDENTIFIER, ASSIGNMENT, INT, NEWLINE,
                EOF,
            ],
            Kinds(source));
    }

    [Fact]
    public void TabCountsAsOneLevel()
        => Assert.Equal(
            [IF, IDENTIFIER, NEWLINE, INDENT, IDENTIFIER, NEWLINE, DEDENT, EOF],
            Kinds("if a\n\tb"));

    [Fact]
    public void WindowsLineEndings()
        => Assert.Equal(
            [IF, IDENTIFIER, NEWLINE, INDENT, IDENTIFIER, NEWLINE, DEDENT, IDENTIFIER, NEWLINE, EOF],
            Kinds("if a\r\n    b\r\nc\r\n"));

    [Fact]
    public void NewlinesInsideParentheses_AreIgnored()
    {
        const string source = """
            plotcandle(open,
                       high,
                       close)
            x = 1
            """;

        Assert.Equal(
            [
                IDENTIFIER, OPEN_PAREN, IDENTIFIER, COMMA, IDENTIFIER, COMMA, IDENTIFIER, CLOSE_PAREN, NEWLINE,
                IDENTIFIER, ASSIGNMENT, INT, NEWLINE,
                EOF,
            ],
            Kinds(source));
    }

    [Fact]
    public void NonMultipleOfFourIndentation_ContinuesLine()
        => Assert.Equal(
            [IDENTIFIER, ASSIGNMENT, IDENTIFIER, PLUS, IDENTIFIER, NEWLINE, EOF],
            Kinds("x = a +\n  b"));

    [Fact]
    public void InconsistentDedent_Throws()
    {
        var ex = Assert.Throws<LexerException>(() => Lexer.Tokenize("if a\n        b\n    c"));
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public void Positions_AreOneBased()
    {
        var tokens = Lexer.Tokenize("x = 1\n  + foo");

        Assert.Equal((1, 1), (tokens[0].Line, tokens[0].Column));
        Assert.Equal((1, 3), (tokens[1].Line, tokens[1].Column));
        Assert.Equal((2, 3), (tokens[3].Line, tokens[3].Column));
        Assert.Equal((2, 5), (tokens[4].Line, tokens[4].Column));
    }

    [Fact]
    public void Error_ReportsPosition()
    {
        var ex = Assert.Throws<LexerException>(() => Lexer.Tokenize("x = 1\ny = a & b"));
        Assert.Equal((2, 7), (ex.Line, ex.Column));
    }

    [Fact]
    public void Sample_Test002_Tokenizes()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "test002.pine"));

        var tokens = Lexer.Tokenize(source);

        Assert.Equal(VERSION, tokens[0].Kind);
        Assert.Equal("6", tokens[0].Value);
        Assert.Equal(EOF, tokens[^1].Kind);
        Assert.Equal(tokens.Count(t => t.Kind == INDENT), tokens.Count(t => t.Kind == DEDENT));
        Assert.Contains(tokens, t => t.Kind == ARROW);
        Assert.Contains(tokens, t => t.Kind == REASSIGNMENT);
    }
}
