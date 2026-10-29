using Backtester.Engine.Reporting;
using Pine.Core.Parsing;
using Pine.Core.Runtime;

namespace Backtester.Engine.Tests;

public class ReportingTests
{
    /// <summary>Opens 10, 12, 13, 14, 15: a long from 12 to 13 (+1), then a short from 14 to 15 (−1).</summary>
    private static BacktestResult TwoTrades(string comment = "out")
    {
        var bars = new[] { 10.0, 12, 13, 14, 15 }
            .Select((p, i) => new Bar(i * 60_000L, p, p + 0.5, p - 0.5, p, 1000))
            .ToArray();

        return Backtest.Run(Parser.Parse($$"""
            strategy("Two trades", initial_capital = 1000)
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 1
                strategy.close("L", comment = "{{comment}}")
            if bar_index == 2
                strategy.entry("S", strategy.short)
            if bar_index == 3
                strategy.close("S")
            plot(close, "Close")
            """), bars);
    }

    [Fact]
    public void Metrics()
    {
        var m = TwoTrades().Metrics;

        Assert.Equal((1000.0, 1000.0, 0.0), (m.InitialCapital, m.FinalEquity, m.NetProfit));
        Assert.Equal((1.0, 1.0, 1.0), (m.GrossProfit, m.GrossLoss, m.ProfitFactor));
        Assert.Equal((2, 1, 1, 0), (m.TotalClosedTrades, m.WinningTrades, m.LosingTrades, m.EvenTrades));
        Assert.Equal(50, m.PercentProfitable);
        Assert.Equal((0.0, 1.0, 1.0, 1.0), (m.AverageTrade, m.LargestWinningTrade, m.LargestLosingTrade, m.RatioAverageWinAverageLoss));
        Assert.Equal(1, m.AverageBarsInTrade);
        Assert.Equal(50, m.BuyAndHoldReturnPercent);
        Assert.Null(m.SharpeRatio);
    }

    [Fact]
    public void Metrics_DrawdownFromPeak()
    {
        // Equity at the closes: 1000, 1000, 1001 (long +1), 1001, 1000 (short −1)
        var m = TwoTrades().Metrics;

        Assert.Equal(1, m.MaxDrawdown, 9);
        Assert.Equal(1.0 / 1001 * 100, m.MaxDrawdownPercent, 9);
        Assert.Equal(1, m.MaxRunUp, 9);
    }

    [Fact]
    public void Metrics_NoLosses_HasNoProfitFactor()
    {
        var bars = Enumerable.Range(0, 4).Select(i => new Bar(i * 60_000L, 10 + i, 11 + i, 9 + i, 10 + i, 1)).ToArray();
        var result = Backtest.Run(Parser.Parse("""
            strategy("Up")
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 2
                strategy.close("L")
            """), bars);

        Assert.Null(result.Metrics.ProfitFactor);
        Assert.Null(result.Metrics.AverageLosingTrade);
    }

    [Fact]
    public void Csv_Trades()
    {
        var writer = new StringWriter();
        CsvReport.WriteTrades(TwoTrades(comment: "out, finally"), writer);
        var lines = writer.ToString().TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("trade,status,direction,entry_id,entry_time,", lines[0]);
        Assert.Equal("1,closed,long,L,1970-01-01T00:01:00Z,1,12,L,1970-01-01T00:02:00Z,2,13,1,1,8.3333333333,1,0,1,0.5,,\"out, finally\"", lines[1]);
        Assert.StartsWith("2,closed,short,S,", lines[2]);
    }

    [Fact]
    public void Csv_EquityMetricsPlots()
    {
        var result = TwoTrades();

        var equity = new StringWriter();
        CsvReport.WriteEquity(result, equity);
        Assert.Equal("2,1970-01-01T00:02:00Z,13,1001,0,0,0", equity.ToString().ReplaceLineEndings("\n").Split('\n')[3]);

        var metrics = new StringWriter();
        CsvReport.WriteMetrics(result, metrics);
        Assert.Contains("net_profit,0", metrics.ToString());
        Assert.Contains("profit_factor,1", metrics.ToString());
        Assert.Contains("sharpe_ratio,\n", metrics.ToString().ReplaceLineEndings("\n"));

        var plots = new StringWriter();
        CsvReport.WritePlots(result, plots);
        Assert.StartsWith("time,Close", plots.ToString());
        Assert.Contains("1970-01-01T00:04:00Z,15", plots.ToString());
    }

    [Fact]
    public void Csv_WriteAll()
    {
        var directory = Path.Combine(Path.GetTempPath(), "backtest-" + Guid.NewGuid());
        try
        {
            var files = CsvReport.WriteAll(TwoTrades(), directory);
            Assert.Equal(4, files.Count);
            Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("20", ValueKind.Int, "20")]
    [InlineData("2.5", ValueKind.Float, "2.5")]
    [InlineData("true", ValueKind.Bool, "true")]
    [InlineData("#FF0000", ValueKind.Color, "#FF0000")]
    [InlineData("OTE Grid", ValueKind.String, "OTE Grid")]
    public void InputValues_FromText(string text, ValueKind kind, string expected)
    {
        var value = InputValues.FromText(text);
        Assert.Equal((kind, expected), (value.Kind, value.ToString()));
    }

    [Fact]
    public void InputValues_FromJson()
    {
        var inputs = InputValues.ParseJson("""{"Fast": 20, "Mult": 2.5, "On": true, "Mode": "20"}""");

        Assert.Equal(ValueKind.Int, inputs["Fast"].Kind);
        Assert.Equal(ValueKind.Float, inputs["Mult"].Kind);
        Assert.Equal(ValueKind.Bool, inputs["On"].Kind);
        Assert.Equal(ValueKind.String, inputs["Mode"].Kind);
    }
}
