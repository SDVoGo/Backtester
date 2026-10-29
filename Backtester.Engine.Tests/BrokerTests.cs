using Backtester.Engine;
using Pine.Core.Parsing;
using Pine.Core.Runtime;

namespace Backtester.Engine.Tests;

public class BrokerTests
{
    /// <summary>Bars from (open, high, low, close) tuples, one minute apart.</summary>
    private static Bar[] Bars(params (double O, double H, double L, double C)[] bars)
        => bars.Select((b, i) => new Bar(i * 60_000L, b.O, b.H, b.L, b.C, 1000)).ToArray();

    /// <summary>Five quiet bars opening at 10, 12, 13, 14, 15.</summary>
    private static readonly Bar[] Steps = Bars((10, 10.5, 9.5, 10), (12, 12.5, 11.5, 12), (13, 13.5, 12.5, 13), (14, 14.5, 13.5, 14), (15, 15.5, 14.5, 15));

    private static BacktestResult Run(string body, Bar[]? bars = null, string settings = "", BacktestOptions? options = null)
        => Backtest.Run(Parser.Parse($"strategy(\"Test\"{settings})\n{body}"), bars ?? Steps, options);

    // ── Market orders ───────────────────────────────────────

    [Fact]
    public void MarketEntry_FillsAtNextOpen()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            """);

        var trade = Assert.Single(result.OpenTrades);
        Assert.Equal(1, trade.EntryBar);
        Assert.Equal(12, trade.EntryPrice);
        Assert.Equal(1, trade.Quantity);
        Assert.Empty(result.Trades);
    }

    [Fact]
    public void Close_FillsAtNextOpen()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, qty = 2)
            if bar_index == 1
                strategy.close("L", comment = "out")
            """);

        var trade = Assert.Single(result.Trades);
        Assert.Equal((12.0, 13.0, 2.0, 2.0), (trade.EntryPrice, trade.ExitPrice, trade.Quantity, trade.Profit));
        Assert.Equal(("L", "L", "out"), (trade.EntryId, trade.ExitId, trade.ExitComment));
        Assert.Empty(result.OpenTrades);
    }

    [Fact]
    public void ProcessOrdersOnClose_FillsAtThisClose()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 2
                strategy.close_all()
            """, settings: ", process_orders_on_close = true");

        var trade = Assert.Single(result.Trades);
        Assert.Equal((0, 10.0), (trade.EntryBar, trade.EntryPrice));
        Assert.Equal((2, 13.0), (trade.ExitBar, trade.ExitPrice));
    }

    [Fact]
    public void Close_Immediately_FillsAtCurrentClose()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 2
                strategy.close("L", immediately = true)
            """);

        var trade = Assert.Single(result.Trades);
        Assert.Equal((2, 13.0), (trade.ExitBar, trade.ExitPrice));
    }

    [Fact]
    public void Entry_ReversesOppositePosition()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 1
                strategy.entry("S", strategy.short)
            """);

        var closed = Assert.Single(result.Trades);
        Assert.Equal(("L", "S", 1.0), (closed.EntryId, closed.ExitId, closed.Profit));

        var open = Assert.Single(result.OpenTrades);
        Assert.Equal((Direction.Short, 13.0, 1.0), (open.Direction, open.EntryPrice, open.Quantity));
    }

    [Fact]
    public void Order_ReducesOppositePositionWithoutReversing()
    {
        var result = Run("""
            if bar_index == 0
                strategy.order("Buy", strategy.long, qty = 3)
            if bar_index == 1
                strategy.order("Sell", strategy.short, qty = 1)
            """);

        var closed = Assert.Single(result.Trades);
        Assert.Equal(1, closed.Quantity);
        Assert.Equal(2, Assert.Single(result.OpenTrades).Quantity);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData(", pyramiding = 3", 3)]
    public void Entry_RespectsPyramiding(string settings, int expected)
    {
        var result = Run("strategy.entry(\"L\", strategy.long)", settings: settings);
        Assert.Equal(expected, result.OpenTrades.Count);
    }

    // ── Price orders and the intrabar path ──────────────────

    [Fact]
    public void Exit_SameBarAsEntry_TakeProfit()
    {
        // Open is nearer the low: path 100 → 99 → 112 → 105, the limit at 110 is reached first
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
                strategy.exit("X", "L", stop = 95, limit = 110)
            """, Bars((100, 100, 100, 100), (100, 112, 99, 105)));

        var trade = Assert.Single(result.Trades);
        Assert.Equal((1, 1, 110.0, "X"), (trade.EntryBar, trade.ExitBar, trade.ExitPrice, trade.ExitId));
    }

    [Fact]
    public void Exit_PathVisitsNearerExtremeFirst()
    {
        // Open is nearer the low: path 100 → 94 → 111 → 100, the stop at 95 is reached first
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
                strategy.exit("X", "L", stop = 95, limit = 110)
            """, Bars((100, 100, 100, 100), (100, 111, 94, 100)));

        Assert.Equal(95, Assert.Single(result.Trades).ExitPrice);
    }

    [Fact]
    public void Exit_StopIsRoundedToTheTick()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
                strategy.exit("X", "L", stop = 95.0137)
            """, Bars((100, 100, 100, 100), (100, 101, 94, 100)), options: new BacktestOptions { MinTick = 0.05 });

        Assert.Equal(95, Assert.Single(result.Trades).ExitPrice, 9);
    }

    [Fact]
    public void Exit_GapThroughStop_FillsAtOpen()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            strategy.exit("X", "L", stop = 95)
            """, Bars((100, 100, 100, 100), (100, 101, 99, 100), (90, 91, 89, 90)));

        var trade = Assert.Single(result.Trades);
        Assert.Equal((2, 90.0), (trade.ExitBar, trade.ExitPrice));
    }

    [Fact]
    public void Exit_ProfitAndLossInTicks()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
                strategy.exit("X", "L", profit = 500, loss = 300, comment_profit = "tp", comment_loss = "sl")
            """, Bars((100, 100, 100, 100), (100, 101, 99, 100), (100, 106, 99.5, 105)));

        var trade = Assert.Single(result.Trades);
        Assert.Equal((105.0, "tp"), (trade.ExitPrice, trade.ExitComment));
    }

    [Fact]
    public void Exit_TrailingStop()
    {
        // Activates at 102 (200 ticks), trails 1.00 (100 ticks) below the highest price
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            strategy.exit("T", "L", trail_points = 200, trail_offset = 100)
            """, Bars((100, 100, 100, 100), (100, 100.5, 99.5, 100), (100, 103, 100, 103), (103, 103.5, 101, 101.5)));

        var trade = Assert.Single(result.Trades);
        Assert.Equal((3, 102.5), (trade.ExitBar, trade.ExitPrice));
    }

    [Fact]
    public void Exit_QuantityPercent_ClosesPartOfThePosition()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, qty = 4)
                strategy.exit("Half", "L", qty_percent = 50, limit = 110)
            """, Bars((100, 100, 100, 100), (100, 111, 99, 105)));

        Assert.Equal(2, Assert.Single(result.Trades).Quantity);
        Assert.Equal(2, Assert.Single(result.OpenTrades).Quantity);
    }

    [Fact]
    public void StopEntry_FillsWhenPriceCrossesTheStop()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, stop = 105)
            """, Bars((100, 100, 100, 100), (100, 104, 99, 103), (103, 106, 102, 105)));

        var trade = Assert.Single(result.OpenTrades);
        Assert.Equal((2, 105.0), (trade.EntryBar, trade.EntryPrice));
    }

    [Fact]
    public void LimitEntry_AndCancel()
    {
        var bars = Bars((100, 100, 100, 100), (100, 101, 99, 100), (100, 101, 94, 96));
        var filled = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, limit = 95)
            """, bars);
        var cancelled = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, limit = 95)
            if bar_index == 1
                strategy.cancel("L")
            """, bars);

        Assert.Equal(95, Assert.Single(filled.OpenTrades).EntryPrice);
        Assert.Empty(cancelled.OpenTrades);
    }

    // ── Costs and sizing ────────────────────────────────────

    [Fact]
    public void Commission_Percent()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
                strategy.exit("X", "L", limit = 110)
            """, Bars((100, 100, 100, 100), (100, 111, 99, 105)),
            settings: ", commission_type = strategy.commission.percent, commission_value = 0.1");

        var trade = Assert.Single(result.Trades);
        Assert.Equal(0.21, trade.Commission, 9);
        Assert.Equal(9.79, trade.Profit, 9);
    }

    [Fact]
    public void Slippage_MovesMarketFillsAgainstTheTrader()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            """, settings: ", slippage = 2");

        Assert.Equal(12.02, Assert.Single(result.OpenTrades).EntryPrice, 9);
    }

    [Fact]
    public void PercentOfEquity_SizesFromCloseAtPlacement()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            """, settings: ", initial_capital = 10000, default_qty_type = strategy.percent_of_equity, default_qty_value = 50");

        Assert.Equal(500, Assert.Single(result.OpenTrades).Quantity, 9);
    }

    // ── Strategy variables ──────────────────────────────────

    [Fact]
    public void Variables_SeeFillsFromTheNextBar()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long, qty = 2)
            plot(strategy.position_size, "size")
            plot(strategy.position_size[1], "previous")
            plot(strategy.equity, "equity")
            plot(strategy.position_avg_price, "avg")
            """, settings: ", initial_capital = 1000");

        Assert.Equal([0, 2, 2, 2, 2], result.Execution.Plots[0].Values);
        Assert.Equal([null, 0, 2, 2, 2], result.Execution.Plots[1].Values);
        Assert.Equal([1000, 1000, 1002, 1004, 1006], result.Execution.Plots[2].Values);
        Assert.Equal([null, 12, 12, 12, 12], result.Execution.Plots[3].Values);
        Assert.Equal(1006, result.Equity[^1].Equity);
    }

    [Fact]
    public void Variables_TradeCounters()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 1
                strategy.close("L")
            if bar_index == 2
                strategy.entry("S", strategy.short)
            if bar_index == 3
                strategy.close("S")
            """);

        var globals = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 1
                strategy.close("L")
            x = strategy.closedtrades
            w = strategy.wintrades
            p = strategy.closedtrades.profit(0)
            """).Execution.Globals;

        Assert.Equal([1.0, -1.0], result.Trades.Select(t => t.Profit));
        Assert.Equal(("1", "1", "1"), (globals["x"].ToString(), globals["w"].ToString(), globals["p"].ToString()));
    }

    [Fact]
    public void DrawdownAndRunUp()
    {
        var result = Run("""
            if bar_index == 0
                strategy.entry("L", strategy.long)
            if bar_index == 2
                strategy.close("L")
            """, Bars((100, 100, 100, 100), (100, 104, 97, 101), (101, 102, 100, 101), (102, 102, 102, 102)));

        var trade = Assert.Single(result.Trades);
        Assert.Equal((4.0, 3.0), (trade.RunUp, trade.Drawdown));
    }

    // ── Sample strategy ─────────────────────────────────────

    [Fact]
    public void Sample_Strategy001_IsConsistent()
    {
        var script = Parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "strategy001.pine")));
        var bars = Enumerable.Range(0, 600).Select(i =>
        {
            var close = 100 + 15 * Math.Sin(i / 25.0) + 5 * Math.Sin(i / 7.0);
            var open = close - Math.Cos(i / 3.0);
            return new Bar(i * 60_000L, open, Math.Max(open, close) + 1, Math.Min(open, close) - 1, close, 1000);
        }).ToArray();

        var result = Backtest.Run(script, bars);

        Assert.Equal(10_000, result.Settings.InitialCapital);
        Assert.NotEmpty(result.Trades);
        Assert.All(result.Trades, t => Assert.Equal(Direction.Long, t.Direction));
        Assert.All(result.Trades, t => Assert.True(t.ExitBar > t.EntryBar));

        var openProfit = result.OpenTrades.Sum(t => t.ProfitAt(bars[^1].Close));
        var expected = result.Settings.InitialCapital + result.Trades.Sum(t => t.Profit) + openProfit;
        Assert.Equal(expected, result.Equity[^1].Equity, 6);
        // Every signal becomes one trade, except a signal on the last bar whose entry never fills
        var signals = result.Execution.Globals["tradesCount"].AsInt;
        Assert.InRange(result.Trades.Count + result.OpenTrades.Count, signals - 1, signals);
    }
}
