using System.Text.Json;
using Backtester.Engine.Golden;
using Pine.Core.Parsing;
using Pine.Core.Runtime;
using Xunit.Abstractions;

namespace Backtester.Engine.Tests;

public class GoldenTests(ITestOutputHelper output)
{
    // The same two trades in both TradingView layouts, newest first as the export can be
    private const string OldLayout = """
        Trade #,Type,Signal,Date/Time,Price USD,Contracts,Profit USD,Profit %,Cum. Profit USD,Cum. Profit %,Run-up USD,Run-up %,Drawdown USD,Drawdown %
        3,Exit Short,Open,1970-01-01 00:05,16,1,-1,-6.67,0,0,0,0,1,6.67
        3,Entry Short,S2,1970-01-01 00:04,15,1,-1,-6.67,0,0,0,0,1,6.67
        2,Exit Short,S,1970-01-01 00:04,15,1,−1,−7.14,0,0,0.5,3.57,1.5,10.71
        2,Entry Short,S,1970-01-01 00:03,14,1,−1,−7.14,0,0,0.5,3.57,1.5,10.71
        1,Exit Long,out,1970-01-01 00:02,13,1,1,8.33,1,0.1,0.5,4.17,0.5,4.17
        1,Entry Long,L,1970-01-01 00:01,12,1,1,8.33,1,0.1,0.5,4.17,0.5,4.17
        """;

    private const string NewLayout = """
        Trade #,Type,Date/Time,Signal,Price USD,Position size (qty),Position size (value),Net P&L USD,Net P&L %,Run-up USD,Run-up %,Drawdown USD,Drawdown %,Cumulative P&L USD,Cumulative P&L %
        1,Entry long,1970-01-01 00:01,L,12,1,12,1,8.33,0.5,4.17,0.5,4.17,1,0.1
        1,Exit long,1970-01-01 00:02,out,13,1,13,1,8.33,0.5,4.17,0.5,4.17,1,0.1
        2,Entry short,1970-01-01 00:03,S,14,1,14,-1,-7.14,0.5,3.57,1.5,10.71,0,0
        2,Exit short,1970-01-01 00:04,S,15,1,15,-1,-7.14,0.5,3.57,1.5,10.71,0,0
        """;

    private static IReadOnlyList<ClosedTrade> OurTrades()
    {
        var bars = new[] { 10.0, 12, 13, 14, 15, 16 }
            .Select((p, i) => new Bar(i * 60_000L, p, p + 0.5, p - 0.5, p, 1000))
            .ToArray();

        return Backtest.Run(Parser.Parse("""
            strategy("Two trades")
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 1
                strategy.close("L", comment = "out")
            if bar_index == 2
                strategy.entry("S", strategy.short)
            if bar_index == 3
                strategy.close("S")
            """), bars).Trades;
    }

    [Theory]
    [InlineData(OldLayout)]
    [InlineData(NewLayout)]
    public void TradingViewExport_Parses(string csv)
    {
        var trades = TradingViewTrades.Parse(csv);
        var first = trades[0];

        Assert.Equal((1, Direction.Long, "L", 12.0, "out", 13.0, 1.0, 1.0),
            (first.Number, first.Direction, first.EntrySignal, first.EntryPrice, first.ExitSignal, first.ExitPrice, first.Quantity, first.Profit));
        Assert.Equal(new DateTime(1970, 1, 1, 0, 1, 0), first.EntryTime);
        Assert.Equal(-1, trades[1].Profit);
    }

    [Fact]
    public void TradingViewExport_OpenTradeIsSkipped()
    {
        var trades = TradingViewTrades.Parse(OldLayout);
        Assert.True(trades[^1].IsOpen);
        Assert.True(GoldenComparison.Compare(OurTrades(), trades).Matches);
    }

    [Fact]
    public void Comparison_Matches()
    {
        var report = GoldenComparison.Compare(OurTrades(), TradingViewTrades.Parse(NewLayout));
        Assert.True(report.Matches, report.ToString());
    }

    [Fact]
    public void Comparison_ReportsDifferences()
    {
        var altered = NewLayout.Replace("Exit long,1970-01-01 00:02,out,13,", "Exit long,1970-01-01 00:02,out,13.5,");
        var report = GoldenComparison.Compare(OurTrades(), TradingViewTrades.Parse(altered), new GoldenOptions { ProfitTolerance = null });

        var difference = Assert.Single(report.Differences);
        Assert.Equal((1, "exit price", "13.5", "13"), (difference.Trade, difference.Field, difference.Expected, difference.Actual));
    }

    [Fact]
    public void Comparison_ChartTimezone()
    {
        var shifted = NewLayout.Replace("1970-01-01 00:", "1970-01-01 02:");

        Assert.False(GoldenComparison.Compare(OurTrades(), TradingViewTrades.Parse(shifted)).Matches);
        Assert.True(GoldenComparison.Compare(OurTrades(), TradingViewTrades.Parse(shifted),
            new GoldenOptions { ChartUtcOffset = TimeSpan.FromHours(2) }).Matches);
    }

    [Fact]
    public void Comparison_CountMismatch()
    {
        var oneTrade = string.Join('\n', NewLayout.Split('\n').Take(3));
        var report = GoldenComparison.Compare(OurTrades(), TradingViewTrades.Parse(oneTrade));

        Assert.Contains(report.Differences, d => d.Field == "count" && d.Expected == "1" && d.Actual == "2");
    }

    /// <summary>
    /// Runs every case in <c>Golden/</c> (see Golden/README.md): a strategy, the bars and TradingView's
    /// list of trades exported from the same chart. A case still waiting for <c>tradingview.csv</c>
    /// only runs the strategy. Passes with no cases.
    /// </summary>
    [Fact]
    public void GoldenCases()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Golden");
        var cases = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        if (cases.Length == 0)
            output.WriteLine("No golden cases found in Golden/");

        var failures = new List<string>();
        foreach (var directory in cases)
        {
            var name = Path.GetFileName(directory);
            var settings = ReadSettings(directory);
            var result = Backtest.Run(
                Parser.Parse(File.ReadAllText(Path.Combine(directory, "strategy.pine"))),
                OhlcvCsv.ReadFile(DataPath(directory, settings.Data)),
                new BacktestOptions { MinTick = settings.MinTick, Inputs = settings.Inputs });

            var expected = Path.Combine(directory, "tradingview.csv");
            if (!File.Exists(expected))
            {
                output.WriteLine($"{name}: {result.Trades.Count} trades, waiting for tradingview.csv");
                continue;
            }

            var report = GoldenComparison.Compare(
                result.Trades,
                TradingViewTrades.ReadFile(expected),
                new GoldenOptions
                {
                    ChartUtcOffset = settings.UtcOffset,
                    PriceTolerance = settings.MinTick / 2,
                    ProfitTolerance = settings.ProfitTolerance,
                });

            output.WriteLine($"{name}: {report}");
            if (!report.Matches)
                failures.Add($"{name}: {report}");
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    /// <summary>The bars file in the case folder, or one of the shared samples (Backtester/Resources).</summary>
    private static string DataPath(string directory, string file)
    {
        var local = Path.Combine(directory, file);
        return File.Exists(local) ? local : Path.Combine(AppContext.BaseDirectory, "Samples", file);
    }

    private sealed record CaseSettings(
        string Data, double MinTick, TimeSpan UtcOffset, double? ProfitTolerance, IReadOnlyDictionary<string, Value>? Inputs);

    /// <summary>
    /// Optional <c>case.json</c>:
    /// <c>{"data": "ohlcv.csv", "minTick": 0.01, "utcOffset": "+02:00", "profitTolerance": 0.01, "inputs": {...}}</c>.
    /// </summary>
    private static CaseSettings ReadSettings(string directory)
    {
        var path = Path.Combine(directory, "case.json");
        if (!File.Exists(path))
            return new CaseSettings("ohlcv.csv", 0.01, TimeSpan.Zero, 0.01, null);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;

        return new CaseSettings(
            root.TryGetProperty("data", out var data) ? data.GetString()! : "ohlcv.csv",
            root.TryGetProperty("minTick", out var tick) ? tick.GetDouble() : 0.01,
            root.TryGetProperty("utcOffset", out var offset) ? TimeSpan.Parse(offset.GetString()!.TrimStart('+')) : TimeSpan.Zero,
            root.TryGetProperty("profitTolerance", out var tolerance) ? tolerance.ValueKind == JsonValueKind.Null ? null : tolerance.GetDouble() : 0.01,
            root.TryGetProperty("inputs", out var inputs) ? InputValues.ParseJson(inputs.GetRawText()) : null);
    }
}
