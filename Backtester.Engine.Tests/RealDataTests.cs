using System.Runtime.CompilerServices;
using Backtester.Engine.Reporting;
using Pine.Core.Parsing;
using Pine.Core.Runtime;

namespace Backtester.Engine.Tests;

/// <summary>
/// The sample strategies on real market data: BINANCE:BTCUSDT 1h, 2024-01-01 to 2024-05-04
/// (3000 bars from Binance's public API, the same bars TradingView shows for that symbol).
/// </summary>
public class RealDataTests
{
    private static readonly Lazy<List<Bar>> Bars = new(() => OhlcvCsv.ReadFile(Sample("btcusdt_1h.csv")));

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private static BacktestResult RunStrategy001()
        => Backtest.Run(Parser.Parse(File.ReadAllText(Sample("strategy001.pine"))), Bars.Value);

    [Fact]
    public void Dataset_IsContinuous()
    {
        var bars = Bars.Value;
        Assert.Equal(3000, bars.Count);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bars[0].Time);
        Assert.All(bars.Zip(bars.Skip(1)), pair => Assert.Equal(3_600_000, pair.Second.Time - pair.First.Time));
        Assert.All(bars, b => Assert.True(b.Low <= Math.Min(b.Open, b.Close) && b.High >= Math.Max(b.Open, b.Close)));
    }

    /// <summary>
    /// Checks every trade against the bars with an independent calculation of the signals:
    /// entries follow a crossover of SMA(10) over SMA(30) and fill at the next open;
    /// exits by strategy.close fill at the open, stop exits within the bar's range and on the tick.
    /// </summary>
    [Fact]
    public void Strategy001_TradesFollowTheSignals()
    {
        var bars = Bars.Value;
        var result = RunStrategy001();
        var fast = Sma(bars, 10);
        var slow = Sma(bars, 30);

        bool CrossOver(int i) => i >= 1 && fast[i] > slow[i] && fast[i - 1] <= slow[i - 1];
        bool CrossUnder(int i) => i >= 1 && fast[i] < slow[i] && fast[i - 1] >= slow[i - 1];

        Assert.True(result.Trades.Count > 20);
        foreach (var trade in result.Trades)
        {
            var label = $"trade {trade.Number}";
            Assert.True(CrossOver(trade.EntryBar - 1), $"{label}: no crossover before the entry");
            Assert.Equal(bars[trade.EntryBar].Open, trade.EntryPrice);
            Assert.True(trade.ExitBar > trade.EntryBar, label);

            var exitBar = bars[trade.ExitBar];
            if (trade.ExitId == "Long")
            {
                Assert.True(CrossUnder(trade.ExitBar - 1), $"{label}: no crossunder before the close");
                Assert.Equal(exitBar.Open, trade.ExitPrice);
            }
            else
            {
                Assert.Equal("Stop", trade.ExitId);
                Assert.InRange(trade.ExitPrice, exitBar.Low, exitBar.High);
                Assert.Equal(Math.Round(trade.ExitPrice, 2), trade.ExitPrice, 9);
            }
        }

        // No crossover was missed while flat: from each exit bar (flat again from its open) to the next signal
        var flatFrom = 0;
        foreach (var trade in result.Trades)
        {
            var missed = Enumerable.Range(flatFrom, trade.EntryBar - 1 - flatFrom).Where(CrossOver).ToList();
            Assert.True(missed.Count == 0, $"Crossovers not traded at bars {string.Join(", ", missed)}");
            flatFrom = trade.ExitBar;
        }
    }

    [Fact]
    public void Strategy001_EquityAddsUp()
    {
        var result = RunStrategy001();
        var m = result.Metrics;

        Assert.Equal(m.InitialCapital + m.NetProfit + m.OpenProfit, m.FinalEquity, 6);
        Assert.Equal(m.NetProfit, result.Trades.Sum(t => t.Profit), 6);
        Assert.Equal(result.Equity[^1].Equity, m.FinalEquity, 6);
        Assert.All(result.Trades, t => Assert.True(t.Commission > 0));
    }

    /// <summary>
    /// Regression snapshot: the trades must not change unless intended. To accept a change,
    /// run the tests with BACKTEST_UPDATE_SNAPSHOTS=1 and review the diff of the snapshot file.
    /// </summary>
    [Fact]
    public void Strategy001_Snapshot()
    {
        var writer = new StringWriter();
        CsvReport.WriteTrades(RunStrategy001(), writer);
        var actual = writer.ToString().ReplaceLineEndings("\n");

        var path = SnapshotPath("strategy001_btcusdt_1h.trades.csv");
        if (System.Environment.GetEnvironmentVariable("BACKTEST_UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            File.WriteAllText(path, actual);
            Assert.Fail($"Snapshot written to {path}: review it and run the tests again");
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void Test002_RunsOnRealData()
    {
        var script = Parser.Parse(File.ReadAllText(Sample("test002.pine")));
        var result = Interpreter.Run(script, Bars.Value);

        var supertrend = Assert.Single(result.Plots, p => p.Title == "Supertrend Stop Loss");
        Assert.True(supertrend.Values.Count(v => v is not null) > 2000);
        Assert.True(result.Globals["dir"].AsDouble is 1 or -1);
    }

    private static double[] Sma(IReadOnlyList<Bar> bars, int length)
    {
        var sma = new double[bars.Count];
        for (var i = 0; i < bars.Count; i++)
            sma[i] = i + 1 < length ? double.NaN : bars.Skip(i + 1 - length).Take(length).Average(b => b.Close);
        return sma;
    }

    private static string SnapshotPath(string name, [CallerFilePath] string testFile = "")
        => Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots", name);
}
