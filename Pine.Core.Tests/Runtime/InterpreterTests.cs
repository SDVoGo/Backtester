using Pine.Core.Parsing;
using Pine.Core.Runtime;

namespace Pine.Core.Tests.Runtime;

public class InterpreterTests
{
    private static readonly double[] Closes = [1, 2, 3, 4, 5];

    private static Bar[] Bars(params double[] closes)
        => closes.Select((c, i) => new Bar(i * 60_000L, c, c + 0.5, c - 0.5, c, 100)).ToArray();

    private static ExecutionResult Run(string source, params double[] closes)
        => Interpreter.Run(Parser.Parse(source), Bars(closes.Length == 0 ? Closes : closes));

    /// <summary>Values of the only plot in the script.</summary>
    private static double?[] Plot(string source, params double[] closes)
        => Assert.Single(Run(source, closes).Plots).Values;

    private static Value Global(string source, string name)
        => Run(source).Globals[name];

    // ── Expressions ─────────────────────────────────────────

    [Theory]
    [InlineData("x = 1 + 2 * 3", "7")]
    [InlineData("x = 5 / 2", "2.5")]
    [InlineData("x = 4 / 2", "2")]
    [InlineData("x = 7 % 3", "1")]
    [InlineData("x = 1 / 0", "na")]
    [InlineData("x = 2 * 1.5", "3")]
    [InlineData("x = -3 + 1", "-2")]
    [InlineData("x = \"a\" + \"b\"", "ab")]
    [InlineData("x = 1 < 2 and 2 < 3", "true")]
    [InlineData("x = not (1 == 1.0)", "false")]
    [InlineData("x = 3 > 2 ? \"y\" : \"n\"", "y")]
    [InlineData("x = #FF0000", "#FF0000")]
    [InlineData("float x = 1", "1")]
    public void Expressions(string source, string expected)
        => Assert.Equal(expected, Global(source, "x").ToString());

    [Fact]
    public void IntAndFloatKinds()
    {
        Assert.Equal(ValueKind.Int, Global("x = 7 % 3", "x").Kind);
        Assert.Equal(ValueKind.Float, Global("x = 4 / 2", "x").Kind);
        Assert.Equal(ValueKind.Float, Global("float x = 1", "x").Kind);
    }

    [Theory]
    [InlineData("x = na + 1", "na")]
    [InlineData("x = -na", "na")]
    [InlineData("x = na == na", "false")]
    [InlineData("x = na != 1", "false")]
    [InlineData("x = na > 1", "false")]
    [InlineData("x = na(na)", "true")]
    [InlineData("x = na(1)", "false")]
    [InlineData("x = nz(na)", "0")]
    [InlineData("x = nz(na, 5)", "5")]
    [InlineData("x = nz(3, 5)", "3")]
    [InlineData("x = na ? 1 : 2", "2")]
    public void NaPropagation(string source, string expected)
        => Assert.Equal(expected, Global(source, "x").ToString());

    [Fact]
    public void AndOr_ShortCircuit()
        => Assert.Equal("false", Global("x = false and undefinedFunction()", "x").ToString());

    // ── Bars and history ────────────────────────────────────

    [Fact]
    public void PlotRecordsEveryBar()
    {
        var plot = Assert.Single(Run("plot(close * 2, \"Double\")").Plots);

        Assert.Equal("Double", plot.Title);
        Assert.Equal([2, 4, 6, 8, 10], plot.Values);
    }

    [Fact]
    public void BuiltinHistory()
        => Assert.Equal([null, 1, 2, 3, 4], Plot("plot(close[1])"));

    [Fact]
    public void BuiltinHistory_DynamicOffset()
        => Assert.Equal([null, null, 1, 2, 3], Plot("n = 2\nplot(close[n])"));

    [Fact]
    public void VariableHistory()
        => Assert.Equal([null, 10, 20, 30, 40], Plot("x = close * 10\nplot(x[1])"));

    [Fact]
    public void ExpressionHistory()
        => Assert.Equal([null, null, 3, 5, 7], Plot("plot((close + close[1])[1])"));

    [Fact]
    public void BarState()
    {
        var result = Run("""
            plot(bar_index, "index")
            plot(barstate.isfirst ? 1 : 0, "first")
            plot(barstate.islast ? 1 : 0, "last")
            plot(time, "time")
            """);

        Assert.Equal([0, 1, 2, 3, 4], result.Plots[0].Values);
        Assert.Equal([1, 0, 0, 0, 0], result.Plots[1].Values);
        Assert.Equal([0, 0, 0, 0, 1], result.Plots[2].Values);
        Assert.Equal([0, 60_000, 120_000, 180_000, 240_000], result.Plots[3].Values);
    }

    [Fact]
    public void BarFields()
    {
        var result = Run("plot(high, \"h\")\nplot(low, \"l\")\nplot(hl2, \"hl2\")", 10);

        Assert.Equal([10.5], result.Plots[0].Values);
        Assert.Equal([9.5], result.Plots[1].Values);
        Assert.Equal([10], result.Plots[2].Values);
    }

    // ── var ─────────────────────────────────────────────────

    [Fact]
    public void Var_PersistsAcrossBars()
        => Assert.Equal([1, 2, 3, 4, 5], Plot("var n = 0\nn += 1\nplot(n)"));

    [Fact]
    public void NonVar_ReinitialisedEveryBar()
        => Assert.Equal([1, 1, 1, 1, 1], Plot("n = 0\nn += 1\nplot(n)"));

    [Fact]
    public void Var_InitialisedOnlyOnce()
        => Assert.Equal([1, 1, 1, 1, 1], Plot("var float first = close\nplot(first)"));

    [Fact]
    public void Var_UpdatedConditionally()
        => Assert.Equal([0, 0, 1, 2, 3], Plot("var count = 0\nif close > 2\n    count += 1\nplot(count)"));

    [Fact]
    public void Var_History()
        => Assert.Equal([null, 1, 2, 3, 4], Plot("var n = 0\nn += 1\nplot(n[1])"));

    [Fact]
    public void LocalHistory_CountsExecutionsOnly()
    {
        // The block runs on bars 1, 3 and 4: y[1] is the value of the previous run
        const string source = """
            float out = na
            if close != 3
                y = close * 10
                out := y[1]
            plot(out)
            """;

        Assert.Equal([null, 10, null, 20, 40], Plot(source));
    }

    // ── Functions ───────────────────────────────────────────

    [Fact]
    public void Function_ReturnsLastExpression()
        => Assert.Equal("7", Global("add(a, b) => a + b\nx = add(3, 4)", "x").ToString());

    [Fact]
    public void Function_NamedAndDefaultArguments()
    {
        const string source = """
            f(a, b = 10, c = 100) => a + b + c
            x = f(1, c = 2)
            """;

        Assert.Equal("13", Global(source, "x").ToString());
    }

    [Fact]
    public void Function_BlockBodyAndTuple()
    {
        const string source = """
            minmax(a, b) =>
                lo = a < b ? a : b
                hi = a < b ? b : a
                [lo, hi]
            [l, h] = minmax(5, 2)
            x = l * 10 + h
            """;

        Assert.Equal("25", Global(source, "x").ToString());
    }

    [Fact]
    public void Function_EachCallSiteHasItsOwnState()
    {
        const string source = """
            counter(step) =>
                var total = 0
                total += step
                total
            plot(counter(1), "a")
            plot(counter(10), "b")
            """;

        var result = Run(source);

        Assert.Equal([1, 2, 3, 4, 5], result.Plots[0].Values);
        Assert.Equal([10, 20, 30, 40, 50], result.Plots[1].Values);
    }

    [Fact]
    public void Function_ParameterHistory()
    {
        const string source = """
            prev(src) => src[1]
            plot(prev(close), "close")
            plot(prev(close * 100), "scaled")
            """;

        var result = Run(source);

        Assert.Equal([null, 1, 2, 3, 4], result.Plots[0].Values);
        Assert.Equal([null, 100, 200, 300, 400], result.Plots[1].Values);
    }

    [Fact]
    public void Function_ReadsGlobals()
        => Assert.Equal("6", Global("k = 3\ntriple(x) => x * k\nx = triple(2)", "x").ToString());

    // ── Stateful built-ins ──────────────────────────────────

    [Fact]
    public void Sma()
        => Assert.Equal([null, null, 2, 3, 4], Plot("plot(ta.sma(close, 3))"));

    [Fact]
    public void Sma_CallSitesAreIndependent()
    {
        var result = Run("plot(ta.sma(close, 2), \"2\")\nplot(ta.sma(close, 3), \"3\")");

        Assert.Equal([null, 1.5, 2.5, 3.5, 4.5], result.Plots[0].Values);
        Assert.Equal([null, null, 2, 3, 4], result.Plots[1].Values);
    }

    [Fact]
    public void Sma_InsideFunction_StatePerCallSite()
    {
        const string source = """
            smooth(src) => ta.sma(src, 2)
            plot(smooth(close), "a")
            plot(smooth(close * 2), "b")
            """;

        var result = Run(source);

        Assert.Equal([null, 1.5, 2.5, 3.5, 4.5], result.Plots[0].Values);
        Assert.Equal([null, 3, 5, 7, 9], result.Plots[1].Values);
    }

    // ── Structures ──────────────────────────────────────────

    [Fact]
    public void IfAsValue()
        => Assert.Equal([-1, -1, 0, 1, 1], Plot("""
            x = if close > 3
                1
            else if close < 3
                -1
            else
                0
            plot(x)
            """));

    [Fact]
    public void IfWithoutElse_IsNa()
        => Assert.Equal([null, null, null, 1, 1], Plot("x = if close > 3\n    1\nplot(x)"));

    [Fact]
    public void SwitchWithSubject()
    {
        const string source = """
            mode = "b"
            x = switch mode
                "a" => 1
                "b" => 2
                => 3
            """;

        Assert.Equal("2", Global(source, "x").ToString());
    }

    [Fact]
    public void SwitchDefaultAndWithoutSubject()
        => Assert.Equal([10, 10, 20, 30, 30], Plot("""
            x = switch
                close < 3 => 10
                close == 3 => 20
                => 30
            plot(x)
            """));

    [Fact]
    public void ForTo_SumsHistory()
        => Assert.Equal([null, null, 6, 9, 12], Plot("""
            float s = 0
            for i = 0 to 2
                s += close[i]
            plot(s)
            """));

    [Fact]
    public void ForBy_Descending()
        => Assert.Equal("100806040200", Global("""
            s = 0
            for i = 10 to 0 by -2
                s := s * 100 + i
            """, "s").ToString());

    [Fact]
    public void ForIn()
        => Assert.Equal("12", Global("""
            s = 0
            for [i, v] in [1, 2, 3]
                s += i * v
            x = s + 4
            """, "x").ToString());

    [Fact]
    public void WhileBreakContinue()
    {
        const string source = """
            i = 0
            s = 0
            while true
                i += 1
                if i % 2 == 0
                    continue
                if i > 7
                    break
                s += i
            """;

        Assert.Equal("16", Global(source, "s").ToString());
    }

    [Fact]
    public void LoopValue_IsLastIteration()
        => Assert.Equal("6", Global("x = for i = 1 to 3\n    i * 2", "x").ToString());

    // ── Errors ──────────────────────────────────────────────

    [Theory]
    [InlineData("x = y + 1", "Undeclared identifier 'y'", 1, 5)]
    [InlineData("y := 1", "Undeclared variable 'y'", 1, 1)]
    [InlineData("x = foo(1)", "Unknown function 'foo'", 1, 5)]
    [InlineData("x = 1\nx = 2", "already declared", 2, 1)]
    [InlineData("x = \"a\" + 1", "Cannot add", 1, 5)]
    [InlineData("x = close[-1]", "non-negative", 1, 11)]
    [InlineData("f(a) => a\nx = f(1, 2)", "Too many arguments", 2, 10)]
    [InlineData("f(a) => a\nx = f(b = 1)", "no parameter 'b'", 2, 7)]
    [InlineData("f(a) => a\nx = f()", "Missing argument 'a'", 2, 5)]
    [InlineData("f(a) => f(a)\nx = f(1)", "Recursive call", 1, 9)]
    [InlineData("g = 0\nf() => g := 1\nx = f()", "Cannot modify global", 2, 8)]
    [InlineData("x = ta.sma(close, 0)", "positive length", 1, 5)]
    [InlineData("x = foo.bar", "Unknown built-in 'foo.bar'", 1, 5)]
    public void RuntimeErrors(string source, string message, int line, int column)
    {
        var ex = Assert.Throws<PineRuntimeException>(() => Run(source));

        Assert.Contains(message, ex.Message);
        Assert.Equal((line, column), (ex.Pos.Line, ex.Pos.Column));
    }

    [Fact]
    public void RuntimeError_ReportsBar()
    {
        var ex = Assert.Throws<PineRuntimeException>(() => Run("if bar_index == 3\n    x := 1"));
        Assert.Equal(3, ex.BarIndex);
    }

    // ── Series ──────────────────────────────────────────────

    [Fact]
    public void Series_RingBufferKeepsMaxBarsBack()
    {
        var series = new Series();
        for (var bar = 0; bar < Series.MaxBarsBack + 100; bar++)
            series.Set(Value.Int(bar), bar);

        Assert.Equal(Value.Int(Series.MaxBarsBack + 99), series.Get(0));
        Assert.Equal(Value.Int(99), series.Get(Series.MaxBarsBack));
    }

    [Fact]
    public void Series_SameBarOverwrites()
    {
        var series = new Series();
        series.Set(Value.Int(1), 0);
        series.Set(Value.Int(2), 1);
        series.Set(Value.Int(3), 1);

        Assert.Equal(Value.Int(3), series.Get(0));
        Assert.Equal(Value.Int(1), series.Get(1));
        Assert.Equal(Value.Na, series.Get(2));
    }
}
