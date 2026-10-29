using Pine.Core.Parsing;
using Pine.Core.Syntax;

namespace Pine.Core.Tests.Parsing;

public class ParserTests
{
    private static string Parse(string source)
        => AstPrinter.Print(Parser.Parse(source));

    private static string ReadSample(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    // ── Expressions ─────────────────────────────────────────

    [Theory]
    [InlineData("1 + 2 * 3", "(+ 1 (* 2 3))")]
    [InlineData("(1 + 2) * 3", "(* (+ 1 2) 3)")]
    [InlineData("a - b - c", "(- (- a b) c)")]
    [InlineData("a / b % c", "(% (/ a b) c)")]
    [InlineData("-a * b", "(* (- a) b)")]
    [InlineData("not a and b", "(and (not a) b)")]
    [InlineData("a or b and c", "(or a (and b c))")]
    [InlineData("a == b and c != d", "(and (== a b) (!= c d))")]
    [InlineData("a + 1 > b * 2", "(> (+ a 1) (* b 2))")]
    [InlineData("a < b == c >= d", "(== (< a b) (>= c d))")]
    [InlineData("a ? b : c", "(? a b c)")]
    [InlineData("a ? b : c ? d : e", "(? a b (? c d e))")]
    [InlineData("x > 0 and y ? 1 : -1", "(? (and (> x 0) y) 1 (- 1))")]
    public void Precedence(string source, string expected)
        => Assert.Equal(expected, Parse(source));

    [Theory]
    [InlineData("42", "42")]
    [InlineData("1.5", "1.5")]
    [InlineData("2.0", "2.0")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("\"abc\"", "\"abc\"")]
    [InlineData("#26c477", "#26c477")]
    [InlineData("na", "na")]
    public void Literals(string source, string expected)
        => Assert.Equal(expected, Parse(source));

    [Fact]
    public void LiteralTypes()
    {
        var script = Parser.Parse("1\n1.0\ntrue\n\"s\"\n#ffffff");
        var kinds = script.Statements.Cast<ExpressionStatement>().Select(s => s.Expression.GetType());

        Assert.Equal(
            [typeof(IntLiteral), typeof(FloatLiteral), typeof(BoolLiteral), typeof(StringLiteral), typeof(ColorLiteral)],
            kinds);
    }

    [Theory]
    [InlineData("close[1]", "close[1]")]
    [InlineData("dir != dir[1]", "(!= dir dir[1])")]
    [InlineData("ta.sma(close, 14)[2]", "(call ta.sma close 14)[2]")]
    [InlineData("barstate.islast", "barstate.islast")]
    [InlineData("f()", "(call f)")]
    [InlineData("color.new(c, 20)", "(call color.new c 20)")]
    [InlineData("plot(x, \"T\", color = c, linewidth = 2)", "(call plot x \"T\" color=c linewidth=2)")]
    [InlineData("input.string(\"A\", options = [\"A\", \"B\"])", "(call input.string \"A\" options=[\"A\", \"B\"])")]
    [InlineData("[a, b]", "[a, b]")]
    [InlineData("-close[1]", "(- close[1])")]
    public void Postfix(string source, string expected)
        => Assert.Equal(expected, Parse(source));

    [Fact]
    public void ArgumentsSpanningLines()
        => Assert.Equal(
            "(call plotcandle o h c title=\"x\")",
            Parse("plotcandle(o,\n           h,\n           c,\n           title = \"x\")"));

    [Fact]
    public void ExpressionContinuedOnNextLine()
        => Assert.Equal("(decl x (+ a b))", Parse("x = a +\n  b"));

    // ── Declarations and assignments ────────────────────────

    [Theory]
    [InlineData("x = 1", "(decl x 1)")]
    [InlineData("float x = 1", "(decl float x 1)")]
    [InlineData("var x = 1", "(decl var x 1)")]
    [InlineData("var float x = high", "(decl var float x high)")]
    [InlineData("varip int n = 0", "(decl varip int n 0)")]
    [InlineData("series float x = close", "(decl series float x close)")]
    [InlineData("float[] xs = na", "(decl float[] xs na)")]
    [InlineData("array<float> xs = array.new_float()", "(decl array<float> xs (call array.new_float))")]
    [InlineData("var label lb = na", "(decl var label lb na)")]
    [InlineData("[st, dir] = ta.supertrend(f, p)", "(decl [st, dir] (call ta.supertrend f p))")]
    public void Declarations(string source, string expected)
        => Assert.Equal(expected, Parse(source));

    [Theory]
    [InlineData("x := 1", "(:= x 1)")]
    [InlineData("x += 1", "(+= x 1)")]
    [InlineData("x -= 1", "(-= x 1)")]
    [InlineData("x *= 2", "(*= x 2)")]
    [InlineData("x /= 2", "(/= x 2)")]
    [InlineData("x %= 2", "(%= x 2)")]
    [InlineData("p.x := 1", "(:= p.x 1)")]
    public void Assignments(string source, string expected)
        => Assert.Equal(expected, Parse(source));

    [Fact]
    public void CommaSeparatedStatements()
        => Assert.Equal(
            "(decl var label a na)\n(decl var label b na)\n(call label.delete a)\n(call label.delete b)",
            Parse("var label a = na, var label b = na\nlabel.delete(a), label.delete(b)"));

    [Fact]
    public void Declaration_Positions()
    {
        var script = Parser.Parse("x = 1\n\nvar float y = x + 2");
        var y = Assert.IsType<VariableDeclaration>(script.Statements[1]);

        Assert.Equal(new SourcePos(3, 1), y.Pos);
        Assert.Equal(new SourcePos(3, 15), y.Value.Pos);
    }

    // ── Structures ──────────────────────────────────────────

    [Fact]
    public void If()
        => Assert.Equal(
            "(if (> a 0) {(:= x 1); (:= y 2)})\n(decl z 3)",
            Parse("if a > 0\n    x := 1\n    y := 2\nz = 3"));

    [Fact]
    public void IfElseIfElse()
    {
        const string source = """
            if a
                x := 1
            else if b
                x := 2
            else
                x := 3
            """;

        Assert.Equal("(if a {(:= x 1)} else {(if b {(:= x 2)} else {(:= x 3)})})", Parse(source));
    }

    [Fact]
    public void NestedIf_ElseBelongsToOuter()
    {
        const string source = """
            if a
                if b
                    x := 1
            else
                x := 2
            """;

        Assert.Equal("(if a {(if b {(:= x 1)})} else {(:= x 2)})", Parse(source));
    }

    [Fact]
    public void IfAsExpression()
    {
        const string source = """
            x = if a
                1
            else
                2
            y = x
            """;

        Assert.Equal("(decl x (if a {1} else {2}))\n(decl y x)", Parse(source));
    }

    [Fact]
    public void SwitchWithSubject()
    {
        const string source = """
            getLineStyle(style) =>
                switch style
                    "Solid"  => line.style_solid
                    "Dotted" => line.style_dotted
                    => line.style_dashed
            """;

        Assert.Equal(
            "(def getLineStyle (style) {(switch style (case \"Solid\" {line.style_solid}) (case \"Dotted\" {line.style_dotted}) (default {line.style_dashed}))})",
            Parse(source));
    }

    [Fact]
    public void SwitchWithoutSubject_BlockCases()
    {
        const string source = """
            switch
                a > b =>
                    x := 1
                    y := 1
                a < b => x := 2
            """;

        Assert.Equal("(switch (case (> a b) {(:= x 1); (:= y 1)}) (case (< a b) {(:= x 2)}))", Parse(source));
    }

    [Fact]
    public void ForTo()
        => Assert.Equal(
            "(for i 0 (- n 1) {(+= s close[i])})",
            Parse("for i = 0 to n - 1\n    s += close[i]"));

    [Fact]
    public void ForToBy()
        => Assert.Equal("(for i 10 0 (- 2) {(call f i)})", Parse("for i = 10 to 0 by -2\n    f(i)"));

    [Fact]
    public void ForIn()
        => Assert.Equal("(for-in x xs {(+= s x)})", Parse("for x in xs\n    s += x"));

    [Fact]
    public void ForInWithIndex()
        => Assert.Equal("(for-in [i, x] xs {(call f i x)})", Parse("for [i, x] in xs\n    f(i, x)"));

    [Fact]
    public void WhileBreakContinue()
    {
        const string source = """
            while i < 10
                i += 1
                if i == 3
                    continue
                if i == 5
                    break
            """;

        Assert.Equal("(while (< i 10) {(+= i 1); (if (== i 3) {continue}); (if (== i 5) {break})})", Parse(source));
    }

    // ── Functions ───────────────────────────────────────────

    [Fact]
    public void Function_SingleLine()
        => Assert.Equal("(def add (a, b) {(+ a b)})", Parse("add(a, b) => a + b"));

    [Fact]
    public void Function_NoParameters()
        => Assert.Equal("(def mid () {(/ (+ high low) 2)})", Parse("mid() => (high + low) / 2"));

    [Fact]
    public void Function_TypedParametersAndDefaults()
    {
        const string source = """
            props(float level, color col, bool isOte = false, len = 14) =>
                bool isNear = math.abs(close - level) <= len
                [col, isNear ? 3 : 1]
            """;

        Assert.Equal(
            "(def props (float level, color col, bool isOte=false, len=14) {(decl bool isNear (<= (call math.abs (- close level)) len)); [col, (? isNear 3 1)]})",
            Parse(source));
    }

    [Fact]
    public void CallIsNotMistakenForFunction()
        => Assert.Equal("(call f a)\n(decl x 1)", Parse("f(a)\nx = 1"));

    // ── Script ──────────────────────────────────────────────

    [Fact]
    public void Version()
    {
        var script = Parser.Parse("//@version=6\nindicator(\"x\")");

        Assert.Equal("6", script.Version);
        Assert.Single(script.Statements);
    }

    [Fact]
    public void Empty()
        => Assert.Empty(Parser.Parse("// nothing\n").Statements);

    // ── Errors ──────────────────────────────────────────────

    [Theory]
    [InlineData("x = ", 1, 5)]
    [InlineData("x = (1 + 2", 1, 11)]
    [InlineData("f(a = 1, 2)", 1, 10)]
    [InlineData("1 := 2", 1, 1)]
    [InlineData("if a\nx := 1", 2, 1)]
    [InlineData("x = 1 2", 1, 7)]
    [InlineData("a ? b", 1, 6)]
    [InlineData("import user/lib/1", 1, 1)]
    [InlineData("for i = 0 10\n    f()", 1, 11)]
    public void Errors_ReportPosition(string source, int line, int column)
    {
        var ex = Assert.Throws<ParseException>(() => Parser.Parse(source));
        Assert.Equal((line, column), (ex.Line, ex.Column));
    }

    // ── Samples ─────────────────────────────────────────────

    [Fact]
    public void Sample_Test002_Indicator()
    {
        var script = Parser.Parse(ReadSample("test002.pine"));

        Assert.Equal("6", script.Version);
        Assert.Contains(script.Statements, s => s is TupleDeclaration { Names: ["supertrend", "dir"] });
        Assert.Contains(script.Statements, s => s is FunctionDeclaration { Name: "getGridProperties", Parameters.Count: 4 });
        Assert.Contains(script.Statements, s => s is VariableDeclaration { Mode: DeclarationMode.Var, Name: "oteFill" });
    }

    [Fact]
    public void Sample_Strategy001()
    {
        var script = Parser.Parse(ReadSample("strategy001.pine"));
        var printed = AstPrinter.Print(script);

        Assert.StartsWith("(call strategy \"SMA Cross + ATR Stop\" overlay=true", printed);
        Assert.Contains("(decl var float stopLevel na)", printed);
        Assert.Contains("(call strategy.exit \"Stop\" \"Long\" stop=stopLevel)", printed);
    }
}
