using Pine.Core.Parsing;
using Pine.Core.Runtime;

namespace Pine.Core.Tests.Runtime;

public class BuiltinTests
{
    private static readonly double[] Closes = [1, 2, 3, 4, 5];

    /// <summary>Bars with high = close + 0.5 and low = close - 0.5, so the true range is 1.5 after the first bar.</summary>
    private static Bar[] Bars(params double[] closes)
        => closes.Select((c, i) => new Bar(i * 60_000L, c, c + 0.5, c - 0.5, c, 100)).ToArray();

    private static ExecutionResult Run(string source, double[]? closes = null, Dictionary<string, Value>? inputs = null)
        => Interpreter.Run(Parser.Parse(source), Bars(closes ?? Closes), inputs: inputs);

    private static double?[] Plot(string source, params double[] closes)
        => Assert.Single(Run(source, closes.Length == 0 ? null : closes).Plots).Values;

    private static string Global(string source, string name = "x")
        => Run(source).Globals[name].ToString();

    private static void AssertSeries(double?[] expected, double?[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] is { } value)
                Assert.True(actual[i] is { } a && Math.Abs(a - value) < 1e-9, $"Bar {i}: expected {value}, got {actual[i]?.ToString() ?? "na"}");
            else
                Assert.Null(actual[i]);
        }
    }

    // ── math.* ──────────────────────────────────────────────

    [Theory]
    [InlineData("x = math.max(1, 5, 3)", "5")]
    [InlineData("x = math.max(1, 2.5)", "2.5")]
    [InlineData("x = math.min(3, na)", "na")]
    [InlineData("x = math.avg(1, 2, 3, 4)", "2.5")]
    [InlineData("x = math.abs(-3)", "3")]
    [InlineData("x = math.round(2.5)", "3")]
    [InlineData("x = math.round(-2.5)", "-3")]
    [InlineData("x = math.round(1.2345, 2)", "1.23")]
    [InlineData("x = math.floor(1.7)", "1")]
    [InlineData("x = math.ceil(1.2)", "2")]
    [InlineData("x = math.pow(2, 10)", "1024")]
    [InlineData("x = math.sqrt(16)", "4")]
    [InlineData("x = math.log(0)", "na")]
    [InlineData("x = math.sign(-5)", "-1")]
    [InlineData("x = math.round(math.pi, 4)", "3.1416")]
    public void MathFunctions(string source, string expected)
        => Assert.Equal(expected, Global(source));

    [Fact]
    public void MathKinds()
    {
        Assert.Equal(ValueKind.Int, Run("x = math.max(1, 2)").Globals["x"].Kind);
        Assert.Equal(ValueKind.Int, Run("x = math.round(2.4)").Globals["x"].Kind);
        Assert.Equal(ValueKind.Float, Run("x = math.max(1, 2.0)").Globals["x"].Kind);
    }

    [Fact]
    public void MathSum()
        => AssertSeries([null, null, 6, 9, 12], Plot("plot(math.sum(close, 3))"));

    // ── str.*, color.*, fixnan ──────────────────────────────

    [Theory]
    [InlineData("x = str.tostring(1.23456, \"#.##\")", "1.23")]
    [InlineData("x = str.tostring(3)", "3")]
    [InlineData("x = str.tostring(1 / 3)", "0.3333333333")]
    [InlineData("x = str.tostring(na)", "NaN")]
    [InlineData("x = str.tostring(true)", "true")]
    [InlineData("x = \"v=\" + str.tostring(2.5)", "v=2.5")]
    [InlineData("x = str.format(\"{0} e {1,number,#.#}\", \"a\", 1.26)", "a e 1.3")]
    [InlineData("x = str.length(\"abc\")", "3")]
    [InlineData("x = str.upper(\"ab\")", "AB")]
    [InlineData("x = str.contains(\"abc\", \"bc\")", "true")]
    [InlineData("x = str.tonumber(\"1.5\")", "1.5")]
    [InlineData("x = str.tonumber(\"abc\")", "na")]
    [InlineData("x = str.substring(\"abcdef\", 1, 3)", "bc")]
    public void Strings(string source, string expected)
        => Assert.Equal(expected, Global(source));

    [Theory]
    [InlineData("x = color.red", "#F23645")]
    [InlineData("x = color.new(#FF0000, 50)", "#FF000080")]
    [InlineData("x = color.new(na, 50)", "na")]
    [InlineData("x = color.rgb(255, 0, 0)", "#FF0000")]
    [InlineData("x = color.r(color.red)", "242")]
    [InlineData("x = color.t(color.new(color.red, 100))", "100")]
    [InlineData("x = color.from_gradient(5, 0, 10, #000000, #FFFFFF)", "#808080")]
    public void Colors(string source, string expected)
        => Assert.Equal(expected, Global(source));

    [Fact]
    public void FixNan()
        => AssertSeries([1, 2, 2, 4, 5], Plot("plot(fixnan(close == 3 ? na : close))"));

    // ── input.* ─────────────────────────────────────────────

    [Fact]
    public void Inputs_DefaultValues()
    {
        var result = Run("""
            a = input.int(10, "Length", minval = 1)
            b = input.float(2, "Mult")
            c = input.bool(true, "Flag")
            d = input.string("A", "Mode", options = ["A", "B"])
            e = input.source(close, "Source")
            f = input(5)
            """);

        Assert.Equal("10", result.Globals["a"].ToString());
        Assert.Equal(ValueKind.Float, result.Globals["b"].Kind);
        Assert.Equal("true", result.Globals["c"].ToString());
        Assert.Equal("A", result.Globals["d"].ToString());
        Assert.Equal("5", result.Globals["e"].ToString());
        Assert.Equal("5", result.Globals["f"].ToString());

        Assert.Equal(["Flag", "Length", "Mode", "Mult", "Source"], result.Inputs.Keys.Order());
        Assert.Equal("close", result.Inputs["Source"].ToString());
    }

    [Fact]
    public void Inputs_Overrides()
    {
        var result = Run("""
            a = input.int(10, "Length", minval = 1)
            b = input.float(2, "Mult")
            d = input.string("A", "Mode", options = ["A", "B"])
            e = input.source(close, "Source")
            """, inputs: new()
        {
            ["Length"] = Value.Int(20),
            ["Mult"] = Value.Int(3),
            ["Mode"] = Value.String("B"),
            ["Source"] = Value.String("high"),
        });

        Assert.Equal("20", result.Globals["a"].ToString());
        Assert.Equal(ValueKind.Float, result.Globals["b"].Kind);
        Assert.Equal("3", result.Globals["b"].ToString());
        Assert.Equal("B", result.Globals["d"].ToString());
        Assert.Equal("5.5", result.Globals["e"].ToString());
        Assert.Equal("20", result.Inputs["Length"].ToString());
    }

    [Theory]
    [InlineData("a = input.int(10, \"Length\", minval = 1)", 0, "below minval")]
    [InlineData("a = input.int(10, \"Length\", maxval = 50)", 60, "above maxval")]
    [InlineData("a = input.int(10, \"Length\", options = [10, 20])", 30, "not one of the options")]
    [InlineData("a = input.bool(true, \"Length\")", 1, "expects bool")]
    public void Inputs_InvalidOverride(string source, int value, string message)
    {
        var error = Assert.Throws<PineRuntimeException>(() => Run(source, inputs: new() { ["Length"] = Value.Int(value) }));
        Assert.Contains(message, error.Message);
    }

    // ── ta.* ────────────────────────────────────────────────

    [Fact]
    public void Ta_MovingAverages()
    {
        AssertSeries([null, null, 2, 3, 4], Plot("plot(ta.sma(close, 3))"));
        AssertSeries([null, 1.5, 2.5, 3.5, 4.5], Plot("plot(ta.ema(close, 2))"));
        AssertSeries([null, 1.5, 2.25, 3.125, 4.0625], Plot("plot(ta.rma(close, 2))"));
        AssertSeries([null, null, 14.0 / 6, 20.0 / 6, 26.0 / 6], Plot("plot(ta.wma(close, 3))"));
        AssertSeries([null, 1.5, 2.5, 3.5, 4.5], Plot("plot(ta.vwma(close, 2))"));
    }

    [Fact]
    public void Ta_MovingAverage_NaRestartsTheSeed()
        => AssertSeries([null, 1.5, null, null, 4.5], Plot("plot(ta.ema(close == 3 ? na : close, 2))"));

    [Fact]
    public void Ta_TrueRangeAndAtr()
    {
        AssertSeries([null, 1.5, 1.5, 1.5, 1.5], Plot("plot(ta.tr)"));
        AssertSeries([1, 1.5, 1.5, 1.5, 1.5], Plot("plot(ta.tr(true))"));
        AssertSeries([null, 1.25, 1.375, 1.4375, 1.46875], Plot("plot(ta.atr(2))"));
    }

    [Fact]
    public void Ta_Rsi()
    {
        AssertSeries([null, null, 100, 100, 100], Plot("plot(ta.rsi(close, 2))"));
        AssertSeries([null, null, 50, 75, 37.5], Plot("plot(ta.rsi(close, 2))", 1, 2, 1, 2, 1));
    }

    [Fact]
    public void Ta_HighestLowest()
    {
        AssertSeries([null, null, 3, 4, 5], Plot("plot(ta.highest(close, 3))"));
        AssertSeries([null, 2.5, 3.5, 4.5, 5.5], Plot("plot(ta.highest(2))"));
        AssertSeries([null, 1, 2, 3, 4], Plot("plot(ta.lowest(close, 2))"));
        AssertSeries([null, 0.5, 1.5, 2.5, 3.5], Plot("plot(ta.lowest(2))"));
    }

    [Fact]
    public void Ta_Crosses()
    {
        double[] closes = [1, 3, 1, 3];
        AssertSeries([0, 1, 0, 1], Plot("plot(ta.crossover(close, 2) ? 1 : 0)", closes));
        AssertSeries([0, 0, 1, 0], Plot("plot(ta.crossunder(close, 2) ? 1 : 0)", closes));
        AssertSeries([0, 1, 1, 1], Plot("plot(ta.cross(close, 2) ? 1 : 0)", closes));
    }

    [Fact]
    public void Ta_ChangeMomentum()
    {
        AssertSeries([null, 1, 1, 1, 1], Plot("plot(ta.change(close))"));
        AssertSeries([null, null, 2, 2, 2], Plot("plot(ta.change(close, 2))"));
        AssertSeries([null, null, 2, 2, 2], Plot("plot(ta.mom(close, 2))"));
        AssertSeries([null, null, 200, 100, 200.0 / 3], Plot("plot(ta.roc(close, 2))"));
        AssertSeries([0, 0, 1, 1, 1], Plot("plot(ta.rising(close, 2) ? 1 : 0)"));
        AssertSeries([0, 0, 0, 0, 0], Plot("plot(ta.falling(close, 2) ? 1 : 0)"));

        Assert.Equal(ValueKind.Int, Run("x = ta.change(bar_index)").Globals["x"].Kind);
        Assert.Equal("true", Run("x = ta.change(close > 2)", [1, 2, 3]).Globals["x"].ToString());
    }

    [Fact]
    public void Ta_Stdev()
    {
        var biased = Math.Sqrt(2.0 / 3);
        AssertSeries([null, null, biased, biased, biased], Plot("plot(ta.stdev(close, 3))"));
        AssertSeries([null, null, 1, 1, 1], Plot("plot(ta.stdev(close, 3, false))"));
    }

    [Fact]
    public void Ta_EventFunctions()
    {
        AssertSeries([1, 3, 6, 10, 15], Plot("plot(ta.cum(close))"));
        AssertSeries([null, 0, 1, 0, 1], Plot("plot(ta.barssince(close % 2 == 0))"));
        AssertSeries([null, 2, 2, 4, 4], Plot("plot(ta.valuewhen(close % 2 == 0, close, 0))"));
        AssertSeries([null, null, null, 2, 2], Plot("plot(ta.valuewhen(close % 2 == 0, close, 1))"));
    }

    [Fact]
    public void Ta_TupleFunctions()
    {
        var result = Run("""
            [basis, upper, lower] = ta.bb(close, 3, 2)
            [m, signal, hist] = ta.macd(close, 2, 3, 2)
            """);

        var deviation = 2 * Math.Sqrt(2.0 / 3);
        Assert.Equal(4, result.Globals["basis"].AsDouble, 9);
        Assert.Equal(4 + deviation, result.Globals["upper"].AsDouble, 9);
        Assert.Equal(4 - deviation, result.Globals["lower"].AsDouble, 9);
        Assert.Equal(result.Globals["m"].AsDouble - result.Globals["signal"].AsDouble, result.Globals["hist"].AsDouble, 9);
    }

    [Fact]
    public void Ta_Supertrend()
    {
        // Values worked out by hand from the manual's pine_supertrend, with atr = tr (period 1)
        var result = Run("""
            [st, dir] = ta.supertrend(1, 1)
            plot(st, "st")
            plot(dir, "dir")
            """);

        AssertSeries([0, 3.5, 3.5, 2.5, 3.5], result.Plots[0].Values);
        AssertSeries([1, 1, 1, -1, -1], result.Plots[1].Values);
    }

    [Fact]
    public void Ta_StatePerCallSite()
    {
        var result = Run("""
            a = ta.sma(close, 3)
            b = close != 3 ? ta.sma(close, 3) : na
            """);

        Assert.Equal(4, result.Globals["a"].AsDouble, 9);
        // The second call site skipped the bar with close 3: its window is 2, 4, 5
        Assert.Equal(11.0 / 3, result.Globals["b"].AsDouble, 9);
    }

    // ── Drawing no-ops ──────────────────────────────────────

    [Fact]
    public void Drawing_IsIgnored()
    {
        var result = Run("""
            var line l = na
            if na(l)
                l := line.new(bar_index, close, bar_index + 1, close, style = line.style_dotted)
            line.set_xy2(l, bar_index, close)
            label.new(bar_index, high, str.tostring(close), style = label.style_label_left)
            plotshape(close > 2, style = shape.triangleup, location = location.belowbar, size = size.small)
            bgcolor(color.new(color.green, 90))
            fill(plot(close), plot(open), color = color.blue)
            alertcondition(close > 2, "Alert", "Message")
            s = line.style_dashed
            """);

        Assert.True(result.Globals["l"].IsNa);
        Assert.Equal("line.style_dashed", result.Globals["s"].ToString());
    }

    [Fact]
    public void Drawing_UnknownNamespaceStillFails()
        => Assert.Throws<PineRuntimeException>(() => Run("x = foo.bar(1)"));

    // ── Sample scripts ──────────────────────────────────────

    private static Bar[] Wave(int count)
        => Enumerable.Range(0, count).Select(i =>
        {
            var close = 100 + 10 * Math.Sin(i / 15.0) + i * 0.05;
            var open = close - Math.Cos(i / 3.0);
            return new Bar(i * 60_000L, open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1000);
        }).ToArray();

    [Fact]
    public void Sample_Test002_RunsInBothModes()
    {
        var script = Parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "test002.pine")));
        var bars = Wave(400);

        var grid = Interpreter.Run(script, bars);
        var bands = Interpreter.Run(script, bars, inputs: new Dictionary<string, Value>
        {
            ["Fibonacci Mode"] = Value.String("Fibonacci Bands"),
            ["Supertrend ATR Period"] = Value.Int(20),
        });

        var direction = grid.Globals["dir"];
        Assert.True(direction.AsDouble is 1 or -1);
        Assert.Contains(grid.Plots, p => p.Title == "Supertrend Stop Loss" && p.Values.Any(v => v is not null));
        Assert.Contains(bands.Plots, p => p.Title == "Fib Band 0.618 (OTE)" && p.Values.Any(v => v is not null));
        Assert.Equal("Fibonacci Bands", bands.Inputs["Fibonacci Mode"].ToString());
    }
}
